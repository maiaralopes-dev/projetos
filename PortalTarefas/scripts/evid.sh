#!/usr/bin/env bash
# Utilitário: executa uma requisição curl e grava comando + status + cabeçalhos relevantes + corpo.
# uso: req "descrição" curl-args...
req() {
  local desc="$1"; shift
  echo "### $desc"
  echo "\$ curl $*" | sed -E 's/-sk? //; s/ -D - //'
  curl -sk -D /tmp/h.$$ -o /tmp/b.$$ "$@"
  head -1 /tmp/h.$$ | tr -d '\r'
  grep -iE '^(location|x-correlation-id|x-cliente-atendido|access-control-[a-z-]+|vary|retry-after|cache-control|age|content-type):' /tmp/h.$$ | tr -d '\r'
  if [ -s /tmp/b.$$ ]; then
    (python3 -m json.tool --no-ensure-ascii /tmp/b.$$ 2>/dev/null || cat /tmp/b.$$) | head -60
  fi
  echo
  rm -f /tmp/h.$$ /tmp/b.$$
}
