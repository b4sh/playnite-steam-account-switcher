<#
.SYNOPSIS
    M0 spike: dumps the Steam state the plugin will rely on (SPEC.md §5, §10).

.DESCRIPTION
    Read-only. Never writes to the registry or to any Steam file and never touches
    passwords, Steam Guard codes or tokens (config.vdf is not read at all).

    Prints:
      - HKCU\Software\Valve\Steam: SteamPath, SteamExe, AutoLoginUser, RememberPassword, RunningAppID
      - HKCU\Software\Valve\Steam\ActiveProcess: all values (pid, ActiveUser, ...) and whether
        the process with that PID exists and what it is
      - HKCU\Software\Valve\Steam\Apps\*: apps flagged Running / Updating
      - running steam.exe processes
      - accounts from loginusers.vdf and which one matches ActiveUser

    With -Label the same report is appended (as Markdown) to tools/m0-results.md.

.EXAMPLE
    .\tools\m0-inspect.ps1
    .\tools\m0-inspect.ps1 -Label "S3 logged in as A"
#>
[CmdletBinding()]
param(
    # Scenario label; when set, the report is appended to the results file.
    [string]$Label,
    # Results file (default: m0-results.md next to this script).
    [string]$ResultsPath,
    # Optional Steam directory override (default: from registry).
    [string]$SteamPath
)

Set-StrictMode -Version 2.0
if (-not $ResultsPath) { $ResultsPath = Join-Path $PSScriptRoot 'm0-results.md' }
$ErrorActionPreference = 'Stop'

$SteamId64Base = [UInt64]76561197960265728
$SteamKey = 'HKCU:\Software\Valve\Steam'
$ActiveProcessKey = 'HKCU:\Software\Valve\Steam\ActiveProcess'

# Fields of loginusers.vdf whose values are printed. Other fields are listed by name only.
$KnownUserFields = @('AccountName', 'PersonaName', 'RememberPassword', 'MostRecent', 'AllowAutoLogin',
    'AutoLogin', 'WantsOfflineMode', 'SkipOfflineModeWarning', 'Timestamp')

$out = New-Object System.Collections.Generic.List[string]
function Emit([string]$line = '') { $out.Add($line) }

function Get-RegValue([string]$path, [string]$name) {
    # Returns @{ Exists; Kind; Value } without modifying anything.
    $key = Get-Item -LiteralPath $path -ErrorAction SilentlyContinue
    if (-not $key) { return @{ Exists = $false; Kind = 'key missing'; Value = $null } }
    if ($key.GetValueNames() -notcontains $name) { return @{ Exists = $false; Kind = 'missing'; Value = $null } }
    return @{
        Exists = $true
        Kind   = $key.GetValueKind($name).ToString()
        Value  = $key.GetValue($name, $null, 'DoNotExpandEnvironmentNames')
    }
}

function Format-RegValue($rv) {
    if (-not $rv.Exists) { return "_($($rv.Kind))_" }
    $v = $rv.Value
    if ($rv.Kind -eq 'DWord') {
        $u = [BitConverter]::ToUInt32([BitConverter]::GetBytes([int32]$v), 0)
        return ('`{0}` (0x{0:X8}, DWord)' -f $u)
    }
    if ($v -is [byte[]]) { return ('`{0}` ({1})' -f ([BitConverter]::ToString($v)), $rv.Kind) }
    return ('`{0}` ({1})' -f $v, $rv.Kind)
}

function Get-DwordValue($rv) {
    if (-not $rv.Exists -or $rv.Kind -ne 'DWord') { return $null }
    return [BitConverter]::ToUInt32([BitConverter]::GetBytes([int32]$rv.Value), 0)
}

function Describe-Process([int]$id) {
    $p = Get-Process -Id $id -ErrorAction SilentlyContinue
    if (-not $p) { return @{ Exists = $false; Text = 'no such process' } }
    $path = try { $p.Path } catch { $null }
    $start = try { $p.StartTime.ToString('yyyy-MM-dd HH:mm:ss') } catch { '?' }
    return @{
        Exists = $true
        Name   = $p.ProcessName
        Text   = "``$($p.ProcessName)`` started $start, path ``$path``"
    }
}

function ConvertFrom-TextVdf([string]$text) {
    # Minimal text-VDF reader: quoted strings, braces, // comments. Duplicate keys: last wins.
    $root = [ordered]@{}
    $stack = New-Object System.Collections.Stack
    $current = $root
    $pendingKey = $null
    foreach ($m in [regex]::Matches($text, '"((?:[^"\\]|\\.)*)"|[{}]|//[^\n]*')) {
        $t = $m.Value
        if ($t.StartsWith('//')) { continue }
        if ($t -eq '{') {
            $child = [ordered]@{}
            $current[$pendingKey] = $child
            $stack.Push($current)
            $current = $child
            $pendingKey = $null
        }
        elseif ($t -eq '}') {
            if ($stack.Count -gt 0) { $current = $stack.Pop() }
            $pendingKey = $null
        }
        else {
            $s = $m.Groups[1].Value -replace '\\(.)', '$1'
            if ($null -eq $pendingKey) { $pendingKey = $s }
            else { $current[$pendingKey] = $s; $pendingKey = $null }
        }
    }
    return $root
}

