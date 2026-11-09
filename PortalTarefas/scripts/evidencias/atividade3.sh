#!/usr/bin/env bash
# Evidências de execução da Atividade 3 (seed idempotente, transação, concorrência, EF x Dapper).
set -u
scripts/resetar-banco.sh > docs/evidencias/atividade3/00-banco-limpo.txt
source scripts/evid.sh; source .env
E=docs/evidencias/atividade3; mkdir -p $E; B=https://localhost:7080
SQL() { docker exec portal-sql /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -d PortalTarefas -W "$@"; }
{
echo "== Seed idempotente: a aplicação é iniciada duas vezes (Migrate + UseAsyncSeeding a cada início) =="
for i in 1 2; do
  scripts/servidor.sh start $E/log-servidor-inicio$i.txt; scripts/servidor.sh stop
  echo "-- após o início $i:"; SQL -Q "SET NOCOUNT ON; SELECT COUNT(*) AS TotalTarefas FROM Tarefas"
done
} > $E/08-seed-idempotente.txt 2>&1

scripts/servidor.sh start $E/log-servidor.txt
{
echo "== Transação explícita: concluir tarefa 6 + registrar auditoria =="
echo "-- ANTES"; SQL -Q "SET NOCOUNT ON; SELECT Id, Situacao FROM Tarefas WHERE Id=6; SELECT COUNT(*) AS EventosDaTarefa6 FROM EventosAuditoria WHERE TarefaId=6"
req "Rollback provocado (falha entre o UPDATE e o INSERT de auditoria)" -X POST "$B/api/dev/tarefas/6/concluir?simularFalha=true"
echo "-- DEPOIS DO ROLLBACK (nada mudou)"; SQL -Q "SET NOCOUNT ON; SELECT Id, Situacao FROM Tarefas WHERE Id=6; SELECT COUNT(*) AS EventosDaTarefa6 FROM EventosAuditoria WHERE TarefaId=6"
req "Operação com sucesso (commit)" -X POST "$B/api/dev/tarefas/6/concluir"
echo "-- DEPOIS DO COMMIT (tarefa e auditoria gravadas juntas)"; SQL -Q "SET NOCOUNT ON; SELECT Id, Situacao FROM Tarefas WHERE Id=6; SELECT TarefaId, Acao, Detalhe, Usuario FROM EventosAuditoria WHERE TarefaId=6"
} > $E/09-transacao.txt 2>&1

{
echo "== Concorrência otimista (rowversion) com duas sessões de navegador =="
echo "-- ANTES"; SQL -Q "SET NOCOUNT ON; SELECT Id, Titulo, Situacao, Prioridade, CONVERT(varchar(18), CAST(Versao AS varbinary(8)), 1) AS Versao FROM Tarefas WHERE Id=7"
node scripts/capturas/atividade3-concorrencia.js $E 7
echo "-- DEPOIS (a alteração da sessão 1 foi preservada; a sessão 2 salvou só após revisar os valores atuais)"
SQL -Q "SET NOCOUNT ON; SELECT Id, Titulo, Situacao, Prioridade, CONVERT(varchar(18), CAST(Versao AS varbinary(8)), 1) AS Versao FROM Tarefas WHERE Id=7; SELECT Acao, Detalhe FROM EventosAuditoria WHERE TarefaId=7 ORDER BY Id"
} > $E/10-concorrencia.txt 2>&1

{
req "Painel com EF Core (LINQ + projeção + AsNoTracking)" $B/api/painel/ef
req "Painel com Dapper (SQL parametrizado)" $B/api/painel/dapper
echo "== Comparação automática dos dois resultados =="
diff <(curl -sk $B/api/painel/ef) <(curl -sk $B/api/painel/dapper) && echo "Resultados idênticos (EF Core = Dapper)"
} > $E/11-painel-ef-dapper.txt 2>&1
scripts/servidor.sh stop
python3 scripts/resumir-log.py $E/log-servidor.txt > $E/log-resumido.txt
grep -E "concorrência|Falha simulada|ROLLBACK|Rolling|rolled|Commit" $E/log-servidor.txt | cut -c1-300 > $E/log-transacao-concorrencia.txt
grep -E "GROUP BY \[t\]" $E/log-servidor.txt | head -1 | sed 's/^.*Executed DbCommand/Executed DbCommand/' > $E/12-sql-gerado-painel.txt
grep -E "UPDATE \[Tarefas\]" $E/log-servidor.txt | head -1 | sed 's/^.*Executed DbCommand/Executed DbCommand/' > $E/13-sql-update-concorrencia.txt
env "Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command=Warning" scripts/servidor.sh start $E/log-medicao.txt; { echo "== Medição local da consulta de painel (8 linhas; log de comandos SQL desligado para não distorcer; mesma máquina; não generalizar) =="; req "Medição EF Core x Dapper" "$B/api/dev/painel/medicao?n=500"; } > $E/14-medicao-painel.txt; scripts/servidor.sh stop; rm -f $E/log-medicao.txt
