# Windows 桌面版

这是本地优先的 WPF 版本，使用 WinUI 3 风格的卡片布局，数据保存到 `%LOCALAPPDATA%\\DuxiuLedger\\ledger.db`。

当前支持 `.xlsx`、`.xlsm`、`.csv`，可以自动识别微信/支付宝官方账单常见表头中的日期、金额、收支方向、交易对方和备注，并通过交易指纹去重，重复导入不会重复保存。

## Excel 导入模板

标准模板位于 `desktop/templates/独秀账本-Excel导入模板.xlsx`。其中“交易时间”“收支”“金额(元)”为必填字段；微信和支付宝官方导出的账单可以直接导入，不需要先转换为此模板。

在 Windows 开发机生成独立 EXE：

```powershell
.\\desktop\\publish-win-x64.ps1
```

生成目录：`desktop/publish/win-x64/DuxiuLedger.exe`。目标电脑不需要安装 .NET Runtime。

支付宝或微信导出文件可能因版本和导出语言产生不同表头；遇到无法识别的文件，程序会提示缺少日期/金额列。应用也支持手动录入日期、收支、金额、分类、交易对方和备注。下一步可以增加表头映射向导，以及流水编辑和删除功能。
