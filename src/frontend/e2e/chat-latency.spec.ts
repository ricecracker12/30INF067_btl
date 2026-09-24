import { mkdirSync, writeFileSync } from "node:fs"
import { join } from "node:path"

import { expect, test, type APIRequestContext, type Browser, type Page } from "@playwright/test"

import { API, giuHanMucAuth } from "./dev-api"
import { dangNhapApi, dangNhapUi, taiKhoanCoSan, taoTaiKhoanCoHoSo, type TaiKhoan } from "./post-helpers"

// GĐ5 E8/F3 — đo p95 gửi→nhận (NFR-PERF-03, GOAL-02) bằng CHÍNH màn chat thật (giai-doan-5.md Mục 10.7). KHÔNG chạy trong bộ E2E
// thường: bật bằng `CHAT_LATENCY=1`.
//
// - Hai BrowserContext trong MỘT tiến trình Chrome trên MỘT máy → cùng một đồng hồ, t0/t1 so được với nhau.
// - Màn mở với `?latency=1`: tab gửi ghi t0 theo clientMsgId lúc bấm gửi, tab nhận ghi t1 sau khi tin được VẼ (`features/chat/latency*`).
// - Nhịp 1 tin/giây (đo độ trễ, không đo thông lượng), N tin A→B rồi N tin B→A. Mặc định N = 200 (`CHAT_LATENCY_N`).
// - Tài khoản: trên staging đặt `E2E_A_EMAIL`, `E2E_A_PASSWORD`, `E2E_B_EMAIL`, `E2E_B_PASSWORD` (hai người ĐÃ là bạn — biến môi
//   trường hoặc `.env.e2e.local`) và `PLAYWRIGHT_BASE_URL`, `PLAYWRIGHT_API_URL`. Không đặt thì tạo tài khoản mới qua Mailpit dev và tự kết bạn.
// - Kết quả: `test-results/chat-latency.json` + in p50/p95/p99. Báo cáo viết tay ở `docs/giai-doan-5/bao-cao-p95-chat.md`.

test.skip(process.env.CHAT_LATENCY !== "1", "Chỉ chạy khi CHAT_LATENCY=1 (đo p95, không phải kiểm chức năng)")

const N = Number(process.env.CHAT_LATENCY_N ?? "200")

type Samples = { sent: Record<string, number>; rendered: Record<string, number> }

function percentile(sorted: number[], p: number) {
  if (sorted.length === 0) return NaN
  const i = Math.min(sorted.length - 1, Math.ceil((p / 100) * sorted.length) - 1)
  return sorted[Math.max(0, i)]
}

async function taiKhoan(request: APIRequestContext): Promise<[TaiKhoan, TaiKhoan]> {
  const coSan = taiKhoanCoSan()
  if (coSan) {
    await giuHanMucAuth(4)
    return [
      await dangNhapApi(request, coSan.E2E_A_EMAIL, coSan.E2E_A_PASSWORD),
      await dangNhapApi(request, coSan.E2E_B_EMAIL, coSan.E2E_B_PASSWORD),
    ]
  }
  await giuHanMucAuth(8)
  const tag = Math.random().toString(36).slice(2, 8)
  const a = await taoTaiKhoanCoHoSo(request, "lat-a", `Đo A ${tag}`)
  const b = await taoTaiKhoanCoHoSo(request, "lat-b", `Đo B ${tag}`)
  expect((await request.post(`${API}/friends/requests`, { headers: a.auth, data: { userId: b.userId } })).status()).toBe(201)
  expect((await request.post(`${API}/friends/requests/${a.userId}/accept`, { headers: b.auth })).status()).toBe(200)
  return [a, b]
}

async function moHoiThoai(request: APIRequestContext, a: TaiKhoan, b: TaiKhoan) {
  const res = await request.post(`${API}/conversations`, { headers: a.auth, data: { userId: b.userId } })
  expect([200, 201]).toContain(res.status())
  return ((await res.json()) as { conversationId: string }).conversationId
}

async function trang(browser: Browser, tk: TaiKhoan, path: string): Promise<Page> {
  const page = await (await browser.newContext()).newPage()
  await dangNhapUi(page, tk)
  await page.goto(`${path}?latency=1`)
  await expect(page.getByLabel("Nội dung tin nhắn")).toBeEnabled()
  // Chờ hub kết nối (không đo lúc đang bắt tay): banner "Đang kết nối lại" không có và đã có ít nhất một nhịp.
  await page.waitForTimeout(1_500)
  return page
}

async function mauCua(page: Page): Promise<Samples> {
  return page.evaluate(() => (window as unknown as { __chatLatency?: Samples }).__chatLatency ?? { sent: {}, rendered: {} })
}

async function chieu(from: Page, to: Page, label: string, n: number) {
  for (let i = 0; i < n; i++) {
    const box = from.getByLabel("Nội dung tin nhắn")
    await box.fill(`${label} #${i + 1}`)
    await box.press("Enter")
    await from.waitForTimeout(1_000)
  }
  await to.waitForTimeout(2_000) // tin cuối kịp vẽ
  const sent = (await mauCua(from)).sent
  const rendered = (await mauCua(to)).rendered
  const ms = Object.keys(sent)
    .filter((id) => rendered[id] !== undefined)
    .map((id) => rendered[id] - sent[id])
    .sort((x, y) => x - y)
  return { label, sent: Object.keys(sent).length, received: ms.length, ms }
}

test("đo p95 gửi→nhận trên màn chat thật", async ({ browser, request }) => {
  test.setTimeout((N * 2 * 1_200 + 120_000) | 0)
  const [a, b] = await taiKhoan(request)
  const conversationId = await moHoiThoai(request, a, b)
  const pa = await trang(browser, a, `/messages/${conversationId}`)
  const pb = await trang(browser, b, `/messages/${conversationId}`)

  const ab = await chieu(pa, pb, "A→B", N)
  const ba = await chieu(pb, pa, "B→A", N)

  const all = [...ab.ms, ...ba.ms].sort((x, y) => x - y)
  const report = {
    at: new Date().toISOString(),
    baseURL: test.info().project.use.baseURL,
    n: N,
    directions: [ab, ba].map(({ label, sent, received, ms }) => ({
      label,
      sent,
      received,
      p50: percentile(ms, 50),
      p95: percentile(ms, 95),
      p99: percentile(ms, 99),
      max: ms.at(-1),
    })),
    overall: {
      samples: all.length,
      p50: percentile(all, 50),
      p95: percentile(all, 95),
      p99: percentile(all, 99),
      max: all.at(-1),
    },
  }
  mkdirSync("test-results", { recursive: true })
  writeFileSync(join("test-results", "chat-latency.json"), JSON.stringify(report, null, 2))
  console.log(JSON.stringify(report.overall), JSON.stringify(report.directions))

  expect(all.length).toBeGreaterThan(0)
})
