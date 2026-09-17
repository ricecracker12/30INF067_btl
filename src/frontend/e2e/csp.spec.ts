import { expect, test, type Page } from "@playwright/test"

import { giuHanMucAuth, taoTaiKhoanDaXacMinh } from "./dev-api"

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
  // register + verify + login trên UI + logout
  await giuHanMucAuth(4)
  const { email, password } = await taoTaiKhoanDaXacMinh(request, "csp")
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
