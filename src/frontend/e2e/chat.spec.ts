import { mkdirSync } from "node:fs"
import { join } from "node:path"

import { expect, test, type APIRequestContext, type Page } from "@playwright/test"

import { API, giuHanMucAuth } from "./dev-api"
import {
  dangNhapApi,
  dangNhapUi,
  donRacSauTest,
  taiKhoanCoSan,
  taoTaiKhoanCoHoSo,
  type TaiKhoan,
} from "./post-helpers"

async function laBanBe(request: APIRequestContext, a: TaiKhoan, b: TaiKhoan) {
  const r = await request.get(`${API}/relationships/${b.userId}`, { headers: a.auth })
  return ((await r.json()) as { friendship: string }).friendship === "friends"
}

// GĐ5 E9 — lát cắt chat hai trình duyệt (giai-doan-5.md Mục 10.6, Mục 12 "Lát cắt dọc"). Chạy trên API + FE dev thật, Chrome đã
// cài (Đ-E8), `workers: 1`. Hai `BrowserContext` — mỗi người một cookie `__Host-sid`.
//
// Bạn bè dựng qua API thật (GĐ4). Mọi khẳng định "hiện MỘT lần" đếm theo nội dung có hậu tố ngẫu nhiên của lượt chạy này.

test.afterEach(async ({ request }) => {
  await donRacSauTest(request)
})

async function ketBanApi(request: APIRequestContext, a: TaiKhoan, b: TaiKhoan) {
  const sent = await request.post(`${API}/friends/requests`, { headers: a.auth, data: { userId: b.userId } })
  expect(sent.status(), await sent.text()).toBe(201)
  const accepted = await request.post(`${API}/friends/requests/${a.userId}/accept`, { headers: b.auth })
  expect(accepted.status(), await accepted.text()).toBe(200)
}

/**
 * F2 bằng chứng: đặt `E2E_BANG_CHUNG=<thư mục>` (vd `../../docs/giai-doan-5/bang-chung`) thì chụp ảnh ở từng mốc của lát cắt.
 * Không đặt thì không chụp — lượt thường không đẻ file.
 */
const BANG_CHUNG = process.env.E2E_BANG_CHUNG
async function chup(page: Page, ten: string) {
  if (!BANG_CHUNG) return
  mkdirSync(BANG_CHUNG, { recursive: true })
  await page.screenshot({ path: join(BANG_CHUNG, `f2-${ten}.png`) })
}

const tinNhan = (page: Page, text: string) => page.getByTestId("chat-message").filter({ hasText: text })

async function gui(page: Page, text: string) {
  const box = page.getByLabel("Nội dung tin nhắn")
  await box.fill(text)
  await box.press("Enter")
}

