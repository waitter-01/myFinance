<div align="center">
  <img src="desktop/DuxiuLedger.Desktop/Assets/duxiu-logo.png" width="112" alt="独秀账本 Logo">
  <h1>独秀账本</h1>
  <p>本地优先、轻量易用的 Windows 个人财务账本</p>

  <p>
    <img src="https://img.shields.io/badge/平台-Windows%2010%20%7C%2011-0078D4" alt="Windows">
    <img src="https://img.shields.io/badge/.NET-8.0-512BD4" alt=".NET 8">
    <img src="https://img.shields.io/badge/UI-WinUI%203-146C70" alt="WinUI 3">
    <img src="https://img.shields.io/badge/数据库-SQLite-0F80CC" alt="SQLite">
    <img src="https://img.shields.io/badge/版本-v0.2.0-6B7280" alt="v0.2.0">
  </p>
</div>

## 项目简介

独秀账本是一款面向个人用户的 Windows 桌面记账应用。应用无需服务器和在线账户，账单数据保存在本机 SQLite 数据库中，支持导入 Excel、CSV 以及微信、支付宝常见格式的官方账单。

项目当前处于早期开发阶段，桌面端是主要开发方向。仓库中的 Next.js 代码是早期 Web 原型，不代表当前推荐的使用方式。

## 功能特性

- 本地优先：数据默认保存在当前 Windows 用户目录，不依赖云端服务。
- 账单导入：支持 `.xlsx`、`.xlsm` 和 `.csv` 文件。
- 格式识别：识别微信、支付宝常见账单表头以及项目标准模板。
- 自动去重：通过交易时间、金额、方向、交易对方等信息生成指纹，避免重复导入。
- 财务总览：显示本月收入、支出、结余和最近流水。
- 手动录入：填写日期、收支、金额、分类、交易对方和备注后直接保存。
- 流水维护：支持编辑、删除以及收入、支出、转账、退款和报销类型。
- 账户管理：管理现金、银行卡、信用卡和电子钱包，并计算账户余额。
- 导入预览：写入数据库前核对有效流水、重复记录和问题行。
- 自主设置：可调整小额消费阈值、月度预算、提醒计划和订阅识别关键词。
- 订阅统计：自动归集会员、续费和游戏月卡，按近 12 个月付款折算月均成本。
- 流水搜索：按交易对方或备注查找记录。
- 数据备份：可直接导出 SQLite 数据库备份并打开数据目录。
- 原生 WinUI 3：使用 Windows App SDK 的 NavigationView、Mica、ContentDialog 和主题控件。
- 统一字体：中文界面统一使用 Microsoft YaHei UI，支持系统深浅色主题。
- 独立运行：可发布为真正的单文件 EXE，目标电脑无需安装 .NET Runtime 或 Windows App SDK Runtime。

## 当前版本

当前版本为 **v0.2.0**，重点完善流水准确性、账户管理和导入核对。完整变更内容参见 [CHANGELOG.md](CHANGELOG.md)。

版本号采用 `主版本.次版本.修订版本`：

- 新增一组向后兼容功能：增加次版本，例如 `v0.2.0`。
- 只修复问题且不新增功能：增加修订版本，例如 `v0.1.1`。
- 出现不兼容的数据或使用方式变更：增加主版本，例如 `v1.0.0` 到 `v2.0.0`。

## 当前状态

| 模块 | 状态 | 说明 |
| --- | --- | --- |
| 本地数据库 | 可用 | SQLite 持久化存储 |
| Excel/CSV 导入 | 可用 | 支持标准模板和常见账单表头 |
| 微信/支付宝识别 | 基础可用 | 不同版本账单可能需要补充表头规则 |
| 导入去重 | 可用 | 重复导入不会重复保存 |
| 流水搜索 | 可用 | 支持交易对方和备注 |
| 数据备份 | 可用 | 导出数据库文件 |
| 手动新增流水 | 可用 | 保存后立即刷新总览和流水列表 |
| 偏好设置 | 可用 | 财务阈值、预算、提醒和订阅关键词持久化保存 |
| 订阅与月卡统计 | 可用 | 关键词识别、本月实付和近 12 个月月均分摊 |
| 编辑和删除流水 | 可用 | 编辑保留来源和指纹，删除前二次确认 |
| 完整交易类型 | 可用 | 支出、收入、转账、退款和报销 |
| 账户管理 | 可用 | 期初余额、动态余额、停用和流水关联 |
| 导入预览 | 可用 | 有效记录、重复项和问题行报告 |
| 预算管理 | 规划中 | 已有页面框架，业务功能尚未接入 |
| 分类编辑 | 规划中 | 已有页面框架，业务功能尚未接入 |

