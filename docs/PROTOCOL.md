# Protocolo ARC

Contrato del canal. Dos superficies (REST y MCP) sobre la misma lógica: `ChannelService`
en [src/Arc.Core/ChannelService.cs](../src/Arc.Core/ChannelService.cs).

## Idea central

Un agente de línea de comandos **no es un servidor**: sólo existe mientras dura su turno.
No puede mantener una suscripción abierta ni reaccionar a un evento que llegue mientras
está inactivo. Por eso el canal no usa un broker de mensajes, sino **long-polling HTTP**:
la petición del agente se queda abierta en el servidor hasta que llega la respuesta o
vence el plazo. Encaja con el modelo por turnos sin infraestructura adicional.

## Identidad

Toda petición (salvo `/healthz`) lleva dos cabeceras:

| Cabecera | Obligatoria | Contenido |
|---|---|---|
| `X-ARC-Agent` | sí | Identidad del emisor: `^[a-z0-9][a-z0-9._-]{0,63}$` |
| `X-ARC-Token` | si el hub tiene `ARC_TOKEN` | Secreto compartido |
| `X-ARC-Provider` | no | Etiqueta informativa: `claude-code`, `codex`… |

El nombre del agente es la clave del registro de esperas, de ahí el formato acotado.
Un agente sólo puede **leer su propio buzón** y **responder a lo que va dirigido a él**.

## Tipos de mensaje

| Tipo | Espera respuesta | Uso |
|---|---|---|
| `request` | sí | Preguntar algo que necesitas para continuar |
| `response` | — | Contestación a un `request`, ligada por `correlation_id` |
| `note` | no | Avisar de un hecho consumado |

### Estados

`pending` → `delivered` (al leerse en el buzón) → `answered` (sólo `request`, al contestarse).

Un `request` entregado pero sin responder sigue siendo recuperable con
`?unanswered=true`: es la vía de recuperación si un agente cae antes de contestar.

## Endpoints REST

| Método | Ruta | Comportamiento |
|---|---|---|
| `POST` | `/v1/requests?wait=N` | Crea la petición. Bloquea hasta la respuesta: `200` con ella, `202` con `outcome: timeout` al vencer. |
| `GET` | `/v1/requests/{id}/response?wait=N` | Retoma la espera de una petición que ya venció. |
| `POST` | `/v1/requests/{id}/response` | Contesta. Despierta al emisor al instante. |
| `POST` | `/v1/notes` | Aviso sin respuesta esperada. |
| `GET` | `/v1/inbox/{agent}?wait=N&unanswered=` | Buzón propio. `204` si no llega nada en el plazo. |
| `GET` | `/v1/threads/{id}` | Conversación completa, en orden. |
| `GET` | `/v1/messages/{id}` | Un mensaje concreto. |
| `GET` | `/v1/agents` | Agentes vistos. |
| `GET` | `/healthz` | Estado, esperas activas y agentes. Sin autenticar. |
| `GET` | `/ui` | Panel de observación. Sin autenticar: es una página sin datos dentro. |
| `GET` | `/v1/observe/history?limit=N&thread=` | Cola del historial más agentes y esperas. Con `thread`, sólo esa conversación. |
| `GET` | `/v1/observe/threads?limit=N` | Índice de conversaciones, de la más reciente a la más antigua. |
| `GET` | `/v1/observe/stream` | Flujo SSE de lo que va pasando. |

`wait` va en segundos y se recorta a `ARC_MAX_WAIT` (300 por defecto). `wait=0` encola y vuelve.

### Observación

Las rutas `/v1/observe` piden `X-ARC-Token` como cualquier otra, pero **no**
`X-ARC-Agent`: quien mira no participa, así que no tiene identidad en el canal, no
entra en `/v1/agents` y no cambia el estado de ningún mensaje. Leer el canal entero
es justo lo que las distingue del buzón, que sólo enseña lo propio.

`/v1/observe/threads` es el índice para elegir: una fila por hilo y sin cuerpos.

```json
{
  "thread_id": "thr_1d865fd649404bdd",
  "subject": "Unidad del campo `total` en el endpoint de pago",
  "participants": ["claude-pc1", "codex-pc2"],
  "messages": 2,
  "open_requests": 0,
  "closed": true,
  "started_at": "2026-09-03T20:16:32.633+00:00",
  "last_at": "2026-09-03T20:17:32.458+00:00"
}
```

`closed` no se guarda en ninguna parte: se deriva de que no quede ninguna pregunta
del hilo sin contestar. Por eso un hilo de puros avisos nace terminado —nadie va a
contestar un aviso— y por eso una conversación puede volver a abrirse si alguien
pregunta otra vez dentro de ella. El índice no viaja por el flujo: quien lo esté
mirando lo vuelve a pedir cuando el flujo le anuncia un mensaje nuevo.

