<div align="center">
  <img src="assets/duxiu-logo.png" width="112" alt="独秀账本 Logo">
  <h1>独秀账本</h1>
  <p>本地优先、轻量易用的 Windows 个人财务账本</p>

  <p>
    <img src="https://img.shields.io/badge/平台-Windows%2010%20%7C%2011-0078D4" alt="Windows">
    <img src="https://img.shields.io/badge/.NET-8.0-512BD4" alt=".NET 8">
    <img src="https://img.shields.io/badge/UI-WinUI%203-146C70" alt="WinUI 3">
    <img src="https://img.shields.io/badge/同步-S3%20兼容-0F80CC" alt="S3 compatible">
    <img src="https://img.shields.io/badge/版本-v0.10.0-6B7280" alt="v0.10.0">
  </p>
</div>

## 项目简介

独秀账本是一款面向个人用户的 Windows 桌面记账应用。账本默认保存在本机，无需服务器和在线账户；需要多端使用时，可由用户自行配置 S3 或 S3 兼容对象存储。应用支持导入 Excel、CSV 以及微信、支付宝常见格式的官方账单。

项目当前处于早期开发阶段，以原生 WinUI 3 桌面应用为唯一主线。业务逻辑与界面项目已经分离，便于继续扩展、测试和维护。

## 功能特性

- 本地优先：数据默认保存在当前 Windows 用户目录，不依赖云端服务。
- 账单导入：支持 `.xlsx`、`.xlsm` 和 `.csv` 文件。
- 截图识别：本地识别微信、支付宝账单列表长截图，支持选择文件、拖拽、剪贴板粘贴、自动分段、预览修正和重复过滤。
- 格式识别：识别微信、支付宝常见账单表头以及项目标准模板。
- 自动去重：同时使用统一指纹和“分钟级时间＋金额＋收支类型”检查现有账本与本批次记录。
- 重复确认：疑似重复默认跳过；确为同额同时间的两笔交易时，可在预览中选择仍然导入。
- 问题修正：截图末条信息不完整时保留候选，可从“需要核对”页面一键定位并修改。
- 预览可达：列表显示固定滚动条、动态记录数量，并可一键查看最后一条。
- 易读流水：收支语义色、金额正负号和低饱和分类标签帮助快速浏览长列表。
- 自动分类：根据常见商户和消费关键词建议分类，只补充未分类记录并允许导入前修改。
- 财务总览：显示本月收入、支出、结余和最近流水。
- 手动录入：填写日期、收支、金额、分类、交易对方和备注后直接保存。
- 流水维护：支持编辑、删除以及收入、支出、转账、退款和报销类型。
- 账户管理：管理现金、银行卡、信用卡和电子钱包，并计算账户余额。
- 导入预览：写入账本前核对有效流水、重复记录和问题行。
- 自主设置：可调整小额消费阈值、月度预算、提醒计划和订阅识别关键词。
- 详细分类：内置餐饮、生活、娱乐、游戏、订阅等分类，并支持新增、编辑和停用。
- 订阅统计：记录价格覆盖月数，按最近价格和计费周期计算真实月均负担。
- 周月年分析：按周度、月度或年度查看收入、净支出、结余、储蓄率和同期变化。
- 原生趋势图表：展示分周期收支柱状图、分类占比、商户排行和最大单笔消费。
- 财务行动建议：总结钱的主要去向、小额与可优化消费变化，并给出建议支出上限。
- 预算与目标：设置月度总预算、分类预算和储蓄目标，显示使用进度及每月建议储蓄额。
- 多端同步：用户可自主配置 AWS S3、Cloudflare R2、MinIO 等 S3 兼容对象存储。
- 系统提醒：支持每日记账提醒和每周消费总结通知。
- 流水搜索：按交易对方或备注查找记录。
- 数据备份：可导出独立的 `.duxiu` 账本备份并打开数据目录。
- 原生 WinUI 3：使用 Windows App SDK 的 NavigationView、Mica、ContentDialog 和主题控件。
- 统一字体：中文界面统一使用 Microsoft YaHei UI，支持系统深浅色主题。
- 独立运行：可发布为真正的单文件 EXE，目标电脑无需安装 .NET Runtime 或 Windows App SDK Runtime。

## 当前版本

当前版本为 **v0.10.0**，全部流水现已支持批量分类、批量调整账户、批量删除、结果导出和常用筛选方案。完整变更内容参见 [CHANGELOG.md](CHANGELOG.md)。

版本号采用 `主版本.次版本.修订版本`：

- 新增一组向后兼容功能：增加次版本，例如 `v0.2.0`。
- 只修复问题且不新增功能：增加修订版本，例如 `v0.1.1`。
- 出现不兼容的数据或使用方式变更：增加主版本，例如 `v1.0.0` 到 `v2.0.0`。

## 当前状态

