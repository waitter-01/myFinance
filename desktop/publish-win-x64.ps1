$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot 'DuxiuLedger.WinUI/DuxiuLedger.WinUI.csproj'
$publishRoot = Join-Path $PSScriptRoot 'publish'
$output = Join-Path $publishRoot 'win-x64'
$archive = Join-Path $publishRoot 'DuxiuLedger-win-x64.zip'
$installer = Join-Path $PSScriptRoot 'install-win-x64.ps1'

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
dotnet publish $project -c Release -r win-x64 --self-contained true -p:Platform=x64 -p:WindowsAppSDKSelfContained=true -o $output

$executable = Join-Path $output 'DuxiuLedger.exe'
if (-not (Test-Path -LiteralPath $executable)) {
    throw "发布失败：没有找到 $executable"
}

Copy-Item -LiteralPath $installer -Destination (Join-Path $output '安装独秀账本.ps1') -Force
if (Test-Path -LiteralPath $archive) {
    Remove-Item -LiteralPath $archive -Force
}
Compress-Archive -Path (Join-Path $output '*') -DestinationPath $archive -CompressionLevel Optimal

Write-Host "WinUI 3 便携版已生成：$output"
Write-Host "分发压缩包已生成：$archive"
Write-Host "请保留目录内全部文件，不要单独复制 EXE。"
