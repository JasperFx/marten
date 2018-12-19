#!/usr/bin/env bash
set -euo pipefail
dotnet run -p martenbuild.csproj -- "$@"
