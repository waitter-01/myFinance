# Windows 桌面版

当前桌面版使用原生 Windows App SDK / WinUI 3，界面采用 Mica、NavigationView 和系统主题控件，中文字体统一为 Microsoft YaHei UI。账本默认保存在本机应用数据目录，并与旧 WPF 版本兼容；多端使用时可由用户自主配置 S3 或 S3 兼容对象存储。

当前支持 `.xlsx`、`.xlsm`、`.csv`，也支持微信/支付宝账单列表长截图。截图可以选择文件、直接拖入窗口，或者从剪贴板粘贴（`Ctrl+V`）。本地 OCR 会自动分段处理超长图片，识别日期、金额、收支方向和交易对方；所有结果在确认前都可以校正，并通过交易指纹去重。

导入预览会根据常见商户关键词自动建议分类，例如蜜雪冰城、瑞幸归入“零食饮料”，美发商户归入“服饰美容”，腾讯天游等游戏商户归入“游戏消费”。自动建议只处理未分类记录，不会覆盖账单文件已有分类。

## Excel 导入模板

标准模板位于 `desktop/templates/独秀账本-Excel导入模板.xlsx`。其中“交易时间”“收支”“金额(元)”为必填字段；微信和支付宝官方导出的账单可以直接导入，不需要先转换为此模板。

在 Windows 开发机生成自包含单文件 EXE：

```powershell
.\\desktop\\publish-win-x64.ps1
```

生成文件为 `desktop/publish/win-x64/DuxiuLedger.exe`，压缩包为 `desktop/publish/DuxiuLedger-v0.6.2-win-x64.zip`。目标电脑不需要安装 .NET Runtime 或 Windows App SDK Runtime。

单文件 EXE 会在首次启动时释放 WinUI 3 运行依赖到临时目录，可以独立复制和运行。

如需生成标准安装程序，先安装 Inno Setup 6，再执行：

```powershell
.\desktop\build-installer.ps1
```

安装程序输出到 `desktop/publish/installer/DuxiuLedger-Setup-v0.6.2-win-x64.exe`，支持当前用户安装、开始菜单快捷方式、可选桌面快捷方式和卸载。

当前版本为 `v0.6.2`，版本变更记录位于仓库根目录的 `CHANGELOG.md`。

## S3 同步

在“偏好设置 → S3 对象存储同步”中填写访问地址、API 端点、Access Key 和 Secret Key。应用会自动从访问地址识别 Bucket；只有无法识别或服务商有特殊要求时，才需要打开高级设置填写 Region、Bucket 或 Path Style。凭据使用 Windows 当前用户加密保护，不会进入同步对象或仓库。

支付宝或微信导出文件可能因版本和导出语言产生不同表头；导入预览会列出无法解析的行。应用支持流水编辑、删除、账户关联以及退款、报销和转账。下一步将增加银行账单字段映射向导和可编辑分类规则。
