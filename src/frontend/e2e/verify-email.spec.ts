import { expect, test } from "@playwright/test"

import { API, emailMoi, linkXacMinh } from "./mailpit"

// E5 trên API DEV THẬT, `pnpm dev` — App Router bật StrictMode ở dev nên đây là lượt thật của Đ-E10.
// Cần: `dotnet run --project src/backend/SocialApp.Api` (5259) + postgres, redis, mailpit của compose dev.
// Tốn 4 lượt trong hạn mức 10 req/phút của /auth/* (theo IP): register, verify, verify lần hai (410), login.

test("xác minh: link trong Mailpit → đúng 1 POST dưới StrictMode, token rời URL, đăng nhập được; mở lại link → 410", async ({
  page,
  request,
}) => {
  const email = emailMoi("e5")
  const password = "MatKhau-E5-an-toan"

  const reg = await request.post(`${API}/auth/register`, {
    data: { email, password },
  })
  expect(reg.status(), await reg.text()).toBe(201)

  const link = await linkXacMinh(request, email)
  expect(link).toMatch(
    /^http:\/\/localhost:3000\/verify-email\?token=[0-9a-f]{64}$/
  )

  // Chỉ đếm POST — preflight OPTIONS (khác cổng → khác origin) không tiêu thụ token.
  const verifyPosts: number[] = []
  page.on("response", (r) => {
    if (
      r.url().endsWith("/auth/verify-email") &&
      r.request().method() === "POST"
    )
      verifyPosts.push(r.status())
  })

  await page.goto(link)
  await expect(page.getByText(`Email ${email} đã được xác minh.`)).toBeVisible()
  await expect(page).toHaveURL(/\/verify-email$/)
  // Cho lần chạy effect thứ hai của StrictMode (nếu có) đủ thời gian bắn request.
  await page.waitForLoadState("networkidle")
  expect(verifyPosts).toEqual([200])

  // Mở lại đúng link đó: token đã dùng → 410, câu đúng cho người đã xác minh, KHÔNG có "Gửi lại".
  await page.goto(link)
  await expect(
    page.getByText(
      "Liên kết xác minh đã hết hạn hoặc đã được sử dụng. Nếu bạn đã xác minh trước đó, hãy đăng nhập."
    )
  ).toBeVisible()
  await expect(page).toHaveURL(/\/verify-email$/)
  await expect(page.getByText(/Gửi lại/i)).toHaveCount(0)
  expect(verifyPosts).toEqual([200, 410])

  // Khép vòng: tài khoản vừa xác minh đăng nhập được (trước khi xác minh, login trả 403).
  await page.getByRole("link", { name: "Đăng nhập" }).click()
  await expect(page).toHaveURL(/\/login$/)
  await page.getByLabel("Email").fill(email)
  await page.getByLabel("Mật khẩu").fill(password)
  const login = page.waitForResponse(
    (r) => r.url().endsWith("/auth/login") && r.request().method() === "POST"
  )
  await page.getByRole("button", { name: "Đăng nhập" }).click()
  expect((await login).status()).toBe(200)
})
