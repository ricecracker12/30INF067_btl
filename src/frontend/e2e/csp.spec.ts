import { expect, test, type Page } from "@playwright/test"

import { giuHanMucAuth } from "./dev-api"
import { ANH_JPG, dangNhapUi, donBai, taoTaiKhoanCoHoSo } from "./post-helpers"

// Đ-E15 — Content-Security-Policy có nonce, kiểm trên trình duyệt thật: script của app chạy được, script chèn vào thì
// không. Chạy được cả trên `pnpm dev` (CSP dev có 'unsafe-eval') lẫn bản build (`PLAYWRIGHT_BASE_URL`).

/** Ghi mọi vi phạm CSP — cài trước mọi script của trang (Playwright chèn qua DevTools, CSP không chặn nó). */
async function ghiViPham(page: Page) {
  await page.addInitScript(() => {
    const w = window as unknown as { __csp: string[] }
    w.__csp = []
    document.addEventListener("securitypolicyviolation", (e) => {
      w.__csp.push(`${e.violatedDirective} ${e.blockedURI}`)
    })
  })
}
const viPham = (page: Page) =>
  page.evaluate(() => (window as unknown as { __csp: string[] }).__csp)

const nonceOf = (csp: string | undefined) =>
  /'nonce-([^']+)'/.exec(csp ?? "")?.[1]

test("mỗi trang: CSP có nonce MỚI, MỌI thẻ script trong HTML mang đúng nonce đó, không vi phạm nào", async ({
  page,
}) => {
  await ghiViPham(page)
  const nonces: string[] = []

  for (const path of [
    "/login",
    "/register",
    "/verify-email?token=sai-dang",
    "/me",
  ]) {
    const response = await page.goto(path)
    const csp = response!.headers()["content-security-policy"]
    const nonce = nonceOf(csp)
    expect(nonce, `${path} thiếu nonce trong CSP`).toBeTruthy()
    expect(csp).toContain("frame-ancestors 'none'")
    expect(csp).toContain("object-src 'none'")
    nonces.push(nonce!)

    // HTML GỐC từ server (DOM sau hydrate giấu giá trị thuộc tính nonce — phải đọc body response).
    const html = await response!.text()
    const scripts = html.match(/<script\b[^>]*>/g) ?? []
    expect(scripts.length).toBeGreaterThan(0)
    expect(
      scripts.filter((tag) => !tag.includes(`nonce="${nonce}"`)),
      `${path}: thẻ script không mang nonce`
    ).toEqual([])

    await page.waitForLoadState("networkidle")
  }

  expect(new Set(nonces).size).toBe(nonces.length)
  // /login hydrate xong, form dùng được — script của app không bị chặn.
  await page.goto("/login")
  await expect(page.getByRole("button", { name: "Đăng nhập" })).toBeEnabled()
  expect(await viPham(page)).toEqual([])
})

test("XSS chèn vào trang bị CSP chặn: <img onerror>, <script> inline, javascript: URL", async ({
  page,
}) => {
  await ghiViPham(page)
  await page.goto("/login")
  await expect(page.getByRole("button", { name: "Đăng nhập" })).toBeEnabled()

  await page.evaluate(() => {
    const w = window as unknown as { __xss: string[] }
    w.__xss = []
    // Kiểu XSS phổ biến nhất: dữ liệu người dùng được chèn làm HTML.
    const box = document.createElement("div")
    box.innerHTML = `<img src="/khong-ton-tai.png" onerror="window.__xss.push('img-onerror')">`
    document.body.appendChild(box)
    // Thẻ script inline chèn động, không có nonce.
    const script = document.createElement("script")
    script.textContent = "window.__xss.push('inline-script')"
    document.body.appendChild(script)
    // Liên kết javascript:.
    const link = document.createElement("a")
    link.href = "javascript:window.__xss.push('javascript-url')"
    document.body.appendChild(link)
    link.click()
  })
  await page.waitForTimeout(500)

  expect(
    await page.evaluate(() => (window as unknown as { __xss: string[] }).__xss)
  ).toEqual([])
  const blocked = await viPham(page)
  expect(blocked.length).toBeGreaterThanOrEqual(3)
  expect(blocked.every((v) => v.startsWith("script-src"))).toBe(true)
})

