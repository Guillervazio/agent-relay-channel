#!/usr/bin/env bash
# Prueba de humo de la superficie MCP: handshake, catálogo de herramientas y una
# llamada real. Habla JSON-RPC sobre Streamable HTTP, que es lo que hace un cliente MCP.
#
#   ARC_URL=http://127.0.0.1:8765 ./scripts/smoke-mcp.sh
set -uo pipefail

. "$(dirname "$0")/preflight.sh"
require_python || exit 1
require_cmd curl 'hablar JSON-RPC con el hub' || exit 1

URL="${ARC_URL:-http://127.0.0.1:8765}"
TOKEN="${ARC_TOKEN:-}"
A="${ARC_A:-claude-pc1}"
B="${ARC_B:-codex-pc2}"
C="${ARC_C:-tercero-pc3}"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

pass=0; fail=0
check() {
  if [ "$2" -eq 0 ]; then echo "  ok    $1"; pass=$((pass+1));
  else echo "  FALLO $1"; fail=$((fail+1)); fi
}

# Streamable HTTP puede contestar en JSON o como event-stream; aceptamos ambos
# y nos quedamos con el payload JSON-RPC en cualquiera de los dos casos.
rpc() { # rpc <agente> <fichero-json> [fichero-cabeceras]
  local agent="$1" payload="$2" dump="${3:-/dev/null}"
  local args=(-s -m 60 -D "$dump" -X POST "$URL/mcp"
    -H "Content-Type: application/json"
    -H "Accept: application/json, text/event-stream"
    -H "X-ARC-Agent: $agent")
  [ -n "$TOKEN" ] && args+=(-H "X-ARC-Token: $TOKEN")
  [ -n "${SESSION:-}" ] && args+=(-H "Mcp-Session-Id: $SESSION")
  curl "${args[@]}" --data-binary "@$payload" | sed 's/^data: //' | grep -v '^event:' | grep -v '^$'
}

INIT='{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"smoke","version":"1.0"}}}'

echo "== 1. Handshake =="
printf '%s' "$INIT" > "$WORK/init.json"
SESSION=""
rpc "$A" "$WORK/init.json" "$WORK/init.headers" > "$WORK/init.out"
grep -q '"protocolVersion"' "$WORK/init.out"
check "initialize devuelve la versión del protocolo" $?

# Mcp-Session-Id es opcional en Streamable HTTP: en modo stateless no se emite.
SESSION=$(grep -i 'mcp-session-id' "$WORK/init.headers" | tr -d '\r' | awk '{print $2}')
MODO=$([ -n "$SESSION" ] && echo "con sesión" || echo "stateless")
grep -q '"serverInfo"' "$WORK/init.out"
check "el servidor se identifica en el handshake ($MODO)" $?

# El canal se explica a sí mismo: sin esto, cada proyecto tendría que pegar las
# mismas reglas en su propio CLAUDE.md y las copias se separarían.
grep -q '"instructions"' "$WORK/init.out"
check "el handshake trae las instrucciones del canal" $?

grep -q 'arc_inbox' "$WORK/init.out"
check "las instrucciones dicen que se mira el buzón al empezar" $?

printf '%s' '{"jsonrpc":"2.0","method":"notifications/initialized"}' > "$WORK/ready.json"
rpc "$A" "$WORK/ready.json" > /dev/null

echo "== 2. Catálogo de herramientas =="
printf '%s' '{"jsonrpc":"2.0","id":2,"method":"tools/list"}' > "$WORK/list.json"
rpc "$A" "$WORK/list.json" > "$WORK/list.out"

for tool in arc_ask arc_await arc_inbox arc_respond arc_note arc_thread arc_agents; do
  grep -q "\"$tool\"" "$WORK/list.out"
  check "publica $tool" $?
done

grep -q 'Bloquea hasta que conteste' "$WORK/list.out"
check "las descripciones explican cuándo usar cada herramienta" $?

echo "== 3. Llamada real: arc_note =="
# Vacía el buzón de B para que lo que midamos después sea de esta prueba.
curl -s -o /dev/null -m 10 "$URL/v1/inbox/$B" -H "X-ARC-Agent: $B" ${TOKEN:+-H "X-ARC-Token: $TOKEN"}

ACENTOS='Aviso desde MCP: compilación terminada, ¿reviso el diseño?'
cat > "$WORK/call.json" <<JSON
{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"arc_note","arguments":{"to":"$B","body":"$ACENTOS","subject":"Build"}}}
JSON
rpc "$A" "$WORK/call.json" > "$WORK/call.out"
grep -q 'Aviso enviado' "$WORK/call.out"
check "arc_note se ejecuta y confirma el envío" $?

