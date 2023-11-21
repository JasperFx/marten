#!/usr/bin/env bash
set -euo pipefail

versions=($(dotnet --list-sdks | awk '{print $1}' | cut -d '[' -f 1))
target_framework=""
for version in "${versions[@]}"; do
  if [[ $version = 6.* ]]; then
    target_framework="net6.0"
  elif [[ $version = 7.* ]]; then
    target_framework="net7.0"
  fi
done

if [ -z "$target_framework" ]; then
  echo "BUILD FAILURE: .NET 6 or .NET 7 SDK required to run build"
  exit 1
fi

echo "Using $target_framework"
dotnet run --project build/build.csproj -f $target_framework -c Release -- "$@"
