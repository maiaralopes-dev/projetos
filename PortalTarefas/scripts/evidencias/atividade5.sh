#!/usr/bin/env bash
# Evidências da Atividade 5 (SPA React).
set -u
E=docs/evidencias/atividade5; mkdir -p $E
scripts/resetar-banco.sh > /dev/null
scripts/servidor.sh start $E/log-api.txt
( cd src/PortalTarefas.Frontend && nohup npx vite --port 5173 > ../../$E/log-vite.txt 2>&1 & echo $! > /tmp/vite.pid )
for i in $(seq 1 40); do curl -s -o /dev/null http://localhost:5173 && break; sleep 0.5; done
NODE_PATH=$(npm root -g) node scripts/capturas/atividade5.js $E
{ echo '$ curl http://localhost:5173/api/v2/tarefas?tamanho=1   # via proxy do Vite (mesma origem para o navegador)';
  curl -s -i "http://localhost:5173/api/v2/tarefas?tamanho=1" | grep -iE "^(HTTP|content-type|x-total-count|access-control)" ; } > $E/13-proxy.txt
kill $(cat /tmp/vite.pid) 2>/dev/null; pkill -f "vite --port 5173" 2>/dev/null
scripts/servidor.sh stop
( cd src/PortalTarefas.Frontend && echo '$ npm test' && npx vitest run 2>&1 | sed 's/\x1b\[[0-9;]*m//g' | tail -8 && echo && echo '$ npm run build' && npm run build 2>&1 | sed 's/\x1b\[[0-9;]*m//g' | tail -6 ) > $E/14-testes-e-build.txt