echo "== 4. El aviso llegó de verdad al canal =="
curl -s -m 10 "$URL/v1/inbox/$B" -H "X-ARC-Agent: $B" ${TOKEN:+-H "X-ARC-Token: $TOKEN"} > "$WORK/inbox.json"
grep -q '"kind":"note"' "$WORK/inbox.json"
check "el aviso aparece en el buzón del destinatario" $?

# El texto esperado viaja por fichero: en argv, Windows lo pasa por la codepage
# ANSI y los acentos no sobreviven a la comparación.
printf '%s' "$ACENTOS" > "$WORK/esperado.txt"
"$PY" -c "
import json,io,sys
esperado = io.open(sys.argv[1], encoding='utf-8').read()
mensajes = json.load(io.open(sys.argv[2], encoding='utf-8'))['messages']
sys.exit(0 if any(m['body'] == esperado for m in mensajes) else 1)
" "$WORK/esperado.txt" "$WORK/inbox.json"
check "el texto con acentos cruza intacto la capa MCP" $?

echo "== 5. Un hilo que no es tuyo =="
thread_id=$("$PY" -c "
import json,io,sys
print(json.load(io.open(sys.argv[1], encoding='utf-8'))['messages'][0]['thread_id'])
" "$WORK/inbox.json")

# La identidad viaja por cabecera, pero la sesión es del cliente: C hace la suya.
SESSION=""
rpc "$C" "$WORK/init.json" "$WORK/init-c.headers" > /dev/null
SESSION=$(grep -i 'mcp-session-id' "$WORK/init-c.headers" | tr -d '' | awk '{print $2}')
rpc "$C" "$WORK/ready.json" > /dev/null

cat > "$WORK/thread-c.json" <<JSON
{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"arc_thread","arguments":{"threadId":"$thread_id"}}}
JSON
rpc "$C" "$WORK/thread-c.json" > "$WORK/thread-c.out"

# La herramienta contesta en prosa, así que es la prosa la que no puede delatar
# si el hilo existe: la misma frase que para un identificador inventado.
grep -q 'No existe el hilo' "$WORK/thread-c.out"
check "arc_thread le dice a un tercero lo mismo que de un hilo inventado" $?

! grep -q 'Aviso desde MCP' "$WORK/thread-c.out"
check "y no deja caer el cuerpo en el texto que lee el modelo" $?

echo "== 6. Las refs, en la única superficie que puede rechazarlas =="
# MCP recibe refs como cadena aparte y las parsea el hub, así que es la única
# que puede contestar invalid_refs. Por REST unas refs rotas son un cuerpo roto.
SESSION=""
rpc "$A" "$WORK/init.json" "$WORK/init-refs.headers" > /dev/null
SESSION=$(grep -i 'mcp-session-id' "$WORK/init-refs.headers" | tr -d '' | awk '{print $2}')
rpc "$A" "$WORK/ready.json" > /dev/null

cat > "$WORK/refs-rotas.json" <<'JSON'
{"jsonrpc":"2.0","id":7,"method":"tools/call","params":{"name":"arc_note","arguments":{"to":"codex-pc2","body":"rama subida","refs":"{roto"}}}
JSON
rpc "$A" "$WORK/refs-rotas.json" > "$WORK/refs-rotas.out"
grep -q 'invalid_refs' "$WORK/refs-rotas.out"
check "unas refs ilegibles se rechazan con su código" $?

# Esta es la que importa: sin el filtro, el SDK contesta "An error occurred
# invoking 'arc_note'" y el modelo se entera de que falló, no de por qué.
! grep -q 'An error occurred' "$WORK/refs-rotas.out"
check "y no con la frase genérica del SDK, que no dice nada" $?

! grep -q 'Aviso enviado' "$WORK/refs-rotas.out"
check "y el aviso no sale sin ellas, que era el fallo que importaba" $?

# El contrato prometía un objeto y nada lo comprobaba. Dice ya lo que el código
# hace: cualquier valor JSON, y el objeto como convención.
cat > "$WORK/refs-array.json" <<'JSON'
{"jsonrpc":"2.0","id":8,"method":"tools/call","params":{"name":"arc_note","arguments":{"to":"codex-pc2","body":"revisa estos dos","refs":"[\"src/x.cs\",\"src/y.cs\"]"}}}
JSON
rpc "$A" "$WORK/refs-array.json" > "$WORK/refs-array.out"
grep -q 'Aviso enviado' "$WORK/refs-array.out"
check "unas refs que son un array salen igual" $?

