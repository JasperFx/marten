@echo off
FOR /f %%v IN ('dotnet --version') DO set version=%%v
set target_framework=
IF "%version:~0,3%"=="3.1" (set target_framework=netcoreapp3.1)
IF "%version:~0,2%"=="5." (set target_framework=net5.0)
IF "%version:~0,2%"=="6." (set target_framework=net6.0)

IF [%target_framework%]==[] (
    echo "BUILD FAILURE: .NET Core 3.1, .NET 5, .NET 6 SDK required to run build"
    exit /b 1
)

dotnet run --project build/build.csproj -f %target_framework% -c Release -- %*
