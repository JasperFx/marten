#!/usr/bin/env bash
set -euo pipefail

version="$(dotnet --version)"
if [[ $version = 3.1* ]]; then
  target_framework="netcoreapp3.1"
elif [[ $version = 5.* ]]; then
  target_framework="net5.0"
else
  echo "BUILD FAILURE: .NET Core 3.1 or .NET 5 SDK required to run build"
  exit 1
fi

dotnet run -p martenbuild.csproj -f $target_framework -c Release -- "$@"
