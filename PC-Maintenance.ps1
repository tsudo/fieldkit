#Requires -RunAsAdministrator

<#
.SYNOPSIS
    Automated Windows PC Maintenance Script

.DESCRIPTION
    Performs common Windows maintenance tasks:
      - System Restore Point creation
      - Temporary file cleanup
      - Recycle Bin cleanup
      - DNS cache flush
      - Storage Sense configuration
      - Windows Update (via PSWindowsUpdate module or UsoClient fallback)
      - Microsoft Office update (Click-to-Run installations)
      - Windows Store app updates
      - Third-party app updates via winget
      - System File Checker (SFC)
      - DISM image health repair
      - Disk optimization (auto-selects Defrag or ReTrim per drive)

    Manual tasks not automated: Autoruns review, uninstaller cleanup.

.PARAMETER DryRun
    Preview all actions without making any changes.

.PARAMETER SkipRestorePoint
    Skip creating a System Restore Point before starting.

.PARAMETER SkipWindowsUpdate
    Skip checking for and installing Windows Updates.

.PARAMETER SkipOfficeUpdate
    Skip triggering a Microsoft Office Click-to-Run update.

.PARAMETER SkipWinget
    Skip upgrading third-party apps via winget.

.PARAMETER SkipStoreUpdate
    Skip triggering a Windows Store app update scan.

.PARAMETER SkipSFC
    Skip the System File Checker scan.

.PARAMETER SkipDISM
    Skip the DISM image health repair.

.PARAMETER SkipDiskOptimize
    Skip disk optimization.

.EXAMPLE
    # Full run
    .\PC-Maintenance.ps1

.EXAMPLE
    # Preview only — no changes made
    .\PC-Maintenance.ps1 -DryRun

.EXAMPLE
    # Quick cleanup pass, skip the slow diagnostic steps
    .\PC-Maintenance.ps1 -SkipWindowsUpdate -SkipSFC -SkipDISM -SkipDiskOptimize

.NOTES
    Author:  Keith S. Crawford // @tsudo on Github & Twitter
    License: CC-BY-SA 4.0
    Requires: PowerShell 5.1+, Windows 10/11, Administrator privileges
#>

