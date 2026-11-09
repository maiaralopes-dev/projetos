#!/usr/bin/env bash
# Evidências da Atividade 7 (observabilidade e resiliência).
set -u
source scripts/evid.sh; source .env
E=docs/evidencias/atividade7; mkdir -p $E; B=https://localhost:7080; A=$B/api/v2; D=http://localhost:5099
scripts/resetar-banco.sh > /dev/null
# Painel local (Aspire Dashboard) recebendo OTLP em localhost:4317
docker rm -f aspire >/dev/null 2>&1
docker run -d --name aspire -p 18888:18888 -p 4317:18889 -e DOTNET_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS=true mcr.microsoft.com/dotnet/aspire-dashboard:9.5 >/dev/null
nohup node scripts/dependencia-calendario.js > $E/log-dependencia.txt 2>&1 & echo $! > /tmp/dep.pid
sleep 3
env OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317 OTEL_METRIC_EXPORT_INTERVAL=5000 \
    Telemetria__Console=true \
    Logging__Console__FormatterName=json Logging__Console__FormatterOptions__IncludeScopes=true \
    RateLimit__Publica__Limite=10 RateLimit__Publica__JanelaSegundos=10 \
    scripts/servidor.sh start $E/log-servidor.txt
JAR=$(mktemp -d)
xsrf() { awk '$6=="XSRF-TOKEN"{print $7}' $JAR/$1 | python3 -c 'import sys,urllib.parse; print(urllib.parse.unquote(sys.stdin.read().strip()))'; }
entrar() { curl -sk -c $JAR/$1 -b $JAR/$1 -o /dev/null $A/auth/sessao; curl -sk -c $JAR/$1 -b $JAR/$1 -o /dev/null -X POST -H "Content-Type: application/json" -H "X-XSRF-TOKEN: $(xsrf $1)" -d "{\"email\":\"$2\",\"senha\":\"$SEED_SENHA\"}" $A/auth/login; curl -sk -c $JAR/$1 -b $JAR/$1 -o /dev/null $A/auth/sessao; }
modo() { curl -s -o /dev/null -X POST "$D/controle?modo=$1"; }
recebidas() { curl -s $D/estatisticas; echo; }
entrar carla carla@portal.local

{
echo "===== 1. Erro de aplicação localizável pela correlação ====="
req "Erro provocado com X-Correlation-Id conhecido" -b $JAR/carla -H "X-Correlation-Id: demo-erro-7" $B/api/minimal/diagnostico/erro
sleep 1
echo "-- Linhas de log (JSON estruturado) com CorrelationId=demo-erro-7:"
grep '"demo-erro-7"' $E/log-servidor.txt | python3 -c '
import sys, json
for l in sys.stdin:
    try: d = json.loads(l)
    except: continue
    esc = {k: v for s in d.get("Scopes", []) if isinstance(s, dict) for k, v in s.items() if k in ("CorrelationId","TraceId","SpanId","RequestPath")}
    print(json.dumps({"Timestamp": d.get("Timestamp"), "LogLevel": d["LogLevel"], "Category": d["Category"].split(".")[-1], "Message": d["Message"][:140], **esc}, ensure_ascii=False))'
echo "-- Alguma linha de log contém senha, cookie ou token?"
grep -ciE "$SEED_SENHA|portal\.sessao=|XSRF-TOKEN=" $E/log-servidor.txt || true
} > $E/01-correlacao-erro.txt 2>&1

{
echo "===== 2. Operações de negócio (métrica de negócio + span customizado) ====="
req "Criar tarefa (métrica portal.tarefas.criadas{prioridade=Alta})" -b $JAR/carla -X POST -H "Content-Type: application/json" -H "X-XSRF-TOKEN: $(xsrf carla)" -d '{"titulo":"Instrumentar a API","prioridade":"Alta","prazo":"2026-12-10"}' $A/tarefas
req "Concluir tarefa 6 (span tarefa.concluir)" -b $JAR/carla -X POST -H "X-XSRF-TOKEN: $(xsrf carla)" $B/api/dev/tarefas/6/concluir
} > $E/02-operacoes.txt 2>&1

{
echo "===== 3. Health checks ====="
modo ok
req "Liveness com dependência saudável" $B/health/live
req "Readiness com dependência saudável" $B/health/ready
req "Detalhes (Administrador)" -b $JAR/carla $B/health/detalhes
req "Detalhes (anônimo)" $B/health/detalhes
modo fora
req "Liveness com dependência INDISPONÍVEL (processo continua vivo)" $B/health/live
req "Readiness com dependência INDISPONÍVEL" $B/health/ready
req "Detalhes (Administrador) com dependência indisponível" -b $JAR/carla $B/health/detalhes
modo ok
} > $E/03-health.txt 2>&1

