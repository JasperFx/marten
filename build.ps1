$ErrorActionPreference = "Stop";
$version = dotnet --version;
if ($version.StartsWith("3.1")) {
    $target_framework="netcoreapp3.1"
}
elseif ($version.StartsWith("5.")) {
    $target_framework="net5.0"
} 
elseif ($version.StartsWith("6.")) {
    $target_framework="net6.0"
} else {
    Write-Output "BUILD FAILURE: .NET Core 3.1, .NET 5, .NET 6 SDK required to run build"
    exit 1
}

dotnet run --project build/build.csproj -f $target_framework -c Release -- $args
