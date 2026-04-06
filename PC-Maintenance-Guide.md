# PC-Maintenance-Guide

---

**Title:** PC Maintenance Guide with PowerShell Reference
**Description:** Step-by-step procedures for Windows PC maintenance, with inline PowerShell commands and an automated script.
**Last Updated:** 2026-02-26
**Author:** Keith S. Crawford // @tsudo on Github & Twitter
**DISCLAIMER:** Use at your own risk.
**LICENSE:** [CC-BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/) — Creative Commons Attribution-ShareAlike 4.0 International License.

---

## Automated Script

Most steps below can be run unattended using the included PowerShell script.
Open PowerShell **as Administrator** and run:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
.\PC-Maintenance.ps1
```

Pass `-DryRun` to preview actions without making changes:

```powershell
.\PC-Maintenance.ps1 -DryRun
```

---

## Prerequisites

- **Run PowerShell as Administrator** — right-click the Start button → *Windows PowerShell (Admin)* or *Terminal (Admin)*
- A recent backup or system restore point is recommended before starting
- Internet access is required for update steps

---

## Utility Links

| Tool | Purpose |
|------|---------|
| [BleachBit](https://www.bleachbit.org/download/windows) | Deep junk-file cleaner (open source) |
| [CCleaner Portable](https://www.ccleaner.com/ccleaner/builds) | Registry & temp-file cleaner |
| [PatchMyPC Home Updater](https://patchmypc.com/home-updater) | GUI third-party app updater |
| [Revo Uninstaller Free](https://www.revouninstaller.com/start-freeware-download/) | Deep uninstaller with leftover cleanup |
| [Ninite Installer](https://ninite.com/) / [Essential Apps Bundle](https://ninite.com/7zip-firefox-irfanview-notepadplusplus-vlc/ninite.exe) | Batch install common apps |
| [TeamViewer](https://download.teamviewer.com/download/TeamViewer_Setup_x64.exe) | Remote support |
| [Sysinternals Live](https://live.sysinternals.com/) / [Suite](https://learn.microsoft.com/en-us/sysinternals/downloads/sysinternals-suite) | Advanced system diagnostics |
| [CPU-Z](https://www.cpuid.com/softwares/cpu-z.html) | Hardware identification |

---

## Maintenance Procedures

---

### Step 1 — Create a System Restore Point

Create a restore point before making any changes so you can roll back if something goes wrong.

**Script:** Automated (optional, prompted)

```powershell
# Enable System Restore on C: if not already active
Enable-ComputerRestore -Drive "C:\"

# Create a restore point
Checkpoint-Computer `
    -Description "Pre-Maintenance $(Get-Date -Format 'yyyy-MM-dd')" `
    -RestorePointType MODIFY_SETTINGS
```

> To restore later: open *System Properties → System Protection → System Restore*.

---

### Step 2 — Clear Temporary Files

Removes cached and temporary files from user and system locations to recover disk space.

**Script:** Automated

```powershell
# User temp folders
Remove-Item -Path "$env:TEMP\*"                    -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "$env:TMP\*"                     -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "$env:LOCALAPPDATA\Temp\*"       -Recurse -Force -ErrorAction SilentlyContinue

# System temp folder (requires Admin)
Remove-Item -Path "$env:SystemRoot\Temp\*"         -Recurse -Force -ErrorAction SilentlyContinue

# Run built-in Disk Cleanup with a saved profile
# First-time setup (interactive): cleanmgr.exe /sageset:1
# Run silently with saved profile:
cleanmgr.exe /sagerun:1
```

> **Tip:** Run `cleanmgr.exe /sageset:1` once to choose which categories (Windows Update Cleanup, Thumbnails, etc.) are included in the silent profile.

---

### Step 3 — Empty the Recycle Bin

**Script:** Automated

```powershell
Clear-RecycleBin -Force
```

---

### Step 4 — Flush the DNS Cache

Clears stale DNS entries that can cause slow page loads or broken connections.

**Script:** Automated

```powershell
# Flush
Clear-DnsClientCache

# Verify the cache is clear
Get-DnsClientCache
```

---

### Step 5 — Configure & Run Storage Sense

Storage Sense automatically removes temp files and old Recycle Bin content on a schedule.

**Script:** Automated (registry configuration)

```powershell
$regPath = "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy"
New-Item -Path $regPath -Force | Out-Null

