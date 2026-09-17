import { expect, test, type Page } from "@playwright/test"

import { giuHanMucAuth, taoTaiKhoanDaXacMinh } from "./dev-api"

// E6 trên API DEV THẬT: guard phía client (Đ-E3), tải lại giữ phiên bằng một refresh, đăng xuất.
// Cần: `dotnet run --project src/backend/SocialApp.Api` (5259) + postgres, redis, mailpit của compose dev.

/**
 * Ghi lại nếu nội dung trang có bảo vệ TỪNG xuất hiện trong DOM, dù chỉ một khung hình. Chạy trước mọi script của
 * trang và sống qua điều hướng phía client (cùng document) — "không nháy nội dung" kiểm bằng máy, không bằng mắt.
 * Theo dõi `<main>` (chỉ có trong layout `(app)`, bên trong guard) chứ không chỉ `me-profile`: chưa đăng nhập thì
 * `/me` trả 401 nên hồ sơ không bao giờ hiện, kể cả khi guard hỏng — theo dõi mỗi hồ sơ là test không bắt được gì.
 */
async function theoDoiHoSo(page: Page) {
  await page.addInitScript(() => {
    const w = window as unknown as { __thayHoSo: boolean }
    w.__thayHoSo = false
    new MutationObserver(() => {
      if (document.querySelector('main, [data-testid="me-profile"]'))
        w.__thayHoSo = true
    }).observe(document, { childList: true, subtree: true })
  })
}

const daThayHoSo = (page: Page) =>
  page.evaluate(() => (window as unknown as { __thayHoSo: boolean }).__thayHoSo)

test("chưa đăng nhập vào /me → /login?next=%2Fme, hồ sơ KHÔNG lúc nào hiện", async ({
  page,
}) => {
  await giuHanMucAuth(1) // refresh khởi động → 401
  await theoDoiHoSo(page)

  await page.goto("/me")

  await expect(page).toHaveURL(/\/login\?next=%2Fme$/)
  expect(await daThayHoSo(page)).toBe(false)
})

test("đăng nhập → /me hiện roleDisplayName; tải lại giữ phiên bằng ĐÚNG 1 refresh; đăng xuất 204 → /login, vào lại /me bị chặn", async ({
  page,
  request,
}) => {
  // register + verify + login + refresh khi tải lại + logout + refresh 401 khi vào lại /me
  await giuHanMucAuth(6)
  const { email, password } = await taoTaiKhoanDaXacMinh(request, "e6")

  await page.goto("/login?next=%2Fme")
  await page.getByLabel("Email").fill(email)
  await page.getByLabel("Mật khẩu").fill(password)
  await page.getByRole("button", { name: "Đăng nhập" }).click()

  await expect(page).toHaveURL(/\/me$/)
  const hoSo = page.getByTestId("me-profile")
  await expect(hoSo).toContainText(email)
  await expect(hoSo).toContainText("Người dùng")
  // `role` dành cho so logic — không bao giờ hiện cho người đọc.
  await expect(page.getByRole("main")).not.toContainText(/\bUSER\b/)

  // Tải lại: token trong memory mất, cookie refresh còn → guard khôi phục bằng một refresh. Dev bật StrictMode
  // (effect chạy hai lần) — coordinator phải gộp thành một.
  const refreshes: number[] = []
  page.on("response", (r) => {
    if (r.url().endsWith("/auth/refresh") && r.request().method() === "POST")
      refreshes.push(r.status())
  })
  await page.reload()
  await expect(hoSo).toContainText(email)
  await expect(page).toHaveURL(/\/me$/)
  await page.waitForLoadState("networkidle")
  expect(refreshes).toEqual([200])

  // Đăng xuất.
  const logout = page.waitForResponse(
    (r) => r.url().endsWith("/auth/logout") && r.request().method() === "POST"
  )
  await page.getByRole("button", { name: "Đăng xuất" }).click()
  expect((await logout).status()).toBe(204)
  // Tự đăng xuất → /login trơn, không kèm next.
  await expect(page).toHaveURL(/\/login$/)

  // Cookie refresh đã bị server xóa → vào lại /me là bị chặn.
  await page.goto("/me")
  await expect(page).toHaveURL(/\/login\?next=%2Fme$/)
  await expect(page.getByTestId("me-profile")).toHaveCount(0)
})
