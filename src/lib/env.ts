import { z } from "zod";

const schema = z.object({
  DATABASE_URL: z.string().url("DATABASE_URL 必须是有效的数据库连接地址"),
  AUTH_SECRET: z.string().min(32, "AUTH_SECRET 至少需要 32 个字符"),
  OWNER_EMAIL: z.string().email("OWNER_EMAIL 必须是有效邮箱"),
  OWNER_PASSWORD: z.string().min(12, "OWNER_PASSWORD 至少需要 12 个字符"),
});

export const parseServerEnv = (input: unknown) => schema.parse(input);
export const getServerEnv = () => parseServerEnv(process.env);