[CmdletBinding()]
param(
    [switch]$DryRun,
    [switch]$SkipRestorePoint,
    [switch]$SkipWindowsUpdate,
    [switch]$SkipOfficeUpdate,
    [switch]$SkipWinget,
    [switch]$SkipStoreUpdate,
    [switch]$SkipSFC,
    [switch]$SkipDISM,
    [switch]$SkipDiskOptimize
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Continue"

# ---------------------------------------------------------------------------
# Logging
# ---------------------------------------------------------------------------

$LogFile = "$env:TEMP\PC-Maintenance-$(Get-Date -Format 'yyyyMMdd-HHmmss').log"
$Script:StepResults   = [System.Collections.Generic.List[PSCustomObject]]::new()
$Script:RebootRequired = $false

function Write-Log {
    param(
        [string]$Message,
        [ValidateSet("INFO", "WARN", "ERROR", "OK")]
        [string]$Level = "INFO"
    )
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $entry     = "[$timestamp] [$Level] $Message"

    $color = switch ($Level) {
        "ERROR" { "Red"     }
        "WARN"  { "Yellow"  }
        "OK"    { "Green"   }
        default { "Cyan"    }
    }

    Write-Host $entry -ForegroundColor $color
    Add-Content -Path $LogFile -Value $entry -ErrorAction SilentlyContinue
}

function Write-Section {
    param([string]$Title)
    $sep = "=" * 65
    Write-Log ""
    Write-Log $sep
    Write-Log "  $Title"
    Write-Log $sep
}

function Add-StepResult {
    param([string]$Step, [string]$Status, [string]$Note = "")
    $Script:StepResults.Add([PSCustomObject]@{
        Step   = $Step
        Status = $Status
        Note   = $Note
    })
}

# ---------------------------------------------------------------------------
# Helper functions
# ---------------------------------------------------------------------------

function Get-FreeSpaceGB {
    param([char]$DriveLetter)
    $vol = Get-Volume -DriveLetter $DriveLetter -ErrorAction SilentlyContinue
    if ($vol) { [math]::Round($vol.SizeRemaining / 1GB, 2) } else { $null }
}

function Test-PendingReboot {
    <#
    .SYNOPSIS Checks common registry locations for a pending reboot flag. #>
    $checks = @(
        # Component-Based Servicing (Windows Update)
        { Test-Path "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending" },
        # Windows Update Auto Update
        { Test-Path "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired" },
        # Pending file rename operations (installers, SFC repairs)
        {
            $pfro = Get-ItemProperty `
                -Path "HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager" `
                -Name "PendingFileRenameOperations" `
                -ErrorAction SilentlyContinue
            $null -ne $pfro
        },
        # ConfigMgr / SCCM client
        {
            if (Test-Path "HKLM:\SOFTWARE\Microsoft\SMS\Mobile Client\Reboot Management\RebootData") {
                $reboot = Get-ItemProperty `
                    -Path "HKLM:\SOFTWARE\Microsoft\SMS\Mobile Client\Reboot Management\RebootData" `
                    -ErrorAction SilentlyContinue
                $null -ne $reboot -and $reboot.RebootBy -ne 0
            } else { $false }
        }
    )
    foreach ($check in $checks) {
        if (& $check) { return $true }
    }
    return $false
}

function Test-InternetConnectivity {
    <#
    .SYNOPSIS
        Uses Microsoft's own connectivity test endpoint (same one Windows uses
        for the network tray icon). Returns $true if reachable within 5 seconds.
    #>
    try {
        $null = Invoke-WebRequest `
            -Uri "http://www.msftconnecttest.com/connecttest.txt" `
            -UseBasicParsing -TimeoutSec 5 -ErrorAction Stop
        return $true
    } catch {
        return $false
    }
}

function Get-ServiceStatus {
    param([string]$ServiceName)
    $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($svc) { $svc.Status } else { "NotFound" }
}

# ---------------------------------------------------------------------------
# Header / pre-flight
# ---------------------------------------------------------------------------

Write-Section "PC MAINTENANCE SCRIPT"

if ($DryRun) {
    Write-Log "*** DRY RUN MODE — no changes will be made ***" "WARN"
}

Write-Log "Log file : $LogFile"
Write-Log "Computer : $env:COMPUTERNAME"
Write-Log "User     : $env:USERNAME"
Write-Log "Started  : $(Get-Date)"

try {
    $osCaption = (Get-CimInstance Win32_OperatingSystem -ErrorAction Stop).Caption
    Write-Log "OS       : $osCaption"
} catch {
    Write-Log "OS       : (could not read)" "WARN"
}

# Pre-flight: pending reboot check
if (Test-PendingReboot) {
    Write-Log ""
    Write-Log "!!! WARNING: A system reboot is already pending. !!!" "WARN"
    Write-Log "    Some steps (SFC, DISM, Windows Update) may fail or give misleading" "WARN"
    Write-Log "    results until you reboot. Consider rebooting first, then re-running." "WARN"
    Write-Log ""
}

$cFreeStart = Get-FreeSpaceGB -DriveLetter 'C'
Write-Log "Drive C: free space at start: ${cFreeStart} GB"

# ---------------------------------------------------------------------------
# STEP 1 — System Restore Point
# ---------------------------------------------------------------------------

Write-Section "STEP 1 of 11 — System Restore Point"

if ($SkipRestorePoint) {
    Write-Log "Skipped (SkipRestorePoint flag set)." "WARN"
    Add-StepResult "Restore Point" "Skipped"
} else {
    try {
        Write-Log "Enabling System Restore on C:\..."
        if (-not $DryRun) {
            Enable-ComputerRestore -Drive "C:\" -ErrorAction Stop
        }

        $desc = "Pre-Maintenance $(Get-Date -Format 'yyyy-MM-dd')"
        Write-Log "Creating restore point: '$desc'..."
        if (-not $DryRun) {
            Checkpoint-Computer -Description $desc -RestorePointType MODIFY_SETTINGS -ErrorAction Stop
        }
        Write-Log "Restore point created." "OK"
        Add-StepResult "Restore Point" "OK" $desc

    } catch {
        $msg = $_.Exception.Message
        # Checkpoint-Computer enforces a 24-hour cooldown between restore points.
        # The error message contains "0x81000101" or "only one restore point can be created per day".
        if ($msg -match '0x81000101|per day|24 hour|cannot create') {
            Write-Log "Skipped: a restore point was already created within the last 24 hours." "WARN"
            Write-Log "  Windows limits one automatic restore point per 24 hours." "WARN"
            Write-Log "  To force one anyway: vssadmin create shadow /for=C:" "WARN"
            Add-StepResult "Restore Point" "Skipped" "24-hour cooldown in effect"
        } elseif ($msg -match 'System Restore is disabled|not enabled') {
            Write-Log "System Restore is disabled via Group Policy or system settings." "WARN"
            Write-Log "  To enable manually: Control Panel > System > System Protection" "WARN"
            Add-StepResult "Restore Point" "Warning" "System Restore disabled"
        } else {
            Write-Log "Could not create restore point: $msg" "WARN"
            Add-StepResult "Restore Point" "Warning" $msg
        }
    }
}

# ---------------------------------------------------------------------------
# STEP 2 — Clear Temporary Files
# ---------------------------------------------------------------------------

Write-Section "STEP 2 of 11 — Clear Temporary Files"

$tempPaths = @(
    $env:TEMP,
    $env:TMP,
    "$env:LOCALAPPDATA\Temp",
    "$env:SystemRoot\Temp"
) | Select-Object -Unique

$totalTargeted = 0
$totalRemaining = 0

foreach ($path in $tempPaths) {
    if (-not (Test-Path $path)) { continue }

    $before = (Get-ChildItem -Path $path -Recurse -Force -ErrorAction SilentlyContinue | Measure-Object).Count
    $totalTargeted += $before

    if (-not $DryRun) {
        Remove-Item -Path "$path\*" -Recurse -Force -ErrorAction SilentlyContinue
        $after = (Get-ChildItem -Path $path -Recurse -Force -ErrorAction SilentlyContinue | Measure-Object).Count
        $locked = $after
        $totalRemaining += $locked
        Write-Log "  $path : $before found, $($before - $locked) removed, $locked locked/skipped"
    } else {
        Write-Log "  [DryRun] $path : $before items would be targeted"
    }
}

$removedCount = $totalTargeted - $totalRemaining
Write-Log "Temp cleanup complete: $removedCount removed of $totalTargeted targeted ($totalRemaining files locked by running processes)." "OK"
if ($totalRemaining -gt 0) {
    Write-Log "  Locked files are held open by other processes. They will clear on next reboot." "WARN"
}
Add-StepResult "Clear Temp Files" "OK" "$removedCount of $totalTargeted items cleared"

# ---------------------------------------------------------------------------
# STEP 3 — Empty Recycle Bin
# ---------------------------------------------------------------------------

Write-Section "STEP 3 of 11 — Empty Recycle Bin"

try {
    if (-not $DryRun) {
        Clear-RecycleBin -Force -ErrorAction Stop
    }
    Write-Log "$(if ($DryRun) {'[DryRun] Would empty'} else {'Emptied'}) Recycle Bin." "OK"
    Add-StepResult "Recycle Bin" "OK"
} catch {
    $msg = $_.Exception.Message
    # An empty Recycle Bin throws a non-fatal error on some systems
    if ($msg -match 'no items|empty') {
        Write-Log "Recycle Bin was already empty." "OK"
        Add-StepResult "Recycle Bin" "OK" "Already empty"
    } else {
        Write-Log "Could not empty Recycle Bin: $msg" "WARN"
        Write-Log "  Try manually: right-click Recycle Bin > Empty Recycle Bin" "WARN"
        Add-StepResult "Recycle Bin" "Warning" $msg
    }
}

# ---------------------------------------------------------------------------
# STEP 4 — Flush DNS Cache
# ---------------------------------------------------------------------------

Write-Section "STEP 4 of 11 — Flush DNS Cache"

# The DNS Client service must be running for Clear-DnsClientCache to work.
$dnsServiceStatus = Get-ServiceStatus -ServiceName "Dnscache"
if ($dnsServiceStatus -ne "Running") {
    Write-Log "DNS Client service is '$dnsServiceStatus' — cannot flush cache." "WARN"
    Write-Log "  Some privacy-hardened configurations disable this service." "WARN"
    Write-Log "  Alternative: run 'ipconfig /flushdns' from an elevated Command Prompt." "WARN"
    Add-StepResult "DNS Cache Flush" "Warning" "DNS Client service not running"
} else {
    try {
        if (-not $DryRun) {
            Clear-DnsClientCache -ErrorAction Stop
        }
        Write-Log "$(if ($DryRun) {'[DryRun] Would flush'} else {'Flushed'}) DNS cache." "OK"
        Add-StepResult "DNS Cache Flush" "OK"
    } catch {
        Write-Log "Failed to flush DNS cache: $($_.Exception.Message)" "WARN"
        Write-Log "  Fallback: run 'ipconfig /flushdns' from an elevated Command Prompt." "WARN"
        Add-StepResult "DNS Cache Flush" "Warning" $_.Exception.Message
    }
}

# ---------------------------------------------------------------------------
# STEP 5 — Configure Storage Sense
# ---------------------------------------------------------------------------

Write-Section "STEP 5 of 11 — Configure Storage Sense"

try {
    $regPath = "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy"

    if (-not $DryRun) {
        if (-not (Test-Path $regPath)) {
            New-Item -Path $regPath -Force | Out-Null
        }
        # Force each value to DWORD type so it doesn't get created as string
        $null = New-ItemProperty -Path $regPath -Name "01"   -Value 1  -PropertyType DWord -Force  # Enable
        $null = New-ItemProperty -Path $regPath -Name "04"   -Value 30 -PropertyType DWord -Force  # Monthly
        $null = New-ItemProperty -Path $regPath -Name "2048" -Value 30 -PropertyType DWord -Force  # Temp files > 30 days
        $null = New-ItemProperty -Path $regPath -Name "08"   -Value 1  -PropertyType DWord -Force  # Clean Recycle Bin
        $null = New-ItemProperty -Path $regPath -Name "256"  -Value 30 -PropertyType DWord -Force  # Recycle Bin > 30 days
    }

    Write-Log "$(if ($DryRun) {'[DryRun] Would configure'} else {'Configured'}) Storage Sense (enabled, runs monthly, 30-day thresholds)." "OK"
    Write-Log "  Open Settings > System > Storage > Storage Sense to review."
    Add-StepResult "Storage Sense" "OK" "Enabled, monthly, 30-day thresholds"
} catch {
    Write-Log "Could not configure Storage Sense: $($_.Exception.Message)" "WARN"
    Write-Log "  Configure manually: Settings > System > Storage > Storage Sense" "WARN"
    Add-StepResult "Storage Sense" "Warning" $_.Exception.Message
}

# ---------------------------------------------------------------------------
# STEP 6 — Windows Update
# ---------------------------------------------------------------------------

Write-Section "STEP 6 of 11 — Windows Update"

if ($SkipWindowsUpdate) {
    Write-Log "Skipped (SkipWindowsUpdate flag set)." "WARN"
    Add-StepResult "Windows Update" "Skipped"
} else {
    # Check internet before attempting any update
    Write-Log "Checking internet connectivity..."
    $hasInternet = Test-InternetConnectivity
    if (-not $hasInternet) {
        Write-Log "No internet connection detected. Windows Update requires internet access." "ERROR"
        Write-Log "  Verify your network connection, then re-run with other steps skipped:" "WARN"
        Write-Log "  .\PC-Maintenance.ps1 -SkipSFC -SkipDISM -SkipDiskOptimize" "WARN"
        Add-StepResult "Windows Update" "Error" "No internet — skipped"
    } else {
        Write-Log "Internet connectivity confirmed." "OK"

        # Check Windows Update service
        $wuStatus = Get-ServiceStatus -ServiceName "wuauserv"
        if ($wuStatus -eq "Disabled") {
            Write-Log "Windows Update service is Disabled. Updates cannot run." "ERROR"
            Write-Log "  To enable: Get-Service wuauserv | Set-Service -StartupType Manual" "WARN"
            Add-StepResult "Windows Update" "Error" "Windows Update service is Disabled"
        } else {
            if ($wuStatus -ne "Running") {
                Write-Log "Starting Windows Update service (currently: $wuStatus)..."
                if (-not $DryRun) {
                    Start-Service -Name "wuauserv" -ErrorAction SilentlyContinue
                }
            }

            $pswuAvailable = $false

            if (-not $DryRun) {
                $pswu = Get-Module -ListAvailable -Name PSWindowsUpdate -ErrorAction SilentlyContinue
                if (-not $pswu) {
                    Write-Log "PSWindowsUpdate module not found. Installing from PSGallery..."
                    try {
                        # Ensure NuGet provider is present (required by Install-Module)
                        $nuget = Get-PackageProvider -Name NuGet -ErrorAction SilentlyContinue
                        if (-not $nuget -or $nuget.Version -lt [Version]"2.8.5.201") {
                            Write-Log "Installing NuGet package provider..."
                            Install-PackageProvider -Name NuGet -MinimumVersion 2.8.5.201 `
                                -Force -Scope CurrentUser -ErrorAction Stop | Out-Null
                        }
                        Install-Module -Name PSWindowsUpdate -Force -Scope CurrentUser `
                            -Repository PSGallery -ErrorAction Stop
                        Write-Log "PSWindowsUpdate installed successfully." "OK"
                    } catch {
                        Write-Log "Could not install PSWindowsUpdate: $($_.Exception.Message)" "WARN"
                        Write-Log "  Possible causes: PSGallery blocked by firewall/proxy, TLS version mismatch." "WARN"
                        Write-Log "  Fallback: will use UsoClient instead." "WARN"
                    }
                }
                $pswuAvailable = [bool](Get-Module -ListAvailable -Name PSWindowsUpdate -ErrorAction SilentlyContinue)
            }

            if ($pswuAvailable -and -not $DryRun) {
                try {
                    Import-Module PSWindowsUpdate -ErrorAction Stop
                    Write-Log "Checking for Windows Updates (this may take a few minutes)..."
                    $updates = Get-WindowsUpdate -ErrorAction Stop

                    if ($updates.Count -eq 0) {
                        Write-Log "System is up to date. No updates available." "OK"
                        Add-StepResult "Windows Update" "OK" "Already up to date"
                    } else {
                        Write-Log "Found $($updates.Count) update(s):"
                        $updates | ForEach-Object { Write-Log "  - $($_.Title)" }
                        Write-Log "Installing updates (AutoReboot disabled — reboot manually if prompted)..."
                        Install-WindowsUpdate -AcceptAll -AutoReboot:$false -Confirm:$false -ErrorAction Stop
                        Write-Log "Windows Updates installed. Reboot may be required." "OK"
                        $Script:RebootRequired = $true
                        Add-StepResult "Windows Update" "OK" "$($updates.Count) update(s) installed — reboot required"
                    }
                } catch {
                    Write-Log "PSWindowsUpdate error: $($_.Exception.Message)" "ERROR"
                    Write-Log "  Fallback: open Settings > Windows Update manually." "WARN"
                    Add-StepResult "Windows Update" "Error" $_.Exception.Message
                }
            } else {
                # UsoClient fallback (no extra module needed)
                Write-Log "$(if ($DryRun) {'[DryRun] Would trigger'} else {'Triggering'}) Windows Update via UsoClient..."
                Write-Log "  Note: UsoClient runs asynchronously. Check Settings > Windows Update for progress." "WARN"
                try {
                    if (-not $DryRun) {
                        Start-Process -FilePath "UsoClient.exe" -ArgumentList "ScanInstallWait" `
                            -Wait -PassThru -NoNewWindow | Out-Null
                    }
                    Write-Log "UsoClient scan triggered. Open Settings > Windows Update to review results." "OK"
                    Add-StepResult "Windows Update" "OK" "UsoClient triggered — verify in Settings"
                } catch {
                    Write-Log "UsoClient failed: $($_.Exception.Message)" "ERROR"
                    Write-Log "  Open Settings > Windows Update manually to run updates." "WARN"
                    Add-StepResult "Windows Update" "Error" $_.Exception.Message
                }
            }
        }
    }
}

