#!/usr/bin/env bash
# Verificación completa: unidades, y las tres superficies contra un hub real
# levantado y apagado por este mismo script.
#
#   ./scripts/test-all.sh
set -uo pipefail

cd "$(dirname "$0")/.."

. scripts/preflight.sh
require_python || exit 1
require_cmd curl 'hablar HTTP con el hub' || exit 1
require_cmd dotnet 'compilar y arrancar el hub' || exit 1

PORT="${ARC_TEST_PORT:-8791}"
export ARC_URL="http://127.0.0.1:$PORT"
export ARC_TOKEN="test-$(date +%s)-$RANDOM"
export ARC_URLS="$ARC_URL"
export ARC_DB="$(mktemp -d)/arc-test.db"

echo "### Pruebas unitarias ###"
# La salida entera a un fichero, y sólo el resumen a la vista. Un `| tail -3` deja
# el recuento y se come el nombre de lo que falló: si el fallo es intermitente,
# esa pasada es la única prueba que había y se pierde.
UNIT_LOG="$(dirname "$ARC_DB")/unit.log"
dotnet test --nologo -v q > "$UNIT_LOG" 2>&1
unit=$?
tail -3 "$UNIT_LOG"
if [ "$unit" -ne 0 ]; then
  echo
  echo "  Qué falló, que el resumen no dice:"
  grep -E "^\s*(Failed|Error Message|Assert\.|Expected|Actual)" "$UNIT_LOG" | head -30
  # Fuera del directorio que borra el trap: si el fallo no se repite, esta pasada
  # era la única prueba. `*.log` ya está en .gitignore.
  cp "$UNIT_LOG" ./unit-fail.log
  echo "  Registro completo: ./unit-fail.log"
fi

echo
echo "### Compilando el hub ###"
dotnet build src/Arc.Hub/Arc.Hub.csproj --nologo -v q > /dev/null || exit 1
dotnet build src/Arc.Cli/Arc.Cli.csproj --nologo -v q > /dev/null || exit 1

dotnet run --project src/Arc.Hub --no-launch-profile --no-build > "$ARC_DB.log" 2>&1 &
hub_pid=$!
trap 'kill "$hub_pid" 2>/dev/null; rm -rf "$(dirname "$ARC_DB")"' EXIT

for _ in $(seq 1 40); do
  curl -s -m 2 "$ARC_URL/healthz" > /dev/null 2>&1 && break
  sleep 1
done
if ! curl -s -m 2 "$ARC_URL/healthz" > /dev/null 2>&1; then
  echo "El hub no arrancó. Registro:"; cat "$ARC_DB.log"; exit 1
fi

failed=0
for suite in smoke smoke-cli smoke-mcp smoke-ui; do
  echo
  echo "### $suite ###"
  bash "scripts/$suite.sh" || failed=1
done

echo
if [ "$unit" -eq 0 ] && [ "$failed" -eq 0 ]; then
  echo "Todo en verde."
else
  echo "Hay fallos."
  exit 1
fi
