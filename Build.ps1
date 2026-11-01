param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'bin')
)

$ErrorActionPreference = 'Stop'
$compiler = Join-Path ([System.Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()) 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'Use Windows PowerShell 5.1 with .NET Framework 4.x installed. The framework C# compiler was not found.'
}

$null = New-Item -ItemType Directory -Path $OutputDirectory -Force
$output = Join-Path $OutputDirectory 'SimulatedPayload.exe'
& $compiler /nologo /target:exe /platform:anycpu /optimize+ "/out:$output" (Join-Path $PSScriptRoot 'SimulatedPayload.cs')
if ($LASTEXITCODE -ne 0) {
    throw "C# compilation failed with exit code $LASTEXITCODE."
}
Write-Output "Built $output"
