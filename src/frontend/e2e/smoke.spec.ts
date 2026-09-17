import { expect, test } from "@playwright/test"

import { giuHanMucAuth } from "./dev-api"

// Chạy trên Chrome ĐÃ CÀI của máy (lệch Đ-E8) — bản Chrome khác nhau giữa các máy, nên test tự in
// bản ra để dán vào PR cùng kết quả.
test("Chrome dùng để chạy", async ({ browser }) => {
  test.info().annotations.push({
    type: "chrome",
    description: browser.version(),
  })
  expect(browser.version()).not.toBe("")
})

test("vào trang gốc chưa đăng nhập: / → /me → /login?next=%2Fme, không lỗi console, Web Storage rỗng", async ({
  page,
}) => {
  // Guard của /me gọi một POST /auth/refresh (401 — chưa có cookie).
  await giuHanMucAuth(1)

  const loi: string[] = []
  page.on("console", (m) => {
    // Trình duyệt tự in mọi response 4xx ra console. 401 của refresh khởi động là ĐÚNG thiết kế (Đ-E3: tab mới
    // chưa biết còn phiên hay không, phải hỏi server) — bỏ riêng dòng đó, còn lại vẫn phải rỗng.
    if (
      m.type() === "error" &&
      !(m.text().includes("401") && m.location().url.endsWith("/auth/refresh"))
    )
      loi.push(m.text())
  })
  page.on("pageerror", (e) => loi.push(e.message))

  // `/` redirect phía server sang /me; guard ở client (không proxy.ts) đưa tiếp về /login.
  await page.goto("/")
  await expect(page).toHaveURL(/\/login\?next=%2Fme$/)
  // `CardTitle` của kit render ra `div`, không phải thẻ heading, và chữ "Đăng nhập" còn nằm trên
  // nút submit nữa — nên bám `data-slot` của kit thay vì tìm theo text hay theo role heading.
  await expect(page.locator('[data-slot="card-title"]')).toHaveText("Đăng nhập")
  await expect(page.getByRole("button", { name: "Đăng nhập" })).toBeVisible()
  await expect(page.getByLabel("Email")).toBeVisible()
  await expect(page.getByLabel("Mật khẩu")).toBeVisible()

  // Đ-E2: token không bao giờ nằm trong Web Storage. Chưa đăng nhập nên phải RỖNG.
  const storage = await page.evaluate(() => ({
    // eslint-disable-next-line no-restricted-properties -- Đ-E2: đây là bài test CHỨNG MINH luật đó; code chạy trong trang đang kiểm, không phải trong app.
    local: window.localStorage.length,
    // eslint-disable-next-line no-restricted-properties -- Đ-E2: như trên.
    session: window.sessionStorage.length,
  }))
  expect(storage).toEqual({ local: 0, session: 0 })

  expect(loi).toEqual([])
})
