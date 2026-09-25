import { execFileSync } from "node:child_process"
import { existsSync, readFileSync } from "node:fs"

import { expect, type APIRequestContext, type Page } from "@playwright/test"

import { API, taoTaiKhoanDaXacMinh } from "./dev-api"
import { dangNhapApi, type TaiKhoan } from "./post-helpers"

// GĐ6 E10 — dựng dữ liệu cho lát cắt kiểm duyệt/quản trị. Không có API nào tạo ADMIN (đúng thiết kế: vai trò ADMIN chỉ trao được
// bởi một ADMIN khác, Đ-6.7), nên spec cần một tài khoản ADMIN có sẵn:
//
//  - `E2E_ADMIN_EMAIL` / `E2E_ADMIN_PASSWORD` (biến môi trường hoặc `.env.e2e.local`) — bắt buộc khi trỏ máy khác localhost (F2).
//  - Dev: thiếu hai khóa đó thì tạo tài khoản mới rồi NÂNG bằng psql trong container Postgres dev — cùng tinh thần nhánh Mailpit
//    của `taoTaiKhoanDaXacMinh`: chỉ có nghĩa trên máy dev, trỏ máy thật thì dừng.

const PG_CONTAINER = process.env.E2E_PG_CONTAINER ?? "socialapp-dev-postgres-1"

function khoaAdmin(): { email: string; password: string } | null {
  const fromFile: Record<string, string> = {}
  if (existsSync(".env.e2e.local"))
    for (const line of readFileSync(".env.e2e.local", "utf8").replace(/^﻿/, "").split(/\r?\n/)) {
      const i = line.indexOf("=")
      if (i > 0) fromFile[line.slice(0, i).trim()] = line.slice(i + 1).trim()
    }
  const email = process.env.E2E_ADMIN_EMAIL ?? fromFile.E2E_ADMIN_EMAIL
  const password = process.env.E2E_ADMIN_PASSWORD ?? fromFile.E2E_ADMIN_PASSWORD
  if (email && password) return { email, password }
  if (!/^https?:\/\/(localhost|127\.0\.0\.1)[:/]/.test(API))
    throw new Error(`Thiếu E2E_ADMIN_EMAIL/E2E_ADMIN_PASSWORD khi trỏ ${API} — không tự nâng quyền trên máy thật`)
  return null
}

/**
 * Tài khoản ADMIN có hồ sơ, token ĐÃ mang vai trò ADMIN. Nhánh dev tốn **3** lượt `/auth/*` (register + verify + login); nhánh có
 * sẵn tốn 1. Người gọi khai vào `giuHanMucAuth`.
 */
export async function taoAdmin(request: APIRequestContext, displayName: string): Promise<TaiKhoan> {
  let creds = khoaAdmin()
  if (!creds) {
    creds = await taoTaiKhoanDaXacMinh(request, "gd6-admin")
    // Nâng TRƯỚC khi đăng nhập: vai trò đóng dấu vào access token lúc phát. Email do chính spec sinh (`emailMoi`), không phải
    // dữ liệu người dùng — nối thẳng vào câu SQL là an toàn ở đây, và chỉ chạy trên container dev.
    execFileSync("docker", [
      "exec",
      PG_CONTAINER,
      "psql",
      "-U",
      "socialapp",
      "-d",
      "socialapp",
      "-v",
      "ON_ERROR_STOP=1",
      "-c",
      `UPDATE identity.users SET role_id = 3 WHERE email = '${creds.email}'`,
    ])
  }
  const tk = await dangNhapApi(request, creds.email, creds.password)
  const profile = await request.put(`${API}/users/me/profile`, {
    headers: tk.auth,
    data: { displayName, bio: null },
  })
  expect(profile.status(), await profile.text()).toBe(200)
  const me = await request.get(`${API}/me`, { headers: tk.auth })
  expect(((await me.json()) as { role: string }).role, "tài khoản E2E_ADMIN phải là ADMIN").toBe("ADMIN")
  return tk
}

/** `reportId` đại diện của đối tượng trong hàng đợi (Đ-6.13: mỗi đối tượng một dòng). DB dev có hàng đợi của mọi lượt chạy cũ. */
export async function timBaoCao(request: APIRequestContext, admin: TaiKhoan, targetId: string): Promise<string> {
  let cursor: string | null = null
  do {
    const qs: string = cursor ? `&cursor=${encodeURIComponent(cursor)}` : ""
    const res = await request.get(`${API}/reports?status=open&limit=50${qs}`, { headers: admin.auth })
    expect(res.status(), await res.text()).toBe(200)
    const page = (await res.json()) as {
      items: { reportId: string; target: { id: string } }[]
      nextCursor: string | null
    }
    const hit = page.items.find((i) => i.target.id === targetId)
    if (hit) return hit.reportId
    cursor = page.nextCursor
  } while (cursor)
  throw new Error("không thấy báo cáo của đối tượng trong hàng đợi")
}

/**
 * "Tab lấy lại focus" (Đ-6.11): hai `BrowserContext` không tranh focus của hệ điều hành, nên bắn đúng sự kiện mà app nghe. Đây là
 * CÁCH app phát hiện thay đổi quyền, không phải đường tắt quanh nó — app không có đường nào khác ngoài focus và vào route.
 */
export async function focusLai(page: Page) {
  await page.evaluate(() => window.dispatchEvent(new Event("focus")))
}
