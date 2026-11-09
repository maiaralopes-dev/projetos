#!/usr/bin/env bash
# Coleta as evidências da Atividade 2 (capturas de tela + log do filtro).
E=docs/evidencias/atividade2; mkdir -p $E
scripts/servidor.sh start $E/log-servidor.txt
node scripts/capturas/atividade2.js $E
scripts/servidor.sh stop
python3 scripts/resumir-log.py $E/log-servidor.txt > $E/log-resumido.txt
grep -E "Filtro|Log\] saída (GET|POST) /Tarefas" $E/log-resumido.txt > $E/log-filtro.txt
