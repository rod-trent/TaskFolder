# BuildInstaller.ps1
# Automated build script for TaskFolder installer

param(
    [string]$Configuration = "Release",
    [switch]$SelfContained = $true,
    [switch]$SkipPublish = $false,
    [switch]$SignInstaller = $false,
    [string]$CertificatePath = "",
    [string]$CertificatePassword = ""
)

$ErrorActionPreference = "Stop"

# Color output functions
function Write-Success { Write-Host $args -ForegroundColor Green }
function Write-Info { Write-Host $args -ForegroundColor Cyan }
function Write-Warning { Write-Host $args -ForegroundColor Yellow }
function Write-Failure { Write-Host $args -ForegroundColor Red }

# Configuration
$ProjectFile = "TaskFolder.csproj"
$InnoSetupScript = "TaskFolder.iss"
$InnoSetupPath = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
$OutputDir = "installer"

Write-Info "========================================="
Write-Info "TaskFolder Installer Build Script"
Write-Info "========================================="
Write-Info ""

# Step 1: Check prerequisites
Write-Info "Step 1: Checking prerequisites..."

if (-not (Test-Path $ProjectFile)) {
    Write-Failure "ERROR: $ProjectFile not found in current directory!"
    exit 1
}

if (-not (Test-Path $InnoSetupScript)) {
    Write-Failure "ERROR: $InnoSetupScript not found in current directory!"
    exit 1
}

if (-not (Test-Path $InnoSetupPath)) {
    Write-Warning "WARNING: Inno Setup not found at $InnoSetupPath"
    Write-Warning "Please install Inno Setup 6 from: https://jrsoftware.org/isdl.php"
    exit 1
}

Write-Success "✓ All prerequisites found"
Write-Info ""

# Step 2: Clean previous builds
Write-Info "Step 2: Cleaning previous builds..."

if (Test-Path "bin\$Configuration") {
    Remove-Item -Path "bin\$Configuration" -Recurse -Force
    Write-Success "✓ Cleaned bin\$Configuration"
}

if (Test-Path $OutputDir) {
    Remove-Item -Path $OutputDir -Recurse -Force
    Write-Success "✓ Cleaned $OutputDir"
}

Write-Info ""

# Step 3: Publish the application
if (-not $SkipPublish) {
    Write-Info "Step 3: Publishing application..."
    
    $publishArgs = @(
        "publish",
        $ProjectFile,
        "-c", $Configuration,
        "-r", "win-x64",
        "--self-contained", $SelfContained.ToString().ToLower(),
        "-p:PublishSingleFile=false",
        "-p:PublishReadyToRun=true"
    )
    
    Write-Info "Running: dotnet $($publishArgs -join ' ')"
    
    & dotnet @publishArgs
    
    if ($LASTEXITCODE -ne 0) {
        Write-Failure "ERROR: Application publish failed!"
        exit 1
    }
    
    Write-Success "✓ Application published successfully"
    Write-Info ""
} else {
    Write-Warning "Skipping publish step (using existing build)"
    Write-Info ""
}

# Step 4: Verify published files
Write-Info "Step 4: Verifying published files..."

$PublishPath = "bin\$Configuration\net8.0-windows\win-x64\publish"

if (-not (Test-Path $PublishPath)) {
    Write-Failure "ERROR: Publish path not found: $PublishPath"
    exit 1
}

$ExePath = Join-Path $PublishPath "TaskFolder.exe"
if (-not (Test-Path $ExePath)) {
    Write-Failure "ERROR: TaskFolder.exe not found in publish directory!"
    exit 1
}

$publishedFiles = Get-ChildItem -Path $PublishPath -File
Write-Success "✓ Found $($publishedFiles.Count) published files"
Write-Info ""

# Step 5: Check for required files
Write-Info "Step 5: Checking for required installer files..."

$requiredFiles = @(
    "LICENSE.txt",
    "Resources\TaskFolder.ico"
)

$missingFiles = @()
foreach ($file in $requiredFiles) {
    if (-not (Test-Path $file)) {
        $missingFiles += $file
    }
}