# ---------------------------------------------------------------------------
# STEP 7 — Microsoft Office Update (Click-to-Run)
# ---------------------------------------------------------------------------

Write-Section "STEP 7 of 11 — Microsoft Office Update"

if ($SkipOfficeUpdate) {
    Write-Log "Skipped (SkipOfficeUpdate flag set)." "WARN"
    Add-StepResult "Office Update" "Skipped"
} else {
    # Check both 64-bit and 32-bit (x86 Office on 64-bit Windows) install paths
    $c2rPaths = @(
        "${env:CommonProgramFiles}\microsoft shared\ClickToRun\OfficeC2RClient.exe",
        "${env:CommonProgramFiles(x86)}\microsoft shared\ClickToRun\OfficeC2RClient.exe"
    )
    $c2r = $c2rPaths | Where-Object { Test-Path $_ } | Select-Object -First 1

    if ($c2r) {
        try {
            Write-Log "Found C2R client: $c2r"
            Write-Log "Triggering Office update (update runs in the background — the script will not wait for it to finish)..."
            if (-not $DryRun) {
                # /update user starts the update service in the background and returns immediately.
                # There is no reliable synchronous wait for C2R updates via command line.
                Start-Process -FilePath $c2r -ArgumentList "/update user" -ErrorAction Stop
            }
            Write-Log "$(if ($DryRun) {'[DryRun] Would trigger'} else {'Office update triggered'})." "OK"
            Write-Log "  Verify completion: open any Office app > File > Account > Update Options." "WARN"
            Add-StepResult "Office Update" "OK" "C2R triggered — verify in File > Account"
        } catch {
            Write-Log "Office C2R update failed: $($_.Exception.Message)" "WARN"
            Write-Log "  Update manually: open any Office app > File > Account > Update Options > Update Now." "WARN"
            Add-StepResult "Office Update" "Warning" $_.Exception.Message
        }
    } else {
        Write-Log "Office Click-to-Run client not found (checked 64-bit and 32-bit paths)." "WARN"
        Write-Log "  This is expected for MSI/volume-licensed Office, or if Office is not installed." "WARN"
        Write-Log "  If Office is installed, update manually: File > Account > Update Options > Update Now." "WARN"
        Add-StepResult "Office Update" "Manual" "C2R client not found"
    }
}

