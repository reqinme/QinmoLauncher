param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$Rid
)

$ErrorActionPreference = "Stop"

$ProjectPath = "src/Polymerium.Avalonia/Polymerium.Avalonia.csproj"

# Extract TargetFramework from csproj
$CsprojContent = Get-Content $ProjectPath -Raw
if ($CsprojContent -match '<TargetFramework>(net[0-9]+\.[0-9]+)</TargetFramework>') {
    $DotnetFramework = $Matches[1]
} else {
    Write-Error "Could not extract TargetFramework from $ProjectPath"
    exit 1
}

# Detect current operating system
# PowerShell Core (6+) has $IsWindows, $IsLinux, $IsMacOS
# Windows PowerShell (5.1) doesn't have these, so we check for them
if ($null -eq $IsWindows) {
    # Running on Windows PowerShell 5.1
    $IsWindows = $true
    $IsLinux = $false
    $IsMacOS = $false
}

$CurrentOS = if ($IsWindows) { "win" } elseif ($IsLinux) { "linux" } elseif ($IsMacOS) { "osx" } else { "unknown" }

# Determine target OS from RID
$TargetOS = if ($Rid -like "win-*") { "win" } elseif ($Rid -like "linux-*") { "linux" } elseif ($Rid -like "osx-*") { "osx" } else { "unknown" }

# Determine if cross-platform packing is needed
$IsCrossPlatform = $CurrentOS -ne $TargetOS

# Determine executable name and icon based on target runtime
if ($Rid -like "win-*") {
    $ExeName = "QinmoLauncher.exe"
    $IconPath = "./src/Polymerium.Avalonia/Assets/Icon.Installer.ico"
} elseif ($Rid -like "osx-*") {
    $ExeName = "QinmoLauncher"
    $IconPath = "./src/Polymerium.Avalonia/Assets/Icon.App.icns"
} else {
    $ExeName = "QinmoLauncher"
    $IconPath = "./src/Polymerium.Avalonia/Assets/Icon.App.png"
}

$PackDir = "Publish/$Rid"

Write-Host "Publishing QinmoLauncher..."
Write-Host "Version: $Version"
Write-Host "Runtime: $Rid"
Write-Host "Framework: $DotnetFramework"
Write-Host "Output Directory: $PackDir"
Write-Host "Executable: $ExeName"
Write-Host "Icon: $IconPath"
Write-Host "Current OS: $CurrentOS"
Write-Host "Target OS: $TargetOS"
Write-Host "Cross-platform: $IsCrossPlatform"

# Clean and publish the project
Write-Host "`nStep 1: Publishing project..."
$PublishArgs = @(
    "publish",
    $ProjectPath,
    "-f", $DotnetFramework,
    "-r", $Rid,
    "-c", "Release",
    "-o", $PackDir,
    "--self-contained",
    "/p:PublishSingleFile=false",
    "/p:IncludeNativeLibrariesForSelfExtract=true"
)

if ($IsCrossPlatform) {
    Write-Host "Running: dotnet $($PublishArgs -join ' ')"
    & dotnet @PublishArgs
    $exitCode = $LASTEXITCODE
} else {
    Write-Host "Running: dotnet $($PublishArgs -join ' ')"
    & dotnet @PublishArgs
    $exitCode = $LASTEXITCODE
}

if ($exitCode -ne 0) {
    Write-Error "dotnet publish failed with exit code $exitCode"
    exit $exitCode
}

Write-Host "Project published successfully to: $PackDir`n"

# 修正附属程序集目录名的大小写：MSBuild 由 Resources.zh-hans.resx 推导出的目录名是小写 zh-hans，
# 而 .NET 在区分大小写的文件系统上按精确 culture 名（zh-Hans）查找，小写会导致中文界面回退成英文。
$SatelliteSource = @(Get-ChildItem -LiteralPath $PackDir -Directory -Filter "zh-hans" -ErrorAction SilentlyContinue |
                     Where-Object { $_.Name -cne "zh-Hans" })
foreach ($Satellite in $SatelliteSource)
{
    $SatelliteTarget = Join-Path $PackDir "zh-Hans"
    $SatelliteStaging = Join-Path $PackDir "zh-hans.staging"
    # 经临时名两步改名，避免在不区分大小写的文件系统上发生目标已存在的冲突。
    Move-Item -LiteralPath $Satellite.FullName -Destination $SatelliteStaging
    if (Test-Path -LiteralPath $SatelliteTarget)
    {
        Remove-Item -LiteralPath $SatelliteTarget -Recurse -Force
    }
    Move-Item -LiteralPath $SatelliteStaging -Destination $SatelliteTarget
    Write-Host "Renamed satellite directory: $($Satellite.Name) -> zh-Hans"
}

# Step 2: Build vpk command arguments
Write-Host "Step 2: Packing with Velopack..."
$VpkArgs = @(
    "pack",
    "--runtime", $Rid,
    "--packId", "QinmoLauncher",
    "--packVersion", $Version,
    "--packDir", $PackDir,
    "--releaseNotes", "README.md",
    "--mainExe", $ExeName,
    "--icon", $IconPath,
    "--packAuthors", "reqinme"
)

if ($Rid -like "osx-*") {
    $VpkArgs += @("--bundleId", "dev.reqinme.QinmoLauncher")
}

# For cross-platform packing, we need to use vpk [platform] pack syntax
# Velopack requires specifying the target platform in brackets when cross-compiling
if ($IsCrossPlatform) {
    Write-Host "Cross-platform packing detected: $CurrentOS -> $TargetOS"
    Write-Host "Running: vpk [$TargetOS] $($VpkArgs -join ' ')"

    # Use Start-Process to handle the bracket syntax properly
    $VpkArgsString = "[$TargetOS] " + ($VpkArgs -join ' ')
    $process = Start-Process -FilePath "vpk" -ArgumentList $VpkArgsString -NoNewWindow -PassThru -Wait
    $exitCode = $process.ExitCode
} else {
    Write-Host "Running: vpk $($VpkArgs -join ' ')"
    & vpk @VpkArgs
    $exitCode = $LASTEXITCODE
}

if ($exitCode -ne 0) {
    Write-Error "vpk pack failed with exit code $exitCode"
    exit $exitCode
}

Write-Host "Velopack packing completed successfully."

# Release 发布会把 obj/project.assets.json 重写成不含 Debug 条件包的版本，
# 导致下一次 Debug 构建报 CS1061（AppBuilder 缺 WithDeveloperTools）。发布后补一次默认还原。
dotnet restore