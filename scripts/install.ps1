$ErrorActionPreference = 'Stop'
$major = [int](node -p 'process.versions.node.split(".")[0]')
if ($major -lt 22) { throw '需要 Node.js 22 或更高版本' }
if (!(Test-Path -LiteralPath '.env')) { Copy-Item '.env.example' '.env'; Write-Host '已创建 .env，请先填写配置，再重新运行此脚本'; exit 0 }
npm ci
npx prisma generate
npx prisma migrate deploy
npx prisma db seed
npm run build
Write-Host '安装完成。生产环境请使用 npm start，并通过 HTTPS 反向代理。'
