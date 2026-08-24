import { expect, it } from "vitest";
import { parseAmountToCents } from "@/lib/money";
it("精确转换元为分", () => expect(parseAmountToCents("123.45")).toBe(12345));
it("拒绝零和三位小数", () => { expect(() => parseAmountToCents("0")).toThrow(); expect(() => parseAmountToCents("1.234")).toThrow(); });
