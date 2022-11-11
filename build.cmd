@echo off
FOR /f %%v IN ('dotnet --version') DO set version=%%v
set target_framework=
IF "%version:~0,2%"=="6." (set target_framework=net6.0)
IF "%version:~0,2%"=="7." (set target_framework=net7.0)

IF [%target_framework%]==[] (
    echo "BUILD FAILURE: .NET 6, .NET 7 SDK required to run build"
    exit /b 1
)

dotnet run --project build/build.csproj -f %target_framework% -c Release -- %*
