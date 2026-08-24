import { expect, it } from "vitest";
import { parseServerEnv } from "@/lib/env";
it("拒绝缺少数据库地址", () => expect(() => parseServerEnv({ AUTH_SECRET: "x".repeat(32), OWNER_EMAIL: "a@b.com", OWNER_PASSWORD: "x".repeat(12) })).toThrow("DATABASE_URL"));
