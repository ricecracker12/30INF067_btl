import { expect, test, type Page } from "@playwright/test"

import { API, giuHanMucAuth, taoTaiKhoanDaXacMinh } from "./dev-api"

// E7 + Đ-E14 — E2E-02 kịch bản 3 tab trên API DEV THẬT chạy access token NGẮN. Refresh single-flight giờ nằm ở BFF
// (khóa Redis): trình duyệt không thấy lời gọi refresh server-to-server, nên spec kiểm KẾT QUẢ — 3 tab cùng nhận 401 ở
// server vẫn đều ra hồ sơ, không tab nào về /login, và trình duyệt không bao giờ gọi refresh. SỐ LẦN refresh = 1 do
// Vitest `lib/bff/handlers.test.ts` ("3 request đồng thời…") đếm, trên API giả.
//
// Chạy RIÊNG, trên API + FE riêng để không phải khởi động lại API dev đang dùng:
//
//   # 1) API token 10 giây, cổng 5260 (repo root; --no-build: API 5259 đang giữ file trong bin/)
//   $env:ASPNETCORE_ENVIRONMENT='Development'; $env:ASPNETCORE_URLS='http://localhost:5260'
//   $env:Jwt__AccessTokenSeconds='10'
//   dotnet run --no-build --no-launch-profile --project src/backend/SocialApp.Api
//
//   # 2) FE cổng 3001 — bản BUILD (Next 16 không cho hai `next dev` chung thư mục); BFF trỏ API 5260
//   pnpm build
//   $env:API_INTERNAL_URL='http://localhost:5260/api/v1'; $env:APP_ORIGIN='http://localhost:3001'
//   $env:REDIS_URL='redis://localhost:6379'; $env:SESSION_ENCRYPTION_KEY=(openssl rand -base64 32)
//   pnpm exec next start -p 3001
//
//   # 3)
//   $env:PLAYWRIGHT_BASE_URL='http://localhost:3001'; $env:PLAYWRIGHT_API_URL='http://localhost:5260/api/v1'
//   pnpm exec playwright test single-flight
//
// Không đặt PLAYWRIGHT_API_URL (lượt `pnpm test:e2e` thường) → bỏ qua ngay, không tốn request. Đặt rồi mà API vẫn phát
// token dài → bỏ qua, nêu `expiresIn` đọc được — không đoán.

/** 10 giây token + ClockSkew 30 giây của JwtBearer + dư. */
const CHO_HET_HAN_MS = 45_000

async function bamTaiLaiVaChoXong(page: Page) {
  const me = page.waitForResponse(
    (r) =>
      r.url().endsWith("/bff/api/me") &&
      r.request().method() === "GET" &&
      r.status() === 200
  )
  await page.getByRole("button", { name: "Tải lại" }).click()
  await me
}

test("3 tab, access token hết hạn, bấm Tải lại đồng thời → cả 3 ra hồ sơ, không tab nào về /login, trình duyệt không gọi refresh", async ({
  context,
  request,
}) => {
  test.skip(
    !process.env.PLAYWRIGHT_API_URL,
    "Chạy riêng trên API Jwt__AccessTokenSeconds=10 — xem đầu file."
  )
  test.setTimeout(150_000)

  // register + verify + login dò expiresIn + login trên UI + refresh (BFF) khi token hết hạn
  await giuHanMucAuth(5)
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

  // Mọi request của mọi tab tới đường auth — trình duyệt KHÔNG được gọi refresh nào.
  const authCalls: string[] = []
  context.on("request", (r) => {
    if (r.url().includes("/auth/")) authCalls.push(new URL(r.url()).pathname)
  })

  const tab1 = await context.newPage()
  await tab1.goto("/login?next=%2Fme")
  await tab1.getByLabel("Email").fill(email)
  await tab1.getByLabel("Mật khẩu").fill(password)
  await tab1.getByRole("button", { name: "Đăng nhập" }).click()
  await expect(tab1.getByTestId("me-profile")).toContainText(email)

  // Tab 2, 3 mở /me: cùng cookie phiên → BFF đã có token, không cần refresh.
  const tab2 = await context.newPage()
  await tab2.goto("/me")
  await expect(tab2.getByTestId("me-profile")).toContainText(email)
  const tab3 = await context.newPage()
  await tab3.goto("/me")
  await expect(tab3.getByTestId("me-profile")).toContainText(email)
  const tabs = [tab1, tab2, tab3]

  // Access token trong Redis hết hạn phía API.
  await tab1.waitForTimeout(CHO_HET_HAN_MS)

  // Bắn ĐỒNG THỜI 9 request /bff/api/me bằng CHÍNH cookie phiên của các tab (context.request dùng chung cookie jar).
  // Bấm nút ở ba tab — hay fetch từ trong trang — không tạo được tranh chấp thật: refresh đầu tiên đã xong trước khi các
  // request kia nhận 401 (đã thử: bỏ khóa Redis mà cả hai cách vẫn xanh). Gửi từ Node thì chồng lên nhau thật.
  // Không khóa → mỗi request tự refresh → 9 lượt /auth/* dồn vào API, vượt 10/phút → có request 429. Có khóa → cả 9 là 200.
  const burst = await Promise.all(
    Array.from({ length: 9 }, () =>
      context.request.get("/bff/api/me").then((r) => r.status())
    )
  )
  expect(burst).toEqual(Array(9).fill(200))

  // Giao diện: bấm "Tải lại" ở cả 3 tab vẫn ra hồ sơ.
  await Promise.all(tabs.map(bamTaiLaiVaChoXong))
  for (const t of tabs) {
    await expect(t).toHaveURL(/\/me$/)
    await expect(t.getByTestId("me-profile")).toContainText(email)
  }
  // Bấm lần nữa vẫn được — refresh token mới đã được BFF cất đúng (xoay vòng không làm hỏng phiên).
  await Promise.all(tabs.map(bamTaiLaiVaChoXong))

  expect(authCalls.filter((p) => p.endsWith("/refresh"))).toEqual([])
})
