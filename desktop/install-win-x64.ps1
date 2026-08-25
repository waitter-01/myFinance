$ErrorActionPreference = 'Stop'

$source = [System.IO.Path]::GetFullPath($PSScriptRoot)
$sourceExecutable = Join-Path $source 'DuxiuLedger.exe'
if (-not (Test-Path -LiteralPath $sourceExecutable)) {
    throw '请将安装脚本放在已解压的独秀账本发布目录内，再重新运行。'
}

$programsRoot = Join-Path $env:LOCALAPPDATA 'Programs'
$destination = Join-Path $programsRoot 'DuxiuLedger'
$resolvedProgramsRoot = [System.IO.Path]::GetFullPath($programsRoot)
$resolvedDestination = [System.IO.Path]::GetFullPath($destination)
if (-not $resolvedDestination.StartsWith($resolvedProgramsRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "安装目录校验失败：$resolvedDestination"
}

$running = Get-Process -Name 'DuxiuLedger' -ErrorAction SilentlyContinue
if ($running) {
    throw '请先关闭正在运行的独秀账本，再重新安装或升级。'
}

New-Item -ItemType Directory -Path $destination -Force | Out-Null
Get-ChildItem -LiteralPath $source -File | Where-Object { $_.Name -ne '安装独秀账本.ps1' } | Copy-Item -Destination $destination -Force
Get-ChildItem -LiteralPath $source -Directory | Copy-Item -Destination $destination -Recurse -Force

$installedExecutable = Join-Path $destination 'DuxiuLedger.exe'
$shell = New-Object -ComObject WScript.Shell
$startMenu = [Environment]::GetFolderPath('StartMenu')
$shortcutPath = Join-Path $startMenu 'Programs\独秀账本.lnk'
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $installedExecutable
$shortcut.WorkingDirectory = $destination
$shortcut.Description = '独秀账本 - 本地优先的个人财务中心'
$shortcut.Save()

Write-Host "安装完成：$installedExecutable"
Write-Host '已创建开始菜单快捷方式“独秀账本”。'
Start-Process -FilePath $installedExecutable