| 模块 | 状态 | 说明 |
| --- | --- | --- |
| 本地账本 | 可用 | 离线保存，重新启动后无需重复导入 |
| Excel/CSV 导入 | 可用 | 支持标准模板和常见账单表头 |
| 微信/支付宝长截图 | 基础可用 | 本地 OCR、超长图分段、预览校正和去重 |
| 微信/支付宝识别 | 基础可用 | 不同版本账单可能需要补充表头规则 |
| 导入去重 | 可用 | 重复导入不会重复保存 |
| 流水搜索 | 可用 | 支持交易对方和备注 |
| 数据备份 | 可用 | 导出 `.duxiu` 账本备份 |
| 手动新增流水 | 可用 | 保存后立即刷新总览和流水列表 |
| 偏好设置 | 可用 | 财务阈值、预算、提醒和订阅关键词持久化保存 |
| 订阅与月卡统计 | 可用 | 分类/关键词识别、本月实付和按覆盖月数折算的月均负担 |
| 消费洞察 | 可用 | 周/月/年切换、收支与储蓄指标、分类/商户排行和行动建议 |
| 预算与储蓄目标 | 可用 | 月度总预算、分类预算、超支提示和目标月存建议 |
| S3 对象同步 | 可用 | 自定义服务地址、本地优先合并、删除同步和启动同步 |
| Windows 提醒 | 可用 | 每日记账提醒和每周近 7 天支出总结 |
| 趋势与导出 | 可用 | 原生收支趋势图，周度、月度和年度 CSV 分析报告 |
| 编辑和删除流水 | 可用 | 编辑保留来源和指纹，删除前二次确认 |
| 完整交易类型 | 可用 | 支出、收入、转账、退款和报销 |
| 账户管理 | 可用 | 期初余额、动态余额、停用和流水关联 |
| 导入预览 | 可用 | 有效记录、重复项和问题行报告 |
| 预算管理 | 可用 | 总预算和分类预算进度 |
| 分类编辑 | 可用 | 详细预设分类，支持新增、修改、排序、停用和安全删除 |

## 快速开始

### 环境要求

- Windows 10 或 Windows 11，64 位
- 构建源码需要 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- 运行自包含 EXE 不需要预装 .NET

### 从源码运行

```powershell
git clone https://github.com/waitter-01/myFinance.git
cd myFinance
dotnet run --project .\src\DuxiuLedger.App\DuxiuLedger.App.csproj -p:Platform=x64
```

### 生成独立 EXE

在仓库根目录执行：

```powershell
.\scripts\publish-win-x64.ps1
```

生成文件位于：

```text
artifacts\win-x64\DuxiuLedger.exe
artifacts\DuxiuLedger-v0.10.0-win-x64.zip
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
.\scripts\build-installer.ps1
```

生成文件：

```text
artifacts\installer\DuxiuLedger-Setup-v0.10.0-win-x64.exe
```

安装程序默认安装到当前用户的 `%LOCALAPPDATA%\Programs\DuxiuLedger`，不要求管理员权限，并提供开始菜单、可选桌面快捷方式和标准卸载入口。

### 发布新版本

每次发布版本时按以下顺序操作：

1. 修改 `src/DuxiuLedger.App/DuxiuLedger.App.csproj` 中的 `Version`、`AssemblyVersion`、`FileVersion` 和 `InformationalVersion`。
2. 在 `CHANGELOG.md` 顶部增加新版本、发布日期、新增内容、修复内容和已知限制。
3. 更新 README 顶部版本徽章以及文档中的安装包文件名。
4. 执行 `scripts/publish-win-x64.ps1` 和 `scripts/build-installer.ps1`，验证 EXE 与安装程序。
5. 使用中文提交版本变更，然后创建并推送 Git 标签：

```powershell
git tag -a v0.10.0 -m "版本：发布 v0.10.0"
git push origin master
git push origin v0.10.0
```

6. 在 GitHub Releases 中使用相同标签创建发行版，并上传单文件 EXE、ZIP 和安装程序。发布说明以 `CHANGELOG.md` 对应版本内容为准。

## 导入账单

点击应用右上角的“导入表格”，可以选择一个或多个 Excel/CSV 账单文件。账单截图可以通过以下三种方式导入：点击“识别账单截图”选择图片、把图片直接拖入应用窗口，或者复制图片后点击“粘贴截图”/按 `Ctrl+V`。所有内容先进入预览，确认后才写入账本。

截图识别在本机完成，不上传图片。建议使用账单列表页原始截图，保留月份标题、商户、金额和时间；如果截图最底部只显示半条记录，请在预览中修正时间或跳过该条。OCR 可能误认相似汉字，确认导入前可选择流水并修改金额、方向、分类、交易对方和时间。

标准 Excel 模板位于：

[下载独秀账本 Excel 导入模板](templates/独秀账本-Excel导入模板.xlsx)

模板必填字段：

| 字段 | 格式 | 示例 |
| --- | --- | --- |
| 交易时间 | `yyyy-mm-dd hh:mm:ss` | `2026-08-24 08:30:00` |
| 收支 | `支出` 或 `收入` | `支出` |
| 金额(元) | 大于 0 的数字 | `18.50` |

选填字段包括交易对方、商品说明、分类、订阅月数和备注。归类为“订阅消费”时建议填写价格覆盖的月数，例如年度会员填写 `12`；微信、支付宝官方导出的账单可直接尝试导入，不必先转换为标准模板。

