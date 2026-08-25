param(
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot 'DuxiuLedger.WinUI/DuxiuLedger.WinUI.csproj'
$definition = Join-Path $PSScriptRoot 'installer/DuxiuLedger.iss'
$publishScript = Join-Path $PSScriptRoot 'publish-win-x64.ps1'
[xml]$projectXml = Get-Content -LiteralPath $project
$version = [string]($projectXml.Project.PropertyGroup.Version | Select-Object -First 1)

if (-not $SkipPublish) {
    & $publishScript
}

$compiler = (Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue).Source
if (-not $compiler) {
    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6/ISCC.exe')
    )
    $compiler = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $compiler) {
    throw "没有找到 Inno Setup 6。请先执行：winget install --id JRSoftware.InnoSetup -e"
}

& $compiler "/DMyAppVersion=$version" $definition
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup 编译失败，退出代码：$LASTEXITCODE"
}

$installer = Join-Path $PSScriptRoot "publish/installer/DuxiuLedger-Setup-v$version-win-x64.exe"
if (-not (Test-Path -LiteralPath $installer)) {
    throw "安装程序没有生成：$installer"
}
Write-Host "安装程序已生成：$installer"
