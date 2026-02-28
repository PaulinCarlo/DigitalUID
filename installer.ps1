# Downloads and installs SecuGen WBF drivers and .NET SDK.
#
# PREREQUISITES:
#   1) WBF Driver: Download the installer from https://secugen.com/drivers/
#      under "WBF and Windows Hello Drivers" > "SecuGen WBF Driver Installer"
#      (v1.0.0.1, ~22 MB, supports HUPx / HUPx-AP / HU20 / HU20-A etc.)
#      For legacy devices (Hamster IV/Plus, OptiMouse): use "SecuGen WBF Driver Installer (c)" (v2.6.2)
#
#   2) .NET SDK (FDx SDK Pro): SecuGen requires a free registration request.
#      Visit https://secugen.com/request-free-software/ and submit the form.
#      SecuGen will email you a download link for "FDx SDK Pro for Windows".
#
#   3) Once you have both installers, set the paths below and run from an
#      elevated (Administrator) PowerShell prompt.
#
# NOTE: SecuGen distributes drivers via Dropbox token URLs which expire/rotate,
#       so direct URL automation is not reliable. Set $driverPath / $sdkPath
#       to your locally downloaded files, OR set the URLs if you have fresh links.

param(
    [string]$DriverInstaller = "",   # Full path to downloaded WBF driver installer EXE
    [string]$SdkInstaller    = "",   # Full path to downloaded FDx SDK Pro EXE
    [string]$DriverUrl       = "",   # Optional: direct URL if you have a fresh Dropbox link
    [string]$SdkUrl          = "",   # Optional: direct URL if you have a fresh SDK link
    [string]$DriverSha256    = "",   # Optional: SHA256 hash for driver installer
    [string]$SdkSha256       = ""    # Optional: SHA256 hash for SDK installer
)

# --- Helper: open download pages if no local installers provided ---
function Open-DownloadPages {
    Write-Host ""
    Write-Host "=== ACTION REQUIRED ===" -ForegroundColor Yellow
    Write-Host "SecuGen does not provide static download URLs."
    Write-Host ""
    Write-Host "Step 1 - WBF Driver: Opening SecuGen driver page..."
    Write-Host "  -> Download 'SecuGen WBF Driver Installer' (recommended, ~22 MB)"
    Write-Host "  -> Supports: Hamster Pro, Hamster Pro V2, Hamster Pro 20, Duo, Trio, U20 sensors"
    Write-Host "  -> For Hamster IV/Plus/OptiMouse: use 'SecuGen WBF Driver Installer (c)'"
    Start-Process "https://secugen.com/drivers/#wbf-and-windows-hello-drivers"
    Write-Host ""
    Write-Host "Step 2 - FDx .NET SDK: Opening SecuGen free software request form..."
    Write-Host "  -> Fill in the form; SecuGen will email you the download link."
    Start-Process "https://secugen.com/request-free-software/"
    Write-Host ""
    Write-Host "Once downloaded, re-run this script with:"
    Write-Host '  .\Install-SecuGen.ps1 -DriverInstaller "C:\path\to\wbf-driver.exe" -SdkInstaller "C:\path\to\fdx-sdk.exe"'
    Write-Host ""
    exit 0
}

# --- Verify running as Administrator ---
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Error "Please run this script from an elevated (Administrator) PowerShell prompt."
    exit 1
}

$tempDir = Join-Path $env:TEMP "SecuGenInstall"
New-Item -ItemType Directory -Path $tempDir -Force | Out-Null

# --- Resolve installer paths ---
# Priority: explicit path param > URL download > prompt user
function Resolve-Installer {
    param(
        [string]$LocalPath,
        [string]$Url,
        [string]$DestFileName,
        [string]$Label
    )

    if ($LocalPath -and (Test-Path $LocalPath)) {
        Write-Host "Using local $Label installer: $LocalPath"
        return $LocalPath
    }

    if ($Url) {
        $dest = Join-Path $tempDir $DestFileName
        Write-Host "Downloading $Label from $Url ..."
        try {
            Invoke-WebRequest -Uri $Url -OutFile $dest -UseBasicParsing
        } catch {
            Write-Error "Failed to download $Label`: $_"
            exit 1
        }
        return $dest
    }

    return $null
}

