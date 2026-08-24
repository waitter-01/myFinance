import { z } from "zod";
export const transactionSchema = z.object({ occurredOn: z.string().regex(/^\d{4}-\d{2}-\d{2}$/, "日期格式应为 YYYY-MM-DD"), direction: z.enum(["INCOME", "EXPENSE"]), amountCents: z.number().int().positive("金额必须大于 0"), categoryId: z.string().optional().nullable(), merchant: z.string().max(120).optional().nullable(), note: z.string().max(500).optional().nullable(), expenseNature: z.enum(["NECESSARY", "OPTIONAL", "UNCLASSIFIED"]).default("UNCLASSIFIED") });
export type TransactionInput = z.infer<typeof transactionSchema>;
