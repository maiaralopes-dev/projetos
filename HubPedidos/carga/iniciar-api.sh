#!/usr/bin/env bash
# Sobe a API para os testes de carga: Release, ambiente Production, log em Warning
# (o log de SQL do Development distorceria a medição) e banco SQLite NOVO com 20.000 pedidos.
# Uso: carga/iniciar-api.sh [arquivo.db]
set -euo pipefail
RAIZ=$(cd "$(dirname "$0")/.." && pwd)
DB=${1:-/tmp/hub-carga.db}
rm -f "$DB" "$DB-wal" "$DB-shm" "$DB-journal"
dotnet build -c Release "$RAIZ/src/HubPedidos.Api" > /dev/null
cd "$RAIZ/src/HubPedidos.Api"
export ASPNETCORE_ENVIRONMENT=Production
export ASPNETCORE_URLS=http://localhost:5080
export ConnectionStrings__Hub="Data Source=$DB"
export Banco__SemearPedidos=20000
export Logging__LogLevel__Default=Warning
exec dotnet bin/Release/net10.0/HubPedidos.Api.dll
