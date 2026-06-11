param(
    [string]$Version = "1.0.0",
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $repoRoot "artifacts\publish"
$distDir = Join-Path $repoRoot "dist"
$project = Join-Path $repoRoot "src\OpenTranslate\OpenTranslate.csproj"
$issFile = Join-Path $repoRoot "installer\OpenTranslate.iss"

Write-Host "Publishing OpenTranslate v$Version..." -ForegroundColor Cyan

dotnet publish $project `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -p:Version=$Version `
    -o $publishDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$isccCandidates = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
)

$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    Write-Host ""
    Write-Host "Inno Setup not found. Install it from https://jrsoftware.org/isinfo.php" -ForegroundColor Yellow
    Write-Host "Published files are ready at: $publishDir" -ForegroundColor Yellow
    exit 0
}

Write-Host "Building installer..." -ForegroundColor Cyan

New-Item -ItemType Directory -Force -Path $distDir | Out-Null

& $iscc "/DMyAppVersion=$Version" "/DPublishDir=$publishDir" $issFile

if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compiler failed with exit code $LASTEXITCODE"
}

$installer = Get-ChildItem -Path $distDir -Filter "OpenTranslate-Setup-$Version.exe" | Select-Object -First 1
Write-Host ""
Write-Host "Done: $($installer.FullName)" -ForegroundColor Green
