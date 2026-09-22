# AudioSwitch 打包脚本：版本号从 csproj 读取，发布便携版并编译安装包
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csproj = Join-Path $root 'src\AudioSwitch\AudioSwitch.csproj'
$publishDir = Join-Path $root 'publish'
$installerScript = Join-Path $root 'installer\AudioSwitch.iss'

# 环境里 ProgramFiles 若为空会导致 NuGet 初始化失败
if (-not $env:ProgramFiles) { $env:ProgramFiles = 'C:\Program Files' }
if (-not $env:ProgramW6432) { $env:ProgramW6432 = 'C:\Program Files' }

# 从 csproj 读取唯一版本源
[xml]$proj = Get-Content -LiteralPath $csproj
$version = $proj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "无法从 $csproj 读取 <Version>" }
$versionFull = if ($version -match '^\d+\.\d+\.\d+$') { "$version.0" } else { $version }
Write-Host "Version: $version ($versionFull)"

Write-Host '==> dotnet publish'
dotnet publish $csproj -c Release -o $publishDir --nologo
if ($LASTEXITCODE -ne 0) { throw 'publish 失败' }

# 同步便携版到 安装包\
$dist = Join-Path (Split-Path -Parent $root) '安装包'
if (-not (Test-Path $dist)) { New-Item -ItemType Directory -Path $dist | Out-Null }
Copy-Item -Force (Join-Path $publishDir 'AudioSwitch.exe') (Join-Path $dist 'AudioSwitch.exe')

$iscc = Get-Command 'iscc' -ErrorAction SilentlyContinue
if (-not $iscc) {
    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    ) | Where-Object { $_ -and (Test-Path $_) }
    if ($candidates) { $iscc = $candidates[0] }
}
if (-not $iscc) {
    Write-Warning '未找到 Inno Setup (ISCC)，只生成了便携版。安装包请安装 Inno Setup 后重跑。'
    exit 0
}

Write-Host '==> ISCC'
& $iscc "/DAppVersion=$version" "/DAppVersionFull=$versionFull" $installerScript
if ($LASTEXITCODE -ne 0) { throw 'ISCC 失败' }
Write-Host "完成。输出目录: $dist"
