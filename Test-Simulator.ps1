$ErrorActionPreference = 'Stop'
$temp = Join-Path ([System.IO.Path]::GetTempPath()) ('CybrslcTest-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $temp

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Invoke-Sample {
    param([string]$Arguments = '', [string]$InputText = '')
    $start = New-Object System.Diagnostics.ProcessStartInfo
    $start.FileName = Join-Path $temp 'SimulatedPayload.exe'
    $start.Arguments = $Arguments
    $start.UseShellExecute = $false
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $start
    try {
        $null = $process.Start()
        $process.StandardInput.Write($InputText)
        $process.StandardInput.Close()
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(15000)) {
            $process.Kill()
            $process.WaitForExit()
            throw 'Sample exceeded the 15-second test deadline.'
        }
        [PSCustomObject]@{
            ExitCode = $process.ExitCode
            Out = $stdout.GetAwaiter().GetResult()
            Err = $stderr.GetAwaiter().GetResult()
        }
    }
    finally {
        $process.Dispose()
    }
}

try {
    & (Join-Path $PSScriptRoot 'Build.ps1') -OutputDirectory $temp

    $help = Invoke-Sample '--help'
    Assert-True ($help.ExitCode -eq 0 -and $help.Out.Contains('Usage:')) 'Help failed.'
    $invalid = Invoke-Sample '--invalid'
    Assert-True ($invalid.ExitCode -eq 2 -and $invalid.Err.Contains('Unknown argument:')) 'Invalid arguments were not rejected.'
    $declined = Invoke-Sample
    Assert-True ($declined.ExitCode -eq 2 -and $declined.Err.Contains('Consent required.')) 'Redirected input bypassed consent.'
    Assert-True (-not $declined.Out.Contains('[SIMULATION]')) 'Simulation ran without consent.'

    $fast = Invoke-Sample '--consent --fast'
    Assert-True ($fast.ExitCode -eq 0 -and $fast.Err.Length -eq 0) 'Fast simulation failed.'
    Assert-True ($fast.Out.Contains('Network: OFF.')) 'Default network is not off.'
    Assert-True (-not $fast.Out.Contains('[REAL LAB HTTP]')) 'Default run attempted telemetry.'
    Assert-True ($fast.Out.Contains('REVEAL: You were not hacked.')) 'Final reveal missing.'
    Assert-True (([regex]::Matches($fast.Out, '\[SIMULATION\]')).Count -eq 12) 'Expected exactly 12 fictional scenes.'

    $timer = [System.Diagnostics.Stopwatch]::StartNew()
    $normal = Invoke-Sample '--consent'
    $timer.Stop()
    Assert-True ($normal.ExitCode -eq 0 -and $normal.Out.Contains('Closing normally.')) 'Animated run failed.'
    Assert-True ($timer.Elapsed.TotalSeconds -ge 8 -and $timer.Elapsed.TotalSeconds -lt 15) 'Animation did not finish in the expected 8-15 second window.'

    $bytes = [System.IO.File]::ReadAllBytes((Join-Path $temp 'SimulatedPayload.exe'))
    Assert-True ($bytes[0] -eq 0x4D -and $bytes[1] -eq 0x5A) 'Output lacks an MZ header.'
    $binaryHex = [System.BitConverter]::ToString($bytes)
    foreach ($marker in @('SIMULATED_MALWARE_TELEMETRY_PING', 'CYBRSLC&CDRU_BENIGN_SIMULATOR_V1')) {
        $markerHex = [System.BitConverter]::ToString([System.Text.Encoding]::Unicode.GetBytes($marker))
        Assert-True ($binaryHex.Contains($markerHex)) "UTF-16 marker missing: $marker"
    }

    Write-Output 'PASS: help, invalid arguments, consent gate, offline default, 12 scenes, reveal, bounded animation, PE header, UTF-16 markers.'
    Write-Output 'Manual VM checks still required: interactive consent, Esc/Ctrl+C, optional HTTP, YARA match, and ProcMon capture.'
}
finally {
    foreach ($name in @('SimulatedPayload.exe')) {
        $file = Join-Path $temp $name
        if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file }
    }
    Remove-Item -LiteralPath $temp
}
