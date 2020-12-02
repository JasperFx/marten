@echo off
FOR /f %%v IN ('dotnet --version') DO set version=%%v
set target_framework=
IF "%version:~0,3%"=="3.1" (set target_framework=netcoreapp3.1)
IF "%version:~0,2%"=="5." (set target_framework=net5.0)

IF [%target_framework%]==[] (
    echo "BUILD FAILURE: .NET Core 3.1 or .NET 5 SDK required to run build"
    exit /b 1
)

dotnet run -p martenbuild.csproj -f %target_framework% -c Release -- %*
