# 发布单文件自包含 win-x64 Release
# 用法: .\scripts\publish.ps1
#       .\scripts\publish.ps1 -FrameworkDependent   # 依赖本机已安装的 .NET（体积更小）

param(
    [switch]$FrameworkDependent
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$project = Join-Path $root "Translator\LavaTranslator.csproj"
$outDir = Join-Path $root "publish\win-x64-single"

Get-Process Translator -ErrorAction SilentlyContinue | Stop-Process -Force

if (Test-Path $outDir) {
    Remove-Item $outDir -Recurse -Force
}

$selfContained = -not $FrameworkDependent
$args = @(
    "publish", $project,
    "-c", "Release",
    "-r", "win-x64",
    "--self-contained", ($selfContained.ToString().ToLowerInvariant()),
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:EnableCompressionInSingleFile=true",
    "-p:DebugType=None",
    "-p:DebugSymbols=false",
    "-o", $outDir,
    "--nologo"
)

Write-Host "Publishing -> $outDir"
Write-Host "  self-contained: $selfContained"
& dotnet @args
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

Get-ChildItem $outDir -Filter *.pdb -ErrorAction SilentlyContinue | Remove-Item -Force

$exe = Join-Path $outDir "Translator.exe"
if (-not (Test-Path $exe)) {
    throw "Translator.exe not found in $outDir"
}

$sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host ""
Write-Host "OK: $exe ($sizeMb MB)"
Write-Host "Files:"
Get-ChildItem $outDir | ForEach-Object {
    "{0,8:N1} MB  {1}" -f ($_.Length / 1MB), $_.Name
}
