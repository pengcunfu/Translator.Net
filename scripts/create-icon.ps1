Add-Type -AssemblyName System.Drawing
$src = Join-Path $PSScriptRoot "..\LavaTranslator\icon.jpg"
$dest = Join-Path $PSScriptRoot "..\LavaTranslator\icon.ico"
$bitmap = [System.Drawing.Bitmap]::FromFile((Resolve-Path $src))
$icon = [System.Drawing.Icon]::FromHandle($bitmap.GetHicon())
try {
    $stream = [System.IO.File]::Create($dest)
    $icon.Save($stream)
    $stream.Close()
    Write-Host "Created $dest"
}
finally {
    $icon.Dispose()
    $bitmap.Dispose()
}