Set-ItemProperty -Path $regPath -Name "01"   -Value 1   # Enable Storage Sense
Set-ItemProperty -Path $regPath -Name "04"   -Value 30  # Run frequency: every 30 days
Set-ItemProperty -Path $regPath -Name "2048" -Value 30  # Delete temp files unused > 30 days
Set-ItemProperty -Path $regPath -Name "08"   -Value 1   # Clean up Recycle Bin automatically
Set-ItemProperty -Path $regPath -Name "256"  -Value 30  # Delete Recycle Bin items > 30 days old

# Open the Settings page to confirm
Start-Process "ms-settings:storagesense"
```

---

### Step 6 — Windows Updates

> **Note:** The old `wuauclt.exe /updatenow` command is deprecated on modern Windows. Use `UsoClient` or the `PSWindowsUpdate` module.

**Script:** Automated

**Option A — PSWindowsUpdate module (recommended for scripting):**

```powershell
# Install the module once (from PowerShell Gallery)
Install-Module -Name PSWindowsUpdate -Force -Scope CurrentUser

# Check for available updates
Import-Module PSWindowsUpdate
Get-WindowsUpdate

# Install all updates without auto-rebooting
Install-WindowsUpdate -AcceptAll -AutoReboot:$false
```

**Option B — Built-in UsoClient (no extra module required):**

```powershell
# Trigger a scan-and-install cycle
UsoClient.exe ScanInstallWait

# Open Windows Update settings to review results
Start-Process "ms-settings:windowsupdate"
```

---

### Step 7 — Microsoft Office Updates *(Manual)*

Office updates through its own Click-to-Run channel and cannot be fully automated without the C2R client.

**Manual steps:** Open any Office application → **File → Account → Update Options → Update Now**

**Script:** Automated for Microsoft 365 / Click-to-Run installations

```powershell
$c2r = "${env:CommonProgramFiles}\microsoft shared\ClickToRun\OfficeC2RClient.exe"
if (Test-Path $c2r) {
    Start-Process $c2r -ArgumentList "/update user" -Wait
    Write-Host "Office update triggered."
} else {
    Write-Host "Click-to-Run client not found. Update manually via File > Account."
}
```

---

### Step 8 — Windows Store App Updates

**Script:** Automated (WMI trigger)

```powershell
# Trigger a Store update scan via MDM WMI interface
$wmiObj = Get-WmiObject `
    -Namespace "root\cimv2\mdm\dmmap" `
    -Class "MDM_EnterpriseModernAppManagement_AppManagement01"
$result = $wmiObj.UpdateScanMethod()
Write-Host "Store update scan result: $($result.ReturnValue)"

# Open the Microsoft Store to verify / install updates manually
Start-Process "ms-windows-store:"
```

---

### Step 9 — Third-Party App Updates (winget)

`winget` is Windows' built-in package manager, available on Windows 10 1709+ via the *App Installer* package.

**Script:** Automated

```powershell
# See what can be upgraded
winget upgrade

# Upgrade everything silently
winget upgrade --all --silent --accept-source-agreements --accept-package-agreements

# Upgrade a single app by ID
winget upgrade --id Mozilla.Firefox
```

> **Tip:** Install `winget` by searching for *App Installer* in the Microsoft Store if the command is not found.

---

### Step 10 — System File Checker (SFC)

Scans Windows system files for corruption and repairs them from a cached copy.

**Script:** Automated

```powershell
# Run as Administrator — takes 10–30 minutes
sfc /scannow

# Check results in the CBS log
Get-Content "$env:SystemRoot\Logs\CBS\CBS.log" |
    Select-String "corrupt|repair|Cannot repair" |
    Select-Object -Last 30
```

> **If SFC cannot repair files**, run DISM (Step 11) first, then re-run SFC.

---

### Step 11 — DISM — Windows Image Health Repair

Repairs the Windows component store that SFC relies on. Requires internet access to download replacement files from Windows Update.

**Script:** Automated

```powershell
# Quick check — reports if the image is flagged as damaged
DISM /Online /Cleanup-Image /CheckHealth

# Full scan — slower, more thorough
DISM /Online /Cleanup-Image /ScanHealth

