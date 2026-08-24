import bcrypt from "bcryptjs";
import { PrismaClient, CategoryKind } from "@prisma/client";
import { parseServerEnv } from "../src/lib/env";
const prisma = new PrismaClient();
async function main() {
  const env = parseServerEnv(process.env);
  const user = await prisma.user.upsert({ where: { email: env.OWNER_EMAIL.toLowerCase() }, update: { passwordHash: await bcrypt.hash(env.OWNER_PASSWORD, 12) }, create: { email: env.OWNER_EMAIL.toLowerCase(), passwordHash: await bcrypt.hash(env.OWNER_PASSWORD, 12) } });
  const categories = [{ name: "工资", kind: CategoryKind.INCOME }, { name: "其他收入", kind: CategoryKind.INCOME }, { name: "餐饮", kind: CategoryKind.EXPENSE }, { name: "交通", kind: CategoryKind.EXPENSE }, { name: "住房", kind: CategoryKind.EXPENSE }, { name: "购物", kind: CategoryKind.EXPENSE }, { name: "娱乐", kind: CategoryKind.EXPENSE }, { name: "医疗", kind: CategoryKind.EXPENSE }, { name: "教育", kind: CategoryKind.EXPENSE }, { name: "其他支出", kind: CategoryKind.EXPENSE }];
  for (const [sortOrder, item] of categories.entries()) {
    const existing = await prisma.category.findFirst({ where: { userId: user.id, name: item.name, kind: item.kind } });
    if (existing) await prisma.category.update({ where: { id: existing.id }, data: { sortOrder } });
    else await prisma.category.create({ data: { ...item, sortOrder, userId: user.id } });
  }
}
main().finally(() => prisma.$disconnect());