echo "== 7. Escribirse a uno mismo =="
# Un aviso a uno mismo siempre pudo; una petición, no. Ahora las dos pueden y lo
# que se niega es la espera, que es lo único que no podía terminar.
cat > "$WORK/propia.json" <<'JSON'
{"jsonrpc":"2.0","id":9,"method":"tools/call","params":{"name":"arc_ask","arguments":{"to":"claude-pc1","body":"revisar el pin","wait":0}}}
JSON
rpc "$A" "$WORK/propia.json" > "$WORK/propia.out"
grep -q 'req_' "$WORK/propia.out"
check "arc_ask a uno mismo con wait 0 encola la petición" $?

cat > "$WORK/propia-espera.json" <<'JSON'
{"jsonrpc":"2.0","id":10,"method":"tools/call","params":{"name":"arc_ask","arguments":{"to":"claude-pc1","body":"revisar el pin","wait":5}}}
JSON
rpc "$A" "$WORK/propia-espera.json" > "$WORK/propia-espera.out"
! grep -q 'req_' "$WORK/propia-espera.out"
check "y con espera se niega en vez de gastar el turno" $?

# Negar la espera sólo sirve si el que la pidió puede corregirse, y aquí el que
# la pide es un modelo: la negativa tiene que decirle qué hacer en su lugar.
grep -q 'self_addressed' "$WORK/propia-espera.out"
check "y le dice con qué código, no sólo que algo fue mal" $?

echo "== 8. Un aviso que se dio por entregado =="
# Lo que ve un modelo es texto, no un codigo de estado: la afirmacion es que el
# cuerpo esta en la redaccion que arc_inbox devuelve.
cat > "$WORK/aviso-perdido.json" <<'JSON'
{"jsonrpc":"2.0","id":11,"method":"tools/call","params":{"name":"arc_note","arguments":{"to":"recupera-mcp","body":"La clave está en el fichero"}}}
JSON
rpc "$A" "$WORK/aviso-perdido.json" > /dev/null

cat > "$WORK/lee.json" <<'JSON'
{"jsonrpc":"2.0","id":12,"method":"tools/call","params":{"name":"arc_inbox","arguments":{"wait":0}}}
JSON
rpc "recupera-mcp" "$WORK/lee.json" > "$WORK/lee.out"
grep -q 'La clave' "$WORK/lee.out"
check "arc_inbox entrega el aviso" $?

# Y a partir de aqui el buzon por defecto ya no lo tiene.
rpc "recupera-mcp" "$WORK/lee.json" > "$WORK/lee2.out"
! grep -q 'La clave' "$WORK/lee2.out"
check "y no lo vuelve a entregar" $?

cat > "$WORK/lee-unanswered.json" <<'JSON'
{"jsonrpc":"2.0","id":13,"method":"tools/call","params":{"name":"arc_inbox","arguments":{"wait":0,"unanswered":true}}}
JSON
rpc "recupera-mcp" "$WORK/lee-unanswered.json" > "$WORK/lee3.out"
! grep -q 'La clave' "$WORK/lee3.out"
check "unanswered tampoco: un aviso no se responde" $?

cat > "$WORK/lee-replay.json" <<'JSON'
{"jsonrpc":"2.0","id":14,"method":"tools/call","params":{"name":"arc_inbox","arguments":{"wait":0,"replay":60}}}
JSON
rpc "recupera-mcp" "$WORK/lee-replay.json" > "$WORK/lee4.out"
grep -q 'La clave' "$WORK/lee4.out"
check "con replay vuelve, y con su cuerpo" $?

# Una negativa que el modelo no puede leer no es una negativa: es lo que el
# incremento 08 encontro, y lo que este no puede volver a perder.
cat > "$WORK/lee-malo.json" <<'JSON'
{"jsonrpc":"2.0","id":15,"method":"tools/call","params":{"name":"arc_inbox","arguments":{"wait":0,"replay":90000}}}
JSON
rpc "recupera-mcp" "$WORK/lee-malo.json" > "$WORK/lee5.out"
grep -q 'invalid_replay' "$WORK/lee5.out"
check "y una ventana fuera de rango se lee como invalid_replay" $?

echo "== 9. Identidad obligatoria también en MCP =="
SESSION=""
code=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$URL/mcp" \
  -H "Content-Type: application/json" -H "Accept: application/json, text/event-stream" \
  ${TOKEN:+-H "X-ARC-Token: $TOKEN"} --data-binary "@$WORK/init.json")
[ "$code" = "422" ]
check "sin X-ARC-Agent el servidor MCP rechaza la conexión" $?

echo
echo "$pass correctas, $fail fallidas"
[ "$fail" -eq 0 ]
