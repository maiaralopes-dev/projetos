#!/usr/bin/env bash
# Experimento reproduzível: API nova + banco novo, dotnet-counters gravando durante a carga.
# Uso: [Banco__ModoWal=false] carga/medir.sh <rotulo> <taxa-criacao/s> <taxa-consulta/s> <dir-saida> [--trace]
#   Variáveis de ambiente (ex.: Banco__ModoWal) são repassadas para a API.
#   --trace: coleta também um trace curto (10 s) no meio da janela de medição.
set -euo pipefail
RAIZ=$(cd "$(dirname "$0")/.." && pwd)
ROTULO=$1; TC=$2; TQ=$3; SAIDA=$(realpath -m "$4"); TRACE=${5:-}
mkdir -p "$SAIDA"
TMP=$(mktemp -d)
"$RAIZ/carga/iniciar-api.sh" "$TMP/carga.db" > "$SAIDA/api-$ROTULO.log" 2>&1 &
for _ in $(seq 1 180); do curl -s localhost:5080/processadores > /dev/null 2>&1 && break; sleep 1; done
PID=$(pgrep -f "bin/Release/net10.0/HubPedidos.Api.dll" | head -1)
cd "$RAIZ"
dotnet build -c Release carga/HubPedidos.Carga > /dev/null

dotnet dotnet-counters collect --process-id "$PID" --refresh-interval 1 --format csv \
  --counters System.Runtime,Microsoft.AspNetCore.Hosting,Microsoft.AspNetCore.Server.Kestrel,HubPedidos.Fila \
  --output "$SAIDA/counters-$ROTULO.csv" > /dev/null 2>&1 &
CONT=$!

if [ "$TRACE" = "--trace" ]; then
  ( sleep 18; dotnet dotnet-trace collect --process-id "$PID" --duration 00:00:00:10 \
      --output "$SAIDA/trace-$ROTULO.nettrace" > "$SAIDA/trace-$ROTULO.log" 2>&1 ) &
fi

dotnet run -c Release --no-build --project carga/HubPedidos.Carga -- --aquecimento 10 --duracao 30 \
  --taxa-criacao "$TC" --taxa-consulta "$TQ" --rotulo "$ROTULO" --saida "$SAIDA"

sleep 15  # garante que o trace (se houver) terminou de ser gravado
pkill -f "dotnet-counters collect --process-id $PID" 2> /dev/null || true
kill "$PID" 2> /dev/null || true; sleep 2; rm -rf "$TMP"
