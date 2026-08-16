#!/bin/sh
set -e
# Multi-instance labs mount a named volume at /shared (directory, not a file subpath).
# File subpath mounts break Docker Desktop recreate ("open /var/lib/docker/tmp/...").
# Symlink keeps ASP.NET load order: appsettings.json then appsettings.Production.json.
if [ -f /shared/appsettings.json ]; then
  rm -f /app/appsettings.json
  ln -s /shared/appsettings.json /app/appsettings.json
fi
exec dotnet CacheOrchestrator.Sample.dll "$@"
