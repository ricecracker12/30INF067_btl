// Đ-E18 (GĐ5, chốt 2026-09-24): URL của hub chat.
//
// - Staging/production: `/hubs/chat` TƯƠNG ĐỐI, cùng origin — apache chuyển `/hubs/` về API (giai-doan-5.md Mục 9.6), đúng Đ-E16
//   (trình duyệt chỉ nói chuyện với origin của mình). `connect-src 'self'` của CSP đã phủ `wss://` cùng origin (CSP Level 3).
// - Dev: FE ở :3000, API ở :5259, và luật FE cấm `rewrites` (Mục 4) — `/hubs/chat` trên :3000 rơi vào Next và 404. Nối thẳng
//   API dev; CSP dev (và CHỈ dev) mở `http://localhost:5259 ws://localhost:5259` (`buildCsp`).
//
// `process.env.NODE_ENV` được Next nhúng sẵn vào bundle trình duyệt — không phải biến `NEXT_PUBLIC_*` nào.
export const DEV_API_ORIGIN = "http://localhost:5259"

export const CHAT_HUB_PATH = "/hubs/chat"

export function chatHubUrl(env: string | undefined = process.env.NODE_ENV): string {
  return env === "development" ? `${DEV_API_ORIGIN}${CHAT_HUB_PATH}` : CHAT_HUB_PATH
}