test("hai tài khoản: nhắn realtime → Đã xem → mất mạng + Thử lại không lặp tin → badge chưa đọc → hủy kết bạn thì chỉ đọc", async ({
  browser,
  request,
}) => {
  test.setTimeout(180_000)
  const tag = Math.random().toString(36).slice(2, 8)
  const coSan = taiKhoanCoSan()
  let A: TaiKhoan
  let B: TaiKhoan
  if (coSan) {
    // Staging: login API hai người + login UI hai người → 4 lượt `/auth/*`.
    await giuHanMucAuth(4)
    A = await dangNhapApi(request, coSan.E2E_A_EMAIL, coSan.E2E_A_PASSWORD)
    B = await dangNhapApi(request, coSan.E2E_B_EMAIL, coSan.E2E_B_PASSWORD)
    if (!(await laBanBe(request, A, B))) await ketBanApi(request, A, B)
  } else {
    // A, B: register + verify + login API (3 lượt mỗi người) + login UI của cả hai → 8 lượt `/auth/*`.
    await giuHanMucAuth(8)
    A = await taoTaiKhoanCoHoSo(request, "chat-a", `An ${tag}`)
    B = await taoTaiKhoanCoHoSo(request, "chat-b", `Bình ${tag}`)
    await ketBanApi(request, A, B)
  }

  const ctxA = await browser.newContext()
  const ctxB = await browser.newContext()
  try {
    const pa = await ctxA.newPage()
    const pb = await ctxB.newPage()
    await dangNhapUi(pa, A)
    await dangNhapUi(pb, B)

    // 1. A mở hồ sơ B → "Nhắn tin" (chỉ hiện khi là bạn) → màn chat.
    await pa.goto(`/users/${B.userId}`)
    await pa.getByTestId("start-chat").click()
    await pa.waitForURL(/\/messages\/[0-9a-f-]{36}$/)
    const conversationUrl = new URL(pa.url()).pathname
    // Đ-5.16: MỘT lần xin vé cho MỘT lần kết nối (không negotiate). Đếm từ lần tải trang này của B tới hết bước 2.
    let veCuaB = 0
    pb.on("request", (r) => {
      if (r.url().includes("/bff/api/realtime/tickets")) veCuaB += 1
    })
    // Đ-5.16 thay ảnh tab Network: URL WebSocket mang VÉ (43 ký tự base64url), không bao giờ mang JWT (`eyJ…`).
    const wsCuaB: string[] = []
    pb.on("websocket", (ws) => wsCuaB.push(ws.url()))
    await pb.goto(conversationUrl)
    await expect(pb.getByLabel("Nội dung tin nhắn")).toBeVisible()

    // 2. AC-01: A gửi → B thấy; tab B đang hiển thị nên A thấy "Đã xem".
    const T1 = `Chào Bình ${tag}`
    await gui(pa, T1)
    await expect(tinNhan(pb, T1)).toHaveCount(1, { timeout: 5_000 })
    await expect(pa.getByTestId("chat-delivery")).toHaveText(/Đã xem/, { timeout: 10_000 })
    expect(veCuaB).toBe(1)
    expect(wsCuaB).toHaveLength(1)
    const ve = new URL(wsCuaB[0]).searchParams.get("access_token") ?? ""
    expect(ve).toMatch(/^[A-Za-z0-9_-]{43}$/)
    expect(wsCuaB[0]).not.toMatch(/eyJ/)
    await chup(pa, "1-a-da-xem")
    await chup(pb, "1-b-nhan-tin")

    // 3. AC-03: A mất mạng giữa lúc gửi → Thất bại + Thử lại; có mạng lại, Thử lại → B thấy ĐÚNG MỘT tin.
    const T2 = `Gửi lúc mất mạng ${tag}`
    await ctxA.setOffline(true)
    await gui(pa, T2)
    const thuLai = pa.getByRole("button", { name: "Thử lại" })
    await expect(thuLai).toBeVisible({ timeout: 15_000 })
    await ctxA.setOffline(false)
    // Hai đường đều ĐÚNG và đều phải ra MỘT tin: (a) lời gọi hub bị treo lúc offline tới server khi có mạng lại → lượt lấp chỗ
    // hở sau khi nối lại nạp đúng tin đó và thay tin "Thất bại" (nút biến mất trước khi kịp bấm — đo được ở lượt chạy đầu);
    // (b) tin chưa tới server → bấm Thử lại, CÙNG clientMsgId. Bấm nếu còn nút; đếm tin là khẳng định chính.
    await thuLai.click({ timeout: 3_000 }).catch(() => undefined)
    await expect(tinNhan(pb, T2)).toHaveCount(1, { timeout: 20_000 })
    await expect(tinNhan(pa, T2)).toHaveCount(1)
    await pb.waitForTimeout(1_000)
    await expect(tinNhan(pb, T2)).toHaveCount(1)

    // 4. AC-02: B rời màn chat; A gửi 2 tin → badge của B là 2; B mở lại hội thoại → badge về 0.
    await pb.goto("/friends")
    const badge = pb.getByTestId("unread-badge")
    await expect(pb.getByTestId("nav-messages")).toBeVisible()
    // Tài khoản có sẵn (staging) có thể còn tin chưa đọc của lượt trước — so với số trước đó, không so với 0.
    const chuaDocTruoc = (
      (await (await request.get(`${API}/conversations/unread-count`, { headers: B.auth })).json()) as { total: number }
    ).total
    await gui(pa, `Tin chưa đọc 1 ${tag}`)
    await expect(tinNhan(pa, `Tin chưa đọc 1 ${tag}`)).toHaveCount(1)
    await gui(pa, `Tin chưa đọc 2 ${tag}`)
    await expect(badge).toHaveText(String(chuaDocTruoc + 2), { timeout: 10_000 })
    await chup(pb, "2-b-badge-chua-doc")
    await pb.goto("/messages")
    await expect(pb.getByTestId("conversation-unread").first()).toHaveText("2")
    await pb.getByTestId("conversation-row").first().click()
    await expect(tinNhan(pb, `Tin chưa đọc 2 ${tag}`)).toBeVisible()
    if (chuaDocTruoc === 0) await expect(badge).toHaveCount(0, { timeout: 10_000 })
    else await expect(badge).toHaveText(String(chuaDocTruoc), { timeout: 10_000 })

    // 5. AC-04: B hủy kết bạn → cả hai thấy thanh "chỉ đọc", ô soạn khóa; lịch sử còn nguyên.
    const unfriend = await request.delete(`${API}/friends/${A.userId}`, { headers: B.auth })
    expect(unfriend.status()).toBe(204)
    await pa.reload()
    await expect(pa.getByTestId("chat-read-only")).toBeVisible()
    await expect(pa.getByLabel("Nội dung tin nhắn")).toBeDisabled()
    await expect(tinNhan(pa, T1)).toHaveCount(1)
    await pb.reload()
    await expect(pb.getByTestId("chat-read-only")).toBeVisible()
    await chup(pa, "3-a-chi-doc")

    // Tài khoản có sẵn: kết bạn LẠI để lần chạy sau (và F3 đo p95) vẫn có hai người là bạn.
    if (coSan) await ketBanApi(request, A, B)
  } finally {
    await ctxA.close()
    await ctxB.close()
  }
})

