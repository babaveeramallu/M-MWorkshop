param(
    [string]$Executable = (Join-Path $PSScriptRoot 'bin\DoNtStopMePorfavor.exe'),
    [switch]$InstallInDisposableVm,
    [switch]$DefinitionOnly
)

$ErrorActionPreference = 'Stop'
if ($DefinitionOnly -and $InstallInDisposableVm) {
    throw 'DefinitionOnly cannot be combined with InstallInDisposableVm.'
}
Add-Type -AssemblyName System.Windows.Forms
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $Executable).Path)
$lab = $assembly.GetType('Cybrslc.Workshop.LabPersistence', $true)
$static = [Reflection.BindingFlags]'Static,NonPublic'
$instance = [Reflection.BindingFlags]'Instance,NonPublic'

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Invoke-Lab {
    param([string]$Name, [object[]]$Arguments = @())
    $lab.GetMethod($Name, $static).Invoke($null, $Arguments)
}

function Get-LabProperty {
    param([string]$Name)
    $lab.GetProperty($Name, $static).GetValue($null, $null)
}

function Invoke-MarkerProcess {
    param([string]$Arguments, [int]$ExpectedCode)
    $start = New-Object System.Diagnostics.ProcessStartInfo
    $start.FileName = $assembly.Location
    $start.Arguments = $Arguments
    $start.UseShellExecute = $false
    $start.RedirectStandardError = $true
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $start
    try {
        $null = $process.Start()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(10000)) {
            $process.Kill()
            $process.WaitForExit()
            throw 'Marker/argument check exceeded its 10-second deadline.'
        }
        $errorText = $stderr.GetAwaiter().GetResult()
        Assert-True ($process.ExitCode -eq $ExpectedCode) "Wrong exit code for '$Arguments': $($process.ExitCode). $errorText"
        if ($ExpectedCode -ne 0) {
            Assert-True (-not [string]::IsNullOrWhiteSpace($errorText)) 'Rejected arguments had no diagnostic.'
        }
    }
    finally { $process.Dispose() }
}

$before = Invoke-Lab 'Status'
$xml = Invoke-Lab 'BuildTaskXml'
$document = New-Object System.Xml.XmlDocument
$document.LoadXml($xml)
$ns = New-Object System.Xml.XmlNamespaceManager($document.NameTable)
$ns.AddNamespace('t', 'http://schemas.microsoft.com/windows/2004/02/mit/task')
function Task-Text {
    param([string]$Path)
    $document.SelectSingleNode("/t:Task/$Path", $ns).InnerText
}

Assert-True ((Task-Text 't:Principals/t:Principal/t:LogonType') -eq 'InteractiveToken') 'Task must not store credentials.'
Assert-True ((Task-Text 't:Principals/t:Principal/t:RunLevel') -eq 'LeastPrivilege') 'Task must not elevate.'
Assert-True ((Task-Text 't:Principals/t:Principal/t:UserId') -eq (Get-LabProperty 'UserId')) 'Task principal is not the current user.'
Assert-True ((Task-Text 't:Actions/t:Exec/t:Command') -eq $assembly.Location) 'Task executable path was not preserved.'
Assert-True ((Task-Text 't:Actions/t:Exec/t:Arguments') -eq '--lab-marker task') 'Task launches something other than the marker.'
Assert-True ($document.SelectNodes('/t:Task/t:Actions/*', $ns).Count -eq 1) 'Expected exactly one task action.'
Assert-True ((Task-Text 't:Triggers/t:TimeTrigger/t:Repetition/t:Interval') -eq 'PT5M') 'Task interval is not five minutes.'
Assert-True ((Task-Text 't:Settings/t:ExecutionTimeLimit') -eq 'PT1M') 'Task execution is not bounded.'
$delay = ([DateTime]::Parse((Task-Text 't:Triggers/t:TimeTrigger/t:StartBoundary')) - [DateTime]::Now).TotalSeconds
Assert-True ($delay -gt 30 -and $delay -le 60) 'First trigger must be approximately one minute after installation.'
Assert-True ((Get-LabProperty 'RegistryCommand') -eq ('"' + $assembly.Location + '" --lab-marker registry')) 'Registry command path must be quoted.'
Assert-True (Invoke-Lab 'IsOwnedTask' @($xml)) 'Own task definition was not recognized.'
$document.SelectSingleNode('/t:Task/t:Actions/t:Exec/t:Arguments', $ns).InnerText = '--different-action'
Assert-True (-not (Invoke-Lab 'IsOwnedTask' @($document.OuterXml))) 'Modified task would be removed as an owned task.'

