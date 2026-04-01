# Find-PublishPath.ps1
# Helper script to locate TaskFolder.exe and determine the correct publish path for Inno Setup

Write-Host ""
Write-Host "================================================" -ForegroundColor Cyan
Write-Host "TaskFolder Publish Path Finder" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Check if we're in the right directory
if (-not (Test-Path "TaskFolder.csproj")) {
    Write-Host "ERROR: TaskFolder.csproj not found!" -ForegroundColor Red
    Write-Host "Please run this script from your TaskFolder project root directory." -ForegroundColor Yellow
    Write-Host ""
    exit 1
}

Write-Host "Step 1: Checking for existing builds..." -ForegroundColor White

# Step 2: Search for TaskFolder.exe
$files = Get-ChildItem -Path "bin" -Filter "TaskFolder.exe" -Recurse -ErrorAction SilentlyContinue

if ($files.Count -eq 0) {
    Write-Host "  No existing build found." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Step 2: Building the application..." -ForegroundColor White
    Write-Host "  Running: dotnet publish -c Release" -ForegroundColor Gray
    Write-Host ""
    
    # Try to build it
    dotnet publish -c Release
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host ""
        Write-Host "ERROR: Build failed!" -ForegroundColor Red
        exit 1
    }
    
    Write-Host ""
    Write-Host "Step 3: Searching again..." -ForegroundColor White
    $files = Get-ChildItem -Path "bin" -Filter "TaskFolder.exe" -Recurse -ErrorAction SilentlyContinue
}

if ($files.Count -eq 0) {
    Write-Host "ERROR: TaskFolder.exe still not found after build!" -ForegroundColor Red
    Write-Host "Something went wrong with the build process." -ForegroundColor Yellow
    exit 1
}

# Step 3: Display all found locations
Write-Host ""
Write-Host "================================================" -ForegroundColor Green
Write-Host "SUCCESS! Found TaskFolder.exe" -ForegroundColor Green
Write-Host "================================================" -ForegroundColor Green
Write-Host ""

if ($files.Count -gt 1) {
    Write-Host "Found in multiple locations:" -ForegroundColor Yellow
    $index = 1
    foreach ($file in $files) {
        $relativePath = $file.DirectoryName.Replace($PWD.Path + "\", "")
        Write-Host "  [$index] $relativePath" -ForegroundColor White
        $index++
    }
    Write-Host ""
    Write-Host "Using the most recent one:" -ForegroundColor Cyan
}

# Use the most recent one
$latestFile = $files | Sort-Object LastWriteTime -Descending | Select-Object -First 1
$publishPath = $latestFile.DirectoryName.Replace($PWD.Path + "\", "")

Write-Host ""
Write-Host "Publish Directory: $publishPath" -ForegroundColor White
Write-Host "Full Path: $($latestFile.DirectoryName)" -ForegroundColor Gray
Write-Host ""

# Count files in the directory
$allFiles = Get-ChildItem -Path $latestFile.DirectoryName -File
Write-Host "Files in publish directory: $($allFiles.Count)" -ForegroundColor White

# Show some key files
$keyFiles = @("*.dll", "*.exe", "*.json", "*.config")
Write-Host ""
Write-Host "Key files found:" -ForegroundColor Cyan
foreach ($pattern in $keyFiles) {
    $matchingFiles = Get-ChildItem -Path $latestFile.DirectoryName -Filter $pattern -File
    if ($matchingFiles.Count -gt 0) {
        Write-Host "  $pattern : $($matchingFiles.Count) files" -ForegroundColor Gray
    }
}

# Step 4: Generate the Inno Setup configuration
Write-Host ""
Write-Host "================================================" -ForegroundColor Green
Write-Host "COPY THIS INTO YOUR .ISS FILE" -ForegroundColor Green
Write-Host "================================================" -ForegroundColor Green
Write-Host ""
Write-Host "#define PublishPath ""$publishPath""" -ForegroundColor Yellow
Write-Host ""

# Step 5: Offer to update the file automatically
$issFiles = Get-ChildItem -Filter "*.iss" -File

if ($issFiles.Count -gt 0) {
    Write-Host "Found Inno Setup script(s):" -ForegroundColor Cyan
    foreach ($iss in $issFiles) {
        Write-Host "  - $($iss.Name)" -ForegroundColor White
    }
    Write-Host ""
    
    $response = Read-Host "Would you like to automatically update the PublishPath in one of these files? (y/n)"
    
    if ($response -eq 'y' -or $response -eq 'Y') {
        if ($issFiles.Count -eq 1) {
            $selectedFile = $issFiles[0]
        } else {
            Write-Host ""
            Write-Host "Which file would you like to update?"
            for ($i = 0; $i -lt $issFiles.Count; $i++) {
                Write-Host "  [$($i+1)] $($issFiles[$i].Name)"
            }
            $selection = Read-Host "Enter number"
            $selectedFile = $issFiles[[int]$selection - 1]
        }
        
        Write-Host ""
        Write-Host "Updating $($selectedFile.Name)..." -ForegroundColor Cyan
        
        # Read the file
        $content = Get-Content $selectedFile.FullName -Raw
        
        # Update the PublishPath line
        $newPath = $publishPath -replace '\\', '\\'
        $pattern = '#define\s+PublishPath\s+"[^"]*"'
        $replacement = "#define PublishPath ""$publishPath"""
        
        if ($content -match $pattern) {
            $content = $content -replace $pattern, $replacement
            $content | Set-Content $selectedFile.FullName -NoNewline
            Write-Host "SUCCESS! Updated PublishPath in $($selectedFile.Name)" -ForegroundColor Green
        } else {
            Write-Host "WARNING: Could not find PublishPath definition in file." -ForegroundColor Yellow
            Write-Host "Please add this line manually:" -ForegroundColor Yellow
            Write-Host "#define PublishPath ""$publishPath""" -ForegroundColor White
        }
    }
}

Write-Host ""
Write-Host "Next Steps:" -ForegroundColor Cyan
Write-Host "  1. Open your .iss file in Inno Setup Compiler" -ForegroundColor White
Write-Host "  2. Verify the PublishPath is correct" -ForegroundColor White
Write-Host "  3. Press F9 to build the installer" -ForegroundColor White
Write-Host ""
