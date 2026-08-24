"use client";
import { signIn } from "next-auth/react";
import { useState } from "react";
export default function Login() { const [error,setError]=useState(""); const submit=async(e:React.FormEvent<HTMLFormElement>)=>{e.preventDefault();const f=new FormData(e.currentTarget);const r=await signIn("credentials",{email:f.get("email"),password:f.get("password"),redirect:false});if(r?.error)setError("邮箱或密码不正确");else location.href="/dashboard"}; return <main className="login card"><h1>独秀指数基础账本</h1><p className="muted">单用户安全账本</p><form className="form" onSubmit={submit}><label>邮箱<input name="email" type="email" required /></label><label>密码<input name="password" type="password" required /></label>{error&&<p className="negative">{error}</p>}<button>登录</button></form></main> }
