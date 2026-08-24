# Windows 桌面版

这是本地优先的 WPF 版本，使用 WinUI 3 风格的卡片布局，数据保存到 `%LOCALAPPDATA%\\DuxiuLedger\\ledger.db`。

当前支持 `.xlsx`、`.xlsm`、`.csv`，可以自动识别微信/支付宝官方账单常见表头中的日期、金额、收支方向、交易对方和备注，并通过交易指纹去重，重复导入不会重复保存。

在 Windows 开发机生成独立 EXE：

```powershell
.\\desktop\\publish-win-x64.ps1
```

生成目录：`desktop/publish/win-x64/DuxiuLedger.exe`。目标电脑不需要安装 .NET Runtime。

支付宝或微信导出文件可能因版本和导出语言产生不同表头；遇到无法识别的文件，程序会提示缺少日期/金额列。下一步可以增加表头映射向导和手动录入窗口。
