#!/usr/bin/env bash
# Evidências da Atividade 4 (API REST v2, Problem Details, OpenAPI, cliente TypeScript).
set -u
source scripts/evid.sh
E=docs/evidencias/atividade4; mkdir -p $E; B=https://localhost:7080; A=$B/api/v2/tarefas
scripts/resetar-banco.sh > /dev/null
scripts/servidor.sh start $E/log-servidor.txt
{
req "Listagem padrão (sucesso, com X-Total-Count e Link)" "$A?tamanho=3"
req "Página vazia (além do fim)" "$A?pagina=99"
req "Limite mínimo violado: tamanho=0" "$A?tamanho=0"
req "Limite máximo violado: tamanho=51" "$A?tamanho=51"
req "Limite mínimo violado: pagina=0" "$A?pagina=0"
req "Limite máximo aceito: tamanho=50" "$A?tamanho=50&ordenarPor=id&direcao=desc"
req "Ordenação por campo permitido (prazo asc)" "$A?ordenarPor=prazo&tamanho=4"
req "Ordenação por campo NÃO permitido" "$A?ordenarPor=versao"
req "Combinação de dois filtros (situacao + prioridade)" "$A?situacao=Pendente&prioridade=Alta"
req "Combinação de filtros (situacao + prazoAte)" "$A?situacao=Pendente&prazoAte=2026-11-20"
req "Valor de enum inválido no filtro" "$A?situacao=Arquivada"
req "GET por id existente" $A/5
req "GET por id inexistente (404)" $A/999
req "POST válido (201 + Location)" -X POST -H "Content-Type: application/json" -d '{"titulo":"Documentar API REST","prioridade":"Media","prazo":"2026-12-01"}' $A
req "POST inválido (título curto + Alta sem prazo)" -X POST -H "Content-Type: application/json" -d '{"titulo":"ab","prioridade":"Alta"}' $A
req "POST regra de negócio (Alta sem prazo)" -X POST -H "Content-Type: application/json" -d '{"titulo":"Urgente sem prazo","prioridade":"Alta"}' $A
req "POST conflito (título duplicado → 409)" -X POST -H "Content-Type: application/json" -d '{"titulo":"Montar pipeline HTTP"}' $A
V=$(curl -sk $A/7 | python3 -c 'import json,sys; print(json.load(sys.stdin)["versao"])')
req "PUT com versão atual (200)" -X PUT -H "Content-Type: application/json" -d "{\"titulo\":\"Testar concorrência\",\"situacao\":\"EmAndamento\",\"prioridade\":\"Alta\",\"prazo\":\"2026-11-30\",\"versao\":\"$V\"}" $A/7
req "PUT repetido com a versão antiga (409 conflito de concorrência)" -X PUT -H "Content-Type: application/json" -d "{\"titulo\":\"Testar concorrência\",\"situacao\":\"Concluida\",\"prioridade\":\"Alta\",\"prazo\":\"2026-11-30\",\"versao\":\"$V\"}" $A/7
req "PUT em id inexistente (404)" -X PUT -H "Content-Type: application/json" -d "{\"titulo\":\"Qualquer\",\"situacao\":\"Pendente\",\"prioridade\":\"Baixa\",\"versao\":\"$V\"}" $A/999
req "DELETE existente (204)" -X DELETE $A/8
req "DELETE repetido (404)" -X DELETE $A/8
req "v1 continua ativa, mas sinaliza descontinuação" $B/api/v1/tarefas/1
} > $E/01-requisicoes.txt 2>&1
curl -sk $B/openapi/v2.json | python3 -m json.tool --no-ensure-ascii > $E/02-openapi-v2-servido.json
NODE_PATH=$(npm root -g) node -e '
const { chromium } = require("playwright");
(async () => { const b = await chromium.launch(); const p = await b.newPage({ ignoreHTTPSErrors: true, viewport: { width: 1280, height: 900 } });
  await p.goto("https://localhost:7080/scalar/v2"); await p.waitForTimeout(3000);
  await p.screenshot({ path: process.argv[1] + "/03-scalar-ui.png" });
  await b.close(); })();' $E
( cd src/PortalTarefas.Frontend && echo '$ npm run chamada-real' && API_URL=$B NODE_EXTRA_CA_CERTS=${DEV_CERT:-} npm run --silent chamada-real ) > $E/04-cliente-ts-chamada-real.txt 2>&1
scripts/servidor.sh stop
python3 scripts/resumir-log.py $E/log-servidor.txt > $E/log-resumido.txt
grep -E "OFFSET|ORDER BY" $E/log-servidor.txt | head -3 | sed 's/^.*Executed DbCommand/Executed DbCommand/' > $E/05-sql-paginacao.txt