# ---------------------------------------------------------------------------
# STEP 8 — Windows Store App Updates
# ---------------------------------------------------------------------------

Write-Section "STEP 8 of 11 — Windows Store App Updates"

if ($SkipStoreUpdate) {
    Write-Log "Skipped (SkipStoreUpdate flag set)." "WARN"
    Add-StepResult "Store Updates" "Skipped"
} else {
    try {
        Write-Log "Triggering Microsoft Store update scan via MDM CIM interface..."
        if (-not $DryRun) {
            # Use Get-CimInstance (modern) instead of the deprecated Get-WmiObject
            $cimObj = Get-CimInstance `
                -Namespace "root\cimv2\mdm\dmmap" `
                -ClassName "MDM_EnterpriseModernAppManagement_AppManagement01" `
                -ErrorAction Stop
            $result = Invoke-CimMethod -InputObject $cimObj -MethodName "UpdateScanMethod" -ErrorAction Stop
            Write-Log "Store update scan triggered (return value: $($result.ReturnValue))." "OK"
            Write-Log "  Updates install in the background. Open the Store to verify: Start-Process 'ms-windows-store:'"
            Add-StepResult "Store Updates" "OK" "CIM scan triggered"
        } else {
            Write-Log "[DryRun] Would invoke MDM_EnterpriseModernAppManagement_AppManagement01.UpdateScanMethod()"
            Add-StepResult "Store Updates" "OK (DryRun)"
        }
    } catch {
        $msg = $_.Exception.Message
        Write-Log "MDM Store update scan failed: $msg" "WARN"
        if ($msg -match 'not found|invalid class|Invalid namespace') {
            Write-Log "  The MDM WMI class was not found — this is normal on non-enterprise or heavily" "WARN"
            Write-Log "  customized Windows installations." "WARN"
        }
        Write-Log "  ACTION REQUIRED: Open the Microsoft Store > Library > Get updates." "WARN"
        Add-StepResult "Store Updates" "Warning" "Manual update required — open Store > Library"
    }
}

# ---------------------------------------------------------------------------
# STEP 9 — Third-Party App Updates (winget)
# ---------------------------------------------------------------------------

Write-Section "STEP 9 of 11 — Third-Party App Updates (winget)"

if ($SkipWinget) {
    Write-Log "Skipped (SkipWinget flag set)." "WARN"
    Add-StepResult "winget Upgrades" "Skipped"
} else {
    $wingetCmd = Get-Command winget -ErrorAction SilentlyContinue
    if (-not $wingetCmd) {
        Write-Log "winget not found on this system." "WARN"
        Write-Log "  To install: open Microsoft Store > search 'App Installer' > Install." "WARN"
        Write-Log "  Or download from: https://aka.ms/getwinget" "WARN"
        Add-StepResult "winget Upgrades" "Warning" "winget not installed"
    } else {
        Write-Log "winget found: $($wingetCmd.Source)"

        if ($DryRun) {
            Write-Log "[DryRun] Listing available upgrades (no changes will be made):"
            & winget upgrade 2>&1 | ForEach-Object { Write-Log "  [winget] $_" }
            Add-StepResult "winget Upgrades" "OK (DryRun)" "List shown above"
        } else {
            try {
                Write-Log "Running winget upgrade --all (output below)..."
                Write-Log "  NOTE: Some installers ignore --silent and may show interactive prompts." "WARN"
                Write-Log "  If winget appears to hang, check for a hidden installer window." "WARN"

                # Capture output so it lands in the log as well as the console
                $wingetOutput = & winget upgrade --all --silent `
                    --accept-source-agreements --accept-package-agreements 2>&1
                $wingetExit = $LASTEXITCODE

                $wingetOutput | ForEach-Object { Write-Log "  [winget] $_" }

                # Exit code -1978335135 (0x8A150021) = no applicable updates found — treat as success
                if ($wingetExit -eq 0 -or $wingetExit -eq -1978335135) {
                    Write-Log "winget upgrade complete." "OK"
                    Add-StepResult "winget Upgrades" "OK"
                } else {
                    Write-Log "winget exited with code $wingetExit. Some upgrades may have failed." "WARN"
                    Write-Log "  Run 'winget upgrade' manually to see remaining items." "WARN"
                    Add-StepResult "winget Upgrades" "Warning" "Exit code $wingetExit — check manually"
                }
            } catch {
                Write-Log "winget upgrade failed to run: $($_.Exception.Message)" "ERROR"
                Add-StepResult "winget Upgrades" "Error" $_.Exception.Message
            }
        }
    }
}