# ---------------------------------------------------------------------------

$now = Get-Date
$title = if ($Label) { $Label } else { '(no label)' }
Emit "## $title"
Emit ''
Emit ("_Captured {0}, host {1}_" -f $now.ToString('yyyy-MM-dd HH:mm:ss'), $env:COMPUTERNAME)
Emit ''

# --- HKCU\Software\Valve\Steam ---------------------------------------------
Emit '### HKCU\Software\Valve\Steam'
Emit ''
Emit '| Value | Data |'
Emit '|---|---|'
$steamValues = @{}
foreach ($name in 'SteamPath', 'SteamExe', 'AutoLoginUser', 'RememberPassword', 'RunningAppID') {
    $rv = Get-RegValue $SteamKey $name
    $steamValues[$name] = $rv
    Emit "| $name | $(Format-RegValue $rv) |"
}
$hklm = Get-RegValue 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam' 'InstallPath'
Emit "| _HKLM WOW6432Node InstallPath_ | $(Format-RegValue $hklm) |"
Emit ''

$runningAppId = Get-DwordValue $steamValues['RunningAppID']
if ($runningAppId) {
    $appName = Get-RegValue "$SteamKey\Apps\$runningAppId" 'Name'
    Emit ("RunningAppID {0} -> Apps\{0}\Name = {1}" -f $runningAppId, (Format-RegValue $appName))
    Emit ''
}

# --- ActiveProcess ----------------------------------------------------------
Emit '### HKCU\Software\Valve\Steam\ActiveProcess'
Emit ''
$apKey = Get-Item -LiteralPath $ActiveProcessKey -ErrorAction SilentlyContinue
$activeUser = $null
$pid_ = $null
if (-not $apKey) {
    Emit '_key missing_'
}
else {
    Emit '| Value | Data |'
    Emit '|---|---|'
    $names = @('pid', 'ActiveUser') + @($apKey.GetValueNames() | Where-Object { $_ -notin 'pid', 'ActiveUser' } | Sort-Object)
    foreach ($name in $names) {
        $rv = Get-RegValue $ActiveProcessKey $name
        Emit "| $name | $(Format-RegValue $rv) |"
        if ($name -eq 'pid') { $pid_ = Get-DwordValue $rv }
        if ($name -eq 'ActiveUser') { $activeUser = Get-DwordValue $rv }
    }
}
Emit ''
$steamRunningBySpec = $false
if ($null -ne $pid_) {
    if ($pid_ -eq 0) {
        Emit '- Process for `pid`: pid is 0'
    }
    else {
        $d = Describe-Process ([int]$pid_)
        Emit "- Process for ``pid`` $($pid_): $($d.Text)"
        $steamRunningBySpec = $d.Exists -and ($d.Name -ieq 'steam')
    }
}
Emit "- Steam running per SPEC §5.3 rule (pid exists and is steam.exe): **$steamRunningBySpec**"
Emit ''

# --- Processes --------------------------------------------------------------
Emit '### Processes'
Emit ''
$procs = @(Get-Process -Name steam, steamwebhelper, steamservice -ErrorAction SilentlyContinue)
$steamExes = @($procs | Where-Object { $_.ProcessName -ieq 'steam' })
Emit ("- steam.exe PIDs: {0}" -f $(if ($steamExes) { ($steamExes | ForEach-Object { $_.Id }) -join ', ' } else { 'none' }))
Emit ("- steamwebhelper.exe count: {0}" -f @($procs | Where-Object { $_.ProcessName -ieq 'steamwebhelper' }).Count)
Emit ("- steamservice.exe running: {0}" -f [bool]@($procs | Where-Object { $_.ProcessName -ieq 'steamservice' }).Count)
Emit ''

# --- Apps flags -------------------------------------------------------------
Emit '### HKCU\Software\Valve\Steam\Apps (Running / Updating = 1)'
Emit ''
$flagged = @()
$appsKey = Get-Item -LiteralPath "$SteamKey\Apps" -ErrorAction SilentlyContinue
if ($appsKey) {
    foreach ($sub in $appsKey.GetSubKeyNames()) {
        $k = $appsKey.OpenSubKey($sub)
        if (-not $k) { continue }
        $running = $k.GetValue('Running', 0)
        $updating = $k.GetValue('Updating', 0)
        if ($running -or $updating) {
            $flagged += ("- {0} ``{1}``: Running={2}, Updating={3}" -f $sub, $k.GetValue('Name', ''), $running, $updating)
        }
        $k.Close()
    }
}
if ($flagged) { $flagged | ForEach-Object { Emit $_ } } else { Emit '_none_' }
Emit ''

