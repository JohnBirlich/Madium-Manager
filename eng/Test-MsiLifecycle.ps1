[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PreviousMsi,

    [Parameter(Mandatory = $true)]
    [string]$CurrentMsi
)

$ErrorActionPreference = 'Stop'
$displayName = 'Madium Manager'
$installFolder = Join-Path $env:LOCALAPPDATA 'Madium Manager'
$startMenuFolder = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Madium Manager'

function Get-MadiumInstallation {
    return @(
        Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*' -ErrorAction SilentlyContinue |
            Where-Object { $_.DisplayName -eq $displayName }
    )
}

function Invoke-MsiExec {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    $process = Start-Process -FilePath "$env:SystemRoot\System32\msiexec.exe" -ArgumentList $Arguments -Wait -PassThru
    if ($process.ExitCode -notin @(0, 3010)) {
        throw "msiexec $($Arguments[0]) failed with exit code $($process.ExitCode)."
    }
}

function Remove-MadiumInstallations {
    foreach ($installation in @(Get-MadiumInstallation)) {
        Write-Host "Cleanup: removing $($installation.PSChildName)"
        Invoke-MsiExec -Arguments @('/x', $installation.PSChildName, '/qn', '/norestart')
    }
}

try {
    $previous = (Resolve-Path $PreviousMsi).Path
    $current = (Resolve-Path $CurrentMsi).Path

    Write-Host "Installing previous package: $previous"
    Invoke-MsiExec -Arguments @('/i', "`"$previous`"", '/qn', '/norestart')
    $oldEntries = @(Get-MadiumInstallation)
    if ($oldEntries.Count -ne 1) {
        throw "Expected one previous installation, found $($oldEntries.Count)."
    }
    $oldProductCode = $oldEntries[0].PSChildName
    Write-Host "Previous ProductCode: $oldProductCode"

    Write-Host "Installing upgrade: $current"
    Invoke-MsiExec -Arguments @('/i', "`"$current`"", '/qn', '/norestart')
    $newEntries = @(Get-MadiumInstallation)
    if ($newEntries.Count -ne 1) {
        throw "Expected one installation after upgrade, found $($newEntries.Count)."
    }
    if ($newEntries.PSChildName -contains $oldProductCode) {
        throw 'The previous ProductCode is still registered after MajorUpgrade.'
    }
    if (-not (Test-Path (Join-Path $installFolder 'MadiumManager.exe'))) {
        throw 'The upgraded executable was not installed.'
    }
    Write-Host "Upgrade replaced the old installation with $($newEntries[0].PSChildName)."

    Invoke-MsiExec -Arguments @('/x', $newEntries[0].PSChildName, '/qn', '/norestart')
    if (@(Get-MadiumInstallation).Count -ne 0) {
        throw 'The application remains registered after uninstall.'
    }
    if (Test-Path $installFolder) {
        throw "Install folder remains after uninstall: $installFolder"
    }
    if (Test-Path $startMenuFolder) {
        throw "Start Menu folder remains after uninstall: $startMenuFolder"
    }

    Write-Host 'MSI upgrade and uninstall smoke test passed.'
}
finally {
    Remove-MadiumInstallations
}
