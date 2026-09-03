# ARC — Agent Relay Channel

Canal de comunicación entre agentes de distintos proveedores (Claude Code, Codex CLI)
que trabajan en PCs distintas de la misma red.

Sustituye al fichero `.md` compartido que hacía de buzón: aquí hay destinatario,
hilo, estado, historial y —lo que importa— **espera real**. El agente A pregunta y
se queda bloqueado dentro de su propio turno hasta que B contesta.

```
PC1  Claude Code ──┐                    ┌── SQLite (arc.db)
                   ├─► Arc.Hub :8765 ───┤
PC2  Codex CLI  ───┘   REST + MCP       └── esperas en memoria
                            │
                            └── /ui  panel en vivo, sólo para mirar
```

## Por qué no un broker de mensajes

Un agente de línea de comandos **no es un servidor**: sólo existe mientras dura su
turno. No puede mantener una suscripción abierta ni despertar ante un evento que
llegue mientras está inactivo.

Kafka está dimensionado para millones de eventos particionados y arrastra una JVM;
aquí hablamos de decenas de mensajes al día entre dos procesos. RabbitMQ es más
razonable, pero su modelo asume consumidores conectados de forma permanente —
justo lo que un agente CLI no puede ser. Emular RPC sobre él obliga igualmente a
escribir un puente HTTP que el agente pueda consultar: el broker no sustituye al
hub, se suma a él.

El problema difícil no es el transporte, es la ventana de vida del agente. Lo
resuelve el **long-polling HTTP**: la petición se queda abierta en el servidor
hasta que llega la respuesta.

## Requisitos

En la máquina donde compilas y hospedas el hub:

| Necesitas | Para qué | Comprobación |
|---|---|---|
| **.NET SDK 10** | Todo el código va a `net10.0` | `dotnet --version` |
| **PowerShell** | `publish.ps1` e `install-hub.ps1` | Viene con Windows |

Y sólo si vas a ejecutar las pruebas de humo, que son scripts de shell:

| Necesitas | Para qué | Comprobación |
|---|---|---|
| **bash** | Los cinco scripts `.sh` | Git Bash o WSL |
| **curl** | `smoke.sh`, `smoke-mcp.sh` y `smoke-ui.sh` hablan HTTP crudo | `curl --version` |
| **python 3** | Los smokes parsean JSON con él | `python --version` |

> Si `python` no está en el PATH, los smokes **no fallan con un mensaje claro**:
> devuelven campos vacíos y verás comprobaciones caer sin motivo aparente.
> Verifica los tres antes de interpretar un fallo.

La otra PC no necesita nada de esto: el cliente que se copia allí es autocontenido.

## Prueba rápida en una sola máquina

```bash
dotnet build
```

Arranca el hub (en modo anónimo sólo escucha en loopback):

```bash
ARC_ALLOW_ANONYMOUS=1 dotnet run --project src/Arc.Hub
```

Y en otra terminal, comprueba las cuatro superficies:

```bash
bash scripts/smoke.sh && bash scripts/smoke-cli.sh && bash scripts/smoke-mcp.sh && bash scripts/smoke-ui.sh
```

El panel queda en <http://127.0.0.1:8765/ui>.

## Instalación en las dos PCs

### 1. En la máquina que hospeda el hub

```powershell
./scripts/publish.ps1
```

Y desde una consola **de administrador**:

```powershell
./scripts/install-hub.ps1 -HubPath .\publish\hub
```

Crea la regla de firewall (sólo perfil de red privado), instala el servicio de
Windows, genera el token si no le pasas uno y te dice la IP y las variables que
deben usar los agentes.

### 2. En la otra PC

Copia `publish/cli` y añade la carpeta al PATH. El cliente es autocontenido: no
necesita .NET instalado allí.

### 3. En cada agente

Cada máquina se identifica con un nombre distinto:

```powershell
# PC1
$env:ARC_URL = 'http://192.168.1.10:8765'
$env:ARC_TOKEN = '<el token que imprimió install-hub>'
$env:ARC_AGENT = 'claude-pc1'
$env:ARC_PROVIDER = 'claude-code'

# PC2 — mismo token y URL, distinto ARC_AGENT
$env:ARC_AGENT = 'codex-pc2'
$env:ARC_PROVIDER = 'codex'
```

`ARC_AGENT` no admite cualquier cosa: es la clave del registro de esperas, así que
tiene que casar con `^[a-z0-9][a-z0-9._-]{0,63}$` — **minúsculas**, dígitos, punto,
guion o guion bajo, empezando por letra o dígito y como mucho 64 caracteres.
Una mayúscula o un espacio y el hub responde `400 bad_agent`.

Comprueba la conexión con `arc health`.

## Uso desde un agente

### Por línea de comandos

Funciona en cualquier agente que pueda ejecutar comandos, sin depender de su
soporte MCP:

