#!/usr/bin/env bash
# Prueba de humo del ciclo bloqueante: A pregunta y se queda esperando,
# B lo recibe por su buzón y contesta, A se despierta con la respuesta.
#
#   ARC_URL=http://127.0.0.1:8765 ./scripts/smoke.sh
#
# Los cuerpos se pasan SIEMPRE por fichero, nunca por argv: en Windows los
# argumentos de línea de comandos pasan por la codepage ANSI y corrompen el UTF-8.
set -uo pipefail

URL="${ARC_URL:-http://127.0.0.1:8765}"
TOKEN="${ARC_TOKEN:-}"
A="${ARC_A:-claude-pc1}"
B="${ARC_B:-codex-pc2}"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

hdr=(-H "Content-Type: application/json; charset=utf-8")
[ -n "$TOKEN" ] && hdr+=(-H "X-ARC-Token: $TOKEN")

pass=0; fail=0
check() {
  if [ "$2" -eq 0 ]; then echo "  ok    $1"; pass=$((pass+1));
  else echo "  FALLO $1"; fail=$((fail+1)); fi
}

# Extrae un campo de un JSON en stdin. Ruta en sintaxis de índices de Python.
jget() {
  python -c "
import json,sys
sys.stdout.reconfigure(encoding='utf-8')
raw = sys.stdin.buffer.read().decode('utf-8')
d = json.loads(raw) if raw.strip() else None
print(eval('d'+sys.argv[1]) if d is not None else '')
" "$1" 2>/dev/null
}

body() { printf '%s' "$1" > "$WORK/$2"; }

PREGUNTA='¿El campo total viene en céntimos o en euros?'
RESPUESTA='Entero en céntimos. Definición en src/pagos/Total.cs:42'

echo "== 1. El hub responde =="
curl -s -m 5 "$URL/healthz" | grep -qE '"status": ?"ok"'
check "/healthz devuelve ok" $?

# Un manejador cuyo único parámetro es HttpContext encaja con la forma de
# RequestDelegate y ASP.NET descarta lo que devuelva: 200 con el cuerpo vacío.
# Aquí se comprueba que el cuerpo llega de verdad, no sólo el código de estado.
curl -s -m 5 "$URL/v1/agents" "${hdr[@]}" -H "X-ARC-Agent: $A" | grep -q '"id"'
check "/v1/agents devuelve la lista, no un cuerpo vacío" $?

echo "== 2. Ciclo petición/respuesta bloqueante =="
body '{"to":"'"$B"'","subject":"Contrato del endpoint","body":"'"$PREGUNTA"'","refs":{"branch":"feat/pagos","commit":"a1b2c3d"}}' ask-body.json

# A pregunta en segundo plano y se queda esperando hasta 60 s.
curl -s -m 90 -X POST "$URL/v1/requests?wait=60" "${hdr[@]}" -H "X-ARC-Agent: $A" \
  --data-binary "@$WORK/ask-body.json" > "$WORK/ask.json" &
ask_pid=$!

# B espera en su buzón; el long-poll debe entregarle la petición sin sondeo.
curl -s -m 60 "$URL/v1/inbox/$B?wait=45" "${hdr[@]}" -H "X-ARC-Agent: $B" > "$WORK/inbox.json"
grep -qE '"kind": ?"request"' "$WORK/inbox.json"
check "B recibe la petición por long-poll" $?

req_id=$(jget "['messages'][0]['id']" < "$WORK/inbox.json")
[ -n "$req_id" ]
check "la petición trae id ($req_id)" $?

# Regresión: los agentes escriben en español, el texto debe volver byte a byte.
[ "$(jget "['messages'][0]['body']" < "$WORK/inbox.json")" = "$PREGUNTA" ]
check "el cuerpo con acentos llega intacto" $?

[ "$(jget "['messages'][0]['refs']['branch']" < "$WORK/inbox.json")" = "feat/pagos" ]
check "las refs del repositorio viajan con el mensaje" $?

# B contesta: esto debe despertar a A.
body '{"body":"'"$RESPUESTA"'"}' resp-body.json
curl -s -m 10 -X POST "$URL/v1/requests/$req_id/response" "${hdr[@]}" -H "X-ARC-Agent: $B" \
  --data-binary "@$WORK/resp-body.json" > "$WORK/resp.json"
grep -qE '"kind": ?"response"' "$WORK/resp.json"
check "B publica la respuesta" $?

wait "$ask_pid"
grep -qE '"outcome": ?"answered"' "$WORK/ask.json"
check "A se despierta con outcome=answered" $?

[ "$(jget "['response']['body']" < "$WORK/ask.json")" = "$RESPUESTA" ]
check "A recibe el cuerpo de la respuesta" $?

thread_id=$(jget "['thread_id']" < "$WORK/ask.json")
[ "$(curl -s "$URL/v1/threads/$thread_id" "${hdr[@]}" -H "X-ARC-Agent: $A" | jget "[1]['kind']")" = "response" ]
check "el hilo agrupa petición y respuesta" $?

echo "== 3. Expiración =="
body '{"to":"'"$B"'","body":"Nadie va a contestar esto"}' timeout-body.json
start=$(date +%s)
curl -s -m 30 -X POST "$URL/v1/requests?wait=3" "${hdr[@]}" -H "X-ARC-Agent: $A" \
  --data-binary "@$WORK/timeout-body.json" > "$WORK/timeout.json"
