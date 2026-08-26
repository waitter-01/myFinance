param(
    [string]$PublishDirectory,
    [int]$TimeoutSeconds = 10
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrWhiteSpace($PublishDirectory)) {
    $PublishDirectory = Join-Path $repositoryRoot 'artifacts/win-x64'
}

$resolvedPublishDirectory = [System.IO.Path]::GetFullPath($PublishDirectory)
$resolvedRepositoryRoot = [System.IO.Path]::GetFullPath($repositoryRoot)
if (-not $resolvedPublishDirectory.StartsWith($resolvedRepositoryRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "发布目录不在仓库内，已停止启动测试：$resolvedPublishDirectory"
}

$executable = Join-Path $resolvedPublishDirectory 'DuxiuLedger.exe'
if (-not (Test-Path -LiteralPath $executable)) {
    throw "启动测试失败：没有找到 $executable"
}

$process = Start-Process -FilePath $executable -WorkingDirectory $resolvedPublishDirectory -PassThru
$deadline = [DateTime]::Now.AddSeconds($TimeoutSeconds)
try {
    do {
        Start-Sleep -Milliseconds 250
        $process.Refresh()
        if ($process.HasExited) {
            throw "启动测试失败：程序在显示主窗口前退出，退出代码 $($process.ExitCode)。"
        }
        if ($process.MainWindowHandle -ne 0) {
            Write-Host "启动测试通过：$($process.MainWindowTitle)"
            return
        }
    } while ([DateTime]::Now -lt $deadline)

    throw "启动测试失败：等待 $TimeoutSeconds 秒后仍未显示主窗口。"
}
finally {
    $process.Refresh()
    if (-not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
    }
}
