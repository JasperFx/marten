#!/usr/bin/env bash
set -euo pipefail

version="$(dotnet --version)"
if [[ $version = 3.1* ]]; then
  target_framework="netcoreapp3.1"
elif [[ $version = 5.* ]]; then
  target_framework="net5.0"
elif [[ $version = 6.* ]]; then
  target_framework="net6.0"
else
  echo "BUILD FAILURE: .NET Core 3.1, .NET 5 or .NET 6 SDK required to run build"
  exit 1
fi

dotnet run --project build/build.csproj -f $target_framework -c Release -- "$@"