test("luồng đăng nhập → /me → tải lại → đăng xuất: không vi phạm CSP nào", async ({
  page,
  request,
}) => {
  // register + verify + login API (dựng hồ sơ) + login UI + logout
  await giuHanMucAuth(5)
  // HỒ SƠ là TIỀN ĐỀ, không phải thứ ca này kiểm: từ `E2`, `RequireProfile` đá mọi tài khoản chưa
  // onboarding sang `/onboarding`, nên `taoTaiKhoanDaXacMinh` (chỉ xác minh, chưa có hồ sơ) không bao giờ
  // tới được `/me`. Bốn ca của GĐ1 đã đỏ vì điều này từ commit E2 và không ai biết — Playwright không vào
  // CI (Q-E8), và tới `E8` mới có người chạy cả bộ.
  const { email, password } = await taoTaiKhoanCoHoSo(request, "csp", "An CSP")
  await ghiViPham(page)

  await page.goto("/login?next=%2Fme")
  await page.getByLabel("Email").fill(email)
  await page.getByLabel("Mật khẩu").fill(password)
  await page.getByRole("button", { name: "Đăng nhập" }).click()
  await expect(page.getByTestId("me-profile")).toContainText(email)
  expect(await viPham(page)).toEqual([])

  await page.reload()
  await expect(page.getByTestId("me-profile")).toContainText(email)
  expect(await viPham(page)).toEqual([])

  await page.getByRole("button", { name: "Đăng xuất" }).click()
  await expect(page).toHaveURL(/\/login$/)
  expect(await viPham(page)).toEqual([])
})

// --- E8 mở rộng: Đ-E17 ---
//
// `E7` đã kiểm rằng header CSP CÓ host R2 trong `connect-src` và `img-src`. Ca dưới đây kiểm điều khác
// hẳn, và là điều duy nhất đáng tin: CSP **không chặn** lượt `PUT` thật lên R2. Header đúng mà host lệch
// một ký tự thì lượt `PUT` chết, và triệu chứng của nó **giống hệt** CORS sai — đúng ca ISS-02.

test("CSP không chặn PUT lên R2: upload ảnh thật, không vi phạm connect-src nào", async ({
  page,
  request,
}) => {
  await giuHanMucAuth(4)
  const tk = await taoTaiKhoanCoHoSo(request, "csp-r2", "An CSP R2")
  await ghiViPham(page)

  await dangNhapUi(page, tk)
  await page.goto("/compose")
  await page.getByLabel("Nội dung").fill("CSP không chặn PUT.")
  await page.getByRole("radio", { name: /Chỉ mình tôi/ }).click()
  await page.getByLabel(/^Ảnh \(tối đa/).setInputFiles(ANH_JPG)

  const dong = page.getByTestId("upload-row")
  await expect(dong).toHaveCount(1)
  // `xong` nghĩa là lượt `PUT` tới host R2 đã trả 2xx — CSP cho đi, CORS cho đi, chữ ký đúng.
  await expect(dong).toHaveAttribute("data-status", "xong", { timeout: 60_000 })

  await page.getByRole("button", { name: "Đăng bài" }).click()
  await expect(page.getByTestId("post-created")).toBeVisible({
    timeout: 30_000,
  })

  // Không vi phạm nào — đặc biệt không `connect-src` (chặn `PUT`) và không `img-src` (chặn ảnh hiện ra).
  expect(await viPham(page)).toEqual([])

  await page.goto("/me")
  const card = page.getByTestId("post-card").first()
  await expect(card.getByTestId("post-image")).toHaveCount(1)
  await expect
    .poll(async () =>
      card
        .getByTestId("post-image")
        .first()
        .evaluate((img) => (img as HTMLImageElement).naturalWidth)
    )
    .toBeGreaterThan(0)
  expect(await viPham(page)).toEqual([])

  const postId = await card.getAttribute("data-post-id")
  await donBai(request, tk, postId ? [postId] : [])
})
