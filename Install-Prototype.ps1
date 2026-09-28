#requires -Version 5.1
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter()]
    [string] $PackageDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path -LiteralPath $PSScriptRoot).Path.TrimEnd('\')
$appPackagesRoot = Join-Path $repositoryRoot 'AppPackages'

function Resolve-ContainedPath {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $Container,
        [Parameter(Mandatory)] [bool] $MustBeDirectory
    )

    $resolved = (Resolve-Path -LiteralPath $Path -ErrorAction Stop).Path.TrimEnd('\')
    $containerPath = (Resolve-Path -LiteralPath $Container -ErrorAction Stop).Path.TrimEnd('\') + '\'
    if (-not $resolved.StartsWith($containerPath, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing a path outside the repository AppPackages directory: $resolved"
    }

    $item = Get-Item -LiteralPath $resolved -ErrorAction Stop
    if ($MustBeDirectory -and -not $item.PSIsContainer) { throw "Expected a directory: $resolved" }
    if (-not $MustBeDirectory -and $item.PSIsContainer) { throw "Expected a file: $resolved" }
    return $resolved
}

$candidateDirectories = @(Get-ChildItem -LiteralPath $appPackagesRoot -Directory -Filter 'MolHub.Windows_*_x64_Debug_Test' | Where-Object {
    $_.Name -match '^MolHub\.Windows_[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+_x64_Debug_Test$'
})
if ([string]::IsNullOrWhiteSpace($PackageDirectory)) {
    if ($candidateDirectories.Count -ne 1) {
        throw 'Pass -PackageDirectory with the single generated x64 Debug_Test AppPackages directory.'
    }
    $PackageDirectory = $candidateDirectories[0].FullName
}

$packageDirectoryPath = Resolve-ContainedPath -Path $PackageDirectory -Container $appPackagesRoot -MustBeDirectory $true
if ((Split-Path -Parent $packageDirectoryPath) -ne $appPackagesRoot) {
    throw "The package directory must be a direct child of $appPackagesRoot"
}
$packageDirectoryName = Split-Path -Leaf $packageDirectoryPath
if ($packageDirectoryName -notmatch '^MolHub\.Windows_(?<version>[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+)_x64_Debug_Test$') {
    throw 'Only a generated MolHub.Windows x64 Debug_Test package directory is accepted.'
}
$packageVersion = $Matches.version

$packagePath = Resolve-ContainedPath -Path (Join-Path $packageDirectoryPath "MolHub.Windows_${packageVersion}_x64_Debug.msix") -Container $packageDirectoryPath -MustBeDirectory $false
$dependenciesDirectory = Resolve-ContainedPath -Path (Join-Path $packageDirectoryPath 'Dependencies\x64') -Container $packageDirectoryPath -MustBeDirectory $true
$dependencyPaths = @(Get-ChildItem -LiteralPath $dependenciesDirectory -Filter '*.msix' -File | ForEach-Object {
    Resolve-ContainedPath -Path $_.FullName -Container $dependenciesDirectory -MustBeDirectory $false
})

$arguments = @{
    Path = $packagePath
    AllowUnsigned = $true
}
if ($dependencyPaths.Count -gt 0) { $arguments.DependencyPath = $dependencyPaths }

if ($PSCmdlet.ShouldProcess($packagePath, 'Install unsigned MolHub for Windows prototype MSIX')) {
    $unlockKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock'
    $unlock = Get-ItemProperty -LiteralPath $unlockKey -ErrorAction SilentlyContinue
    $allowDevelopmentWithoutDevLicense = if ($null -ne $unlock -and $unlock.PSObject.Properties.Name -contains 'AllowDevelopmentWithoutDevLicense') { [int]$unlock.AllowDevelopmentWithoutDevLicense } else { 0 }
    $developerModeEnabled = if ($null -ne $unlock -and $unlock.PSObject.Properties.Name -contains 'DeveloperModeEnabled') { [int]$unlock.DeveloperModeEnabled } else { 0 }
    if (($allowDevelopmentWithoutDevLicense -ne 1) -and ($developerModeEnabled -ne 1)) {
        throw 'Windows Developer Mode is required. Enable Settings > System > For developers > Developer Mode, then run this script again.'
    }
    Add-AppxPackage @arguments
    Write-Output "Installed MolHub for Windows package $packageVersion from $packagePath"
}
else {
    Write-Output "WhatIf: validated package and $($dependencyPaths.Count) x64 dependency package(s); no installation performed."
}
