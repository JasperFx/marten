$ErrorActionPreference = "Stop";
$version = dotnet --version;
if ($version.StartsWith("3.1")) {
    $target_framework="netcoreapp3.1"
}
elseif ($version.StartsWith("5.")) {
    $target_framework="net5.0"
} else {
    Write-Output "BUILD FAILURE: .NET Core 3.1 or .NET 5 SDK required to run build"
    exit 1
}

dotnet run -p martenbuild.csproj -f $target_framework -c Release -- $args
