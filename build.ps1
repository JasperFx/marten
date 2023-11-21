$ErrorActionPreference = "Stop";

$target_framework = ""
$dotnet_sdks = dotnet --list-sdks
$pattern = "\d+\.\d+\.\d+"
$versions = [regex]::Matches($dotnet_sdks, $pattern)

foreach ($item in $versions) {
  if ($item.Value.StartsWith("6.")) {
    $target_framework = "net6.0"
  } 
  elseif ($item.Value.StartsWith("7.")) {
    $target_framework = "net7.0"
  }
}

if ([string]::IsNullOrEmpty($target_framework)) {
    Write-Output "BUILD FAILURE: .NET 6 or .NET 7 SDK required to run build"
    exit 1
}

Write-Output "Using $target_framework"
dotnet run --project build/build.csproj -f $target_framework -c Release -- $args