$scheduler = $null
$definition = $null
try {
    $scheduler = New-Object -ComObject 'Schedule.Service'
    $scheduler.Connect()
    $definition = $scheduler.NewTask(0)
    # Validate with Windows itself without registering anything.
    $definition.XmlText = $xml
}
finally {
    if ($null -ne $definition) { $null = [Runtime.InteropServices.Marshal]::ReleaseComObject($definition) }
    if ($null -ne $scheduler) { $null = [Runtime.InteropServices.Marshal]::ReleaseComObject($scheduler) }
}

$controllerType = $assembly.GetType('Cybrslc.Workshop.MemeController', $true)
$controller = [Activator]::CreateInstance($controllerType, $true)
try {
    Assert-True (-not $controllerType.GetField('started', $instance).GetValue($controller)) 'Demo started without consent.'
    Assert-True ($controllerType.GetField('workers', $instance).GetValue($controller).Count -eq 0) 'Workers started without consent.'
    Assert-True (-not $controllerType.GetField('enableSound', $instance).GetValue($controller).Checked) 'Sound is not off by default.'
    Assert-True ($controllerType.GetField('repeatTimer', $instance).GetValue($controller).Interval -eq 45000) 'Existing repeat interval changed.'
    $buttons = @($controller.Controls[0].Controls | Where-Object { $_ -is [Windows.Forms.FlowLayoutPanel] })[0]
    Assert-True (@($buttons.Controls | Where-Object { $_.Text -eq 'Persistence lab...' }).Count -eq 1) 'Persistence controls are missing.'
}
finally { $controller.Dispose() }

$keyboardType = $assembly.GetType('Cybrslc.Workshop.KeyboardLab', $true)
$keyboard = [Activator]::CreateInstance($keyboardType, $true)
try {
    $consent = $keyboardType.GetField('consent', $instance).GetValue($keyboard)
    $inputBox = $keyboardType.GetField('input', $instance).GetValue($keyboard)
    $outputBox = $keyboardType.GetField('output', $instance).GetValue($keyboard)
    $entries = $keyboardType.GetField('entries', $instance).GetValue($keyboard)
    Assert-True (-not $consent.Checked -and -not $inputBox.Enabled) 'Keyboard capture must start disabled.'
    $key = [System.Windows.Forms.KeyPressEventArgs]::new([char]'x')
    $null = $keyboardType.GetMethod('CaptureCharacter', $instance).Invoke($keyboard, @($inputBox, $key))
    Assert-True ($entries.Count -eq 0) 'Keyboard capture ran without consent.'
    Assert-True ($inputBox.MaxLength -eq 2000 -and -not $inputBox.ShortcutsEnabled) 'Input bounds/shortcut restrictions changed.'
    Assert-True ($keyboardType.GetField('MaximumEvents', $static).GetRawConstantValue() -eq 500) 'Keyboard event limit changed.'
    $entries.Enqueue('dummy event')
    $inputBox.Text = 'dummy input'
    $outputBox.Text = 'dummy output'
    $null = $keyboardType.GetMethod('ClearCapture', $instance).Invoke($keyboard, @())
    Assert-True ($entries.Count -eq 0 -and $inputBox.TextLength -eq 0 -and $outputBox.TextLength -eq 0 -and -not $consent.Checked) 'Clear did not discard captured data and stop capture.'
}
finally { $keyboard.Dispose() }
Write-Output 'PASS: keyboard consent guard, input bounds, event-limit configuration, and Clear. Interactive focus, character capture, export, and rollover still require VM checks.'