# ---------------------------------------------------------------------------
# STEP 10 — System File Checker (SFC)
# ---------------------------------------------------------------------------

Write-Section "STEP 10 of 11 — System File Checker (SFC)"

if ($SkipSFC) {
    Write-Log "Skipped (SkipSFC flag set)." "WARN"
    Add-StepResult "SFC Scan" "Skipped"
} else {
    Write-Log "Running SFC /scannow — this may take 10–30 minutes. Please wait..."

    try {
        if (-not $DryRun) {
            $proc = Start-Process -FilePath "sfc.exe" `
                -ArgumentList "/scannow" -Wait -PassThru -NoNewWindow -ErrorAction Stop
            $sfcExit = $proc.ExitCode
            Write-Log "SFC process exited (code: $sfcExit)."

            # Parse the CBS log for the SFC result summary.
            # CBS.log is cumulative and can be very large — only read the tail.
            $cbsLog = "$env:SystemRoot\Logs\CBS\CBS.log"
            if (Test-Path $cbsLog) {
                $recentLines = Get-Content $cbsLog -Tail 500 -ErrorAction SilentlyContinue
                # SFC entries are tagged [SR] in CBS.log
                $sfcLines = $recentLines | Where-Object { $_ -match '\[SR\]' }

                if ($sfcLines) {
                    # Find the last "Windows Resource Protection" summary line
                    $summary = $sfcLines |
                        Where-Object { $_ -match 'Windows Resource Protection' } |
                        Select-Object -Last 1

                    if ($summary -match 'did not find any integrity violations') {
                        Write-Log "SFC: No integrity violations found. System files are healthy." "OK"
                        Add-StepResult "SFC Scan" "OK" "No violations found"

                    } elseif ($summary -match 'successfully repaired') {
                        Write-Log "SFC: Corrupt files were found and successfully repaired." "WARN"
                        Write-Log "  A reboot is recommended to complete the repair." "WARN"
                        Write-Log "  After rebooting, re-run SFC to confirm all files are repaired." "WARN"
                        $Script:RebootRequired = $true
                        Add-StepResult "SFC Scan" "Warning" "Files repaired — reboot then re-run SFC"

                    } elseif ($summary -match 'unable to fix') {
                        Write-Log "SFC: Corrupt files found but COULD NOT be repaired." "ERROR"
                        Write-Log "  Resolution steps:" "ERROR"
                        Write-Log "    1. Run DISM /Online /Cleanup-Image /RestoreHealth  (Step 11)" "ERROR"
                        Write-Log "    2. Reboot the system" "ERROR"
                        Write-Log "    3. Re-run SFC /scannow" "ERROR"
                        Write-Log "  Full repair log: $env:SystemRoot\Logs\CBS\CBS.log" "WARN"
                        Add-StepResult "SFC Scan" "Error" "Unfixable corruption — run DISM, reboot, re-run SFC"

                    } elseif ($summary -match 'could not perform') {
                        Write-Log "SFC: Could not perform the requested operation." "ERROR"
                        Write-Log "  This sometimes occurs when SFC is run while the system needs a reboot." "WARN"
                        Write-Log "  Try rebooting and re-running the script, or run SFC from WinRE." "WARN"
                        Add-StepResult "SFC Scan" "Error" "Could not perform operation — try after reboot"

                    } else {
                        Write-Log "SFC completed. Review CBS.log for details: $cbsLog" "WARN"
                        $sfcLines | Select-Object -Last 5 | ForEach-Object { Write-Log "  $_" "WARN" }
                        Add-StepResult "SFC Scan" "Warning" "Review CBS.log"
                    }
                } else {
                    Write-Log "No [SR] entries found in recent CBS log. SFC output may be unavailable." "WARN"
                    Write-Log "  Try re-running SFC manually: sfc /scannow" "WARN"
                    Add-StepResult "SFC Scan" "Warning" "No CBS log entries — re-run manually"
                }
            } else {
                Write-Log "CBS.log not found at expected path: $cbsLog" "WARN"
                Add-StepResult "SFC Scan" "Warning" "CBS.log not found"
            }
        } else {
            Write-Log "[DryRun] Would run: sfc /scannow"
            Add-StepResult "SFC Scan" "OK (DryRun)"
        }
    } catch {
        Write-Log "SFC failed to launch: $($_.Exception.Message)" "ERROR"
        Write-Log "  Ensure you are running as Administrator." "WARN"
        Add-StepResult "SFC Scan" "Error" $_.Exception.Message
    }
}

# ---------------------------------------------------------------------------
# STEP 11 — DISM Image Health Repair
# ---------------------------------------------------------------------------

Write-Section "STEP 11 of 11 — DISM Image Health Repair"

if ($SkipDISM) {
    Write-Log "Skipped (SkipDISM flag set)." "WARN"
    Add-StepResult "DISM Repair" "Skipped"
} else {
    # DISM /RestoreHealth downloads files from Windows Update — internet required
    Write-Log "Checking internet connectivity (required for DISM /RestoreHealth)..."
    $hasInternet = Test-InternetConnectivity

    if (-not $hasInternet) {
        Write-Log "No internet connection. DISM /RestoreHealth requires internet access to download repair files." "ERROR"
        Write-Log "  To run offline, mount a Windows ISO and use:" "WARN"
        Write-Log "  DISM /Online /Cleanup-Image /RestoreHealth /Source:WIM:D:\sources\install.wim:1 /LimitAccess" "WARN"
        Add-StepResult "DISM Repair" "Error" "No internet — skipped"
    } else {
        Write-Log "Running DISM /RestoreHealth — this may take 10–20 minutes. Please wait..."

        try {
            if (-not $DryRun) {
                $proc = Start-Process -FilePath "dism.exe" `
                    -ArgumentList "/Online /Cleanup-Image /RestoreHealth" `
                    -Wait -PassThru -NoNewWindow -ErrorAction Stop
                $dismExit = $proc.ExitCode

                # Map common DISM exit codes to actionable messages
                switch ($dismExit) {
                    0 {
                        Write-Log "DISM: Image health verified/repaired successfully." "OK"
                        Add-StepResult "DISM Repair" "OK" "No issues found"
                    }
                    3010 {
                        Write-Log "DISM: Repair successful — a reboot is required to complete the operation." "WARN"
                        $Script:RebootRequired = $true
                        Add-StepResult "DISM Repair" "OK" "Repaired — reboot required"
                    }
                    -2146498529 {  # 0x800f081f — source files not found
                        Write-Log "DISM Error 0x800f081f: Source files not found." "ERROR"
                        Write-Log "  Windows Update could not provide the repair source." "ERROR"
                        Write-Log "  Fixes:" "ERROR"
                        Write-Log "    - Ensure Windows Update is not paused (Settings > Windows Update)" "ERROR"
                        Write-Log "    - Use a mounted Windows ISO as the source:" "WARN"
                        Write-Log "      DISM /Online /Cleanup-Image /RestoreHealth /Source:WIM:D:\sources\install.wim:1" "WARN"
                        Add-StepResult "DISM Repair" "Error" "0x800f081f — source not found; see log"
                    }
                    -2146498810 {  # 0x800f0906 — download failed
                        Write-Log "DISM Error 0x800f0906: Could not download repair files." "ERROR"
                        Write-Log "  Fixes:" "ERROR"
                        Write-Log "    - Check Windows Update service: Get-Service wuauserv" "WARN"
                        Write-Log "    - Temporarily disable proxy/VPN and retry" "WARN"
                        Write-Log "    - Try: net stop wuauserv && net start wuauserv then re-run" "WARN"
                        Add-StepResult "DISM Repair" "Error" "0x800f0906 — download failed; see log"
                    }
                    -2146498809 {  # 0x800f0907 — invalid component
                        Write-Log "DISM Error 0x800f0907: Invalid or unsupported component." "ERROR"
                        Write-Log "  This Windows version may not support this DISM operation." "WARN"
                        Add-StepResult "DISM Repair" "Error" "0x800f0907 — unsupported operation"
                    }
                    default {
                        Write-Log "DISM completed with exit code $dismExit." "WARN"
                        Write-Log "  Review the DISM log for details: $env:SystemRoot\Logs\DISM\dism.log" "WARN"
                        # Try to surface the last error line from dism.log
                        $dismLog = "$env:SystemRoot\Logs\DISM\dism.log"
                        if (Test-Path $dismLog) {
                            $dismError = Get-Content $dismLog -Tail 30 -ErrorAction SilentlyContinue |
                                Where-Object { $_ -match 'Error|Failed' } |
                                Select-Object -Last 3
                            if ($dismError) {
                                Write-Log "  Last DISM error lines:" "WARN"
                                $dismError | ForEach-Object { Write-Log "    $_" "WARN" }
                            }
                        }
                        Add-StepResult "DISM Repair" "Warning" "Exit code $dismExit — review dism.log"
                    }
                }
            } else {
                Write-Log "[DryRun] Would run: DISM /Online /Cleanup-Image /RestoreHealth"
                Add-StepResult "DISM Repair" "OK (DryRun)"
            }
        } catch {
            Write-Log "DISM failed to launch: $($_.Exception.Message)" "ERROR"
            Write-Log "  Ensure you are running as Administrator." "WARN"
            Add-StepResult "DISM Repair" "Error" $_.Exception.Message
        }
    }
}

# ---------------------------------------------------------------------------
# BONUS — Disk Optimization
# ---------------------------------------------------------------------------

Write-Section "BONUS — Disk Optimization"

if ($SkipDiskOptimize) {
    Write-Log "Skipped (SkipDiskOptimize flag set)." "WARN"
    Add-StepResult "Disk Optimization" "Skipped"
} else {
    try {
        $volumes = Get-Volume |
            Where-Object { $_.DriveType -eq "Fixed" -and $_.DriveLetter -and $_.FileSystem -eq "NTFS" }

        if (-not $volumes) {
            Write-Log "No fixed NTFS volumes found to optimize." "WARN"
            Add-StepResult "Disk Optimization" "Warning" "No NTFS volumes found"
        } else {
            $optimized = 0
            foreach ($vol in $volumes) {
                $letter = $vol.DriveLetter
                $freeGB = [math]::Round($vol.SizeRemaining / 1GB, 1)
                $sizeGB = [math]::Round($vol.Size / 1GB, 1)

                Write-Log "Optimizing ${letter}: ($freeGB GB free of $sizeGB GB)..."

                if (-not $DryRun) {
                    try {
                        # Optimize-Volume auto-selects ReTrim (SSD) or Defrag (HDD).
                        # Redirect the Verbose stream (4) so output reaches our log.
                        Optimize-Volume -DriveLetter $letter -Verbose -ErrorAction Stop 4>&1 |
                            ForEach-Object { Write-Log "  [optimize] $_" }
                        $optimized++
                    } catch {
                        Write-Log "  Could not optimize ${letter}: $($_.Exception.Message)" "WARN"
                        Write-Log "  This can happen if the drive is busy, BitLocker is mid-operation, or" "WARN"
                        Write-Log "  the Optimize Drives service (defragsvc) is disabled." "WARN"
                    }
                } else {
                    Write-Log "  [DryRun] Would run: Optimize-Volume -DriveLetter $letter"
                    $optimized++
                }
            }
            Write-Log "Disk optimization complete ($optimized of $($volumes.Count) drives processed)." "OK"
            Add-StepResult "Disk Optimization" "OK" "$optimized of $($volumes.Count) drives optimized"
        }
    } catch {
        Write-Log "Disk optimization error: $($_.Exception.Message)" "ERROR"
        Add-StepResult "Disk Optimization" "Error" $_.Exception.Message
    }
}

# ---------------------------------------------------------------------------
# Summary Report
# ---------------------------------------------------------------------------

Write-Section "MAINTENANCE SUMMARY"

$cFreeEnd = Get-FreeSpaceGB -DriveLetter 'C'
if ($null -ne $cFreeStart -and $null -ne $cFreeEnd) {
    $freed = [math]::Round($cFreeEnd - $cFreeStart, 2)
    $freedLabel = if ($freed -ge 0) { "+${freed} GB freed" } else { "${freed} GB (other processes wrote to disk)" }
    Write-Log "Drive C: free space: ${cFreeStart} GB → ${cFreeEnd} GB  ($freedLabel)"
} else {
    Write-Log "Drive C: free space data unavailable." "WARN"
}

Write-Log ""
Write-Log "Step Results:"

$Script:StepResults | ForEach-Object {
    $icon = switch -Regex ($_.Status) {
        '^OK'      { "[OK]   " }
        'Skipped'  { "[SKIP] " }
        'Warning'  { "[WARN] " }
        'Manual'   { "[MAN]  " }
        default    { "[ERR]  " }
    }
    $line = "$icon $($_.Step)"
    if ($_.Note) { $line += " — $($_.Note)" }
    $level = switch -Regex ($_.Status) {
        '^OK'      { "OK"   }
        'Skipped'  { "WARN" }
        'Warning'  { "WARN" }
        default    { "ERROR" }
    }
    Write-Log $line $level
}

Write-Log ""

if ($Script:RebootRequired) {
    Write-Log "!!! ACTION REQUIRED: ONE OR MORE STEPS REQUIRE A SYSTEM REBOOT !!!" "ERROR"
    Write-Log "    Reboot before running this script again or relying on repair results." "ERROR"
    Write-Log ""
}

Write-Log "MANUAL TASKS REMAINING:"
Write-Log "  1. Review startup programs with Autoruns (run as Admin):"
Write-Log "       Start-Process '\\live.sysinternals.com\tools\Autoruns64.exe'"
Write-Log "  2. Verify Office update: any Office app > File > Account > Update Options"
Write-Log "  3. Verify Store updates: open Microsoft Store > Library > Get updates"
Write-Log "  4. Review Windows Update history: Settings > Windows Update > Update history"
Write-Log "  5. For deeper junk-file removal, consider BleachBit (open source) or CCleaner"
Write-Log ""
Write-Log "Log file  : $LogFile"
Write-Log "Completed : $(Get-Date)"
