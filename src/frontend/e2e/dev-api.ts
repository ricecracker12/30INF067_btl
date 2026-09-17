import { expect, test, type APIRequestContext } from "@playwright/test"

// Dùng chung cho các spec chạy trên API dev thật (E3–E6). Gốc API và Mailpit đổi được bằng biến môi trường.
export const API =
  process.env.PLAYWRIGHT_API_URL ?? "http://localhost:5259/api/v1"
export const MAILPIT =
  process.env.PLAYWRIGHT_MAILPIT_URL ?? "http://localhost:8025"

// ── Hạn mức /auth/* ────────────────────────────────────────────────────────────────────────────────────────
// Server: 10 req/phút theo IP, cửa sổ cố định (FixedWindowRateLimiter). Cả bộ spec tốn hơn 10 lượt, nên mỗi
// test KHAI trước số lượt nó dùng. Lượt khai vượt hạn mức thì chờ: 60 giây cửa sổ + 15 giây cho các request của
// test trước còn bắn sau lúc khai. `workers: 1` nên module này dùng chung cho mọi file spec trong cùng worker.
const AUTH_LIMIT = 10
const WAIT_MS = 75_000
let used = 0
let lastReservedAt = 0

export async function giuHanMucAuth(n: number) {
  if (used + n > AUTH_LIMIT) {
    const waitMs = Math.max(0, lastReservedAt + WAIT_MS - Date.now())
    if (waitMs > 0) {
      test.info().setTimeout(test.info().timeout + waitMs)
      await new Promise((r) => setTimeout(r, waitMs))
    }
    used = 0
  }
  used += n
  lastReservedAt = Date.now()
}

/** Email chưa từng dùng — mỗi lượt chạy một tài khoản mới, không đụng dữ liệu của lượt trước. */
export function emailMoi(prefix: string) {
  return `${prefix}-${Date.now()}-${Math.random().toString(36).slice(2, 8)}@example.com`
}

/** Đọc link xác minh (nguyên văn, cả gốc URL) trong mail gửi tới `email`, qua API REST của Mailpit. */
export async function linkXacMinh(request: APIRequestContext, email: string) {
  let link: string | undefined
  await expect
    .poll(
      async () => {
        const search = await request.get(`${MAILPIT}/api/v1/search`, {
          params: { query: `to:"${email}"` },
        })
        const { messages } = (await search.json()) as {
          messages: { ID: string }[]
        }
        if (messages.length === 0) return false
        const msg = await request.get(
          `${MAILPIT}/api/v1/message/${messages[0].ID}`
        )
        const { Text, HTML } = (await msg.json()) as {
          Text: string
          HTML: string
        }
        link = /https?:\/\/[^\s"'<>]+\/verify-email\?token=[^\s"'<>]+/.exec(
          `${Text}\n${HTML}`
        )?.[0]
        return link !== undefined
      },
      { timeout: 15_000, message: "Không thấy mail xác minh trong Mailpit" }
    )
    .toBe(true)
  return link!
}

/**
 * Tự dựng một tài khoản đã xác minh qua API: register → đọc link trong Mailpit → verify-email. Tốn 2 lượt
 * `/auth/*` — người gọi tính vào `giuHanMucAuth`.
 */
export async function taoTaiKhoanDaXacMinh(
  request: APIRequestContext,
  prefix: string
) {
  const email = emailMoi(prefix)
  const password = `MatKhau-${prefix}-an-toan`

  const reg = await request.post(`${API}/auth/register`, {
    data: { email, password },
  })
  expect(reg.status(), await reg.text()).toBe(201)

  const token = new URL(await linkXacMinh(request, email)).searchParams.get(
    "token"
  )
  const verify = await request.post(`${API}/auth/verify-email`, {
    data: { token },
  })
  expect(verify.status(), await verify.text()).toBe(200)

  return { email, password }
}