if ($missingFiles.Count -gt 0) {
    Write-Warning "WARNING: The following files are missing (installer will skip them):"
    foreach ($file in $missingFiles) {
        Write-Warning "  - $file"
    }
    Write-Info ""
    Write-Warning "Create a LICENSE.txt file if you want to include a license agreement."
    Write-Info ""
}

# Step 6: Build installer
Write-Info "Step 6: Building installer with Inno Setup..."

$innoArgs = @(
    $InnoSetupScript
)

Write-Info "Running: `"$InnoSetupPath`" $($innoArgs -join ' ')"

& $InnoSetupPath @innoArgs

if ($LASTEXITCODE -ne 0) {
    Write-Failure "ERROR: Installer build failed!"
    exit 1
}

Write-Success "✓ Installer built successfully"
Write-Info ""

# Step 7: Find the installer file
$installerFiles = Get-ChildItem -Path $OutputDir -Filter "*.exe"

if ($installerFiles.Count -eq 0) {
    Write-Failure "ERROR: No installer file found in $OutputDir"
    exit 1
}

$installerPath = $installerFiles[0].FullName
$installerSize = [math]::Round($installerFiles[0].Length / 1MB, 2)

Write-Success "✓ Installer created: $($installerFiles[0].Name)"
Write-Info "  Size: $installerSize MB"
Write-Info "  Path: $installerPath"
Write-Info ""

# Step 8: Sign installer (optional)
if ($SignInstaller) {
    Write-Info "Step 8: Signing installer..."
    
    if (-not (Test-Path $CertificatePath)) {
        Write-Failure "ERROR: Certificate not found at $CertificatePath"
        exit 1
    }
    
    $signToolPath = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.22621.0\x64\signtool.exe"
    
    if (-not (Test-Path $signToolPath)) {
        Write-Warning "WARNING: signtool.exe not found. Skipping code signing."
        Write-Warning "Install Windows SDK for code signing support."
    } else {
        $signArgs = @(
            "sign",
            "/f", $CertificatePath,
            "/p", $CertificatePassword,
            "/t", "http://timestamp.digicert.com",
            "/fd", "SHA256",
            $installerPath
        )
        
        & $signToolPath @signArgs
        
        if ($LASTEXITCODE -eq 0) {
            Write-Success "✓ Installer signed successfully"
        } else {
            Write-Failure "ERROR: Code signing failed!"
            exit 1
        }
    }
    Write-Info ""
}

# Step 9: Generate checksums
Write-Info "Step 9: Generating checksums..."

$sha256Hash = (Get-FileHash -Path $installerPath -Algorithm SHA256).Hash
$md5Hash = (Get-FileHash -Path $installerPath -Algorithm MD5).Hash

$checksumFile = Join-Path $OutputDir "checksums.txt"
$checksumContent = @"
TaskFolder Installer Checksums
Generated: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")

File: $($installerFiles[0].Name)
Size: $installerSize MB

SHA-256: $sha256Hash
MD5:     $md5Hash
"@

$checksumContent | Out-File -FilePath $checksumFile -Encoding UTF8

Write-Success "✓ Checksums generated"
Write-Info "  SHA-256: $sha256Hash"
Write-Info ""

# Step 10: Summary
Write-Success "========================================="
Write-Success "BUILD COMPLETED SUCCESSFULLY!"
Write-Success "========================================="
Write-Info ""
Write-Info "Installer Details:"
Write-Info "  File: $($installerFiles[0].Name)"
Write-Info "  Size: $installerSize MB"
Write-Info "  Location: $installerPath"
Write-Info "  Checksums: $checksumFile"
Write-Info ""
Write-Info "Next Steps:"
Write-Info "  1. Test the installer on a clean Windows 11 machine"
Write-Info "  2. Verify all features work correctly"
Write-Info "  3. Distribute the installer file"
Write-Info ""

# Open explorer to the installer directory
Write-Info "Opening installer directory..."
Start-Process explorer.exe -ArgumentList $OutputDir
