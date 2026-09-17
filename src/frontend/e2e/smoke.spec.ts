import { expect, test } from "@playwright/test"

// Chạy trên Chrome ĐÃ CÀI của máy (lệch Đ-E8) — bản Chrome khác nhau giữa các máy, nên test tự in
// bản ra để dán vào PR cùng kết quả.
test("Chrome dùng để chạy", async ({ browser }) => {
  test.info().annotations.push({
    type: "chrome",
    description: browser.version(),
  })
  expect(browser.version()).not.toBe("")
})

test("vào trang gốc rồi sang /login: không lỗi console, Web Storage rỗng", async ({
  page,
}) => {
  // Next bắn request RSC khi điều hướng phía client — chỉ có trong trình duyệt thật, jsdom của
  // Vitest không có, nên bài này buộc phải là E2E.
  const loi: string[] = []
  page.on("console", (m) => {
    if (m.type() === "error") loi.push(m.text())
  })
  page.on("pageerror", (e) => loi.push(e.message))

  await page.goto("/")
  await expect(page.getByRole("heading", { name: "SocialApp" })).toBeVisible()

  // Điều hướng phía client, không tải lại trang → Next gửi request RSC về chính origin này.
  await page.getByRole("link", { name: "Đăng nhập" }).click()
  await expect(page).toHaveURL(/\/login$/)
  // `CardTitle` của kit render ra `div`, không phải thẻ heading, và chữ "Đăng nhập" còn nằm trên
  // nút submit nữa — nên bám `data-slot` của kit thay vì tìm theo text hay theo role heading.
  await expect(page.locator('[data-slot="card-title"]')).toHaveText("Đăng nhập")
  await expect(page.getByRole("button", { name: "Đăng nhập" })).toBeVisible()
  await expect(page.getByLabel("Email")).toBeVisible()
  await expect(page.getByLabel("Mật khẩu")).toBeVisible()

  // Đ-E2: token không bao giờ nằm trong Web Storage. E1/E2 chưa đăng nhập được nên phải RỖNG.
  const storage = await page.evaluate(() => ({
    // eslint-disable-next-line no-restricted-properties -- Đ-E2: đây là bài test CHỨNG MINH luật đó; code chạy trong trang đang kiểm, không phải trong app.
    local: window.localStorage.length,
    // eslint-disable-next-line no-restricted-properties -- Đ-E2: như trên.
    session: window.sessionStorage.length,
  }))
  expect(storage).toEqual({ local: 0, session: 0 })

  expect(loi).toEqual([])
})
