#!/usr/bin/env bash
# Evidências da Atividade 6 (Identity, cookie, 401/403, logout, auditoria). Senha e cookies são mascarados.
set -u
source scripts/evid.sh; source .env
E=docs/evidencias/atividade6; mkdir -p $E; B=https://localhost:7080; A=$B/api/v2
SQL() { docker exec portal-sql /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -d PortalTarefas -W "$@"; }
scripts/resetar-banco.sh > /dev/null
scripts/servidor.sh start $E/log-servidor.txt
JAR=$(mktemp -d)
xsrf() { awk '$6=="XSRF-TOKEN"{print $7}' $JAR/$1 | python3 -c 'import sys,urllib.parse; print(urllib.parse.unquote(sys.stdin.read().strip()))'; }
sessao() { curl -sk -c $JAR/$1 -b $JAR/$1 -o /dev/null $A/auth/sessao; }
entrar() { sessao $1; req "Login como $2" -c $JAR/$1 -b $JAR/$1 -X POST -H "Content-Type: application/json" -H "X-XSRF-TOKEN: $(xsrf $1)" -d "{\"email\":\"$2\",\"senha\":\"$3\"}" $A/auth/login; }
{
echo "===== 1. Login ====="
sessao anon
req "Login sem token antiforgery (CSRF)" -b $JAR/anon -X POST -H "Content-Type: application/json" -d "{\"email\":\"ana@portal.local\",\"senha\":\"$SEED_SENHA\"}" $A/auth/login
entrar x1 "ana@portal.local" "senha-errada-123!"
entrar x2 "naoexiste@portal.local" "qualquer-senha-123!"
entrar ana "ana@portal.local" "$SEED_SENHA"
entrar bruno "bruno@portal.local" "$SEED_SENHA"
entrar carla "carla@portal.local" "$SEED_SENHA"
entrar visit "visitante@portal.local" "$SEED_SENHA"
req "Sessão restaurada (ana)" -b $JAR/ana $A/auth/sessao
} > $E/02-login.txt 2>&1

{
echo "===== 2. Matriz de autorização (servidor; chamadas feitas por curl, fora da SPA) ====="
req "Anônimo lista tarefas" $A/tarefas?tamanho=1
req "Visitante (sem papel) lista tarefas" -b $JAR/visit "$A/tarefas?tamanho=1"
sessao visit; req "Visitante tenta criar" -b $JAR/visit -c $JAR/visit -X POST -H "Content-Type: application/json" -H "X-XSRF-TOKEN: $(xsrf visit)" -d '{"titulo":"Criada pelo visitante"}' $A/tarefas
sessao ana; req "Ana (Usuario/Backend) cria" -b $JAR/ana -c $JAR/ana -X POST -H "Content-Type: application/json" -H "X-XSRF-TOKEN: $(xsrf ana)" -d '{"titulo":"Criada pela Ana"}' $A/tarefas
V8=$(curl -sk -b $JAR/carla $A/tarefas/8 | python3 -c 'import json,sys;print(json.load(sys.stdin)["versao"])')
req "Ana edita tarefa 8 (equipe Frontend)" -b $JAR/ana -X PUT -H "Content-Type: application/json" -H "X-XSRF-TOKEN: $(xsrf ana)" -d "{\"titulo\":\"Montar telas React\",\"situacao\":\"EmAndamento\",\"prioridade\":\"Alta\",\"prazo\":\"2030-01-01\",\"versao\":\"$V8\"}" $A/tarefas/8
V2=$(curl -sk -b $JAR/carla $A/tarefas/2 | python3 -c 'import json,sys;print(json.load(sys.stdin)["versao"])')
req "Ana edita tarefa 2 (equipe Backend, a mesma dela)" -b $JAR/ana -X PUT -H "Content-Type: application/json" -H "X-XSRF-TOKEN: $(xsrf ana)" -d "{\"titulo\":\"Montar pipeline HTTP\",\"situacao\":\"Concluida\",\"prioridade\":\"Media\",\"prazo\":\"2026-11-07\",\"versao\":\"$V2\"}" $A/tarefas/2
sessao carla; req "Carla (Administrador + claim editar-outras-equipes) edita tarefa 8 (Frontend)" -b $JAR/carla -X PUT -H "Content-Type: application/json" -H "X-XSRF-TOKEN: $(xsrf carla)" -d "{\"titulo\":\"Montar telas React\",\"situacao\":\"EmAndamento\",\"prioridade\":\"Alta\",\"prazo\":\"2030-01-01\",\"versao\":\"$V8\"}" $A/tarefas/8
sessao bruno; req "Bruno (Usuario) tenta excluir" -b $JAR/bruno -X DELETE -H "X-XSRF-TOKEN: $(xsrf bruno)" $A/tarefas/3
req "Carla (Administrador) exclui" -b $JAR/carla -X DELETE -H "X-XSRF-TOKEN: $(xsrf carla)" $A/tarefas/3
req "Anônimo tenta excluir" -X DELETE $A/tarefas/4
} > $E/03-matriz-autorizacao.txt 2>&1

{
echo "===== 3. Logout e reutilização do cookie antigo ====="
cp $JAR/bruno $JAR/bruno-copia      # simula cookie copiado/roubado antes do logout
req "Bruno, com sessão, lista tarefas" -b $JAR/bruno "$A/tarefas?tamanho=1"
req "Logout do Bruno" -b $JAR/bruno -c $JAR/bruno -X POST -H "X-XSRF-TOKEN: $(xsrf bruno)" $A/auth/logout
req "Reenvio do cookie ANTIGO (copiado antes do logout)" -b $JAR/bruno-copia "$A/tarefas?tamanho=1"
} > $E/04-logout.txt 2>&1

{ echo "===== Auditoria de segurança (EventosSeguranca) — sem senha ou token ====="
  SQL -Q "SET NOCOUNT ON; SELECT Id, Evento, Usuario, Sucesso, Detalhe, CONVERT(varchar(19), OcorridoEm, 120) AS OcorridoEm FROM EventosSeguranca ORDER BY Id"
  echo; echo "===== Usuários, papéis e claims criados pelo seed controlado ====="
  SQL -Q "SET NOCOUNT ON; SELECT u.Email, ISNULL(r.Name,'(nenhum)') AS Papel, (SELECT STRING_AGG(c.ClaimType + '=' + c.ClaimValue, ', ') FROM AspNetUserClaims c WHERE c.UserId=u.Id) AS Claims FROM AspNetUsers u LEFT JOIN AspNetUserRoles ur ON ur.UserId=u.Id LEFT JOIN AspNetRoles r ON r.Id=ur.RoleId ORDER BY u.Email"
  echo; echo "-- Verificação: a senha de teste aparece em alguma evidência ou log?"
  grep -rl "$SEED_SENHA" docs/ src/ tests/ scripts/ 2>/dev/null || echo "Não encontrada em docs/, src/, tests/, scripts/."
} > $E/05-auditoria-e-usuarios.txt 2>&1

NODE_PATH=$(npm root -g) SEED_SENHA="$SEED_SENHA" bash -c "
( cd src/PortalTarefas.Frontend && nohup npx vite --port 5173 > /dev/null 2>&1 & echo \$! > /tmp/vite.pid )
for i in \$(seq 1 40); do curl -s -o /dev/null http://localhost:5173 && break; sleep 0.5; done
node scripts/capturas/atividade6.js $E"
pkill -f "vite --port 5173"; scripts/servidor.sh stop; rm -rf $JAR
python3 scripts/resumir-log.py $E/log-servidor.txt > $E/log-resumido.txt
