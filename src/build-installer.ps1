# Velopack Build Script for EFT DMA Radar
# This script builds a Release version and packages it with Velopack

param(
    [string]$Version = "1.0.0",
    [string]$Channel = "stable"
)

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Building EFT DMA Radar Installer" -ForegroundColor Cyan
Write-Host "Version: $Version" -ForegroundColor Cyan
Write-Host "Channel: $Channel" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# Paths
$ProjectRoot = $PSScriptRoot
$SrcDir = Join-Path $ProjectRoot "src"
$ProjectFile = Join-Path $SrcDir "Lone-EFT-DMA-Radar.csproj"
$PublishDir = Join-Path $ProjectRoot "publish"
$ReleaseDir = Join-Path $PublishDir "release"
$OutputDir = Join-Path $ProjectRoot "releases"

# Clean previous builds
Write-Host "`nCleaning previous builds..." -ForegroundColor Yellow
if (Test-Path $PublishDir) {
    Remove-Item -Path $PublishDir -Recurse -Force
}
if (Test-Path $OutputDir) {
    Remove-Item -Path $OutputDir -Recurse -Force
}
New-Item -ItemType Directory -Path $OutputDir | Out-Null

# Build Release
Write-Host "`nBuilding Release configuration..." -ForegroundColor Yellow
dotnet publish $ProjectFile `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -o $ReleaseDir `
    /p:PublishSingleFile=false `
    /p:DebugType=None `
    /p:DebugSymbols=false

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}

Write-Host "Build completed successfully!" -ForegroundColor Green

# Check if vpk (Velopack CLI) is installed
Write-Host "`nChecking for Velopack CLI (vpk)..." -ForegroundColor Yellow
$vpkPath = Get-Command vpk -ErrorAction SilentlyContinue

if (-not $vpkPath) {
    Write-Host "Velopack CLI (vpk) not found!" -ForegroundColor Red
    Write-Host "Installing vpk globally..." -ForegroundColor Yellow
    dotnet tool install -g vpk
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Failed to install vpk!" -ForegroundColor Red
        exit 1
    }
}

# Package with Velopack
Write-Host "`nPackaging with Velopack..." -ForegroundColor Yellow

$IconPath = Join-Path $ProjectRoot "Resources\lone-icon.ico"

vpk pack `
    --packId "EFT-DMA-Radar" `
    --packVersion $Version `
    --packDir $ReleaseDir `
    --mainExe "Lone-EFT-DMA-Radar.exe" `
    --packTitle "Gaming Chair - EFT DMA RADAR" `
    --packAuthors "Lum0s36" `
    --icon $IconPath `
    --outputDir $OutputDir `
    --channel $Channel `
    --delta None

if ($LASTEXITCODE -ne 0) {
    Write-Host "Velopack packaging failed!" -ForegroundColor Red
    exit 1
}

Write-Host "`n========================================" -ForegroundColor Green
Write-Host "Build completed successfully!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host "`nInstaller files created in: $OutputDir" -ForegroundColor Cyan
Write-Host "`nFiles generated:" -ForegroundColor Cyan
Get-ChildItem $OutputDir | ForEach-Object { 
    Write-Host "  - $($_.Name)" -ForegroundColor White
}

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "Next Steps:" -ForegroundColor Cyan
Write-Host "1. Upload Setup.exe to GitHub Releases" -ForegroundColor White
Write-Host "2. Upload releases folder to GitHub Releases" -ForegroundColor White
Write-Host "3. Users can download Setup.exe for first install" -ForegroundColor White
Write-Host "4. App will auto-update from GitHub Releases" -ForegroundColor White
Write-Host "========================================" -ForegroundColor Cyan
