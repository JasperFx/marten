#!/bin/bash
set -ev

dotnet restore ./src/Marten.sln --runtime netstandard1.3
dotnet build ./src/Marten.Testing/Marten.Testing.csproj --framework netcoreapp2.0 --configuration Release
npm run test
dotnet test ./src/Marten.Testing/Marten.Testing.csproj --framework netcoreapp2.0 --configuration Release 