#!/usr/bin/env bash
# Prueba de humo de la superficie MCP: handshake, catálogo de herramientas y una
# llamada real. Habla JSON-RPC sobre Streamable HTTP, que es lo que hace un cliente MCP.
#
#   ARC_URL=http://127.0.0.1:8765 ./scripts/smoke-mcp.sh
set -uo pipefail

URL="${ARC_URL:-http://127.0.0.1:8765}"
TOKEN="${ARC_TOKEN:-}"
A="${ARC_A:-claude-pc1}"
B="${ARC_B:-codex-pc2}"
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
python -c "
import json,io,sys
esperado = io.open(sys.argv[1], encoding='utf-8').read()
mensajes = json.load(io.open(sys.argv[2], encoding='utf-8'))['messages']
sys.exit(0 if any(m['body'] == esperado for m in mensajes) else 1)
" "$WORK/esperado.txt" "$WORK/inbox.json"
check "el texto con acentos cruza intacto la capa MCP" $?

echo "== 5. Identidad obligatoria también en MCP =="
SESSION=""
code=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$URL/mcp" \
  -H "Content-Type: application/json" -H "Accept: application/json, text/event-stream" \
  ${TOKEN:+-H "X-ARC-Token: $TOKEN"} --data-binary "@$WORK/init.json")
[ "$code" = "422" ]
check "sin X-ARC-Agent el servidor MCP rechaza la conexión" $?

echo
echo "$pass correctas, $fail fallidas"
[ "$fail" -eq 0 ]
