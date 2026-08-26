#!/usr/bin/env bash
# Skaffold custom builder for the Java services.
#
# Their Dockerfiles are not multi-stage - they COPY target/*.jar - so the jar
# has to exist before docker build, exactly as CI does it ("Package Java
# services" runs mvn package before building any image). This mirrors that
# rather than changing the production Dockerfiles for the sake of the dev
# loop.
#
# Usage: build-java-image.sh <service-directory>
# Skaffold supplies $IMAGE (the tagged name to produce) and runs this from the
# artifact's context, which is the repo root for these artifacts.
set -euo pipefail

SERVICE="${1:?usage: build-java-image.sh <service-directory>}"
: "${IMAGE:?IMAGE must be set (Skaffold sets this)}"

# With a local cluster Skaffold expects the finished image in the daemon the
# cluster reads from, and never pushes it. minikube runs its own daemon
# separate from the host's, so build straight into it - the same thing
# Skaffold does automatically for its built-in docker builder, which a custom
# builder has to arrange for itself.
if [ "${PUSH_IMAGE:-false}" = "false" ] && kubectl config current-context 2>/dev/null | grep -q '^minikube$'; then
    eval "$(minikube docker-env)"
fi

# -am would rebuild every sibling module; these services share only the parent
# POM, so building the one module keeps the loop short.
mvn -B -q -DskipTests package -pl "$SERVICE"

docker build -t "$IMAGE" "$SERVICE"

if [ "${PUSH_IMAGE:-false}" = "true" ]; then
    docker push "$IMAGE"
fi