# --- loginusers.vdf ---------------------------------------------------------
Emit '### loginusers.vdf'
Emit ''
if (-not $SteamPath) {
    if ($steamValues['SteamPath'].Exists) { $SteamPath = [string]$steamValues['SteamPath'].Value }
    elseif ($hklm.Exists) { $SteamPath = [string]$hklm.Value }
}
$accounts = @()
if (-not $SteamPath) {
    Emit '_Steam path not found_'
}
else {
    $vdfPath = Join-Path ($SteamPath -replace '/', '\') 'config\loginusers.vdf'
    if (-not (Test-Path -LiteralPath $vdfPath)) {
        Emit "_not found: ``$vdfPath``_"
    }
    else {
        $fi = Get-Item -LiteralPath $vdfPath
        Emit ("``{0}``, modified {1}, {2} bytes" -f $vdfPath, $fi.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss'), $fi.Length)
        Emit ''
        $vdf = ConvertFrom-TextVdf ([IO.File]::ReadAllText($vdfPath, [Text.Encoding]::UTF8))
        $users = $vdf['users']
        if (-not $users) {
            Emit '_no "users" section_'
        }
        else {
            Emit '| SteamID64 | AccountId | AccountName | PersonaName | RememberPassword | MostRecent | AllowAutoLogin | AutoLogin | WantsOfflineMode | Timestamp | Other fields | = ActiveUser |'
            Emit '|---|---|---|---|---|---|---|---|---|---|---|---|'
            foreach ($sid in $users.Keys) {
                $u = $users[$sid]
                $accountId = $null
                $id64 = [UInt64]0
                if ([UInt64]::TryParse($sid, [ref]$id64) -and $id64 -ge $SteamId64Base) { $accountId = $id64 - $SteamId64Base }
                $get = { param($f) if ($u.Contains($f)) { $u[$f] } else { '–' } }
                $other = @($u.Keys | Where-Object { $_ -notin $KnownUserFields }) -join ', '
                $match = ($null -ne $activeUser) -and ($null -ne $accountId) -and ($accountId -eq $activeUser)
                $ts = & $get 'Timestamp'
                if ($ts -match '^\d+$') { $ts = "$ts ($([DateTimeOffset]::FromUnixTimeSeconds([int64]$ts).LocalDateTime.ToString('yyyy-MM-dd HH:mm')))" }
                Emit ("| {0} | {1} | ``{2}`` | ``{3}`` | {4} | {5} | {6} | {7} | {8} | {9} | {10} | {11} |" -f `
                    $sid, $accountId, (& $get 'AccountName'), (& $get 'PersonaName'), (& $get 'RememberPassword'),
                    (& $get 'MostRecent'), (& $get 'AllowAutoLogin'), (& $get 'AutoLogin'), (& $get 'WantsOfflineMode'), $ts, $other,
                    $(if ($match) { '**YES**' } else { '' }))
                $accounts += [pscustomobject]@{ AccountId = $accountId; AccountName = (& $get 'AccountName'); Match = $match }
            }
        }
    }
}
Emit ''

# --- Summary ----------------------------------------------------------------
Emit '### Summary'
Emit ''
$matched = @($accounts | Where-Object { $_.Match })
$activeText = if ($null -eq $activeUser) { 'n/a' } elseif ($activeUser -eq 0) { '0 (nobody)' } else { "$activeUser" }
Emit "- ActiveUser: $activeText"
Emit ("- Account matching ActiveUser: {0}" -f $(if ($matched) { ($matched | ForEach-Object { "``$($_.AccountName)``" }) -join ', ' } elseif ($activeUser) { '**none – ActiveUser not in loginusers.vdf**' } else { '–' }))
$auto = $steamValues['AutoLoginUser']
Emit ("- AutoLoginUser: {0}" -f $(if ($auto.Exists) { "``$($auto.Value)``" } else { 'missing' }))
Emit "- Steam running (§5.3 rule): $steamRunningBySpec; RunningAppID: $(if ($null -eq $runningAppId) { 'n/a' } else { $runningAppId })"
Emit ''

# --- Output -----------------------------------------------------------------
$out | ForEach-Object { Write-Output $_ }

if ($Label) {
    $utf8 = New-Object System.Text.UTF8Encoding($false)
    if (-not (Test-Path -LiteralPath $ResultsPath)) {
        [IO.File]::WriteAllText($ResultsPath, "# M0 results`r`n`r`nGenerated by ``tools/m0-inspect.ps1``. See SPEC.md §5 and §10.`r`n`r`n", $utf8)
    }
    [IO.File]::AppendAllText($ResultsPath, (($out -join "`r`n") + "`r`n---`r`n`r`n"), $utf8)
    Write-Host "Appended to $ResultsPath" -ForegroundColor Green
}
