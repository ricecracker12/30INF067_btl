import { defineConfig, devices } from "@playwright/test"

const baseURL = process.env.PLAYWRIGHT_BASE_URL ?? "http://localhost:3000"

// Đ-E8: chỉ một trình duyệt họ Chromium, và `workers: 1`. Rate limit nhóm /auth/* là 10 req/phút THEO IP —
// chạy song song là tự đánh 429 vào mặt mình, test đỏ mà server không sai.
// GĐ1 Playwright KHÔNG vào CI (cần API + Postgres + Redis + Mailpit); chạy local, kết quả
// dán vào PR. Spec đầu tiên xuất hiện ở E4.
export default defineConfig({
  testDir: "./e2e",
  fullyParallel: false,
  workers: 1,
  // Một lượt chạy tự động lại cho ca đỏ. KHÔNG phải để che lỗi: Playwright in riêng dòng `flaky` khi ca
  // đỏ rồi xanh lại, và con số đó phải dán vào PR y như `passed`/`failed` — một ca flaky vẫn là một ca cần
  // nhìn. Có `retries` vì worker Playwright thỉnh thoảng chết trên Windows với `0xC0000409`
  // (STATUS_STACK_BUFFER_OVERRUN) TRƯỚC khi test chạy dòng đầu tiên — đã gặp 2026-09-21 với
  // `register.spec.ts`: đỏ ở 0ms trong lượt cả bộ, chạy riêng ngay sau đó xanh trong 1,9 giây. Không có
  // `retries` thì một lần Windows hắt hơi là mất cả lượt 8 phút.
  retries: 1,
  forbidOnly: !!process.env.CI,
  reporter: "list",
  use: {
    baseURL,
    trace: "on-first-retry",
  },
  // Lệch Đ-E8 (2026-09-16, nhóm chốt): dùng Chrome ĐÃ CÀI trên máy (`channel: "chrome"`) thay vì
  // Chromium ghim theo Playwright — khỏi tải thêm trình duyệt. Đổi lại, bản Chrome tự cập nhật và
  // khác nhau giữa các máy: dán kết quả vào PR thì ghi kèm bản Chrome đã chạy.
  projects: [
    {
      name: "chrome",
      use: { ...devices["Desktop Chrome"], channel: "chrome" },
    },
  ],
  webServer: {
    command: "pnpm dev",
    url: baseURL,
    reuseExistingServer: !process.env.CI,
    timeout: 120_000,
  },
})
