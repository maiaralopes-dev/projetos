#!/usr/bin/env bash
# Utilitário: executa uma requisição curl e grava comando + status + cabeçalhos relevantes + corpo.
# uso: req "descrição" curl-args...
req() {
  local desc="$1"; shift
  echo "### $desc"
  # Mascara segredos (senha de teste, cookies) antes de gravar a evidência.
  echo "\$ curl $*" | sed -E 's/-sk? //; s/ -D - //' | mascarar
  curl -sk -D /tmp/h.$$ -o /tmp/b.$$ "$@"
  head -1 /tmp/h.$$ | tr -d '\r'
  grep -iE '^(location|x-correlation-id|x-cliente-atendido|access-control-[a-z-]+|vary|retry-after|link|x-total-count|deprecation|cache-control|age|content-type|set-cookie):' /tmp/h.$$ | tr -d '\r' \
    | sed -E 's/^(set-cookie: [^=]+=)[^;]*/\1<valor omitido>/I' 
  if [ -s /tmp/b.$$ ]; then
    (python3 -m json.tool --no-ensure-ascii /tmp/b.$$ 2>/dev/null || cat /tmp/b.$$) | head -60 | mascarar
  fi
  echo
  rm -f /tmp/h.$$ /tmp/b.$$
}

mascarar() {
  if [ -n "${SEED_SENHA:-}" ]; then sed "s/${SEED_SENHA//\//\\/}/********/g"; else cat; fi \
    | sed -E 's/(X-XSRF-TOKEN: )[^ ]+/\1<token omitido>/I; s/(Cookie: )[^ ]+/\1<cookie omitido>/I'
}
