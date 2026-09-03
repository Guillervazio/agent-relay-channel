# Instrucciones para los agentes

Pega esta sección en el `CLAUDE.md` (Claude Code) y en el `AGENTS.md` (Codex) del
repositorio en el que trabajen. Ajusta los nombres de agente a los tuyos.

---

## Comunicación con el otro agente

Trabajas en paralelo con otro agente, de un proveedor distinto y en otra máquina,
sobre un clon del mismo repositorio. Os comunicáis por ARC, no por ficheros.

| Agente | Máquina | Proveedor |
|---|---|---|
| `claude-pc1` | PC1 | Claude Code |
| `codex-pc2` | PC2 | Codex CLI |

### Cuándo escribir al otro

- **Pregunta (`arc_ask` / `arc ask`)** cuando necesites su respuesta para seguir:
  un contrato de API que él define, una decisión sobre código que él está tocando,
  confirmar un supuesto antes de construir sobre él. Bloquea hasta que conteste.
- **Aviso (`arc_note` / `arc note`)** cuando sólo informas de un hecho consumado:
  "he subido la rama", "cambié la firma de este método". No espera respuesta.
- **No escribas** para narrar tu progreso. El canal es para lo que el otro necesita
  saber, no para llevar un diario.

### Al empezar un turno

Mira el buzón antes de ponerte a trabajar: puede que el otro agente esté
bloqueado esperándote ahora mismo.

```bash
arc inbox
```

Si te llega una petición, contéstala antes de seguir con lo tuyo: cada minuto que
tardas es un minuto que el otro pasa parado.

```bash
arc respond req_1a2b3c --body-file respuesta.md
```

### Qué mandar en un mensaje

Ambas máquinas tienen el mismo repositorio. **Manda referencias, no contenido**:

```bash
arc ask --to codex-pc2 \
  --subject "Contrato del endpoint de pagos" \
  --body-file pregunta.md \
  --refs '{"branch":"feat/pagos","commit":"a1b2c3d","files":["src/pagos/Total.cs"]}' \
  --wait 180
```

Antes de citar código, sube tu rama: así el otro puede mirarlo en su propio clon.
El cuerpo está limitado a 256 KB.

### Escribir el cuerpo

Siempre por fichero, nunca en la línea de comandos: en Windows los argumentos
pasan por la codepage ANSI y los acentos se corrompen.

```bash
cat > pregunta.md <<'EOF'
¿El campo `total` viaja en céntimos o en euros?
Lo necesito para cerrar la validación del formulario.
EOF
arc ask --to codex-pc2 --body-file pregunta.md --wait 180
```

### Cuando la espera vence

No es un error. La petición sigue viva y el otro la verá en su buzón. Tienes dos
opciones y casi siempre conviene la primera:

1. Seguir con otra parte de tu trabajo y recoger la respuesta más tarde:
   `arc await req_1a2b3c --wait 300`.
2. Volver a esperar, si de verdad no puedes avanzar sin ella.

**Nunca os quedéis los dos esperando a la vez**: agotaríais ambos turnos sin que
nadie avance. Si vas a preguntar algo largo, avisa con `arc note` y sigue trabajando.

### Códigos de salida

Ramifica por el código, no por el texto:

| Código | Significado |
|---|---|
| `0` | Respondido / hay mensajes / operación correcta |
| `1` | Error de red o del hub |
| `2` | Uso incorrecto del comando |
| `3` | La espera venció sin respuesta |
| `4` | El buzón está vacío |

### Si el canal no responde

Comprueba `arc health`. Si el hub no está en pie, sigue con tu trabajo y deja
constancia de lo que habrías preguntado; no bloquees el turno reintentando.
