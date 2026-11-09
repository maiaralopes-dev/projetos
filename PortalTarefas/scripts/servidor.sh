#!/usr/bin/env bash
# Inicia/para a API em segundo plano para coleta de evidências.
# uso: scripts/servidor.sh start <arquivo-log> | stop
PIDFILE=/tmp/portal-tarefas.pid
case "$1" in
  start)
    dotnet build src/PortalTarefas.Web -v q -nologo > /dev/null || exit 1
    ASPNETCORE_ENVIRONMENT=${ASPNETCORE_ENVIRONMENT:-Development} \
      nohup dotnet src/PortalTarefas.Web/bin/Debug/net10.0/PortalTarefas.Web.dll \
      --contentRoot "$PWD/src/PortalTarefas.Web" --urls "https://localhost:7080;http://localhost:5080" > "$2" 2>&1 &
    echo $! > $PIDFILE
    for i in $(seq 1 60); do curl -sk -o /dev/null https://localhost:7080/ping && exit 0; sleep 0.5; done
    echo "servidor não respondeu" >&2; exit 1 ;;
  stop) [ -f $PIDFILE ] && kill "$(cat $PIDFILE)" 2>/dev/null; rm -f $PIDFILE; sleep 1 ;;
esac
