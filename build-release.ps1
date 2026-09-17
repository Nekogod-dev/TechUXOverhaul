param(
    [string]$DSPGameDir = "C:\Program Files (x86)\Steam\steamapps\common\Dyson Sphere Program",
    [switch]$PackageOnly
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Version = "0.9.0"
$Dist = Join-Path $Root "dist"
$Stage = Join-Path $Dist "DSPTechTreeUX_$Version"
$Dll = Join-Path $Root "bin\Release\net472\DSPTechTreeUX.dll"

if (-not $PackageOnly) {
    dotnet build (Join-Path $Root "DSPTechTreeUX.csproj") `
        -c Release `
        -p:DSPGameDir="$DSPGameDir"

    if ($LASTEXITCODE -ne 0) {
        throw "Build failed."
    }
}

if (-not (Test-Path $Dll)) {
    throw "DSPTechTreeUX.dll was not found at '$Dll'. Build the project first or copy your compiled DLL there."
}

if (Test-Path $Stage) {
    Remove-Item $Stage -Recurse -Force
}
New-Item -ItemType Directory -Force -Path (Join-Path $Stage "BepInEx\plugins\DSPTechTreeUX") | Out-Null

Copy-Item (Join-Path $Root "manifest.json") $Stage
Copy-Item (Join-Path $Root "README.md") $Stage
Copy-Item (Join-Path $Root "CHANGELOG.md") $Stage
Copy-Item (Join-Path $Root "icon.png") $Stage
Copy-Item $Dll (Join-Path $Stage "BepInEx\plugins\DSPTechTreeUX\DSPTechTreeUX.dll")

$Zip = Join-Path $Dist "Nekogod-DSPTechTreeUX-$Version.zip"
if (Test-Path $Zip) {
    Remove-Item $Zip -Force
}

Compress-Archive -Path (Join-Path $Stage "*") -DestinationPath $Zip
Write-Host ""
Write-Host "Thunderstore package created:"
Write-Host "  $Zip"
