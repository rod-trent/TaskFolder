# TaskFolder Installer Guide

This guide explains how to build and distribute the TaskFolder installer using Inno Setup.

## Prerequisites

1. **Inno Setup 6.x** - Download from: https://jrsoftware.org/isdl.php
2. **Visual Studio 2022** or `.NET 8 SDK`
3. Your TaskFolder project compiled and published

## Building the Application

Before creating the installer, you need to publish your application:

### Option 1: Framework-Dependent Deployment (Smaller installer)

```bash
dotnet publish TaskFolder.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=false
```

### Option 2: Self-Contained Deployment (Includes .NET Runtime - Recommended)

```bash
dotnet publish TaskFolder.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false
```

This creates files in: `bin\Release\net8.0-windows\win-x64\publish\`

## Customizing the Installer Script

1. **Generate a unique GUID** for your application:
   - In Inno Setup, click Tools → Generate GUID
   - Replace `{YOUR-GUID-HERE}` in the script

2. **Update the publisher URL**:
   - Change `#define MyAppURL` to your actual GitHub or website URL

3. **Adjust file paths** if needed:
   - The script assumes standard .NET publish output structure
   - Modify paths if you have custom resource locations

4. **Create a LICENSE.txt** file in your project root (required by the script)

5. **Optional**: Customize the app icon path if different from `Resources\TaskFolder.ico`

## Building the Installer

### Method 1: Using Inno Setup Compiler GUI

1. Open Inno Setup Compiler
2. File → Open → Select `TaskFolder.iss`
3. Build → Compile (or press F9)
4. The installer will be created in the `installer` folder

### Method 2: Using Command Line

```bash
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" TaskFolder.iss
```

## Installer Output

The build process creates:
- `installer\TaskFolder-Setup-1.0.0.exe` - Your distributable installer

## Testing the Installer

1. Run the installer on a clean Windows 11 machine
2. Test both installation paths:
   - Install for all users (requires admin)
   - Install for current user only
3. Verify:
   - Desktop icon creation (if selected)
   - Start menu entry
   - Startup option (if selected)
   - Application launches correctly
   - Uninstaller works properly

## Distribution

### File Signing (Recommended)

For production releases, sign your installer with a code signing certificate:

```bash
signtool sign /f "your-certificate.pfx" /p "password" /t http://timestamp.digicert.com "installer\TaskFolder-Setup-1.0.0.exe"
```

### Checksum Generation

Generate SHA-256 checksums for verification:

```bash
certutil -hashfile "installer\TaskFolder-Setup-1.0.0.exe" SHA256
```

## Installer Features

The created installer includes:

- ✅ Windows 11 version check
- ✅ Optional desktop shortcut
- ✅ Optional startup with Windows
- ✅ Proper uninstallation with data cleanup option
- ✅ Detection of running application during uninstall
- ✅ Start menu shortcuts
- ✅ Modern wizard style
- ✅ Compressed installation package

## Updating the Version

When releasing a new version:

1. Update version in `TaskFolder.csproj`:
   ```xml
   <Version>1.0.1</Version>
   ```

2. Update version in `TaskFolder.iss`:
   ```
   #define MyAppVersion "1.0.1"
   ```

3. Rebuild both the application and installer

## File Structure

Your project should have this structure for the installer to work:

```
TaskFolder/
├── TaskFolder.csproj
├── TaskFolder.iss              ← Inno Setup script
├── LICENSE.txt                 ← Required
├── README.md                   ← Optional
├── Resources/
│   ├── TaskFolder.ico
│   └── Icons/                  ← Optional
├── bin/
│   └── Release/
│       └── net8.0-windows/
│           └── publish/        ← Published files
└── installer/                  ← Output directory (created automatically)
```

## Troubleshooting

### "File not found" errors
- Ensure you've published the application first
- Check that file paths in the .iss script match your project structure

### Installer won't compile
- Verify Inno Setup 6.x is installed
- Check that all referenced files exist
- Ensure LICENSE.txt exists in project root

### Application won't run after installation
- Verify .NET 8 Runtime is installed (if framework-dependent)
- Check that all DLLs were included in publish output
- Test with self-contained deployment instead

## Advanced Customization

### Adding a custom welcome page
Add to `[Code]` section:

```pascal
procedure InitializeWizard();
var
  WelcomePage: TOutputMsgWizardPage;
begin
  WelcomePage := CreateOutputMsgPage(wpWelcome,
    'Welcome to TaskFolder Setup',
    'Quick application launcher for Windows 11',
    'This will install TaskFolder on your computer. Click Next to continue.');
end;
```

### Silent installation
Users can install silently:

```bash
TaskFolder-Setup-1.0.0.exe /VERYSILENT /NORESTART
```

## Support

For issues or questions:
- GitHub Issues: [Your repo URL]
- Email: [Your email]
