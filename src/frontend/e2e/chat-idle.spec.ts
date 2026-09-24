import { expect, test } from "@playwright/test"

import { API, giuHanMucAuth } from "./dev-api"
import {
  dangNhapApi,
  dangNhapUi,
  taiKhoanCoSan,
  taoTaiKhoanCoHoSo,
  type TaiKhoan,
} from "./post-helpers"

// GĐ5 F4 — hai dòng Mục 12 "Realtime" kiểm được TỪ NGOÀI, không cần quyền VM:
//  - Để yên tab chat 10 phút: 0 lần kết nối lại (keep-alive qua được timeout của apache và Cloudflare).
//  - Kết nối tự cắt sau 15 phút (tuổi thọ = access token, Đ-5.10) và tự nối lại không ai nhận ra.
// Chạy ~17 phút nên KHÔNG nằm trong bộ E2E thường: bật bằng `CHAT_IDLE=1`. Tài khoản như `chat.spec.ts`.

test.skip(
  process.env.CHAT_IDLE !== "1",
  "Chỉ chạy khi CHAT_IDLE=1 (để yên tab ~17 phút)"
)

const PHUT = 60_000

test("để yên tab chat: 10 phút không nối lại, hết 15 phút tự nối lại bằng vé mới, tin vẫn tới", async ({
  browser,
  request,
}) => {
  test.setTimeout(20 * PHUT)
  const coSan = taiKhoanCoSan()
  let A: TaiKhoan
  let B: TaiKhoan
  if (coSan) {
    await giuHanMucAuth(3)
    A = await dangNhapApi(request, coSan.E2E_A_EMAIL, coSan.E2E_A_PASSWORD)
    B = await dangNhapApi(request, coSan.E2E_B_EMAIL, coSan.E2E_B_PASSWORD)
  } else {
    await giuHanMucAuth(8)
    const tag = Math.random().toString(36).slice(2, 8)
    A = await taoTaiKhoanCoHoSo(request, "idle-a", `An ${tag}`)
    B = await taoTaiKhoanCoHoSo(request, "idle-b", `Bình ${tag}`)
    expect(
      (
        await request.post(`${API}/friends/requests`, {
          headers: A.auth,
          data: { userId: B.userId },
        })
      ).status()
    ).toBe(201)
    expect(
      (
        await request.post(`${API}/friends/requests/${A.userId}/accept`, {
          headers: B.auth,
        })
      ).status()
    ).toBe(200)
  }
  const opened = await request.post(`${API}/conversations`, {
    headers: A.auth,
    data: { userId: B.userId },
  })
  expect([200, 201]).toContain(opened.status())
  const conversationId = ((await opened.json()) as { conversationId: string })
    .conversationId

  const ctx = await browser.newContext()
  try {
    const page = await ctx.newPage()
    await dangNhapUi(page, A)

    const t0 = Date.now()
    const moc = () => `${((Date.now() - t0) / PHUT).toFixed(1)} phút`
    const ws: { url: string; moLuc: string; dongLuc?: string }[] = []
    let ve = 0
    page.on("request", (r) => {
      if (r.url().includes("/bff/api/realtime/tickets")) ve += 1
    })
    page.on("websocket", (w) => {
      const rec: { url: string; moLuc: string; dongLuc?: string } = {
        url: w.url(),
        moLuc: moc(),
      }
      ws.push(rec)
      w.on("close", () => (rec.dongLuc = moc()))
    })

    await page.goto(`/messages/${conversationId}`)
    await expect(page.getByLabel("Nội dung tin nhắn")).toBeEnabled()
    await expect.poll(() => ws.length).toBe(1)

    // Dòng 1: 10 phút để yên → vẫn đúng MỘT WebSocket, chưa đóng, một vé.
    await page.waitForTimeout(10 * PHUT)
    console.log(
      `10 phút: ${JSON.stringify({ ws: ws.map(({ moLuc, dongLuc }) => ({ moLuc, dongLuc })), ve })}`
    )
    expect(ws).toHaveLength(1)
    expect(ws[0].dongLuc).toBeUndefined()
    expect(ve).toBe(1)
    await expect(page.getByTestId("chat-banner")).toHaveCount(0)

    // Dòng 2: tới ~16,5 phút → kết nối đầu đã bị server cắt, đã có kết nối thứ hai bằng vé thứ hai.
    await page.waitForTimeout(6.5 * PHUT)
    console.log(
      `16,5 phút: ${JSON.stringify({ ws: ws.map(({ moLuc, dongLuc }) => ({ moLuc, dongLuc })), ve })}`
    )
    expect(ws[0].dongLuc).toBeDefined()
    expect(ws.length).toBeGreaterThanOrEqual(2)
    expect(ws.at(-1)!.dongLuc).toBeUndefined()
    expect(ve).toBe(ws.length)
    await expect(page.getByTestId("chat-banner")).toHaveCount(0)

    // "Không ai nhận ra": tin B gửi (REST, token mới — token lúc đầu đã hết hạn) vẫn tới qua kết nối mới.
    await giuHanMucAuth(1)
    const B2 = coSan
      ? await dangNhapApi(request, coSan.E2E_B_EMAIL, coSan.E2E_B_PASSWORD)
      : B
    const text = `Sau khi nối lại ${Math.random().toString(36).slice(2, 8)}`
    const sent = await request.post(
      `${API}/conversations/${conversationId}/messages`,
      {
        headers: B2.auth,
        data: { content: text, clientMsgId: crypto.randomUUID() },
      }
    )
    expect(sent.status(), await sent.text()).toBe(201)
    await expect(
      page.getByTestId("chat-message").filter({ hasText: text })
    ).toHaveCount(1, { timeout: 3_000 })
  } finally {
    await ctx.close()
  }
})
