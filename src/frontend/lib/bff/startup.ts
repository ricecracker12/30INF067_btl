import "server-only"

import { bffConfig } from "./config"

/**
 * E8 — gọi từ `instrumentation.ts` lúc server khởi động. Cấu hình BFF sai / thiếu thì in lỗi và THOÁT mã 1: ném lỗi trong
 * `register` là chưa đủ — đã thử, Next in "Failed to prepare server" rồi tiến trình vẫn sống, container vẫn `healthy`.
 * Thoát hẳn thì container dừng / crash-loop, nhìn thấy ngay ở `docker compose ps` như API (AGENTS.md Mục 13).
 */
export function assertBffConfigOrExit(): void {
  try {
    bffConfig()
  } catch (e) {
    console.error(`[bff] ${(e as Error).message}`)
    process.exit(1)
  }
}
