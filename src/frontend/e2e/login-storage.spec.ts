import { expect, test, type APIRequestContext } from "@playwright/test"

// E4 trên API DEV THẬT.
// Cần: `dotnet run --project src/backend/SocialApp.Api` (5259) + postgres, redis, mailpit của compose dev.
const API = process.env.PLAYWRIGHT_API_URL ?? "http://localhost:5259/api/v1"
const MAILPIT = process.env.PLAYWRIGHT_MAILPIT_URL ?? "http://localhost:8025"

/**
 * Tự dựng một tài khoản đã xác minh: register → đọc link trong Mailpit → verify-email. Tốn 2 lượt
 * trong hạn mức 10 req/phút của /auth/* (theo IP), cộng 1 lượt đăng nhập trên UI.
 */
async function taoTaiKhoanDaXacMinh(request: APIRequestContext) {
  const email = `e4-${Date.now()}-${Math.random().toString(36).slice(2, 8)}@example.com`
  const password = "MatKhau-E4-an-toan"

  const reg = await request.post(`${API}/auth/register`, {
    data: { email, password },
  })
  expect(reg.status(), await reg.text()).toBe(201)

  let token: string | undefined
  await expect
    .poll(
      async () => {
        const search = await request.get(`${MAILPIT}/api/v1/search`, {
          params: { query: `to:"${email}"` },
        })
        const { messages } = (await search.json()) as {
          messages: { ID: string }[]
        }
        if (messages.length === 0) return false
        const msg = await request.get(
          `${MAILPIT}/api/v1/message/${messages[0].ID}`
        )
        const { Text, HTML } = (await msg.json()) as {
          Text: string
          HTML: string
        }
        token = /verify-email\?token=([0-9a-f]{64})/.exec(
          `${Text}\n${HTML}`
        )?.[1]
        return token !== undefined
      },
      { timeout: 15_000, message: "Không thấy mail xác minh trong Mailpit" }
    )
    .toBe(true)

  const verify = await request.post(`${API}/auth/verify-email`, {
    data: { token },
  })
  expect(verify.status(), await verify.text()).toBe(200)

  return { email, password }
}

test("đăng nhập: token KHÔNG nằm trong Web Storage hay document.cookie; cookie refresh là HttpOnly", async ({
  page,
  context,
  request,
}) => {
  const { email, password } = await taoTaiKhoanDaXacMinh(request)

  await page.goto("/login?next=%2Fme")
  await page.getByLabel("Email").fill(email)
  await page.getByLabel("Mật khẩu").fill(password)

  const login = page.waitForResponse(
    (r) => r.url().endsWith("/auth/login") && r.request().method() === "POST"
  )
  await page.getByRole("button", { name: "Đăng nhập" }).click()
  expect((await login).status()).toBe(200)

  // `/me` là việc của E6 — ở E4 chỉ cần điều hướng đã đi đúng chỗ.
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
