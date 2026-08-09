# 本地 Debug 编译
# 用法: .\scripts\build.ps1
#       .\scripts\build.ps1 -Configuration Release

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$project = Join-Path $root "Translator\LavaTranslator.csproj"

Get-Process Translator -ErrorAction SilentlyContinue | Stop-Process -Force

Write-Host "Building ($Configuration)..."
& dotnet build $project -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE"
}

$out = Join-Path $root "Translator\bin\$Configuration\net10.0\Translator.dll"
Write-Host "OK: $out"
