param(
    [Parameter(Mandatory = $true)]
    [string]$Rid
)

$ErrorActionPreference = "Stop"

$ProjectPath = "src/Polymerium.Avalonia/Polymerium.Avalonia.csproj"
$PackDir = "Publish/$Rid"

Write-Host "Publishing QinmoLauncher for $Rid..."

dotnet publish -c Release --self-contained -r $Rid $ProjectPath -o $PackDir

if ($LASTEXITCODE -ne 0)
{
    Write-Error "dotnet publish failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

# 修正附属程序集目录名的大小写：MSBuild 由 Resources.zh-hans.resx 推导出的目录名是小写 zh-hans，
# 而 .NET 在区分大小写的文件系统上按精确 culture 名（zh-Hans）查找，小写会导致中文界面静默回退成英文。
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

Write-Host "Publish completed successfully."