## 本地账本与备份

本地账本文件默认位置：

```text
%LOCALAPPDATA%\DuxiuLedger\ledger.db
```

启动错误日志位置：

```text
%LOCALAPPDATA%\DuxiuLedger\logs\startup-error.log
```

卸载或清理应用前，请先在“数据备份”页面导出 `.duxiu` 账本备份。仅删除 EXE 不会自动删除账本数据。

## S3 对象存储同步

在“偏好设置 → S3 对象存储同步”中开启同步并填写配置，然后依次点击“保存并测试连接”和“立即双向同步”。应用会把一个带版本号的 JSON 同步对象保存到用户指定的 Bucket 中，不需要创建数据表。

对象存储控制台只有四项参数时，直接填写：

- `访问地址`：例如 `https://zxx.cn-nb1.rains3.com`。
- `API 端点`：例如 `https://cn-nb1.rains3.com`。
- `Access Key`。
- `Secret Key`。

应用会比较访问地址和 API 端点并自动识别 Bucket，上述示例会识别为 `zxx`。Region、Bucket 手动覆盖、同步对象路径、Session Token 和 Path Style 位于高级设置，一般无需修改。

如果从 v0.6.0 或 v0.6.1 升级且曾把完整访问地址填写进 Bucket，高级设置可能留有旧值。v0.6.2 会自动忽略并在下次保存时清理该值，无需手动删除本地设置。

Secret Key 和 Session Token 只通过 Windows DPAPI 加密保存在当前 Windows 用户配置中，不会写入仓库、日志或同步对象。更换电脑时需要重新输入凭据。应用采用本地优先模式，断网时仍可记账，恢复网络后再合并。

建议为应用创建独立凭据，并仅授予目标对象所需的 `GetObject` 和 `PutObject` 权限。S3 同步用于多端合并，不替代定期导出 `.duxiu` 账本备份。

## 项目结构

```text
myFinance/
├── src/
│   ├── DuxiuLedger.App/        # WinUI 3 界面、对话框和 Windows 集成
│   └── DuxiuLedger.Core/       # 模型、导入、存储、分类和 S3 同步
├── assets/                     # PNG 与 Windows 多尺寸 ICO
├── templates/                  # Excel 导入模板
├── installer/                  # Inno Setup 安装定义
├── scripts/                    # 构建、发布、安装和图标生成脚本
├── artifacts/                  # 本地发布产物，不提交 Git
├── DuxiuLedger.sln             # Visual Studio 解决方案
└── README.md
```

桌面端主要组件：

- `src/DuxiuLedger.App/MainWindow.xaml`：原生 WinUI 3 主界面和页面导航。
- `src/DuxiuLedger.Core/Services/LocalStore.cs`：本地账本持久化和查询。
- `src/DuxiuLedger.Core/Services/S3SyncService.cs`：S3 兼容对象下载、合并和上传。
- `src/DuxiuLedger.Core/Services/BillImporter.cs`：Excel、CSV 和账单格式识别。
- `src/DuxiuLedger.Core/Models/TransactionRecord.cs`：本地流水数据模型。

## 开发与验证

编译桌面项目：

```powershell
dotnet build .\DuxiuLedger.sln -c Release -p:Platform=x64
```

重新发布：

```powershell
.\scripts\publish-win-x64.ps1
```

提交前至少确认：

1. Release 编译没有错误和警告。
2. 应用可以正常启动。
3. 受影响的导航、导入或备份功能完成基本验证。
4. 没有提交账本文件、访问密钥、日志、构建目录或个人账单。

## 路线图

- [x] 原生 WinUI 3 桌面应用框架
- [x] Mica、NavigationView 与统一中文字体
- [x] 本地账本持久化
- [x] Excel、CSV、微信和支付宝账单基础导入
- [x] 导入去重与数据备份
- [x] 手动新增流水
- [x] 可持久化的财务偏好设置
- [x] 订阅、会员与游戏月卡成本统计
- [x] 编辑和删除流水
- [x] 收入、支出、转账、退款和报销类型
- [x] 本地账户管理和流水关联
- [x] 可编辑详细分类
- [x] 常见商户自动分类建议
- [x] 消费去向、小额支出和商户排行洞察
- [x] 月度预算设置和使用进度
- [x] 储蓄目标与每月建议储蓄额
- [x] S3 兼容对象存储多端同步
- [x] Windows 每日提醒与每周总结
- [x] 近半年趋势和 CSV 月报导出
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

- 请勿提交真实账单、账本文件、S3 密钥或个人隐私数据。
- 导入文件只在本机读取，当前桌面版不会主动上传账单。
- 发布来源不明或未经签名的 EXE 可能触发 Windows SmartScreen；请优先自行构建或从可信 Release 下载。
- 发现安全问题时，请避免在公开 Issue 中粘贴个人账单和敏感日志。

## 许可证

项目目前尚未添加开源许可证。在正式确定许可证之前，默认保留所有权利；如需复制、分发或二次发布，请先联系仓库所有者。
