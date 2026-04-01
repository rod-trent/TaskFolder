# NuGet Package Troubleshooting Guide

## Issue: System.Drawing.Common Version Error

If you're getting errors about System.Drawing.Common version, here are solutions:

### Solution 1: Updated Project File (Already Applied)
The project file has been updated to use version 6.0.0 instead of 8.0.0.

Try building again:
```bash
dotnet restore
dotnet build
```

### Solution 2: Remove Package Reference Entirely
Since `UseWindowsForms=true` is set, System.Drawing should be included automatically.

**Option A: Replace the .csproj file**
```bash
# Backup current file
copy TaskFolder.csproj TaskFolder.csproj.backup

# Use the version without package references
copy TaskFolder-NoPackages.csproj TaskFolder.csproj

# Try building
dotnet restore
dotnet build
```

**Option B: Edit manually**
Remove these lines from TaskFolder.csproj:
```xml
  <ItemGroup>
    <PackageReference Include="System.Drawing.Common" Version="6.0.0" />
  </ItemGroup>
```

### Solution 3: Enable Online NuGet Sources
If you need the latest packages:

1. **Add NuGet.org source:**
```bash
dotnet nuget add source https://api.nuget.org/v3/index.json -n nuget.org
```

2. **Clear NuGet cache:**
```bash
dotnet nuget locals all --clear
```

3. **Restore packages:**
```bash
dotnet restore --force
```

### Solution 4: Update to .NET 8 (Optional)
If you have .NET 8 SDK installed:

1. Change TargetFramework in TaskFolder.csproj:
```xml
<TargetFramework>net8.0-windows</TargetFramework>
```

2. Update package version (if keeping the package reference):
```xml
<PackageReference Include="System.Drawing.Common" Version="8.0.0" />
```

## Verification Steps

After applying any solution:

1. **Clean the project:**
```bash
dotnet clean
```

2. **Restore packages:**
```bash
dotnet restore
```

3. **Build:**
```bash
dotnet build
```

## Quick Fix Commands

Try running these in order:

```bash
# 1. Clean everything
dotnet clean

# 2. Clear NuGet cache
dotnet nuget locals all --clear

# 3. Restore with force
dotnet restore --force

# 4. Build
dotnet build
```

## Alternative: Use the Simplified Project File

If nothing else works, use `TaskFolder-NoPackages.csproj`:

```bash
# Rename files
ren TaskFolder.csproj TaskFolder-Original.csproj
ren TaskFolder-NoPackages.csproj TaskFolder.csproj

# Build
dotnet restore
dotnet build
```

This version removes all package references and relies on the framework's built-in libraries.

## Checking Your Environment

Verify your setup:

```bash
# Check .NET version
dotnet --version

# Check NuGet sources
dotnet nuget list source

# Check installed SDKs
dotnet --list-sdks
```

## Still Having Issues?

### Check Visual Studio Offline Packages
The error mentions "Microsoft Visual Studio Offline Packages". This means VS is using only local packages.

**To enable online packages in VS Code:**
1. Open VS Code settings (Ctrl+,)
2. Search for "nuget"
3. Ensure no offline-only restrictions

**To enable online packages in Visual Studio:**
1. Tools → Options
2. NuGet Package Manager → Package Sources
3. Ensure "nuget.org" is checked and enabled

### Use Only Framework Libraries
The good news: System.Drawing.Common isn't strictly necessary! The code uses:
- System.Drawing (included with Windows Forms)
- System.Drawing.Icon (included with Windows Forms)

Both are part of the framework when `UseWindowsForms=true`.

## Recommended Solution

**For most users:**
1. Use the updated TaskFolder.csproj (already has version 6.0.0)
2. Run: `dotnet restore` then `dotnet build`

**If that fails:**
1. Use TaskFolder-NoPackages.csproj instead
2. This removes all package dependencies

## Support

If you continue having issues:
1. Check your .NET SDK version: `dotnet --version`
2. Ensure you have .NET 6.0 or later
3. Try cleaning all build artifacts
4. Try the NoPackages version of the project file

The application should work with the framework libraries alone!
