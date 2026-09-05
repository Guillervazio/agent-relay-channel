#!/usr/bin/env bash
# Prueba de humo del ciclo bloqueante: A pregunta y se queda esperando,
# B lo recibe por su buzón y contesta, A se despierta con la respuesta.
#
#   ARC_URL=http://127.0.0.1:8765 ./scripts/smoke.sh
#
# Los cuerpos se pasan SIEMPRE por fichero, nunca por argv: en Windows los
# argumentos de línea de comandos pasan por la codepage ANSI y corrompen el UTF-8.
set -uo pipefail

. "$(dirname "$0")/preflight.sh"
require_python || exit 1
require_cmd curl 'hablar HTTP con el hub' || exit 1

URL="${ARC_URL:-http://127.0.0.1:8765}"
TOKEN="${ARC_TOKEN:-}"
A="${ARC_A:-claude-pc1}"
B="${ARC_B:-codex-pc2}"
C="${ARC_C:-tercero-pc3}"
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
  "$PY" -c "
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

echo "== 6. Un tercero que sabe el identificador =="
# Los identificadores son 64 bits aleatorios y cada listado de hilos los reparte,
# así que conocer uno no puede ser lo que dé derecho a leerlo.
[ "$(curl -s -o /dev/null -w '%{http_code}' "$URL/v1/messages/$req_id" "${hdr[@]}" -H "X-ARC-Agent: $C")" = "404" ]
check "un mensaje ajeno es 404 para quien no es ninguno de sus dos extremos" $?

# Y el 404 entero, no sólo el código: un detalle distinto diría por la prosa que
# ese identificador existe, que es justo lo que el código de estado oculta.
ajeno=$(curl -s "$URL/v1/messages/$req_id" "${hdr[@]}" -H "X-ARC-Agent: $C")
inventado=$(curl -s "$URL/v1/messages/req_0000000000000000" "${hdr[@]}" -H "X-ARC-Agent: $C")
[ "$ajeno" = "$inventado" ]
check "el mensaje ajeno y el inexistente contestan lo mismo, palabra por palabra" $?

[ "$(curl -s -o /dev/null -w '%{http_code}' "$URL/v1/threads/$thread_id" "${hdr[@]}" -H "X-ARC-Agent: $C")" = "404" ]
check "un hilo en el que no apareces es 404" $?

# La otra mitad: cerrar la puerta no puede haber cerrado la de sus dueños.
[ "$(curl -s -o /dev/null -w '%{http_code}' "$URL/v1/messages/$req_id" "${hdr[@]}" -H "X-ARC-Agent: $B")" = "200" ]
check "el destinatario sigue leyendo su mensaje" $?

# El panel es un lector deliberado del canal entero sobre el mismo token: si esto
# dejara de ser cierto sin que nadie lo decidiera, esta suite tiene que enterarse.
curl -s "$URL/v1/observe/history" "${hdr[@]}" | grep -q "$req_id"
check "/v1/observe sigue viendo el canal entero, que es lo que es" $?

echo "== 7. Escribirse a uno mismo =="
# Un aviso a uno mismo siempre valio; una peticion, no, y nada decia por que.
# Ahora las dos valen y lo que se niega es la espera, que es lo unico que no
# podia terminar: el unico que podria contestar es el que esta bloqueado.
body '{"to":"'"$A"'","body":"revisar el pin de SQLitePCLRaw"}' propia.json
propia=$(curl -s -X POST "$URL/v1/requests?wait=0" "${hdr[@]}" -H "X-ARC-Agent: $A" --data-binary "@$WORK/propia.json")
[ "$(printf '%s' "$propia" | jget "['outcome']")" = "queued" ]
check "una peticion a uno mismo se encola" $?

propia_id=$(printf '%s' "$propia" | jget "['request_id']")
curl -s "$URL/v1/inbox/$A" "${hdr[@]}" -H "X-ARC-Agent: $A" | grep -q "$propia_id"
check "y llega al buzon del que la mando" $?

out=$(curl -s -w '
%{http_code}' -X POST "$URL/v1/requests?wait=5" "${hdr[@]}" -H "X-ARC-Agent: $A" --data-binary "@$WORK/propia.json")
[ "$(printf '%s' "$out" | tail -n1)" = "422" ] && printf '%s' "$out" | grep -q 'self_addressed'
check "pedir espera sobre uno mismo es 422, no un plazo que se agota" $?

# La otra puerta: si la negativa estuviera solo en el ask, encolar con 0 y
# esperar despues la esquivaria en dos llamadas.
out=$(curl -s -w '
%{http_code}' "$URL/v1/requests/$propia_id/response?wait=5" "${hdr[@]}" -H "X-ARC-Agent: $A")
[ "$(printf '%s' "$out" | tail -n1)" = "422" ] && printf '%s' "$out" | grep -q 'self_addressed'
check "y esperarla en una segunda llamada tampoco cuela" $?

# Contestarse a uno mismo siempre valio, y recoger esa respuesta no se niega:
# lo que no podia terminar era la espera, no la respuesta que ya existe.
body '{"body":"sigue vigente"}' propia-res.json
curl -s -o /dev/null -X POST "$URL/v1/requests/$propia_id/response" "${hdr[@]}" -H "X-ARC-Agent: $A" --data-binary "@$WORK/propia-res.json"
curl -s "$URL/v1/requests/$propia_id/response?wait=5" "${hdr[@]}" -H "X-ARC-Agent: $A" | grep -q 'sigue vigente'
check "una respuesta que uno se dio a si mismo se recoge sin negativa" $?

echo "== 8. Unas refs que no son un objeto =="
# El contrato prometia un objeto y nada lo comprobaba. Dice ya lo que el codigo
# hace, y esto es lo que no puede volver a romperse en silencio.
body '{"to":"'"$B"'","body":"revisa estos dos","refs":["src/x.cs","src/y.cs"]}' array-refs.json
curl -s -X POST "$URL/v1/requests?wait=0" "${hdr[@]}" -H "X-ARC-Agent: $A" --data-binary "@$WORK/array-refs.json" > "$WORK/array-ask.json"
[ -n "$(jget "['request_id']" < "$WORK/array-ask.json")" ]
check "unas refs que son un array se aceptan" $?

curl -s "$URL/v1/inbox/$B" "${hdr[@]}" -H "X-ARC-Agent: $B" > "$WORK/inbox-array.json"
grep -q 'src/y.cs' "$WORK/inbox-array.json"
check "y viajan intactas hasta el buzon del destinatario" $?

# Unas refs rotas rompen el cuerpo entero, asi que por REST son un 400 y no
# invalid_refs: esa es la forma del cable, no un olvido.
printf '%s' '{"to":"'"$B"'","body":"x","refs":{roto}}' > "$WORK/refs-rotas.json"
out=$(curl -s -w '
%{http_code}' -X POST "$URL/v1/requests?wait=0" "${hdr[@]}" -H "X-ARC-Agent: $A" --data-binary "@$WORK/refs-rotas.json")
[ "$(printf '%s' "$out" | tail -n1)" = "400" ] && printf '%s' "$out" | grep -q 'invalid_json'
check "unas refs ilegibles son un cuerpo ilegible: 400 invalid_json" $?

echo
echo "$pass correctas, $fail fallidas"
[ "$fail" -eq 0 ]