{
echo "===== 4. Resiliência: retry só em falha transitória e circuit breaker ====="
modo falha400; req "Dependência responde 400 (não transitória) — sem retry" $A/calendario/feriados/2030
echo "Requisições recebidas pela dependência: $(recebidas)"
modo falha503; req "Dependência responde 503 (transitória) — 1 tentativa + 3 retries" $A/calendario/feriados/2031
echo "Requisições recebidas pela dependência: $(recebidas)"
req "Nova chamada logo em seguida — circuito ABERTO, falha rápida" $A/calendario/feriados/2032
req "Mais uma — circuito continua aberto" $A/calendario/feriados/2033
echo "Requisições recebidas pela dependência (não aumentou com o circuito aberto): $(recebidas)"
modo ok; echo "-- dependência restabelecida; aguardando 16 s (BreakDuration = 15 s)"; sleep 16
req "Após a pausa: circuito meio-aberto -> chamada de teste com sucesso -> fechado" $A/calendario/feriados/2034
echo "Requisições recebidas pela dependência: $(recebidas)"
} > $E/04-resiliencia.txt 2>&1

{
echo "===== 5. Output caching em endpoint público e não personalizado ====="
modo ok
for i in 1 2 3 4 5; do req "GET anônimo nº $i de /calendario/feriados/2026" $A/calendario/feriados/2026 | grep -E "^###|^HTTP|^age|^cache-control"; sleep 1; done
echo "Requisições que chegaram à dependência (5 chamadas anônimas): $(recebidas)"
modo ok
for i in 1 2; do req "GET AUTENTICADO nº $i (com cookie: não usa cache compartilhado)" -b $JAR/carla $A/calendario/feriados/2026 | grep -E "^###|^HTTP|^age"; done
echo "Requisições que chegaram à dependência (2 chamadas autenticadas): $(recebidas)"
modo ok
req "Invalidação do cache pela tag 'feriados' (Administrador)" -b $JAR/carla -X POST -H "X-XSRF-TOKEN: $(xsrf carla)" $A/calendario/cache/invalidar
req "GET anônimo após invalidação" $A/calendario/feriados/2026 | grep -E "^###|^HTTP|^age"
echo "Requisições que chegaram à dependência: $(recebidas)"
} > $E/05-cache.txt 2>&1

{
echo "===== 6. Rate limiting (política 'publica' reduzida para 10 req / 10 s por IP nesta execução) ====="
sleep 11
for i in $(seq 1 14); do curl -sk -o /dev/null -w "%{http_code} " $A/calendario/feriados/2026; done; echo
req "Requisição rejeitada (detalhe)" $A/calendario/feriados/2026
} > $E/06-rate-limiting.txt 2>&1

{
echo "===== 7. Carga controlada local (autocannon) — NÃO representa capacidade de produção ====="
echo "Máquina: $(nproc) vCPU, contêiner de desenvolvimento; API em Debug; SQL Server no mesmo host."
sleep 11
echo; echo '$ npx autocannon -c 10 -d 10 https://localhost:7080/health/live'
NODE_TLS_REJECT_UNAUTHORIZED=0 npx -y autocannon@8 -c 10 -d 10 https://localhost:7080/health/live 2>&1 | sed 's/\x1b\[[0-9;]*m//g' | grep -vE "^Running|^10 connections|^$" | tail -14
} > $E/07-carga.txt 2>&1

sleep 8   # aguarda exportação periódica das métricas
NODE_PATH=$(npm root -g) node scripts/capturas/atividade7.js $E
kill $(cat /tmp/dep.pid) 2>/dev/null; scripts/servidor.sh stop; rm -rf $JAR
grep -E '"Name":|portal\.|Activity\.DisplayName|Metric Name' $E/log-servidor.txt | head -0
python3 - "$E" <<'PY'
import sys,re
E=sys.argv[1]; linhas=open(f"{E}/log-servidor.txt",encoding="utf-8").read().splitlines()
# Trecho do exportador de console: span customizado e métricas próprias
out=[]
for i,l in enumerate(linhas):
    if "Activity.DisplayName:" in l and "tarefa.concluir" in l:
        out.append("== Span customizado (ConsoleExporter) =="); out+=linhas[i-2:i+14]; break
for nome in ("portal.tarefas.criadas","portal.tarefas.concluidas","portal.dependencia.calendario.duracao","http.server.request.duration","aspnetcore.rate_limiting.requests"):
    for i,l in enumerate(linhas):
        if f"Metric Name: {nome}" in l:
            out.append(f"\n== Métrica {nome} (ConsoleExporter) =="); out+=linhas[i:i+12]; break
open(f"{E}/08-traces-e-metricas-console.txt","w",encoding="utf-8").write("\n".join(out)+"\n")
PY
docker rm -f aspire >/dev/null
# As ~100 mil requisições da carga não ajudam no diagnóstico e deixariam o log com centenas de MB.
grep -v '/health/live' $E/log-servidor.txt > $E/log.tmp && mv $E/log.tmp $E/log-servidor.txt
