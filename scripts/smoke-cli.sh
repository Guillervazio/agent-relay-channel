#!/usr/bin/env bash
# Prueba de humo del cliente: el mismo ciclo bloqueante, pero visto como lo
# usará un agente — invocando `arc` y ramificando por el código de salida.
#
#   ARC_URL=http://127.0.0.1:8765 ./scripts/smoke-cli.sh [ruta/al/cliente]
set -uo pipefail

. "$(dirname "$0")/preflight.sh"
require_python || exit 1

# El binario no se llama igual en los dos sistemas: `arc.exe` en Windows, `arc` en
# Linux. Este script no había corrido nunca fuera del primero.
out="$(dirname "$0")/../src/Arc.Cli/bin/Debug/net10.0"
ARC="${1:-}"
if [ -z "$ARC" ]; then
  if [ -x "$out/arc.exe" ]; then ARC="$out/arc.exe"; else ARC="$out/arc"; fi
fi
export ARC_URL="${ARC_URL:-http://127.0.0.1:8765}"
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
jget() {
  "$PY" -c "
import json,sys
sys.stdout.reconfigure(encoding='utf-8')
raw = sys.stdin.buffer.read().decode('utf-8')
d = json.loads(raw) if raw.strip() else None
print(eval('d'+sys.argv[1]) if d is not None else '')
" "$1" 2>/dev/null
}

[ -x "$ARC" ] || { echo "No encuentro el cliente en $ARC"; exit 1; }

PREGUNTA='¿El campo "total" viaja en céntimos? Añádelo al contrato.'
RESPUESTA='Sí: entero en céntimos. Definición en src/pagos/Total.cs:42'
printf '%s' "$PREGUNTA" > "$WORK/pregunta.md"
printf '%s' "$RESPUESTA" > "$WORK/respuesta.md"

echo "== 1. Diagnóstico =="
ARC_AGENT="$A" "$ARC" health | grep -qE '"status": ?"ok"'
check "arc health alcanza el hub" $?

ARC_AGENT="$A" "$ARC" ask --body "x" 2>/dev/null; [ $? -eq 2 ]
check "faltar --to devuelve código 2 (uso incorrecto)" $?

echo "== 2. Ciclo bloqueante =="
# Vacía el buzón de B para que el mensaje que leamos sea el de esta prueba.
ARC_AGENT="$B" "$ARC" inbox > /dev/null 2>&1

( ARC_AGENT="$A" ARC_PROVIDER=claude-code "$ARC" ask --to "$B" \
    --subject "Contrato de pagos" --body-file "$WORK/pregunta.md" --wait 60 \
    --refs '{"branch":"feat/pagos","commit":"a1b2c3d"}' > "$WORK/ask.txt" 2>&1
  echo $? > "$WORK/ask.code" ) &
ask_pid=$!

ARC_AGENT="$B" ARC_PROVIDER=codex "$ARC" inbox --wait 45 --json > "$WORK/inbox.json"
inbox_code=$?
[ "$inbox_code" -eq 0 ]
check "arc inbox devuelve 0 cuando hay correo" $?

req_id=$(jget "['messages'][0]['id']" < "$WORK/inbox.json")
[ -n "$req_id" ]
check "el buzón entrega la petición ($req_id)" $?

[ "$(jget "['messages'][0]['body']" < "$WORK/inbox.json")" = "$PREGUNTA" ]
check "el texto con acentos sobrevive al viaje de ida" $?

ARC_AGENT="$B" "$ARC" respond "$req_id" --body-file "$WORK/respuesta.md" > /dev/null
check "arc respond entrega la respuesta" $?

wait "$ask_pid"
[ "$(cat "$WORK/ask.code")" = "0" ]
check "arc ask devuelve 0 al ser respondido" $?

grep -q "Total.cs:42" "$WORK/ask.txt"
check "A imprime el cuerpo de la respuesta" $?

grep -qF "céntimos" "$WORK/ask.txt"
check "el texto con acentos sobrevive al viaje de vuelta" $?

echo "== 3. Códigos de salida para ramificar =="
ARC_AGENT="$A" "$ARC" ask --to "$B" --body-file "$WORK/pregunta.md" --wait 2 > "$WORK/late.txt" 2>&1
[ $? -eq 3 ]
check "espera expirada devuelve código 3" $?

late_id=$(grep -oE 'req_[0-9a-f]+' "$WORK/late.txt" | head -1)
ARC_AGENT="$A" "$ARC" inbox > /dev/null 2>&1
ARC_AGENT="$A" "$ARC" inbox; [ $? -eq 4 ]
check "buzón vacío devuelve código 4" $?

# Y la petición expirada se puede retomar cuando el otro conteste.
( sleep 2; ARC_AGENT="$B" "$ARC" respond "$late_id" --body-file "$WORK/respuesta.md" > /dev/null 2>&1 ) &
ARC_AGENT="$A" "$ARC" await "$late_id" --wait 30 > "$WORK/await.txt" 2>&1
[ $? -eq 0 ] && grep -q "Total.cs:42" "$WORK/await.txt"
check "arc await recupera una respuesta tardía" $?
wait

echo "== 4. Unas refs que no se pueden leer no salen en silencio =="
# El fallo era que el mensaje se enviaba igual, sin rama ni commit, con código 0:
# el agente creía haberlos mandado.
before=$(ARC_AGENT="$B" "$ARC" inbox --json 2>/dev/null | grep -c 'req_' || true)

