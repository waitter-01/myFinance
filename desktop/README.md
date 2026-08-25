# Windows 桌面版

当前桌面版使用原生 Windows App SDK / WinUI 3，界面采用 Mica、NavigationView 和系统主题控件，中文字体统一为 Microsoft YaHei UI。数据保存到 `%LOCALAPPDATA%\\DuxiuLedger\\ledger.db`，并与旧 WPF 版本兼容。

当前支持 `.xlsx`、`.xlsm`、`.csv`，也支持微信/支付宝账单列表长截图。截图可以选择文件、直接拖入窗口，或者从剪贴板粘贴（`Ctrl+V`）。本地 OCR 会自动分段处理超长图片，识别日期、金额、收支方向和交易对方；所有结果在确认前都可以校正，并通过交易指纹去重。

## Excel 导入模板

标准模板位于 `desktop/templates/独秀账本-Excel导入模板.xlsx`。其中“交易时间”“收支”“金额(元)”为必填字段；微信和支付宝官方导出的账单可以直接导入，不需要先转换为此模板。

在 Windows 开发机生成自包含单文件 EXE：

```powershell
.\\desktop\\publish-win-x64.ps1
```

生成文件为 `desktop/publish/win-x64/DuxiuLedger.exe`，压缩包为 `desktop/publish/DuxiuLedger-v0.5.1-win-x64.zip`。目标电脑不需要安装 .NET Runtime 或 Windows App SDK Runtime。

单文件 EXE 会在首次启动时释放 WinUI 3 运行依赖到临时目录，可以独立复制和运行。

如需生成标准安装程序，先安装 Inno Setup 6，再执行：

```powershell
.\desktop\build-installer.ps1
```

安装程序输出到 `desktop/publish/installer/DuxiuLedger-Setup-v0.5.1-win-x64.exe`，支持当前用户安装、开始菜单快捷方式、可选桌面快捷方式和卸载。

当前版本为 `v0.5.1`，版本变更记录位于仓库根目录的 `CHANGELOG.md`。

支付宝或微信导出文件可能因版本和导出语言产生不同表头；导入预览会列出无法解析的行。应用支持流水编辑、删除、账户关联以及退款、报销和转账。下一步将增加银行账单字段映射向导和可编辑分类规则。