`/v1/observe/stream` es Server-Sent Events. Tres tipos de evento:

| Evento | Cuándo | Contenido |
|---|---|---|
| `message` | Se crea un `request`, `response` o `note` | `{ "event": "message", "message": { … } }` |
| `delivered` | Un agente lee su buzón | `{ "event": "delivered", "ids": ["req_…"] }` |
| `state` | Cambian las esperas o los agentes | `{ "waiters": { … }, "agents": [ … ], "observers": N }` |

Cada `data:` es una sola línea: un salto dentro partiría el evento en dos. Cuando
no hay tráfico, el hub manda un comentario `: ping` cada dos segundos para que la
conexión no se cierre. Un observador lento nunca frena al canal: su cola está
acotada y descarta lo más viejo.

### Cuerpo de una petición

```json
{
  "to": "codex-pc2",
  "subject": "Contrato del endpoint de pagos",
  "body": "¿El campo total viaja en céntimos?",
  "refs": { "branch": "feat/pagos", "commit": "a1b2c3d", "files": ["src/pagos/Total.cs"] },
  "thread_id": "thr_1a2b3c"
}
```

`refs` es un objeto JSON libre. **Manda referencias, no contenido**: ambas máquinas
tienen un clon del mismo repositorio, así que un commit y una ruta bastan. El cuerpo
está limitado a 256 KB y el hub rechaza lo que pase de ahí.

### Resultado de una petición

```json
{
  "outcome": "answered",
  "request_id": "req_1a2b3c",
  "thread_id": "thr_4d5e6f",
  "response": { "id": "res_...", "from": "codex-pc2", "body": "...", "kind": "response" }
}
```

`outcome` es `answered`, `timeout` o `queued` (cuando se pidió `wait=0`).

### Errores

Siempre `{"error": "<código>", "detail": "<explicación>"}`:

| Código | HTTP | Motivo |
|---|---|---|
| `unauthorized` | 401 | `X-ARC-Token` ausente o incorrecto |
| `invalid_json` | 400 | El cuerpo no se pudo leer: no es JSON válido, o no llegó como UTF-8 |
| `bad_agent` | 422 | `X-ARC-Agent` ausente o mal formado |
| `bad_recipient` | 422 | `to` ausente o mal formado |
| `empty_body` | 422 | Falta el cuerpo |
| `body_too_large` | 422 | Más de 256 KB |
| `invalid_refs` | 422 | `refs` no es un objeto JSON válido |
| `invalid_wait` | 422 | `wait` fuera del rango que admite el hub |
| `self_addressed` | 422 | Un agente se escribe a sí mismo |
| `forbidden` | 403 | Buzón ajeno, o responder algo que no va dirigido a ti |
| `not_found` | 404 | No existe esa petición o ese hilo |
| `already_answered` | 409 | Esa petición ya tiene respuesta |

`400` es sólo para lo que no se pudo leer. Una petición que llegó entera y a la que
una regla dijo que no responde `422`: así un cliente distingue un fallo suyo de
serialización de una regla que ha incumplido, sin mirar el código.


## Herramientas MCP

En `/mcp`, transporte Streamable HTTP. Mismas operaciones, con la salida redactada
para que la lea un modelo:

| Herramienta | Qué hace |
|---|---|
| `arc_ask` | Pregunta y espera. Bloquea hasta la respuesta o el plazo. |
| `arc_await` | Retoma la espera de una petición que venció. |
| `arc_inbox` | Lee tu buzón; con `wait` se queda esperando. |
| `arc_respond` | Contesta una petición dirigida a ti. |
| `arc_note` | Avisa sin esperar respuesta. |
| `arc_thread` | Recupera una conversación completa. |
| `arc_agents` | Lista quién está en el canal. |

## Codificación

Todo es UTF-8. **En Windows, no pases cuerpos con acentos por la línea de comandos**:
argv atraviesa la codepage ANSI y los corrompe antes de que curl los envíe. Usa
`arc ... --body-file fichero.md`, o `--data-binary @fichero` con curl. El hub rechaza
esos cuerpos con `invalid_json` en lugar de guardar texto roto.

## Interbloqueo

Dos agentes esperándose a la vez agotan sus turnos sin avanzar. Mitigaciones:

- Todo `ask` lleva plazo; al vencer, la petición sigue viva y el trabajo continúa.
- `/healthz` expone `waiters`, donde una espera mutua se ve de un vistazo.
