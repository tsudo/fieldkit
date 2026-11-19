#Requires -Version 5.1
<#
.SYNOPSIS
    Runs the same Windows and winget updates as the FieldKit Updates page.
.EXAMPLE
    .\FieldKit-Update.ps1 -Only Apps
#>
[CmdletBinding(SupportsShouldProcess)]
param([ValidateSet('Windows', 'Apps', 'Both')][string]$Only = 'Both')

$ErrorActionPreference = 'Stop'
if (-not $PSCmdlet.ShouldProcess($env:COMPUTERNAME, "Install FieldKit updates ($Only)")) { exit 0 }
$exe = Join-Path $PSScriptRoot 'FieldKit.exe'
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
    Write-Error 'Place this script beside FieldKit.exe, or run the copy in the installed FieldKit folder.'
    exit 1
}
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not ([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error 'Open an administrator PowerShell to run FieldKit updates.'
    exit 1
}
# Piping a Windows GUI executable waits for completion and streams its redirected output.
& $exe --update $Only | Out-Host
exit $LASTEXITCODE
