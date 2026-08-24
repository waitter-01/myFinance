# 独秀指数基础账本（MVP）

这是一个单用户家庭/个人账本 MVP：登录、收入/支出流水、分类、预算和月度总览。当前阶段不包含文件导入、S3、Redis、报告导出或多用户注册。

## 已实现

- Auth.js Credentials 登录，管理员账号来自环境变量并由 seed 写入数据库。
- 交易、预算、分类都按当前会话的 `userId` 查询和写入；浏览器不能指定归属用户。
- 金额只以整数 `amountCents` 保存，输入最多两位小数。
- 交易日期保存为 `YYYY-MM-DD`；预算按 `YYYY-MM` 查询。
- 交易 CRUD API、预算 upsert API、分类 API、月度收入/支出/结余页面。
- PostgreSQL 迁移、种子数据、Linux/macOS Shell 与 Windows PowerShell 安装脚本。

## 服务器部署（推荐 Ubuntu 22.04/24.04）

### Ubuntu 一键部署

如果代码已经上传到 Ubuntu 服务器，可以直接运行：

```bash
cd duxiu-ledger
chmod +x scripts/deploy-ubuntu.sh
./scripts/deploy-ubuntu.sh
```

脚本会逐项检查 Docker、Docker Compose 插件、OpenSSL 和 Docker 服务：已安装且可用的组件会直接复用，缺少的组件才会通过 apt 安装。随后脚本交互式询问管理员邮箱/密码，自动生成数据库密码和 `AUTH_SECRET`，构建 Next.js 容器、启动 PostgreSQL、执行数据库迁移和初始化分类。它不会把 PostgreSQL 或 Node.js 端口直接暴露到公网；应用只监听 `127.0.0.1:3000`。如果已有 `.env`，脚本不会覆盖它。

更新代码后执行：

```bash
git pull
docker compose --env-file .env -f docker-compose.prod.yml up -d --build
```

查看状态和日志：

```bash
docker compose --env-file .env -f docker-compose.prod.yml ps
docker compose --env-file .env -f docker-compose.prod.yml logs -f app
```

以下命令在服务器执行，不需要在开发电脑上启动项目。先准备 Node.js 22、npm、Docker 和 Docker Compose；也可以使用云 PostgreSQL，此时跳过本地 PostgreSQL 容器。

### 1. 上传代码并准备配置

```bash
git clone <你的代码仓库地址> duxiu-ledger
cd duxiu-ledger
cp .env.example .env
```

编辑 `.env`：

```dotenv
DATABASE_URL=postgresql://ledger:change-me@127.0.0.1:5432/duxiu_ledger?schema=public
AUTH_SECRET=请使用至少32个字符的随机字符串
OWNER_EMAIL=你的管理员邮箱
OWNER_PASSWORD=至少12位的强密码
```

生成密钥的示例：`openssl rand -base64 48`。`.env` 只放服务器，不能提交到 Git。

### 2. 启动 PostgreSQL

修改 `docker-compose.yml` 里的 `POSTGRES_PASSWORD`，并同步更新 `.env` 的 `DATABASE_URL` 密码，然后执行：

```bash
docker compose up -d postgres
docker compose ps
```

如果使用云数据库，确保服务器安全组允许出站访问数据库，并把数据库连接字符串直接写入 `.env`。

### 3. 安装、迁移、初始化管理员

```bash
chmod +x scripts/install.sh
./scripts/install.sh
```

脚本会执行 `npm ci`、Prisma Client 生成、生产迁移、管理员/默认分类 seed 和 Next.js production build。首次运行若没有 `.env`，脚本只会复制示例配置并退出；填好配置后再次运行即可。

### 4. 启动应用

```bash
npm start
```

默认监听 `http://127.0.0.1:3000`。建议使用 systemd 守护：

```ini
[Unit]
Description=Duxiu Ledger
After=network.target docker.service

[Service]
WorkingDirectory=/opt/duxiu-ledger
ExecStart=/usr/bin/npm start
Restart=always
Environment=NODE_ENV=production
User=ledger

[Install]
WantedBy=multi-user.target
```

保存为 `/etc/systemd/system/duxiu-ledger.service` 后执行：

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now duxiu-ledger
sudo systemctl status duxiu-ledger
```

### 5. 配置 HTTPS 反向代理

用 Nginx 或 Caddy 将公网域名代理到 `127.0.0.1:3000`。Caddy 示例：

```text
ledger.example.com {
    reverse_proxy 127.0.0.1:3000
}
```

把域名解析到服务器后，Caddy 会自动申请和续期 HTTPS 证书。生产环境不要直接把 Node 端口暴露到公网。

### 6. 更新版本

```bash
git pull
npm ci
npx prisma migrate deploy
npm run build
sudo systemctl restart duxiu-ledger
```

### 7. 备份与安全检查

- 每天备份 PostgreSQL，并定期做一次恢复演练；Docker 卷或数据库快照都不能替代异地备份。
- 生产环境必须使用 HTTPS、强管理员密码和独立数据库密码。
- 定期更新 Node、Next.js、Prisma 和操作系统安全补丁。
- 不要把 `.env`、数据库备份或日志提交到代码仓库。
- 当前是单用户 MVP，若以后增加导入任务，按计划引入私有对象存储和队列，并保留现有服务的 `userId` 边界。

## 本地验证（仅供开发环境）

```bash
npm install
npm run test:unit
npx prisma validate
npm run build
```

本次交付不会在当前电脑启动开发服务器或部署应用。