```bash
# A pregunta y espera hasta 3 minutos
arc ask --to codex-pc2 --subject "Contrato de pagos" --body-file pregunta.md --wait 180

# B mira su buzón, esperando si hace falta
arc inbox --wait 300

# B contesta; A se despierta al instante
arc respond req_1a2b3c --body-file respuesta.md
```

Esos tres son el ciclo completo. Los demás comandos cubren el resto de casos:

| Comando | Para qué |
|---|---|
| `arc ask --to A --body-file f [--wait N]` | Pregunta y bloquea hasta la respuesta |
| `arc await <request_id> [--wait N]` | Retoma la espera de una petición que ya venció |
| `arc inbox [--wait N] [--unanswered]` | Buzón propio. `--unanswered` recupera lo entregado y aún sin contestar |
| `arc respond <request_id> --body-file f` | Contesta a una petición dirigida a ti |
| `arc note --to A --body-file f` | Aviso de un hecho consumado, sin esperar respuesta |
| `arc thread <thread_id>` | Historial completo de una conversación |
| `arc agents` | Quién está en el canal y cuándo se le vio |
| `arc health` | Diagnóstico del hub: esperas activas, agentes, configuración |

Opciones comunes a todos: `--json` para salida cruda sin formatear, y
`--url` / `--agent` / `--token`, que equivalen a las variables de entorno.

Los códigos de salida permiten ramificar sin leer el texto:
`0` éxito · `1` error · `2` uso incorrecto · `3` espera expirada · `4` sin mensajes.

Ojo con los valores por defecto de `--wait`, que **no son cero**:

| Comando | Sin `--wait` |
|---|---|
| `arc ask` | espera **120 s** |
| `arc await` | espera **120 s** |
| `arc inbox` | **no espera**: mira y vuelve |

Y el hub recorta lo que pidas contra su `ARC_MAX_WAIT`, 300 segundos por defecto:
`--wait 600` no da error, espera 300. Con `--wait 0` la petición se encola y el
comando vuelve al instante, con código `3` — no hay respuesta *todavía*, pero la
petición queda viva en el buzón del destinatario.

El cuerpo se pasa por fichero (`--body-file f`), por stdin (`--body-file -`) o en
línea (`--body "texto"`).

> En Windows, pasa siempre el cuerpo con `--body-file`. Los argumentos de línea de
> comandos atraviesan la codepage ANSI y corrompen los acentos antes de salir.

### Como herramientas MCP

En Claude Code:

```bash
claude mcp add --transport http arc http://192.168.1.10:8765/mcp \
  --header "X-ARC-Agent: claude-pc1" \
  --header "X-ARC-Token: <token>"
```

En Codex CLI, en `~/.codex/config.toml`:

```toml
[mcp_servers.arc]
url = "http://192.168.1.10:8765/mcp"

[mcp_servers.arc.http_headers]
"X-ARC-Agent" = "codex-pc2"
"X-ARC-Token" = "<token>"
```

El soporte de servidores MCP por HTTP en Codex CLI ha ido por detrás del de stdio;
comprueba tu versión. Si no lo soporta, el cliente `arc` por línea de comandos hace
exactamente lo mismo y siempre funciona.

Herramientas publicadas: `arc_ask`, `arc_await`, `arc_inbox`, `arc_respond`,
`arc_note`, `arc_thread`, `arc_agents`.

## Ver la conversación

El hub sirve un panel en `/ui`. Es sólo para mirar: no deja escribir en el canal.

```
http://192.168.1.10:8765/ui
```

Los mensajes no se sondean, los empuja el servidor: aparecen en el momento en que
se escriben, con su hilo, su asunto y sus `refs`. Y junto al historial se ve lo
que el historial no guarda — **quién está bloqueado esperando ahora mismo**, con
el cronómetro corriendo, y qué agente tiene el buzón abierto. Un interbloqueo se
reconoce de un vistazo: dos preguntas abiertas, cada una con su contador subiendo.

A la izquierda están todas las conversaciones, separadas por estado: **en curso**
—le queda alguna pregunta sin contestar— y **terminadas**. Al elegir una, el panel
deja de enseñar el canal entero y se queda con ese hilo completo, del principio al
final, aunque sea de anteayer; el resto sigue llegando por detrás y los contadores
del lateral siguen siendo los del canal. La conversación elegida va en la dirección
(`/ui#t=thr_…`), así que recargar no la pierde y el enlace se puede pasar tal cual.

Si el hub tiene `ARC_TOKEN`, el panel lo pide al abrirse y lo guarda en ese
navegador. La página en sí no lleva datos dentro, y por eso se sirve sin
autenticar; los datos no. El panel tampoco es un agente: no aparece en
`arc agents` ni marca como entregado lo que enseña.

## Configuración del hub

| Variable | Por defecto | Para qué |
|---|---|---|
| `ARC_TOKEN` | — | Secreto compartido. **Obligatorio** salvo modo anónimo. |
| `ARC_ALLOW_ANONYMOUS` | — | `1` desactiva la autenticación y fuerza escucha en loopback. Sólo para pruebas. |
| `ARC_URLS` | `http://0.0.0.0:8765` | Dónde escuchar. |
| `ARC_DB` | `arc.db` junto al ejecutable | Fichero SQLite. |
| `ARC_MAX_WAIT` | `300` | Tope de segundos por espera. |

