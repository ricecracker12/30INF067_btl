import { expect, test } from "@playwright/test"

const mocking = process.env.NEXT_PUBLIC_API_MOCKING === "enabled"

// Cảnh báo CHỈ xuất hiện ở lượt bật mock, và là hệ quả trực tiếp của việc `Providers` render `null`
// trên server để chờ worker: thẻ <script> Next chèn vào cây RSC bị render ở phía client thay vì
// server. Không đụng tới bản production (mock không bao giờ chạy ở đó). E6 dựng `RequireAuth` —
// cũng có trạng thái chờ — thì xem lại cách chờ này.
const BO_QUA_KHI_BAT_MOCK = [
  /Encountered a script tag while rendering React component/,
]

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
  // MSW đăng ký Service Worker, và Next bắn request RSC khi điều hướng phía client. Hai thứ đó chỉ
  // tồn tại trong trình duyệt thật — jsdom của Vitest không có, nên bài này buộc phải là E2E.
  const loi: string[] = []
  const nhatKy: string[] = []
  page.on("console", (m) => {
    nhatKy.push(m.text())
    if (m.type() === "error") loi.push(m.text())
  })
  page.on("pageerror", (e) => loi.push(e.message))

  await page.goto("/")
  await expect(page.getByRole("heading", { name: "SocialApp" })).toBeVisible()

  if (mocking) {
    // Không thấy dòng này nghĩa là worker chưa chạy — và cả bài test thành vô nghĩa.
    await expect
      .poll(() => nhatKy.some((d) => /Mocking enabled/i.test(d)), {
        timeout: 10_000,
      })
      .toBe(true)
  }

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

  const loiThat = mocking
    ? loi.filter((d) => !BO_QUA_KHI_BAT_MOCK.some((r) => r.test(d)))
    : loi
  expect(loiThat).toEqual([])
})
