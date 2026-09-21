import { expect, test } from "@playwright/test"

import { giuHanMucAuth, taoTaiKhoanDaXacMinh } from "./dev-api"

// E2 + Q-E2 trên trình duyệt thật: bất biến Đ-2.4 — KHÔNG CÓ HỒ SƠ THÌ KHÔNG ĐĂNG ĐƯỢC BÀI. Server chỉ
// trả 403; đây là chỗ kiểm rằng FE biến nó thành một đường đi không cưỡng lại được.
//
// Ca quan trọng nhất là ca thứ hai: gõ THẲNG `/compose`. Guard ở layout thì dễ đúng cho điều hướng trong
// app mà sai cho URL gõ tay, và người dùng thật hay bookmark.

test("tài khoản mới bị đưa về /onboarding; gõ thẳng /compose vẫn bị đưa về", async ({
  page,
  request,
}) => {
  // register + verify + đăng nhập trên UI.
  await giuHanMucAuth(3)
  const { email, password } = await taoTaiKhoanDaXacMinh(request, "onb")

  await page.goto("/login?next=%2Fme")
  await page.getByLabel("Email").fill(email)
  await page.getByLabel("Mật khẩu").fill(password)
  await page.getByRole("button", { name: "Đăng nhập" }).click()

  // `?next=/me` trỏ vào `(with-profile)`, nhưng chưa có hồ sơ → guard đưa sang onboarding.
  await page.waitForURL(/\/onboarding/)
  await expect(page.getByLabel("Tên hiển thị")).toBeVisible()

  // Gõ THẲNG một route khác dưới `(with-profile)`: vẫn bị đưa về, không nháy nội dung.
  await page.goto("/compose")
  await page.waitForURL(/\/onboarding/)
  await expect(page.getByRole("button", { name: "Đăng bài" })).toHaveCount(0)

  await page.goto("/posts/0192f3c1-8a4e-7c31-9f2a-6b5d4e3c2a99")
  await page.waitForURL(/\/onboarding/)

  // Xong onboarding thì vào được, và KHÔNG bị đá ngược ra nữa (nếu không là vòng lặp Q-E2).
  await page.getByLabel("Tên hiển thị").fill("An Onboarding")
  await page.getByRole("button", { name: "Bắt đầu" }).click()
  await page.waitForURL((u) => !u.pathname.includes("onboarding"))

  await page.goto("/compose")
  await expect(page.getByRole("button", { name: "Đăng bài" })).toBeVisible()
  expect(new URL(page.url()).pathname).toBe("/compose")
})
