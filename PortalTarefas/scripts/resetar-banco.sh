#!/usr/bin/env bash
# Recria o banco de desenvolvimento do zero a partir das migrations (ambiente limpo).
source .env
docker exec portal-sql /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$MSSQL_SA_PASSWORD" \
  -Q "IF DB_ID('PortalTarefas') IS NOT NULL BEGIN ALTER DATABASE PortalTarefas SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE PortalTarefas; END" > /dev/null
PATH=$PATH:$HOME/.dotnet/tools dotnet ef database update --project src/PortalTarefas.Web 2>&1 | grep -E "^(Applying|Done)"