# Repair — downloads and replaces corrupted components (~10–20 min)
DISM /Online /Cleanup-Image /RestoreHealth
```

> Run DISM *before* SFC when the system is heavily corrupted or SFC keeps failing.

---

### Step 12 — Disk Optimization

`Optimize-Volume` automatically selects the correct action: **TRIM** for SSDs, **defragmentation** for HDDs.

**Script:** Automated

```powershell
# List all fixed drives with size information
Get-Volume | Where-Object { $_.DriveType -eq "Fixed" -and $_.DriveLetter } |
    Select-Object DriveLetter, FileSystem,
        @{N="Size GB";   E={[math]::Round($_.Size / 1GB, 1)}},
        @{N="Free GB";   E={[math]::Round($_.SizeRemaining / 1GB, 1)}} |
    Format-Table -AutoSize

# Analyze a specific drive
Optimize-Volume -DriveLetter C -Analyze -Verbose

# Optimize one drive (auto-selects Defrag or ReTrim)
Optimize-Volume -DriveLetter C -Verbose

# Optimize all fixed NTFS drives
Get-Volume |
    Where-Object { $_.DriveType -eq "Fixed" -and $_.DriveLetter -and $_.FileSystem -eq "NTFS" } |
    ForEach-Object { Optimize-Volume -DriveLetter $_.DriveLetter -Verbose }
```

---

### Step 13 — Review Startup Programs *(Manual)*

Autoruns from Sysinternals provides the most complete view of programs that launch at startup or login.

**Script:** Launcher only (review is manual)

```powershell
# Launch Autoruns directly from Sysinternals Live (no install needed)
Start-Process "\\live.sysinternals.com\tools\Autoruns64.exe"

# Quick list of startup entries via built-in PowerShell
Get-CimInstance Win32_StartupCommand |
    Select-Object Name, Command, Location, User |
    Format-Table -AutoSize

# Check enabled scheduled tasks that run at startup/logon
Get-ScheduledTask |
    Where-Object { $_.State -eq "Ready" -and $_.Triggers.CimClass.CimClassName -match "Logon|Boot" } |
    Select-Object TaskName, TaskPath |
    Format-Table -AutoSize
```

> Disable suspicious entries in Autoruns by unchecking them — this is safer than deleting them outright.

---

## Maintenance Schedule

| Frequency | Tasks |
|-----------|-------|
| **Weekly** | Clear temp files, empty Recycle Bin, flush DNS, Windows Update |
| **Monthly** | winget upgrade, Store updates, Office update, Storage Sense run |
| **Quarterly** | SFC scan, DISM health check, disk optimization, Autoruns review |
| **Annually** | Full backup verification, hardware dust cleaning, review and remove unused apps |

---

## Quick-Reference Command Table

| Task | PowerShell / Command |
|------|----------------------|
| Create restore point | `Checkpoint-Computer -Description "Backup" -RestorePointType MODIFY_SETTINGS` |
| Clear user temp files | `Remove-Item "$env:TEMP\*" -Recurse -Force` |
| Empty Recycle Bin | `Clear-RecycleBin -Force` |
| Flush DNS cache | `Clear-DnsClientCache` |
| Open Storage Sense | `Start-Process "ms-settings:storagesense"` |
| Check for Windows updates | `Get-WindowsUpdate` *(PSWindowsUpdate module)* |
| Install Windows updates | `Install-WindowsUpdate -AcceptAll -AutoReboot:$false` |
| Trigger update scan (built-in) | `UsoClient.exe ScanInstallWait` |
| Update Office (C2R) | `& "${env:CommonProgramFiles}\microsoft shared\ClickToRun\OfficeC2RClient.exe" /update user` |
| List winget upgrades | `winget upgrade` |
| Upgrade all apps (winget) | `winget upgrade --all --silent` |
| System File Checker | `sfc /scannow` |
| DISM image repair | `DISM /Online /Cleanup-Image /RestoreHealth` |
| Optimize drive C | `Optimize-Volume -DriveLetter C -Verbose` |
| View startup programs | `Get-CimInstance Win32_StartupCommand \| Format-Table -AutoSize` |
| Launch Autoruns | `Start-Process "\\live.sysinternals.com\tools\Autoruns64.exe"` |