## 快速开始

### 环境要求

- Windows 10 或 Windows 11，64 位
- 构建源码需要 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- 运行自包含 EXE 不需要预装 .NET

### 从源码运行

```powershell
git clone https://github.com/waitter-01/myFinance.git
cd myFinance
dotnet run --project .\desktop\DuxiuLedger.Desktop\DuxiuLedger.Desktop.csproj
```

### 生成独立 EXE

在仓库根目录执行：

```powershell
.\desktop\publish-win-x64.ps1
```

生成文件位于：

```text
desktop\publish\win-x64\DuxiuLedger.exe
desktop\publish\DuxiuLedger-v0.2.0-win-x64.zip
```

单文件 EXE 可以单独复制运行，首次启动时会将 WinUI 3 运行依赖释放到临时目录，因此第一次启动可能稍慢。发布目录已被 Git 忽略，不会提交到仓库。

### 生成安装程序

项目提供 Inno Setup 配置，可以把应用封装为单个带安装和卸载向导的 `Setup.exe`。

先安装 [Inno Setup](https://jrsoftware.org/isinfo.php)：

```powershell
winget install --id JRSoftware.InnoSetup -e
```

然后在仓库根目录执行：

```powershell
.\desktop\build-installer.ps1
```

生成文件：

```text
desktop\publish\installer\DuxiuLedger-Setup-v0.2.0-win-x64.exe
```

安装程序默认安装到当前用户的 `%LOCALAPPDATA%\Programs\DuxiuLedger`，不要求管理员权限，并提供开始菜单、可选桌面快捷方式和标准卸载入口。

### 发布新版本

每次发布版本时按以下顺序操作：

1. 修改 `desktop/DuxiuLedger.WinUI/DuxiuLedger.WinUI.csproj` 中的 `Version`、`AssemblyVersion`、`FileVersion` 和 `InformationalVersion`。
2. 在 `CHANGELOG.md` 顶部增加新版本、发布日期、新增内容、修复内容和已知限制。
3. 更新 README 顶部版本徽章以及文档中的安装包文件名。
4. 执行 `desktop/publish-win-x64.ps1` 和 `desktop/build-installer.ps1`，验证 EXE 与安装程序。
5. 使用中文提交版本变更，然后创建并推送 Git 标签：

```powershell
git tag -a v0.2.0 -m "版本：发布 v0.2.0"
git push origin master
git push origin v0.2.0
```

6. 在 GitHub Releases 中使用相同标签创建发行版，并上传单文件 EXE、ZIP 和安装程序。发布说明以 `CHANGELOG.md` 对应版本内容为准。

## 导入账单

点击应用右上角的“导入账单”，选择一个或多个账单文件。导入成功后，程序会自动跳转到“全部流水”。

标准 Excel 模板位于：

[下载独秀账本 Excel 导入模板](desktop/templates/独秀账本-Excel导入模板.xlsx)

模板必填字段：

| 字段 | 格式 | 示例 |
| --- | --- | --- |
| 交易时间 | `yyyy-mm-dd hh:mm:ss` | `2026-08-24 08:30:00` |
| 收支 | `支出` 或 `收入` | `支出` |
| 金额(元) | 大于 0 的数字 | `18.50` |

选填字段包括交易对方、商品说明、分类和备注。微信、支付宝官方导出的账单可直接尝试导入，不必先转换为标准模板。

## 数据存储

数据库默认位置：

```text
%LOCALAPPDATA%\DuxiuLedger\ledger.db
```

启动错误日志位置：

```text
%LOCALAPPDATA%\DuxiuLedger\logs\startup-error.log
```

卸载或清理应用前，请先在“数据备份”页面导出数据库文件。仅删除 EXE 不会自动删除账本数据。

## 项目结构

```text
myFinance/
├── desktop/
│   ├── DuxiuLedger.WinUI/      # 当前 WinUI 3 桌面应用源码
│   ├── DuxiuLedger.Desktop/    # 保留的 WPF 回退版本与共享业务源码
│   ├── templates/              # Excel 导入模板
│   ├── installer/              # Inno Setup 安装程序定义
│   ├── publish-win-x64.ps1     # Windows 单文件 EXE 发布脚本
│   ├── build-installer.ps1     # 安装程序自动构建脚本
│   └── README.md               # 桌面端补充说明
├── src/                        # 早期 Next.js Web 原型
├── prisma/                     # Web 原型数据库模型
└── README.md
```

桌面端主要组件：

- `DuxiuLedger.WinUI/MainWindow.xaml`：原生 WinUI 3 主界面和页面导航。
- `LocalStore.cs`：SQLite 数据持久化和查询。
- `BillImporter.cs`：Excel、CSV 和账单格式识别。
- `TransactionRecord.cs`：本地流水数据模型。

## 开发与验证

编译桌面项目：

```powershell
dotnet build .\desktop\DuxiuLedger.Desktop\DuxiuLedger.Desktop.csproj -c Release
```

重新发布：

```powershell
.\desktop\publish-win-x64.ps1
```

提交前至少确认：

1. Release 编译没有错误和警告。
2. 应用可以正常启动。
3. 受影响的导航、导入或备份功能完成基本验证。
4. 没有提交数据库、日志、构建目录或个人账单文件。

## 路线图

- [x] 原生 WinUI 3 桌面应用框架
- [x] Mica、NavigationView 与统一中文字体
- [x] 本地 SQLite 持久化
- [x] Excel、CSV、微信和支付宝账单基础导入
- [x] 导入去重与数据备份
- [x] 手动新增流水
- [x] 可持久化的财务偏好设置
- [x] 订阅、会员与游戏月卡成本统计
- [x] 编辑和删除流水
- [x] 收入、支出、转账、退款和报销类型
- [x] 本地账户管理和流水关联
- [ ] 可编辑分类与自动分类规则
- [ ] 月度预算设置和使用进度
- [x] 导入预览和错误行报告
- [ ] 银行账单字段映射向导
- [ ] 图表、月报和年度汇总
- [x] Inno Setup 安装程序配置
- [ ] 代码签名和 GitHub Releases
- [ ] 自动化测试和持续集成

## 参与贡献

欢迎通过 [Issues](https://github.com/waitter-01/myFinance/issues) 报告问题或提出建议。

建议的贡献流程：

1. Fork 仓库并创建功能分支。
2. 每个提交只处理一个明确问题。
3. 使用中文提交说明，例如 `修复：兼容新版支付宝账单表头`。
4. 完成编译和必要验证。
5. 提交 Pull Request，并说明变更内容和验证结果。

## 提交约定

本项目采用小步提交、频繁推送的开发方式，方便审查、定位问题和回退：

- 所有新提交使用中文描述。
- 一个提交只包含一个逻辑变更。
- 功能、修复、界面、文档和重构分别提交。
- 每次提交完成并验证后及时推送远程仓库。
- 避免把无关格式化、生成文件或多个大型功能混在同一提交。

推荐格式：

```text
功能：增加手动录入流水
修复：避免重复导入相同交易
界面：优化流水列表空状态
文档：补充 Excel 模板说明
重构：拆分账单解析服务
```

## 隐私与安全

- 请勿提交真实账单、数据库文件或个人隐私数据。
- 导入文件只在本机读取，当前桌面版不会主动上传账单。
- 发布来源不明或未经签名的 EXE 可能触发 Windows SmartScreen；请优先自行构建或从可信 Release 下载。
- 发现安全问题时，请避免在公开 Issue 中粘贴个人账单和敏感日志。

## 许可证

项目目前尚未添加开源许可证。在正式确定许可证之前，默认保留所有权利；如需复制、分发或二次发布，请先联系仓库所有者。