// Mục 12 "Chặn WebSocket → vẫn gửi và nhận qua fallback, trễ ≤ ~3s" (Đ-5.12). B bị chặn MỌI WebSocket `/hubs/*` (như DevTools chặn
// URL); A nối bình thường. B hỏng 3 lần liền (0 / 2 / 5 s) → fallback: gửi bằng REST, hỏi lại mỗi 3 giây.
test("chặn WebSocket của B → B vẫn nhận (hỏi lại 3s) và gửi được qua REST", async ({ browser, request }) => {
  test.setTimeout(120_000)
  const tag = Math.random().toString(36).slice(2, 8)
  const coSan = taiKhoanCoSan()
  let A: TaiKhoan
  let B: TaiKhoan
  if (coSan) {
    await giuHanMucAuth(4)
    A = await dangNhapApi(request, coSan.E2E_A_EMAIL, coSan.E2E_A_PASSWORD)
    B = await dangNhapApi(request, coSan.E2E_B_EMAIL, coSan.E2E_B_PASSWORD)
    if (!(await laBanBe(request, A, B))) await ketBanApi(request, A, B)
  } else {
    await giuHanMucAuth(8)
    A = await taoTaiKhoanCoHoSo(request, "chatfb-a", `An ${tag}`)
    B = await taoTaiKhoanCoHoSo(request, "chatfb-b", `Bình ${tag}`)
    await ketBanApi(request, A, B)
  }
  const opened = await request.post(`${API}/conversations`, { headers: A.auth, data: { userId: B.userId } })
  expect([200, 201]).toContain(opened.status())
  const conversationUrl = `/messages/${((await opened.json()) as { conversationId: string }).conversationId}`

  const ctxA = await browser.newContext()
  const ctxB = await browser.newContext()
  try {
    let wsBiChan = 0
    await ctxB.routeWebSocket(/\/hubs\//, (ws) => {
      wsBiChan += 1
      void ws.close()
    })
    const pa = await ctxA.newPage()
    const pb = await ctxB.newPage()
    await dangNhapUi(pa, A)
    await dangNhapUi(pb, B)
    await pa.goto(conversationUrl)
    await pb.goto(conversationUrl)
    await expect(pa.getByLabel("Nội dung tin nhắn")).toBeEnabled()
    await expect(pb.getByTestId("chat-banner")).toBeVisible({ timeout: 15_000 })
    await expect.poll(() => wsBiChan, { timeout: 15_000 }).toBeGreaterThanOrEqual(3)

    // A (hub) → B (hỏi lại 3 s): hạn 5 s = một chu kỳ hỏi lại + đường truyền.
    const T1 = `Tới B lúc chặn WS ${tag}`
    await gui(pa, T1)
    await expect(tinNhan(pa, T1)).toHaveCount(1)
    const t0 = Date.now()
    await expect(tinNhan(pb, T1)).toHaveCount(1, { timeout: 5_000 })
    const treNhan = Date.now() - t0
    await chup(pb, "4-b-fallback-nhan")

    // B (REST) → A (hub đẩy như mọi tin khác).
    const T2 = `B gửi bằng REST ${tag}`
    await gui(pb, T2)
    await expect(tinNhan(pa, T2)).toHaveCount(1, { timeout: 5_000 })
    await expect(tinNhan(pb, T2)).toHaveCount(1)
    await expect(pb.getByRole("button", { name: "Thử lại" })).toHaveCount(0)
    console.log(`fallback: B nhận sau ${treNhan} ms (${wsBiChan} WebSocket bị chặn)`)
  } finally {
    await ctxA.close()
    await ctxB.close()
  }
})

