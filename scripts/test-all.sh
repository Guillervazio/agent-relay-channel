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
dotnet test --nologo -v q 2>&1 | tail -3
unit=${PIPESTATUS[0]}

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
