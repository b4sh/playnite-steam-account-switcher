<#
.SYNOPSIS
    M0 spike: switches Steam's auto-login account via the registry only (SPEC.md §5.4, §13).

.DESCRIPTION
    Writes exactly two values under HKCU\Software\Valve\Steam:
      - AutoLoginUser (REG_SZ)    = -Account
      - RememberPassword (DWORD)  = 1
    Does NOT touch loginusers.vdf (that is what the test checks), never reads or passes passwords,
    and never stops or kills Steam: it refuses to run while any steam.exe is alive.
    Close Steam first with `steam.exe -shutdown`.

    Prints old and new values. With -Start it launches steam.exe -silent afterwards (watch the
    login with `m0-inspect.ps1 -Watch` in another window). With -Label the change is appended
    to tools/m0-results.md. Supports -WhatIf.

.EXAMPLE
    .\tools\m0-switch.ps1 -Account alpha_login -WhatIf
    .\tools\m0-switch.ps1 -Account alpha_login -Start -Label "W1 switch to A"
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    # AccountName (login) from loginusers.vdf.
    [Parameter(Mandatory = $true)]
    [string]$Account,
    # Launch steam.exe -silent after writing the registry.
    [switch]$Start,
    # Scenario label; when set, the change is appended to the results file.
    [string]$Label,
    # Results file (default: m0-results.md next to this script).
    [string]$ResultsPath,
    # Optional Steam directory override (default: from registry).
    [string]$SteamPath
)

Set-StrictMode -Version 2.0
if (-not $ResultsPath) { $ResultsPath = Join-Path $PSScriptRoot 'm0-results.md' }
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'm0-common.ps1')

$steamDir = Resolve-SteamPath $SteamPath
if (-not $steamDir) { throw 'Steam installation not found.' }

$proc = Get-SteamProcessState
if ($proc.AnyRunning) {
    $pids = if ($proc.SteamPids.Count) { $proc.SteamPids -join ', ' } else { $proc.Pid }
    Write-Error ("Steam is running (steam.exe PID {0}). Close it first with:`n  & '{1}' -shutdown`nand wait until steam.exe exits. This script never stops Steam itself." -f $pids, (Join-Path $steamDir 'steam.exe'))
    exit 1
}

$accounts = @(Read-LoginUsers $steamDir)
$target = @($accounts | Where-Object { $_.AccountName -eq $Account })
if (-not $target) {
    $known = ($accounts | ForEach-Object { $_.AccountName }) -join ', '
    Write-Error "Account '$Account' not found in loginusers.vdf. Known accounts: $known"
    exit 1
}
$target = $target[0]
if ($target.RememberPassword -ne '1') {
    Write-Warning "Account '$Account' has RememberPassword = $($target.RememberPassword) in loginusers.vdf; Steam will probably ask for a password."
}

$before = [ordered]@{
    AutoLoginUser    = Get-RegValue $SteamKey 'AutoLoginUser'
    RememberPassword = Get-RegValue $SteamKey 'RememberPassword'
}
$fmt = { param($rv) if ($rv.Exists) { "``$($rv.Value)`` ($($rv.Kind))" } else { 'missing' } }

if (-not $PSCmdlet.ShouldProcess("$SteamKey", "Set AutoLoginUser = '$Account', RememberPassword = 1")) { return }

Set-ItemProperty -LiteralPath $SteamKey -Name 'AutoLoginUser' -Value $Account -Type String
Set-ItemProperty -LiteralPath $SteamKey -Name 'RememberPassword' -Value 1 -Type DWord

$after = [ordered]@{
    AutoLoginUser    = Get-RegValue $SteamKey 'AutoLoginUser'
    RememberPassword = Get-RegValue $SteamKey 'RememberPassword'
}
$autoLoginVdf = @($accounts | Where-Object { $_.AutoLogin -eq '1' } | ForEach-Object { $_.AccountName }) -join ', '

$out = @()
$out += "## $(if ($Label) { $Label } else { '(no label)' }) (switch)"
$out += ''
$out += ("_Switched {0}, host {1}; target ``{2}`` (AccountId {3})_" -f (Get-Date).ToString('yyyy-MM-dd HH:mm:ss.fff'), $env:COMPUTERNAME, $Account, $target.AccountId)
$out += ''
$out += '| Value | Before | After |'
$out += '|---|---|---|'
foreach ($k in $before.Keys) { $out += "| $k | $(& $fmt $before[$k]) | $(& $fmt $after[$k]) |" }
$out += ''
$autoText = if ($autoLoginVdf) { '`' + $autoLoginVdf + '`' } else { 'none' }
$out += "- loginusers.vdf untouched; account with AutoLogin = 1 there: $autoText"

if ($Start) {
    $exe = Join-Path $steamDir 'steam.exe'
    Start-Process -FilePath $exe -ArgumentList '-silent'
    $out += "- Started ``$exe -silent`` at $((Get-Date).ToString('HH:mm:ss.fff'))"
}
$out += ''

$out | ForEach-Object { Write-Output $_ }
if ($Label) {
    Add-ResultsText $ResultsPath (($out -join "`r`n") + "`r`n---`r`n`r`n")
    Write-Host "Appended to $ResultsPath" -ForegroundColor Green
}