function Verify-Hash {
    param([string]$FilePath, [string]$ExpectedHash)
    if ([string]::IsNullOrWhiteSpace($ExpectedHash)) { return $true }
    $actual = (Get-FileHash -Algorithm SHA256 -Path $FilePath).Hash
    if ($actual -ne $ExpectedHash.ToUpper()) {
        Write-Error "Hash mismatch for '$FilePath'.`n  Expected : $($ExpectedHash.ToUpper())`n  Got      : $actual"
        return $false
    }
    Write-Host "Hash verified OK: $FilePath" -ForegroundColor Green
    return $true
}

$driverExe = Resolve-Installer -LocalPath $DriverInstaller -Url $DriverUrl `
    -DestFileName "secugen-wbf-driver.exe" -Label "WBF Driver"

$sdkExe = Resolve-Installer -LocalPath $SdkInstaller -Url $SdkUrl `
    -DestFileName "secugen-fdx-sdk.exe" -Label "FDx SDK"

# If neither installer is available, open the download pages and exit
if (-not $driverExe -or -not $sdkExe) {
    Write-Warning "One or more installers could not be located."
    Open-DownloadPages
}

# --- Hash verification (if hashes supplied) ---
if (-not (Verify-Hash -FilePath $driverExe -ExpectedHash $DriverSha256)) { exit 1 }
if (-not (Verify-Hash -FilePath $sdkExe    -ExpectedHash $SdkSha256))    { exit 1 }

# --- Install WBF Driver ---
# SecuGen's installer (sgdrvsetup_wbf.exe / similar) typically accepts /S for silent install.
# The WBF Driver Installer wraps an InnoSetup or NSIS installer; /SILENT and /NORESTART are common.
Write-Host ""
Write-Host "Installing SecuGen WBF Driver..." -ForegroundColor Cyan
Write-Host "  Windows Biometric Service must be enabled for WBF drivers to function."
$driverProc = Start-Process -FilePath $driverExe -ArgumentList "/SILENT /NORESTART" -Wait -PassThru -Verb RunAs
if ($driverProc.ExitCode -ne 0) {
    Write-Warning "Driver installer exited with code $($driverProc.ExitCode). Check logs if issues occur."
} else {
    Write-Host "WBF Driver installed successfully." -ForegroundColor Green
}

# --- Install FDx SDK Pro ---
# FDx SDK Pro also uses a silent installer; /SILENT or /quiet depending on the packaging.
Write-Host ""
Write-Host "Installing SecuGen FDx SDK Pro for Windows..." -ForegroundColor Cyan
$sdkProc = Start-Process -FilePath $sdkExe -ArgumentList "/SILENT /NORESTART" -Wait -PassThru -Verb RunAs
if ($sdkProc.ExitCode -ne 0) {
    Write-Warning "SDK installer exited with code $($sdkProc.ExitCode). Check logs if issues occur."
} else {
    Write-Host "FDx SDK Pro installed successfully." -ForegroundColor Green
}

# --- Done ---
Write-Host ""
Write-Host "=== Installation complete ===" -ForegroundColor Green
Write-Host "A system restart may be required for the WBF driver to become active."
Write-Host ""
Write-Host "Post-install notes:"
Write-Host "  - Ensure 'Windows Biometric Service' is running (services.msc)"
Write-Host "  - SDK default install path: C:\Program Files\SecuGen\FDx SDK Pro for Windows\"
Write-Host "  - .NET sample projects are in the SDK's Samples\ folder"
Write-Host "  - SecuGen driver page: https://secugen.com/drivers/"
Write-Host "  - SecuGen SDK info:    https://secugen.com/products/sdk/"