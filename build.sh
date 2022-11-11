#!/usr/bin/env bash
set -euo pipefail

version="$(dotnet --version)"
if [[ $version = 6.* ]]; then
  target_framework="net6.0"
elif [[ $version = 7.* ]]; then
  target_framework="net7.0"
else
  echo "BUILD FAILURE: .NET 6, .NET 7 SDK required to run build"
  exit 1
fi

dotnet run --project build/build.csproj -f $target_framework -c Release -- "$@"