Assert-True ((Invoke-Lab 'Status') -eq $before) 'Definition/UI checks changed persistence state.'
Write-Output 'PASS: Windows task XML validation, least privilege, marker-only actions, quoting, ownership guard, and unchanged demo defaults. No persistence installed by definition/UI checks.'
if ($DefinitionOnly) {
    Write-Output 'Not run: executable launch/argument checks, marker writes, registration, task execution, removal, or Windows logon.'
    return
}

Invoke-MarkerProcess '--invalid' 2
Invoke-MarkerProcess '--lab-marker invalid' 1
Invoke-MarkerProcess '--lab-marker' 2
Assert-True ((Invoke-Lab 'Status') -eq $before) 'Default checks changed persistence state.'
Write-Output 'PASS: executable argument errors. No persistence installed by default checks.'

if (-not $InstallInDisposableVm) {
    Write-Output 'Not run: real registration, task execution, removal, and Windows logon. Use -InstallInDisposableVm ONLY inside your disposable VM for the first three; test logon manually.'
    return
}

Assert-True ($before.StartsWith("Registry: absent`r`nScheduled task: absent")) 'Remove existing lab entries before running VM integration checks.'
$scheduler = $null
$root = $null
$task = $null
$running = $null
$attempted = $false
try {
    $attempted = $true
    Invoke-Lab 'Install'
    Assert-True ((Invoke-Lab 'Status').StartsWith("Registry: installed`r`nScheduled task: installed")) 'Both entries were not installed.'
    $refused = $false
    try { Invoke-Lab 'Install' }
    catch {
        $cause = $_.Exception.GetBaseException()
        if ($cause -isnot [InvalidOperationException] -or
            $cause.Message -ne 'An entry already exists. Inspect it and remove this lab''s entries before installing. Nothing was overwritten.') {
            throw
        }
        $refused = $true
    }
    Assert-True $refused 'Duplicate install did not refuse to overwrite existing entries.'

    $markerDirectory = Get-LabProperty 'MarkerDirectory'
    Invoke-MarkerProcess '--lab-marker registry' 0
    $registryMarker = Join-Path $markerDirectory 'registry-last-run.txt'
    Assert-True ((Get-Content -LiteralPath $registryMarker -Raw).Contains('Source: registry')) 'Registry marker did not write its source.'

    $scheduler = New-Object -ComObject 'Schedule.Service'
    $scheduler.Connect()
    $root = $scheduler.GetFolder('\')
    $task = $root.GetTask((Get-LabProperty 'TaskName'))
    $requested = [DateTime]::UtcNow
    $running = $task.Run($null)
    $taskMarker = Join-Path $markerDirectory 'task-last-run.txt'
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $fresh = $false
    do {
        Start-Sleep -Milliseconds 250
        if (Test-Path -LiteralPath $taskMarker) {
            $fresh = (Get-Item -LiteralPath $taskMarker).LastWriteTimeUtc -ge $requested
        }
    } while ((-not $fresh -or $task.State -eq 4) -and $timer.Elapsed.TotalSeconds -lt 30)
    Assert-True ($fresh -and $task.State -ne 4 -and $task.LastTaskResult -eq 0) 'Scheduled marker failed to complete successfully within 30 seconds.'
    Assert-True ((Get-Content -LiteralPath $taskMarker -Raw).Contains('Source: task')) 'Scheduled marker content is wrong.'
    Invoke-Lab 'Remove'
    Invoke-Lab 'Remove'
    Assert-True ((Invoke-Lab 'Status').StartsWith("Registry: absent`r`nScheduled task: absent")) 'Cleanup left persistence installed.'
    Write-Output 'PASS: real installation, duplicate protection, marker execution, Scheduler execution, removal, and idempotent cleanup.'
    Write-Output 'Marker text files retained for inspection. Manual sign-out/sign-in testing is still required.'
}
finally {
    foreach ($com in @($running, $task, $root, $scheduler)) {
        if ($null -ne $com) { $null = [Runtime.InteropServices.Marshal]::ReleaseComObject($com) }
    }
    if ($attempted) { Invoke-Lab 'Remove' }
}
