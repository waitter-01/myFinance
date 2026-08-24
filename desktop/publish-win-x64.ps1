$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'DuxiuLedger.Desktop/DuxiuLedger.Desktop.csproj'
dotnet restore $project
dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o (Join-Path $PSScriptRoot 'publish/win-x64')
Write-Host "EXE 已生成到 desktop/publish/win-x64/DuxiuLedger.exe"
