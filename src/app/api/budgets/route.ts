import { NextResponse } from "next/server";
import { auth } from "@/lib/auth";
import { listBudgetProgress, upsertBudget } from "@/modules/budgets/budget-service";
export async function GET(request: Request) { const session = await auth(); if (!session?.user?.id) return NextResponse.json({ error: "未登录" }, { status: 401 }); const month = new URL(request.url).searchParams.get("month") ?? new Date().toISOString().slice(0, 7); return NextResponse.json({ data: await listBudgetProgress(session.user.id, month) }); }
export async function POST(request: Request) { const session = await auth(); if (!session?.user?.id) return NextResponse.json({ error: "未登录" }, { status: 401 }); try { return NextResponse.json({ data: await upsertBudget(session.user.id, await request.json()) }, { status: 201 }); } catch (error) { return NextResponse.json({ error: error instanceof Error ? error.message : "请求无效" }, { status: 400 }); } }
