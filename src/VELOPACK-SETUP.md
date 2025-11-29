# Velopack Setup Guide

## Building the Installer

### Prerequisites
1. .NET 9 SDK installed
2. PowerShell 7+ (recommended)

### Build Steps

1. **Open PowerShell** in the project root directory

2. **Run the build script**:
```powershell
.\build-installer.ps1 -Version "1.0.0" -Channel "stable"
```

3. **Output files** will be created in the `releases` folder:
   - `Setup.exe` - Main installer (users download this)
   - `RELEASES` - Update manifest file
   - `*.nupkg` - Application package files

## Publishing to GitHub Releases

### Manual Release

1. **Create a new tag**:
```bash
git tag v1.0.0
git push origin v1.0.0
```

2. **Upload to GitHub Releases**:
   - Go to your repository on GitHub
   - Click "Releases" ? "Create a new release"
   - Select the tag you just created
   - Upload all files from the `releases` folder
   - Publish the release

### Automated Release (GitHub Actions)

The `.github/workflows/release.yml` workflow automatically builds and publishes releases when you push a tag:

```bash
git tag v1.0.0
git push origin v1.0.0
```

The workflow will:
1. Build the application
2. Package with Velopack
3. Create a GitHub Release
4. Upload all installer files

## User Installation

### First Time Install

1. User downloads `Setup.exe` from GitHub Releases
2. Runs `Setup.exe`
3. Setup asks for installation directory (default: `C:\Program Files\EFT-DMA-Radar`)
4. Setup installs the application
5. Creates desktop shortcut (optional)
6. Adds to Start Menu

### Updates

The application automatically checks for updates on startup:

1. User starts the application
2. App checks GitHub Releases for new version
3. If update available, shows prompt: "Update Yes/No"
4. If user clicks "Yes":
   - Downloads update in background
   - Installs update
   - Restarts application
5. If user clicks "No":
   - Continues with current version
   - Will check again next startup

## Update Channel Configuration

You can build different update channels:

```powershell
# Stable channel (default)
.\build-installer.ps1 -Version "1.0.0" -Channel "stable"

# Beta channel
.\build-installer.ps1 -Version "1.0.0-beta.1" -Channel "beta"

# Development channel
.\build-installer.ps1 -Version "1.0.0-dev" -Channel "dev"
```

## Dependency Checks

The application checks for required dependencies on startup:
- .NET 9 Runtime
- If missing, shows error with download link

To add more dependency checks, edit `App.xaml.cs` ? `CheckDependencies()` method.

## Troubleshooting

### "vpk not found"

Install Velopack CLI globally:
```powershell
dotnet tool install -g vpk
```

### Update check fails

Make sure your GitHub repository is public, or configure a GitHub token in the UpdateManager initialization.

### Users can't install

Ensure `Setup.exe` is uploaded to GitHub Releases and users download it from the correct URL.

## Configuration

### Update Source

In `App.xaml.cs`, the update source is configured as:

```csharp
var updateManager = new UpdateManager(
    new GithubSource("https://github.com/Lum0s36/EFT-DMA-Radar", null, false)
);
```

Change the URL to match your repository.

### Update Check Frequency

Currently checks on every application startup. To change frequency, modify the `CheckForVelopackUpdatesAsync()` method in `App.xaml.cs`.
