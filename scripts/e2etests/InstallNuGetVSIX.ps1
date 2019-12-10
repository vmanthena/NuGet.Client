param (
    [Parameter(Mandatory = $true)]
    [string]$NuGetDropPath,
    [Parameter(Mandatory = $true)]
    [string]$FuncTestRoot,
    [Parameter(Mandatory = $true)]
    [string]$NuGetVSIXID,
    [Parameter(Mandatory = $true)]
    [int]$ProcessExitTimeoutInSeconds,
    [Parameter(Mandatory = $true)]
    [ValidateSet("16.0")]
    [string]$VSVersion)

. "$PSScriptRoot\VSUtils.ps1"

$success = IsAdminPrompt

if ($success -eq $false) {
    $errorMessage = 'ERROR: Please re-run this script as an administrator! ' +
    'Actions such as installing VSIX and uninstalling VSIX require admin privileges.'

    Write-Error $errorMessage
    exit 1
}

Function Write-HostWithTimestamp([string] $message)
{
    Write-Host "[$([System.DateTime]::UtcNow.ToString("O"))]  $message"
}

Function Write-RunningProcesses()
{
    $output = tasklist | Out-String

    Write-HostWithTimestamp $output
}

$VSIXSrcPath = Join-Path $NuGetDropPath 'NuGet.Tools.vsix'
$VSIXPath = Join-Path $FuncTestRoot 'NuGet.Tools.vsix'

Write-HostWithTimestamp "Copying $VSIXSrcPath to $VSIXPath."

Copy-Item $VSIXSrcPath $VSIXPath

Write-HostWithTimestamp 'Copy complete.'

# Because we are upgrading an installed system component VSIX, we need to downgrade first.
$numberOfTries = 0
$success = $false
do {
    Write-HostWithTimestamp 'Killing running instances of Visual Studio.'
    Write-RunningProcesses
    KillRunningInstancesOfVS
    Write-RunningProcesses
    Write-HostWithTimestamp 'Killed running instances of Visual Studio.'
    $numberOfTries++
    Write-HostWithTimestamp "Attempt (# $numberOfTries) to downgrade VSIX..."
    $success = DowngradeVSIX $NuGetVSIXID $VSVersion $ProcessExitTimeoutInSeconds
    Write-HostWithTimestamp 'Attempt complete.'
}
until (($success -eq $true) -or ($numberOfTries -gt 3))

# Clearing MEF cache helps load the right dlls for VSIX
Write-HostWithTimestamp "Clearing MEF cache."
ClearMEFCache
Write-HostWithTimestamp "Cleared MEF cache."

$numberOfTries = 0
$success = $false
do {
    Write-HostWithTimestamp 'Killing running instances of Visual Studio.'
    Write-RunningProcesses
    KillRunningInstancesOfVS
    Write-RunningProcesses
    Write-HostWithTimestamp 'Killed running instances of Visual Studio.'
    $numberOfTries++
    Write-HostWithTimestamp "Attempt (# $numberOfTries) to install VSIX..."
    $success = InstallVSIX $VSIXPath $VSVersion $ProcessExitTimeoutInSeconds
    Write-HostWithTimestamp 'Attempt complete.'
}
until (($success -eq $true) -or ($numberOfTries -gt 3))

if ($success -eq $false) {
    exit 1
}

Write-HostWithTimestamp 'Clearing MEF cache.'
ClearMEFCache
Write-HostWithTimestamp 'Cleared MEF cache.'
Write-HostWithTimestamp 'Updating Visual Studio configuration.'
Update-Configuration
Write-HostWithTimestamp 'Updated Visual Studio configuration.'
