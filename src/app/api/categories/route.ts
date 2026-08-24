import { NextResponse } from "next/server";
import { auth } from "@/lib/auth";
import { prisma } from "@/lib/prisma";
export async function GET() { const session = await auth(); if (!session?.user?.id) return NextResponse.json({ error: "未登录" }, { status: 401 }); return NextResponse.json({ data: await prisma.category.findMany({ where: { userId: session.user.id }, orderBy: [{ kind: "asc" }, { sortOrder: "asc" }] }) }); }
