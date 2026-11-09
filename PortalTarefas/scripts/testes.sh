#!/usr/bin/env bash
# Executa os testes .NET apontando para o SQL Server local (bancos de teste isolados e descartáveis).
source .env
export PORTAL_SQL_TESTES="Server=localhost,1433;User Id=sa;Password=$MSSQL_SA_PASSWORD;TrustServerCertificate=True"
dotnet test "$@"
