#!/usr/bin/env bash
# Constrói a imagem com tag ligada ao commit. Se houver proxy/CA corporativa (como neste ambiente),
# repassa-os ao BuildKit sem gravá-los na imagem (args de proxy predefinidos não entram no histórico).
set -euo pipefail
SHA=$(git rev-parse --short HEAD)
VERSAO=${APP_VERSION:-1.0.0+$SHA}
EXTRA=()
if [ -n "${HTTPS_PROXY:-}" ]; then EXTRA+=(--network host --build-arg HTTPS_PROXY --build-arg HTTP_PROXY --build-arg NO_PROXY); fi
if [ -f /root/.ccr/ca-bundle.crt ]; then EXTRA+=(--secret id=ca_extra,src=/root/.ccr/ca-bundle.crt); fi
echo "\$ docker build --build-arg APP_VERSION=$VERSAO --build-arg APP_COMMIT=$SHA -t portal-tarefas:$SHA -t portal-tarefas:dev ."
DOCKER_BUILDKIT=1 docker build --progress=plain "${EXTRA[@]}" \
  --build-arg APP_VERSION="$VERSAO" --build-arg APP_COMMIT="$SHA" \
  -t "portal-tarefas:$SHA" -t portal-tarefas:dev .