`install-hub.ps1` fija `ARC_TOKEN`, `ARC_DB` y `ARC_URLS` a nivel de **máquina**,
no de usuario: el servicio no hereda tu entorno.

## Mantenimiento

Todo lo de aquí necesita consola **de administrador**.

**Quitar el hub de la máquina** — para el servicio, lo borra y retira la regla de
firewall:

```powershell
./scripts/install-hub.ps1 -Uninstall
```

No toca las variables de máquina ni el fichero `arc.db`. Si quieres borrarlas del
todo:

```powershell
'ARC_TOKEN','ARC_DB','ARC_URLS' | ForEach-Object { [Environment]::SetEnvironmentVariable($_, $null, 'Machine') }
```

**Recuperar el token** si lo perdiste, sin reinstalar nada:

```powershell
[Environment]::GetEnvironmentVariable('ARC_TOKEN', 'Machine')
```

**Rotar el token** — vuelve a lanzar el instalador con uno nuevo; recrea el
servicio con él. Después hay que actualizar `ARC_TOKEN` en las dos PCs:

```powershell
./scripts/install-hub.ps1 -HubPath .\publish\hub -Token (Read-Host 'nuevo token')
```

**Abrir sólo el firewall**, sin instalar servicio, con `-FirewallOnly`. Y el puerto
se cambia con `-Port` tanto al instalar como al desinstalar.

## Cuando algo no responde

`/healthz` es el único endpoint que **no** pide token, así que sirve para
diagnosticar desde cualquier máquina:

```bash
curl http://192.168.1.10:8765/healthz
```

Devuelve el tiempo en marcha, si el hub exige autenticación, el `ARC_MAX_WAIT`
efectivo, la ruta de la base de datos, los agentes vistos y —lo más útil— las
**esperas activas**: dos agentes esperándose mutuamente se ven ahí.

| Síntoma | Causa habitual |
|---|---|
| `arc health` funciona en el hub pero no desde la otra PC | La red está clasificada como pública. La regla sólo abre el perfil privado: `Get-NetConnectionProfile` |
| `401 unauthorized` | `ARC_TOKEN` distinto del que tiene el servicio, o sin definir |
| `400 bad_agent` | `ARC_AGENT` con mayúsculas, espacios o vacío. Ver el formato más arriba |
| `403 forbidden` | Leer el buzón de otro, o responder a algo que no va dirigido a ti |
| `409 already_answered` | Esa petición ya tiene respuesta; el ciclo es de una sola contestación |
| Acentos corrompidos | Cuerpo pasado por `--body`. Usa `--body-file` |
| El servicio existe pero no responde | `Get-Service ArcHub`; si arranca y muere, casi siempre es `ARC_TOKEN` sin definir a nivel de máquina |

Una petición que expiró **no se pierde**: sigue viva en el buzón del destinatario y
se recupera con `arc inbox --unanswered` o `arc await <request_id>`.

## Un apunte sobre la app de escritorio de ChatGPT

Sus conectores se ejecutan desde la infraestructura de OpenAI, no desde tu equipo:
un host de tu LAN privada no es alcanzable desde ahí. Incluirla exigiría exponer el
hub a internet por HTTPS mediante un túnel, con la superficie de ataque que eso
añade. El canal fiable son Claude Code y Codex CLI, ambos en tu red.

## Estructura

| Ruta | Contenido |
|---|---|
| [src/Arc.Core](src/Arc.Core) | Modelo, almacén SQLite, registro de esperas y reglas del canal |
| [src/Arc.Hub](src/Arc.Hub) | Servicio HTTP: endpoints REST, herramientas MCP y el panel de `/ui` |
| [src/Arc.Cli](src/Arc.Cli) | Cliente `arc` |
| [tests/Arc.Tests](tests/Arc.Tests) | Pruebas del núcleo |
| [scripts](scripts) | Publicación, instalación y pruebas de humo |
| [docs/PROTOCOL.md](docs/PROTOCOL.md) | Contrato completo de mensajes y endpoints |
| [docs/AGENTES.md](docs/AGENTES.md) | Texto para pegar en el `CLAUDE.md` / `AGENTS.md` de cada agente |

## Verificación

Todo de una pasada — levanta un hub temporal, lo prueba y lo apaga:

```bash
bash scripts/test-all.sh
```

O por partes, contra un hub ya en marcha:

```bash
dotnet test                  # núcleo: registro de esperas y persistencia
bash scripts/smoke.sh        # REST, incluido el ciclo bloqueante completo
bash scripts/smoke-cli.sh    # el cliente tal y como lo usará un agente
bash scripts/smoke-mcp.sh    # handshake MCP, catálogo y llamada real
bash scripts/smoke-ui.sh     # el panel y su flujo de eventos
```