ARC_AGENT="$A" "$ARC" ask --to "$B" --body-file "$WORK/pregunta.md" --refs '{roto' --wait 0 > /dev/null 2>&1
[ $? -eq 2 ]
check "un --refs mal formado devuelve código 2" $?

ARC_AGENT="$A" "$ARC" ask --to "$B" --body-file "$WORK/pregunta.md" --refs-file "$WORK/no-existe.json" --wait 0 > /dev/null 2>&1
[ $? -eq 2 ]
check "un --refs-file inexistente devuelve código 2" $?

after=$(ARC_AGENT="$B" "$ARC" inbox --json 2>/dev/null | grep -c 'req_' || true)
[ "$before" = "$after" ]
check "ninguno de los dos llegó a enviarse" $?

echo "== 5. El hilo, visto por quien no está en él =="
thread_id=$(jget "['messages'][0]['thread_id']" < "$WORK/inbox.json")

ARC_AGENT="$B" "$ARC" thread "$thread_id" > "$WORK/hilo-b.txt" 2>&1
[ $? -eq 0 ] && grep -q "céntimos" "$WORK/hilo-b.txt"
check "el destinatario lee el hilo y ve los cuerpos" $?

# Un 404 llega al agente como código 1 con el motivo por stderr, no como un hilo
# vacío que se confundiría con una conversación que aún no ha empezado.
ARC_AGENT="$C" "$ARC" thread "$thread_id" > "$WORK/hilo-c.txt" 2>&1
[ $? -eq 1 ]
check "un hilo ajeno devuelve código 1, no un hilo vacío con código 0" $?

grep -q "not_found" "$WORK/hilo-c.txt" && ! grep -q "céntimos" "$WORK/hilo-c.txt"
check "y no deja caer ningún cuerpo por el camino" $?

echo "== 6. Escribirse a uno mismo =="
# Los códigos de salida son contrato, y esta es la pregunta que un agente hace
# con ellos. Un ask con --wait 0 sale con 3 sea a quien sea: 3 no es un fallo,
# significa que la petición sigue viva en un buzón. Aquí, en el propio.
ARC_AGENT="$A" "$ARC" ask --to "$A" --body-file "$WORK/pregunta.md" --wait 0 > "$WORK/propia.txt" 2>&1
[ $? -eq 3 ]
check "una petición a uno mismo con --wait 0 sale con 3, como cualquier otra encolada" $?

propia_id=$(grep -o 'req_[0-9a-f]*' "$WORK/propia.txt" | head -n1)
ARC_AGENT="$A" "$ARC" inbox --json 2>/dev/null | grep -q "$propia_id"
check "y esa misma petición está en el buzón de quien la mandó" $?

# Con espera es un 422 del hub, que el CLI traduce a 1. No es 3: no llegó a
# haber espera, y un agente que confundiera los dos volvería a intentarlo.
ARC_AGENT="$A" "$ARC" ask --to "$A" --body-file "$WORK/pregunta.md" --wait 5 > "$WORK/propia-espera.txt" 2>&1
[ $? -eq 1 ]
check "pedir espera sobre uno mismo es 1, no el 3 de un plazo agotado" $?

grep -q "self_addressed" "$WORK/propia-espera.txt"
check "y dice cuál fue el motivo" $?

echo "== 7. Un aviso que se dio por entregado =="
# El codigo de salida es el contrato del CLI, asi que lo que se afirma aqui es
# la pareja: 4 cuando no hay nada, 0 cuando la ventana lo devuelve. Un agente
# ramifica sobre eso y no sobre el texto.
D="recupera-cli"
printf '%s' 'La clave está en el fichero, con acentos: ñáéíóú' > "$WORK/aviso.md"
ARC_AGENT="$A" "$ARC" note --to "$D" --body-file "$WORK/aviso.md" > /dev/null 2>&1

ARC_AGENT="$D" "$ARC" inbox > "$WORK/primera.txt" 2>&1
[ $? -eq 0 ] && grep -q 'ñáéíóú' "$WORK/primera.txt"
check "el aviso llega, con sus acentos" $?

# La respuesta perdida: la lectura ocurrio y su resultado no llego a ninguna
# parte. Para el buzon por defecto el aviso ya no existe.
ARC_AGENT="$D" "$ARC" inbox > /dev/null 2>&1; [ $? -eq 4 ]
check "y el buzon por defecto queda vacio: sale con 4" $?

ARC_AGENT="$D" "$ARC" inbox --unanswered > /dev/null 2>&1; [ $? -eq 4 ]
check "tambien con --unanswered, que no alcanza a un aviso" $?

ARC_AGENT="$D" "$ARC" inbox --replay 60 > "$WORK/replay.txt" 2>&1
[ $? -eq 0 ] && grep -q 'ñáéíóú' "$WORK/replay.txt"
check "con --replay 60 sale con 0 y trae el cuerpo entero" $?

# El CLI reenvia el valor tal cual: si lo filtrara aqui, habria dos sitios que
# opinan sobre el rango y solo uno publicado.
ARC_AGENT="$D" "$ARC" inbox --replay 90000 > "$WORK/replay-malo.txt" 2>&1
[ $? -eq 1 ]
check "una ventana fuera de rango es 1, y no el 4 de un buzon vacio" $?

grep -q 'invalid_replay' "$WORK/replay-malo.txt"
check "y dice cual fue el motivo" $?

echo
echo "$pass correctas, $fail fallidas"
[ "$fail" -eq 0 ]