elapsed=$(( $(date +%s) - start ))
grep -qE '"outcome": ?"timeout"' "$WORK/timeout.json"
check "sin respondedor devuelve outcome=timeout" $?
[ "$elapsed" -ge 3 ] && [ "$elapsed" -lt 15 ]
check "la espera dura lo pedido (${elapsed}s)" $?

stale=$(jget "['request_id']" < "$WORK/timeout.json")
curl -s -m 10 "$URL/v1/inbox/$B" "${hdr[@]}" -H "X-ARC-Agent: $B" | grep -q "$stale"
check "la petición expirada sigue recuperable en el buzón de B" $?

# Reanudar la espera de una petición ya expirada.
body '{"body":"Respondida tarde"}' late-body.json
curl -s -m 10 -X POST "$URL/v1/requests/$stale/response" "${hdr[@]}" -H "X-ARC-Agent: $B" \
  --data-binary "@$WORK/late-body.json" > /dev/null
curl -s -m 10 "$URL/v1/requests/$stale/response" "${hdr[@]}" -H "X-ARC-Agent: $A" \
  | grep -qE '"outcome": ?"answered"'
check "A puede recoger después una respuesta tardía" $?

echo "== 4. Buzón vacío y espera real =="
start=$(date +%s)
code=$(curl -s -o /dev/null -w '%{http_code}' -m 30 "$URL/v1/inbox/sonda-vacia?wait=4" "${hdr[@]}" -H "X-ARC-Agent: sonda-vacia")
waited=$(( $(date +%s) - start ))
[ "$code" = "204" ] && [ "$waited" -ge 4 ]
check "el buzón vacío espera de verdad y devuelve 204 (${waited}s)" $?

echo "== 5. Reglas de acceso =="
[ "$(curl -s -o /dev/null -w '%{http_code}' "$URL/v1/inbox/$B" "${hdr[@]}" -H "X-ARC-Agent: $A")" = "403" ]
check "un agente no puede leer el buzón ajeno" $?

body '{"to":"'"$B"'","body":""}' empty.json
[ "$(curl -s -o /dev/null -w '%{http_code}' -X POST "$URL/v1/requests" "${hdr[@]}" -H "X-ARC-Agent: $A" --data-binary "@$WORK/empty.json")" = "422" ]
check "se rechaza un cuerpo vacío" $?

body '{"to":"'"$B"'","body":"x"}' anon.json
[ "$(curl -s -o /dev/null -w '%{http_code}' -X POST "$URL/v1/requests" "${hdr[@]}" --data-binary "@$WORK/anon.json")" = "422" ]
check "se rechaza una petición sin identidad" $?

body '{"body":"otra vez"}' dup.json
[ "$(curl -s -o /dev/null -w '%{http_code}' -X POST "$URL/v1/requests/$req_id/response" "${hdr[@]}" -H "X-ARC-Agent: $B" --data-binary "@$WORK/dup.json")" = "409" ]
check "no se puede responder dos veces a la misma petición" $?

body '{"body":"no me toca"}' wrong.json
[ "$(curl -s -o /dev/null -w '%{http_code}' -X POST "$URL/v1/requests/$stale/response" "${hdr[@]}" -H "X-ARC-Agent: $A" --data-binary "@$WORK/wrong.json")" = "403" ]
check "sólo el destinatario puede responder" $?

printf '%s' '{"to":"x", esto no es json}' > "$WORK/broken.json"
curl -s -X POST "$URL/v1/requests" "${hdr[@]}" -H "X-ARC-Agent: $A" --data-binary "@$WORK/broken.json" | grep -q 'invalid_json'
check "un JSON malformado explica el motivo, no un 400 mudo" $?

# La distinción que separa 400 de 422: esto no se pudo leer, no es una regla incumplida.
[ "$(curl -s -o /dev/null -w '%{http_code}' -X POST "$URL/v1/requests" "${hdr[@]}" -H "X-ARC-Agent: $A" --data-binary "@$WORK/broken.json")" = "400" ]
check "un cuerpo ilegible sigue siendo 400, no 422" $?

# Una espera fuera de rango se rechaza en vez de recortarse: una espera acortada
# en silencio vuelve indistinguible de un plazo agotado de verdad.
body '{"to":"'"$B"'","body":"x"}' longwait.json
out=$(curl -s -w '\n%{http_code}' -X POST "$URL/v1/requests?wait=999999" "${hdr[@]}" -H "X-ARC-Agent: $A" --data-binary "@$WORK/longwait.json")
[ "$(printf '%s' "$out" | tail -n1)" = "422" ] && printf '%s' "$out" | grep -q 'invalid_wait'
check "un 'wait' fuera de rango se rechaza, no se recorta" $?

# Y no deja la petición creada por el camino.
[ "$(curl -s "$URL/v1/inbox/$B" "${hdr[@]}" -H "X-ARC-Agent: $B" | grep -c '"x"')" = "0" ]
check "una petición rechazada por el 'wait' no llega al buzón" $?

echo
echo "$pass correctas, $fail fallidas"
[ "$fail" -eq 0 ]
