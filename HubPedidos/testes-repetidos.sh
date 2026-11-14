#!/usr/bin/env bash
# Roda a suíte N vezes, cada uma em ORDEM ALEATÓRIA com semente diferente, e resume.
# Uma falha informa a semente para reprodução: HUB_SEMENTE_TESTES=<semente> dotnet test
# Uso: ./testes-repetidos.sh [N=10]
set -uo pipefail
cd "$(dirname "$0")"
N=${1:-10}
dotnet build -c Release > /dev/null || { echo "build falhou"; exit 1; }
falhas=0
for i in $(seq 1 "$N"); do
  semente=$(( (RANDOM << 15 | RANDOM) + 1 ))
  saida=$(HUB_SEMENTE_TESTES=$semente dotnet test -c Release --no-build 2>&1)
  resumo=$(echo "$saida" | grep -E "Passed!|Failed!" | sed 's/ - HubPedidos.Tests.dll.*//')
  if echo "$saida" | grep -q "Failed!"; then
    falhas=$((falhas + 1))
    echo "execução $i  semente=$semente  FALHOU  $resumo"
    echo "$saida" | grep -E "^\s+Failed " | sed 's/^/    /'
  else
    echo "execução $i  semente=$semente  ok      $resumo"
  fi
done
echo "----"
echo "$N execuções, $falhas com falha."
[ "$falhas" -eq 0 ]
