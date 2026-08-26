param(
    [switch]$SkipPublish,
    [switch]$SkipSmokeTest,
    [string]$Version,
    [string]$InnoCompiler
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repositoryRoot 'src/DuxiuLedger.App/DuxiuLedger.App.csproj'
$definition = Join-Path $repositoryRoot 'installer/DuxiuLedger.iss'
$publishScript = Join-Path $PSScriptRoot 'publish-win-x64.ps1'
$smokeTestScript = Join-Path $PSScriptRoot 'test-published-app.ps1'
[xml]$projectXml = Get-Content -LiteralPath $project
$projectVersion = [string]($projectXml.Project.PropertyGroup.Version | Select-Object -First 1)
$releaseVersion = if ([string]::IsNullOrWhiteSpace($Version)) { $projectVersion } else { $Version }

if (-not $SkipPublish) {
    & $publishScript -Version $releaseVersion
}

if (-not $SkipSmokeTest) {
    & $smokeTestScript
}

$compiler = if ($InnoCompiler) { $InnoCompiler } elseif ($env:INNO_SETUP_ISCC) { $env:INNO_SETUP_ISCC } else { (Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue).Source }
if (-not $compiler) {
    $candidates = @(
        'D:\APPs\Inno Setup 7\ISCC.exe',
        'D:\APPs\Inno Setup 6\ISCC.exe',
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7/ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 7/ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6/ISCC.exe')
    )
    $compiler = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $compiler) {
    throw "没有找到 Inno Setup。可通过 -InnoCompiler 或 INNO_SETUP_ISCC 指定 ISCC.exe。"
}
if (-not (Test-Path -LiteralPath $compiler)) { throw "Inno Setup 编译器不存在：$compiler" }

Write-Host "使用 Inno Setup 编译器：$compiler"
& $compiler "/DMyAppVersion=$releaseVersion" $definition
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup 编译失败，退出代码：$LASTEXITCODE"
}

$installer = Join-Path $repositoryRoot "artifacts/installer/DuxiuLedger-Setup-v$releaseVersion-win-x64.exe"
if (-not (Test-Path -LiteralPath $installer)) {
    throw "安装程序没有生成：$installer"
}
Write-Host "安装程序已生成：$installer"
