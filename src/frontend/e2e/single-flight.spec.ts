import { expect, test, type Page } from "@playwright/test"

import { API, giuHanMucAuth, taoTaiKhoanDaXacMinh } from "./dev-api"

// E7 — E2E-02 kịch bản 3 tab (Đ-E4) trên API DEV THẬT chạy access token NGẮN. Chạy RIÊNG, trên một API + FE riêng
// để không phải khởi động lại API dev đang dùng (instance riêng còn có bộ đếm rate limit riêng):
//
//   # 1) API token 10 giây, cổng 5260 (repo root; --no-build: API 5259 đang giữ file trong bin/)
//   $env:ASPNETCORE_ENVIRONMENT='Development'; $env:ASPNETCORE_URLS='http://localhost:5260'
//   $env:Jwt__AccessTokenSeconds='10'; $env:Cors__AllowedOrigins__0='http://localhost:3001'
//   dotnet run --no-build --no-launch-profile --project src/backend/SocialApp.Api
//
//   # 2) FE cổng 3001 — bản BUILD, không `next dev`: Next 16 không cho hai `next dev` chung một thư mục
//   $env:NEXT_PUBLIC_API_BASE_URL='http://localhost:5260/api/v1'; pnpm build; pnpm exec next start -p 3001
//
//   # 3)
//   $env:PLAYWRIGHT_BASE_URL='http://localhost:3001'; $env:PLAYWRIGHT_API_URL='http://localhost:5260/api/v1'
//   pnpm exec playwright test single-flight
//
// Không đặt PLAYWRIGHT_API_URL (lượt `pnpm test:e2e` thường) → bỏ qua ngay, không tốn request. Đặt rồi mà API vẫn
// phát token dài → bỏ qua, nêu `expiresIn` đọc được — không đoán. Xong nhớ `pnpm build` lại với `/api/v1`: bản build
// ở bước 2 nhúng localhost:5260.

/** 10 giây token + ClockSkew 30 giây của JwtBearer + dư. */
const CHO_HET_HAN_MS = 45_000

async function bamTaiLaiVaChoXong(page: Page) {
  const me = page.waitForResponse(
    (r) =>
      r.url().endsWith("/api/v1/me") &&
      r.request().method() === "GET" &&
      r.status() === 200
  )
  await page.getByRole("button", { name: "Tải lại" }).click()
  await me
}

test("3 tab, access token hết hạn, bấm Tải lại đồng thời → ĐÚNG 1 POST /auth/refresh, không tab nào về /login", async ({
  context,
  request,
}) => {
  test.skip(
    !process.env.PLAYWRIGHT_API_URL,
    "Chạy riêng trên API Jwt__AccessTokenSeconds=10 — xem đầu file."
  )
  test.setTimeout(150_000)

  // register + verify + login dò expiresIn + login ở tab 1 + khởi động phiên ở tab 2, tab 3 + refresh lúc bấm đồng thời
  await giuHanMucAuth(7)
  const { email, password } = await taoTaiKhoanDaXacMinh(request, "e7")

  const probe = await request.post(`${API}/auth/login`, {
    data: { email, password },
  })
  expect(probe.status()).toBe(200)
  const { expiresIn } = (await probe.json()) as { expiresIn: number }
  test.skip(
    expiresIn > 60,
    `API đang phát access token ${expiresIn} giây — cần Jwt__AccessTokenSeconds=10 (xem đầu file).`
  )

  // Đếm refresh ở cấp CONTEXT — gồm mọi tab.
  let refreshes = 0
  context.on("request", (r) => {
    if (r.url().endsWith("/auth/refresh") && r.method() === "POST") refreshes++
  })

  const tab1 = await context.newPage()
  await tab1.goto("/login?next=%2Fme")
  await tab1.getByLabel("Email").fill(email)
  await tab1.getByLabel("Mật khẩu").fill(password)
  await tab1.getByRole("button", { name: "Đăng nhập" }).click()
  await expect(tab1.getByTestId("me-profile")).toContainText(email)

  // Tab 2, 3 mở /me: memory trống → mỗi tab khởi động phiên bằng cookie, lần lượt qua Web Lock.
  const tab2 = await context.newPage()
  await tab2.goto("/me")
  await expect(tab2.getByTestId("me-profile")).toContainText(email)
  const tab3 = await context.newPage()
  await tab3.goto("/me")
  await expect(tab3.getByTestId("me-profile")).toContainText(email)
  const tabs = [tab1, tab2, tab3]

  // Token của cả 3 tab hết hạn phía server.
  await tab1.waitForTimeout(CHO_HET_HAN_MS)
  refreshes = 0

  // Bắn đồng thời: 3 request /me nhận 401 gần như cùng lúc ở 3 vùng nhớ khác nhau.
  await Promise.all(tabs.map(bamTaiLaiVaChoXong))

  expect(refreshes, "số POST /auth/refresh khi 3 tab cùng nhận 401").toBe(1)
  for (const t of tabs) {
    await expect(t).toHaveURL(/\/me$/)
    await expect(t.getByTestId("me-profile")).toContainText(email)
  }

  // Cả 3 tab đã có token mới (phát qua BroadcastChannel) → bấm lần nữa không cần refresh.
  refreshes = 0
  await Promise.all(tabs.map(bamTaiLaiVaChoXong))
  expect(refreshes, "token mới đã về cả 3 tab").toBe(0)
})
