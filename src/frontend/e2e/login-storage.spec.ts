import { expect, test, type Response } from "@playwright/test"

import { API, giuHanMucAuth, taoTaiKhoanDaXacMinh } from "./dev-api"

// E4 + Đ-E14 trên API DEV THẬT: sau khi đăng nhập, TRÌNH DUYỆT không bao giờ thấy JWT — không ở header request (Network
// tab), không ở body response, không ở cookie, không ở Web Storage. Token chỉ nằm ở Next server + Redis.
// Cần: `dotnet run --project src/backend/SocialApp.Api` (5259) + postgres, redis, mailpit của compose dev.
// Tốn 3 lượt /auth/*: register + verify (dựng tài khoản) + đăng nhập trên UI (BFF gọi API thay trình duyệt).

/** Ba đoạn base64url ngăn bởi dấu chấm, phần đầu `eyJ` — hình dạng của mọi JWT. */
const JWT = /eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+/

test("đăng nhập qua BFF: trình duyệt KHÔNG thấy JWT ở request, response, cookie hay Web Storage", async ({
  page,
  context,
  request,
}) => {
  await giuHanMucAuth(3)
  const { email, password } = await taoTaiKhoanDaXacMinh(request, "e4")

  // Ghi lại MỌI request trình duyệt gửi và MỌI body response nó nhận — đúng những gì tab Network của DevTools thấy.
  const requests: { url: string; headers: Record<string, string> }[] = []
  page.on("request", (r) =>
    requests.push({ url: r.url(), headers: r.headers() })
  )
  const bodies: Promise<{ url: string; text: string }>[] = []
  page.on("response", (r: Response) => {
    if (
      r.request().resourceType() === "fetch" ||
      r.request().resourceType() === "xhr"
    )
      bodies.push(
        r
          .text()
          .catch(() => "")
          .then((text) => ({ url: r.url(), text }))
      )
  })

  await page.goto("/login?next=%2Fme")
  await page.getByLabel("Email").fill(email)
  await page.getByLabel("Mật khẩu").fill(password)

  const login = page.waitForResponse(
    (r) =>
      r.url().endsWith("/bff/auth/login") && r.request().method() === "POST"
  )
  await page.getByRole("button", { name: "Đăng nhập" }).click()
  const loginResponse = await login
  expect(loginResponse.status()).toBe(204)

  await expect(page).toHaveURL(/\/me$/)
  await expect(page.getByTestId("me-profile")).toContainText(email)
  await page.waitForLoadState("networkidle")

  // 1) Không request nào mang Authorization; trình duyệt không gọi thẳng API .NET.
  expect(requests.filter((r) => r.headers.authorization)).toEqual([])
  const apiOrigin = new URL(API).origin
  expect(requests.filter((r) => r.url.startsWith(apiOrigin))).toEqual([])

  // 2) Không body response nào (login, session, /me…) chứa JWT.
  const seen = await Promise.all(bodies)
  expect(seen.length).toBeGreaterThan(0)
  expect(seen.filter((b) => JWT.test(b.text)).map((b) => b.url)).toEqual([])

  // 3) Cookie: KHÔNG có refresh_token ở trình duyệt; chỉ __Host-sid, HttpOnly, không phải JWT.
  const cookies = await context.cookies()
  expect(cookies.map((c) => c.name)).not.toContain("refresh_token")
  const sid = cookies.find((c) => c.name === "__Host-sid")
  expect(sid, "trình duyệt không nhận cookie phiên __Host-sid").toBeDefined()
  expect(sid).toMatchObject({
    httpOnly: true,
    secure: true,
    sameSite: "Lax",
    path: "/",
  })
  expect(sid!.value).not.toMatch(JWT)

  // 4) JS trong trang không đọc được gì: Web Storage rỗng, document.cookie không lộ cookie phiên.
  const trongTrang = await page.evaluate(() => ({
    // eslint-disable-next-line no-restricted-properties -- Đ-E2: đây là bài test CHỨNG MINH luật đó; code chạy trong trang đang kiểm, không phải trong app.
    local: window.localStorage.length,
    // eslint-disable-next-line no-restricted-properties -- Đ-E2: như trên.
    session: window.sessionStorage.length,
    // eslint-disable-next-line no-restricted-properties -- Đ-E2: như trên — kiểm JS KHÔNG đọc được cookie phiên.
    cookie: document.cookie,
  }))
  expect(trongTrang).toEqual({ local: 0, session: 0, cookie: "" })

  // 5) Header bảo mật có mặt trên trang.
  const pageResponse = await page.request.get("/me")
  expect(pageResponse.headers()["x-frame-options"]).toBe("DENY")
  expect(pageResponse.headers()["x-content-type-options"]).toBe("nosniff")
})

test("CSRF: POST /bff/auth/logout từ Origin khác bị 403 — phiên còn nguyên", async ({
  page,
  request,
}) => {
  await giuHanMucAuth(3)
  const { email, password } = await taoTaiKhoanDaXacMinh(request, "csrf")
  await page.goto("/login?next=%2Fme")
  await page.getByLabel("Email").fill(email)
  await page.getByLabel("Mật khẩu").fill(password)
  await page.getByRole("button", { name: "Đăng nhập" }).click()
  await expect(page.getByTestId("me-profile")).toContainText(email)

  // Dùng CHÍNH cookie của trình duyệt (page.request dùng chung cookie jar) nhưng Origin giả — như một trang lạ.
  const forged = await page.request.post("/bff/auth/logout", {
    headers: { Origin: "https://evil.example" },
  })
  expect(forged.status()).toBe(403)

  await page.reload()
  await expect(page.getByTestId("me-profile")).toContainText(email)
})
