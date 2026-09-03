#!/usr/bin/env bash
# Prueba de humo del panel: la página se sirve sin token, los datos no, y el
# flujo de eventos empuja un mensaje nuevo sin que nadie lo pida.
#
#   ARC_URL=http://127.0.0.1:8765 ./scripts/smoke-ui.sh
set -uo pipefail

URL="${ARC_URL:-http://127.0.0.1:8765}"
TOKEN="${ARC_TOKEN:-}"
A="${ARC_A:-claude-pc1}"
B="${ARC_B:-codex-pc2}"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

hdr=(-H "Content-Type: application/json; charset=utf-8")
obs=()
if [ -n "$TOKEN" ]; then
  hdr+=(-H "X-ARC-Token: $TOKEN")
  obs+=(-H "X-ARC-Token: $TOKEN")
fi

pass=0; fail=0
check() {
  if [ "$2" -eq 0 ]; then echo "  ok    $1"; pass=$((pass+1));
  else echo "  FALLO $1"; fail=$((fail+1)); fi
}

status() { curl -s -m 5 -o /dev/null -w '%{http_code}' "$@"; }

echo "== 1. La página se sirve sola =="
[ "$(status "$URL/ui")" = "200" ]
check "/ui responde sin token: es una cáscara sin datos dentro" $?

curl -s -m 5 "$URL/ui" | grep -q '/v1/observe/stream'
check "la página trae el guion del panel, no una plantilla vacía" $?

echo "== 2. Los datos sí piden token =="
if [ -n "$TOKEN" ]; then
  [ "$(status "$URL/v1/observe/history")" = "401" ]
  check "el historial sin token devuelve 401" $?
else
  echo "  --    hub anónimo: no hay token que comprobar"
fi

[ "$(status "${obs[@]}" "$URL/v1/observe/history")" = "200" ]
check "el historial con token devuelve 200" $?

# El panel no es un agente: mira el canal entero sin cabecera de identidad, y no
# debe aparecer en el registro ni alterar el estado de nadie.
curl -s -m 5 "${obs[@]}" "$URL/v1/observe/history" | grep -q '"messages"'
check "el historial no exige X-ARC-Agent" $?

echo "== 3. El flujo empuja lo que ocurre =="
# El lector arranca antes que el mensaje: es exactamente lo que hace el navegador.
curl -s -N -m 12 "${obs[@]}" "$URL/v1/observe/stream" > "$WORK/stream.txt" &
stream_pid=$!
sleep 2

printf '%s' '{"to":"'"$B"'","subject":"Panel","body":"Aviso de prueba para el panel: acentuación intacta."}' > "$WORK/note.json"
curl -s -m 5 -X POST "$URL/v1/notes" "${hdr[@]}" -H "X-ARC-Agent: $A" \
  --data-binary "@$WORK/note.json" > "$WORK/note-out.json"

sleep 3
kill "$stream_pid" 2>/dev/null
wait "$stream_pid" 2>/dev/null

grep -q '^event: message$' "$WORK/stream.txt"
check "el mensaje sale por el flujo sin que nadie lo pida" $?

# Dos cosas de una vez: que cada data: quepa en una sola línea —un salto dentro
# partiría el evento y el navegador leería JSON truncado— y que el cuerpo llegue
# entero. Se comprueba sobre el JSON ya parseado: el serializador escapa los
# acentos, así que buscarlos en crudo daría un falso negativo.
python - "$WORK/stream.txt" <<'PY'
import json, sys

lines = open(sys.argv[1], encoding='utf-8').read().splitlines()
data = [line[5:].strip() for line in lines if line.startswith('data:')]
if not data:
    sys.exit(1)

bodies = []
for raw in data:
    payload = json.loads(raw)           # falla si el evento venía partido en dos
    if payload.get('message'):
        bodies.append(payload['message']['body'])

sys.exit(0 if any('acentuación intacta' in body for body in bodies) else 1)
PY
check "cada data: es un JSON completo y el cuerpo llega intacto" $?

grep -q '^event: state$' "$WORK/stream.txt"
check "el flujo publica el estado del canal (agentes y esperas)" $?

echo "== 4. Se puede elegir una conversación =="
if [ -n "$TOKEN" ]; then
  [ "$(status "$URL/v1/observe/threads")" = "401" ]
  check "el índice de conversaciones sin token devuelve 401" $?
fi

curl -s -m 5 "${obs[@]}" "$URL/v1/observe/threads" > "$WORK/threads.json"

# El aviso de arriba abrió su propio hilo. Tiene que salir en el índice y, como a un
# aviso no le contesta nadie, tiene que salir ya terminado.
python - "$WORK/threads.json" "$WORK/note-out.json" <<'PY'
import json, sys

threads = json.load(open(sys.argv[1], encoding='utf-8'))
note = json.load(open(sys.argv[2], encoding='utf-8'))

mine = [t for t in threads if t['thread_id'] == note['thread_id']]
if len(mine) != 1:
    sys.exit(1)

thread = mine[0]
sys.exit(0 if thread['closed']
              and thread['open_requests'] == 0
              and thread['subject'] == note['subject']
              and note['from'] in thread['participants']
              and note['to'] in thread['participants'] else 1)
PY
check "el hilo del aviso sale terminado y con sus dos agentes" $?

THREAD="$(python -c "import json,sys; print(json.load(open(sys.argv[1], encoding='utf-8'))['thread_id'])" "$WORK/note-out.json")"

# Elegir una conversación en el panel es esto: el mismo historial, acotado a un hilo.
curl -s -m 5 "${obs[@]}" "$URL/v1/observe/history?thread=$THREAD" > "$WORK/one.json"

python - "$WORK/one.json" "$THREAD" <<'PY'
import json, sys

messages = json.load(open(sys.argv[1], encoding='utf-8'))['messages']
sys.exit(0 if messages and all(m['thread_id'] == sys.argv[2] for m in messages) else 1)
PY
check "el historial acotado a un hilo no trae nada de fuera" $?

echo
echo "$pass correctas, $fail fallidas"
[ "$fail" -eq 0 ]
