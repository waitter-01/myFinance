import { auth } from "@/lib/auth";
import { monthlyCashflow } from "@/modules/transactions/transaction-service";
import { formatCents } from "@/lib/money";
export default async function Dashboard() { const s=await auth(); const month=new Date().toISOString().slice(0,7); const cash=await monthlyCashflow(s!.user.id,month); return <><h1>{month} 月度总览</h1><section className="grid"><div className="card">收入<div className="stat positive">{formatCents(cash.incomeCents)}</div></div><div className="card">支出<div className="stat negative">{formatCents(cash.expenseCents)}</div></div><div className="card">结余<div className="stat">{formatCents(cash.balanceCents)}</div></div></section><p className="muted">金额以整数分保存，按北京时间记录交易日。</p></> }
