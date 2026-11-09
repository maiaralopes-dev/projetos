#!/usr/bin/env bash
# Coleta as evidências da Atividade 1. Executar a partir da raiz do repositório.
set -u
source scripts/evid.sh
E=docs/evidencias/atividade1; mkdir -p $E; B=https://localhost:7080
scripts/servidor.sh start $E/log-servidor.txt
{
req "Parâmetro de rota válido" $B/api/v1/tarefas/1
req "Parâmetro de rota inexistente" $B/api/v1/tarefas/999
req "Parâmetro de rota inválido (não numérico)" $B/api/v1/tarefas/abc
req "Filtro por query string + cabeçalho opcional" -H "X-Cliente-Id: app-relatorio" "$B/api/v1/tarefas?situacao=Pendente&busca=relat"
req "Filtro sem cabeçalho opcional" "$B/api/v1/tarefas?situacao=Concluida"
req "Valor inválido no filtro de query string" "$B/api/v1/tarefas?situacao=Arquivada"
req "POST com corpo válido" -X POST -H "Content-Type: application/json" -d '{"titulo":"Estudar middleware","descricao":"Ordem do pipeline","situacao":"EmAndamento"}' $B/api/v1/tarefas
req "POST com dados inválidos" -X POST -H "Content-Type: application/json" -d '{"titulo":"","descricao":null}' $B/api/v1/tarefas
req "POST com JSON malformado" -X POST -H "Content-Type: application/json" -d '{"titulo":' $B/api/v1/tarefas
req "Exceção controlada pelo middleware" -H "X-Correlation-Id: teste-erro-001" $B/api/minimal/diagnostico/erro
req "Diagnóstico Minimal API" -H "X-Cliente-Id: cli-42" $B/api/minimal/diagnostico
req "Diagnóstico MVC (controller)" -H "X-Cliente-Id: cli-42" $B/api/mvc/diagnostico
req "CORS preflight - origem autorizada" -X OPTIONS -H "Origin: http://localhost:5173" -H "Access-Control-Request-Method: POST" -H "Access-Control-Request-Headers: content-type" $B/api/v1/tarefas
req "CORS preflight - origem NÃO autorizada" -X OPTIONS -H "Origin: http://evil.example" -H "Access-Control-Request-Method: POST" $B/api/v1/tarefas
req "CORS GET simples - origem autorizada" -H "Origin: http://localhost:5173" $B/api/v1/tarefas/1
req "Redirecionamento HTTP -> HTTPS" http://localhost:5080/api/v1/tarefas/1
req "Arquivo estático" $B/index.html
req "Encerramento antecipado (ShortCircuit)" $B/ping
} > $E/requisicoes.txt 2>&1
scripts/servidor.sh stop
python3 scripts/resumir-log.py $E/log-servidor.txt > $E/log-resumido.txt
