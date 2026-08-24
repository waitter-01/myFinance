#!/usr/bin/env bash
set -Eeuo pipefail

APP_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$APP_DIR"

if [ "$(id -u)" -eq 0 ]; then SUDO=""; else SUDO="sudo"; fi

if ! command -v apt-get >/dev/null 2>&1; then
  echo "此脚本只支持基于 Debian/Ubuntu 的系统。"
  exit 1
fi

MISSING_PACKAGES=()
command -v docker >/dev/null 2>&1 || MISSING_PACKAGES+=(docker.io)
docker compose version >/dev/null 2>&1 || MISSING_PACKAGES+=(docker-compose-plugin)
command -v openssl >/dev/null 2>&1 || MISSING_PACKAGES+=(openssl)

if [ "${#MISSING_PACKAGES[@]}" -gt 0 ]; then
  echo "==> 安装缺少的组件: ${MISSING_PACKAGES[*]}"
  $SUDO apt-get update
  $SUDO apt-get install -y "${MISSING_PACKAGES[@]}"
else
  echo "==> Docker、Compose、OpenSSL 已存在，跳过安装"
fi

if ! docker compose version >/dev/null 2>&1; then
  echo "Docker Compose 插件安装失败，请检查 Ubuntu 软件源。"
  exit 1
fi

if command -v systemctl >/dev/null 2>&1; then
  echo "==> 确保 Docker 服务已启动"
  $SUDO systemctl enable --now docker
fi

if [ ! -f .env ]; then
  echo "==> 首次配置"
  read -r -p "管理员邮箱: " OWNER_EMAIL_INPUT
  read -r -s -p "管理员密码（至少12位）: " OWNER_PASSWORD_INPUT
  echo
  if [ "${#OWNER_PASSWORD_INPUT}" -lt 12 ]; then echo "密码长度不足12位"; exit 1; fi
  POSTGRES_PASSWORD_INPUT="$(openssl rand -hex 24)"
  AUTH_SECRET_INPUT="$(openssl rand -base64 48 | tr -d '\n')"
  cat > .env <<EOF
POSTGRES_PASSWORD=${POSTGRES_PASSWORD_INPUT}
AUTH_SECRET=${AUTH_SECRET_INPUT}
OWNER_EMAIL=${OWNER_EMAIL_INPUT}
OWNER_PASSWORD=${OWNER_PASSWORD_INPUT}
EOF
  chmod 600 .env
  echo "已生成 .env，数据库密码和认证密钥已随机生成。"
else
  echo "==> 使用已有 .env"
fi

echo "==> 构建并启动服务（首次可能需要几分钟）"
docker compose --env-file .env -f docker-compose.prod.yml up -d --build

echo
echo "部署完成。"
echo "应用仅监听服务器本机 127.0.0.1:3000，请继续配置 Caddy/Nginx HTTPS。"
echo "查看状态: docker compose --env-file .env -f docker-compose.prod.yml ps"
echo "查看日志: docker compose --env-file .env -f docker-compose.prod.yml logs -f app"
