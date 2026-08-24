import NextAuth from "next-auth";
import Credentials from "next-auth/providers/credentials";
import bcrypt from "bcryptjs";
import { prisma } from "@/lib/prisma";

export const { handlers, auth, signIn, signOut } = NextAuth({
  secret: process.env.AUTH_SECRET || "build-only-secret-change-in-production",
  session: { strategy: "jwt" },
  providers: [Credentials({ name: "密码", credentials: { email: {}, password: {} }, async authorize(credentials) {
    const email = String(credentials?.email ?? "").trim().toLowerCase();
    const password = String(credentials?.password ?? "");
    const user = await prisma.user.findUnique({ where: { email } });
    if (!user || !(await bcrypt.compare(password, user.passwordHash))) return null;
    return { id: user.id, email: user.email };
  } })],
  callbacks: { async jwt({ token, user }) { if (user) token.id = user.id; return token; }, async session({ session, token }) { if (session.user) { session.user.id = String(token.id); } return session; } },
  pages: { signIn: "/login" },
});
