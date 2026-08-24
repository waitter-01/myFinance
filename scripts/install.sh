#!/usr/bin/env bash
set -Eeuo pipefail
if ! command -v node >/dev/null || [ "$(node -p 'process.versions.node.split(".")[0]')" -lt 22 ]; then echo '需要 Node.js 22 或更高版本'; exit 1; fi
if ! command -v npm >/dev/null; then echo '需要 npm'; exit 1; fi
if [ ! -f .env ]; then cp .env.example .env; echo '已创建 .env，请先填写配置，再重新运行此脚本'; exit 0; fi
npm ci
npx prisma generate
npx prisma migrate deploy
npx prisma db seed
npm run build
echo '安装完成。生产环境请使用 npm start，并通过 Nginx/Caddy 提供 HTTPS。'
