import { expect, test } from "@playwright/test"

import { emailMoi, giuHanMucAuth, linkXacMinh } from "./dev-api"

// E3 trên API DEV THẬT.
// Cần: `dotnet run --project src/backend/SocialApp.Api` (5259) + postgres, redis, mailpit của compose dev.
// Tốn 2 lượt trong hạn mức 10 req/phút của /auth/* (theo IP): một 201, một 409.

test("đăng ký: 201 → màn kiểm tra hộp thư (email không vào URL), mail có link xác minh; đăng ký lại → 409 dưới trường email", async ({
  page,
  request,
}) => {
  await giuHanMucAuth(2)
  const email = emailMoi("e3")
  const password = "MatKhau-E3-an-toan"

  await page.goto("/login")
  await page.getByRole("link", { name: "Đăng ký" }).click()
  await expect(page).toHaveURL(/\/register$/)

  await page.getByLabel("Email").fill(email)
  // `exact`: nút "Hiện mật khẩu" cũng chứa chữ "mật khẩu".
  const matKhau = page.getByLabel("Mật khẩu", { exact: true })
  await matKhau.fill(password)
  await page.getByRole("button", { name: "Hiện mật khẩu" }).click()
  await expect(matKhau).toHaveAttribute("type", "text")

  const register = page.waitForResponse(
    (r) => r.url().endsWith("/auth/register") && r.request().method() === "POST"
  )
  await page.getByRole("button", { name: "Đăng ký" }).click()
  expect((await register).status()).toBe(201)

  await expect(page).toHaveURL(/\/register\/check-email$/)
  expect(page.url()).not.toContain(encodeURIComponent(email))
  expect(page.url()).not.toContain(email)
  await expect(page.getByText(email)).toBeVisible()
  await expect(page.getByText(/24 giờ/)).toBeVisible()

  // Link trong mail trỏ về FE dev, token 64 hex.
  expect(await linkXacMinh(request, email)).toMatch(
    /^http:\/\/localhost:3000\/verify-email\?token=[0-9a-f]{64}$/
  )

  // Đăng ký lại cùng email: ngoại lệ có ý thức của hợp đồng — 409 hiện dưới trường, có đường đi tiếp.
  await page.goto("/register")
  await page.getByLabel("Email").fill(email)
  await page.getByLabel("Mật khẩu", { exact: true }).fill(password)
  const conflict = page.waitForResponse(
    (r) => r.url().endsWith("/auth/register") && r.request().method() === "POST"
  )
  await page.getByRole("button", { name: "Đăng ký" }).click()
  expect((await conflict).status()).toBe(409)

  await expect(page.getByLabel("Email")).toHaveAttribute("aria-invalid", "true")
  await expect(page.getByText("Email này đã được đăng ký.")).toBeVisible()
  // Hai link "Đăng nhập": trong lỗi của trường và ở chân Card — bấm cái trong lỗi.
  await page
    .locator('[data-slot="field-error"]')
    .getByRole("link", { name: "Đăng nhập" })
    .click()
  await expect(page).toHaveURL(/\/login$/)
})