// Mục 12 AC-02 đúng nguyên văn: B KHÔNG đăng nhập ở đâu cả (đã đăng xuất), A gửi 3 tin, B đăng nhập lại → badge +3 nạp qua REST
// lúc vào trang (không phải đếm từ sự kiện realtime như ca đầu), mở hội thoại thấy đủ, badge về mức cũ.
test("B đang đăng xuất, A gửi 3 tin → B đăng nhập lại thấy badge +3, mở ra đủ tin, badge về lại", async ({
  browser,
  request,
}) => {
  test.setTimeout(90_000)
  const tag = Math.random().toString(36).slice(2, 8)
  const coSan = taiKhoanCoSan()
  let A: TaiKhoan
  let B: TaiKhoan
  if (coSan) {
    await giuHanMucAuth(3)
    A = await dangNhapApi(request, coSan.E2E_A_EMAIL, coSan.E2E_A_PASSWORD)
    B = await dangNhapApi(request, coSan.E2E_B_EMAIL, coSan.E2E_B_PASSWORD)
    if (!(await laBanBe(request, A, B))) await ketBanApi(request, A, B)
  } else {
    await giuHanMucAuth(7)
    A = await taoTaiKhoanCoHoSo(request, "chatoff-a", `An ${tag}`)
    B = await taoTaiKhoanCoHoSo(request, "chatoff-b", `Bình ${tag}`)
    await ketBanApi(request, A, B)
  }
  const opened = await request.post(`${API}/conversations`, { headers: A.auth, data: { userId: B.userId } })
  expect([200, 201]).toContain(opened.status())
  const conversationId = ((await opened.json()) as { conversationId: string }).conversationId

  const chuaDocTruoc = (
    (await (await request.get(`${API}/conversations/unread-count`, { headers: B.auth })).json()) as { total: number }
  ).total
  for (let i = 1; i <= 3; i++) {
    const sent = await request.post(`${API}/conversations/${conversationId}/messages`, {
      headers: A.auth,
      data: { content: `Lúc B vắng ${i} ${tag}`, clientMsgId: crypto.randomUUID() },
    })
    expect(sent.status(), await sent.text()).toBe(201)
  }

  const ctxB = await browser.newContext()
  try {
    const pb = await ctxB.newPage()
    await dangNhapUi(pb, B)
    const badge = pb.getByTestId("unread-badge")
    await expect(badge).toHaveText(String(chuaDocTruoc + 3), { timeout: 10_000 })
    // Ảnh chụp ở /friends, KHÔNG ở /me: trang /me hiện email của tài khoản — bằng chứng vào repo không được mang PII.
    await pb.goto("/friends")
    await expect(badge).toHaveText(String(chuaDocTruoc + 3))
    await chup(pb, "5-b-dang-nhap-lai-badge")
    await pb.goto(`/messages/${conversationId}`)
    for (let i = 1; i <= 3; i++) await expect(tinNhan(pb, `Lúc B vắng ${i} ${tag}`)).toHaveCount(1)
    if (chuaDocTruoc === 0) await expect(badge).toHaveCount(0, { timeout: 10_000 })
    else await expect(badge).toHaveText(String(chuaDocTruoc), { timeout: 10_000 })
  } finally {
    await ctxB.close()
  }
})
