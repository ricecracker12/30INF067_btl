import { expect, type APIRequestContext } from "@playwright/test"

// Dùng chung cho các spec chạy trên API dev thật (E3, E4, E5). Gốc API và Mailpit đổi được bằng biến môi trường.
export const API =
  process.env.PLAYWRIGHT_API_URL ?? "http://localhost:5259/api/v1"
export const MAILPIT =
  process.env.PLAYWRIGHT_MAILPIT_URL ?? "http://localhost:8025"

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
