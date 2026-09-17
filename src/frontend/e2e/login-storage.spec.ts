import { expect, test } from "@playwright/test"

import { giuHanMucAuth, taoTaiKhoanDaXacMinh } from "./dev-api"

// E4 trên API DEV THẬT.
// Cần: `dotnet run --project src/backend/SocialApp.Api` (5259) + postgres, redis, mailpit của compose dev.
// Tốn 3 lượt /auth/*: register + verify (dựng tài khoản) + đăng nhập trên UI.

test("đăng nhập: token KHÔNG nằm trong Web Storage hay document.cookie; cookie refresh là HttpOnly", async ({
  page,
  context,
  request,
}) => {
  await giuHanMucAuth(3)
  const { email, password } = await taoTaiKhoanDaXacMinh(request, "e4")

  await page.goto("/login?next=%2Fme")
  await page.getByLabel("Email").fill(email)
  await page.getByLabel("Mật khẩu").fill(password)

  const login = page.waitForResponse(
    (r) => r.url().endsWith("/auth/login") && r.request().method() === "POST"
  )
  await page.getByRole("button", { name: "Đăng nhập" }).click()
  expect((await login).status()).toBe(200)

  // Đã đăng nhập trong tab này → guard của /me không gọi refresh (E6).
  await expect(page).toHaveURL(/\/me$/)

  // Đ-E2 / Mục 12: token chỉ ở memory.
  const trongTrang = await page.evaluate(() => ({
    // eslint-disable-next-line no-restricted-properties -- Đ-E2: đây là bài test CHỨNG MINH luật đó; code chạy trong trang đang kiểm, không phải trong app.
    local: window.localStorage.length,
    // eslint-disable-next-line no-restricted-properties -- Đ-E2: như trên.
    session: window.sessionStorage.length,
    // eslint-disable-next-line no-restricted-properties -- Đ-E2: như trên — kiểm JS KHÔNG đọc được cookie refresh.
    cookie: document.cookie,
  }))
  expect(trongTrang.local).toBe(0)
  expect(trongTrang.session).toBe(0)
  expect(trongTrang.cookie).not.toContain("refresh_token")

  // Cookie jar của trình duyệt thì CÓ — đây là mục còn treo ở checklist khối D (Mục 15).
  const refresh = (await context.cookies()).find(
    (c) => c.name === "refresh_token"
  )
  expect(refresh, "trình duyệt không nhận cookie refresh_token").toBeDefined()
  expect(refresh).toMatchObject({
    httpOnly: true,
    secure: true,
    sameSite: "Lax",
    path: "/api/v1/auth",
  })
})
