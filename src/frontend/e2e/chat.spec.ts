import { existsSync, readFileSync } from "node:fs"

import { expect, test, type APIRequestContext, type Page } from "@playwright/test"

import { API, giuHanMucAuth } from "./dev-api"
import { dangNhapApi, dangNhapUi, donRacSauTest, taoTaiKhoanCoHoSo, type TaiKhoan } from "./post-helpers"

/**
 * F2 (staging): hai tài khoản THẬT, đã là bạn, lấy từ biến môi trường hoặc file `.env.e2e.local` (gitignore — không commit).
 * Không có thì spec tạo tài khoản mới qua Mailpit dev như mọi spec khác.
 */
function taiKhoanCoSan(): Record<string, string> | null {
  const keys = ["E2E_A_EMAIL", "E2E_A_PASSWORD", "E2E_B_EMAIL", "E2E_B_PASSWORD"]
  const fromFile: Record<string, string> = {}
  if (existsSync(".env.e2e.local"))
    for (const line of readFileSync(".env.e2e.local", "utf8").split(/\r?\n/)) {
      const i = line.indexOf("=")
      if (i > 0) fromFile[line.slice(0, i).trim()] = line.slice(i + 1).trim()
    }
  const values = Object.fromEntries(keys.map((k) => [k, process.env[k] ?? fromFile[k] ?? ""]))
  return keys.every((k) => values[k]) ? values : null
}

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
    await pb.goto(conversationUrl)
    await expect(pb.getByLabel("Nội dung tin nhắn")).toBeVisible()

    // 2. AC-01: A gửi → B thấy; tab B đang hiển thị nên A thấy "Đã xem".
    const T1 = `Chào Bình ${tag}`
    await gui(pa, T1)
    await expect(tinNhan(pb, T1)).toHaveCount(1, { timeout: 5_000 })
    await expect(pa.getByTestId("chat-delivery")).toHaveText(/Đã xem/, { timeout: 10_000 })
    expect(veCuaB).toBe(1)

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

    // Tài khoản có sẵn: kết bạn LẠI để lần chạy sau (và F3 đo p95) vẫn có hai người là bạn.
    if (coSan) await ketBanApi(request, A, B)
  } finally {
    await ctxA.close()
    await ctxB.close()
  }
})
