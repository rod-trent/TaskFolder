# QUICK FIX for "No files found matching" Error

## The Problem

Inno Setup can't find your published files because the path doesn't match where your files actually are.

## The Solution - 3 Easy Steps

### Step 1: Publish Your Application First

Open PowerShell in your TaskFolder project directory and run:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true
```

This creates files in: `bin\Release\net8.0-windows\win-x64\publish\`

### Step 2: Use the Correct Inno Setup Script

Use **TaskFolder-Flexible.iss** instead of TaskFolder.iss (or update the path in the original).

The flexible version already has the correct path:
```
#define PublishPath "bin\Release\net8.0-windows\win-x64\publish"
```

### Step 3: Build the Installer

In Inno Setup Compiler:
1. Open **TaskFolder-Flexible.iss**
2. Press **F9** to compile
3. Done!

## Alternative: If You Published Differently

If you used a different publish command, update line 20 in the .iss file:

### For Framework-Dependent Build:
```
dotnet publish -c Release
```
Use this in .iss:
```
#define PublishPath "bin\Release\net8.0-windows\publish"
```

### For Self-Contained Build:
```
dotnet publish -c Release -r win-x64 --self-contained true
```
Use this in .iss:
```
#define PublishPath "bin\Release\net8.0-windows\win-x64\publish"
```

### For Custom Publish Location:
```
dotnet publish -c Release -o publish
```
Use this in .iss:
```
#define PublishPath "publish"
```

## Verify the Path

Before running Inno Setup, check that these files exist:

1. Navigate to your project folder
2. Open the path in File Explorer: `bin\Release\net8.0-windows\win-x64\publish\`
3. Confirm you see **TaskFolder.exe** and other DLL files
4. If the folder is empty or doesn't exist, you need to run `dotnet publish` first

## Still Having Issues?

### Error: "No files found"
- Make sure you ran `dotnet publish` **before** building the installer
- Check that the path in the .iss file matches where your files are
- Look for TaskFolder.exe in your bin folder to find the correct path

### Error: Missing LICENSE.txt
- Create a LICENSE.txt file in your project root, or
- Remove the line `LicenseFile=LICENSE.txt` from the [Setup] section

### Error: Missing icon file
- Make sure `Resources\TaskFolder.ico` exists, or
- Comment out the line: `SetupIconFile=Resources\{#MyAppIconName}`
