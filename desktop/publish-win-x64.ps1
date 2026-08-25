$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot 'DuxiuLedger.WinUI/DuxiuLedger.WinUI.csproj'
$publishRoot = Join-Path $PSScriptRoot 'publish'
$output = Join-Path $publishRoot 'win-x64'
[xml]$projectXml = Get-Content -LiteralPath $project
$version = [string]($projectXml.Project.PropertyGroup.Version | Select-Object -First 1)
if ([string]::IsNullOrWhiteSpace($version)) { throw '项目文件中没有设置 Version。' }
$archive = Join-Path $publishRoot "DuxiuLedger-v$version-win-x64.zip"

$resolvedDesktop = [System.IO.Path]::GetFullPath($PSScriptRoot)
$resolvedOutput = [System.IO.Path]::GetFullPath($output)
if (-not $resolvedOutput.StartsWith($resolvedDesktop, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "发布目录不在 desktop 目录内，已停止操作：$resolvedOutput"
}

if (Test-Path -LiteralPath $output) {
    Remove-Item -LiteralPath $output -Recurse -Force
}
New-Item -ItemType Directory -Path $output -Force | Out-Null

dotnet restore $project -p:Platform=x64
dotnet publish $project -c Release -r win-x64 `
    -p:Platform=x64 `
    -p:WindowsPackageType=None `
    -p:WindowsAppSDKSelfContained=true `
    -p:SelfContained=true `
    -p:EnableMsixTooling=true `
    -p:PublishSingleFile=true `
    -p:IncludeAllContentForSelfExtract=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $output

$executable = Join-Path $output 'DuxiuLedger.exe'
if (-not (Test-Path -LiteralPath $executable)) {
    throw "发布失败：没有找到 $executable"
}

if (Test-Path -LiteralPath $archive) {
    Remove-Item -LiteralPath $archive -Force
}
Compress-Archive -LiteralPath $executable -DestinationPath $archive -CompressionLevel Optimal

Write-Host "WinUI 3 单文件 EXE 已生成：$executable"
Write-Host "分发压缩包已生成：$archive"
Write-Host '该 EXE 可单独复制运行，首次启动时会释放运行依赖到临时目录。'
