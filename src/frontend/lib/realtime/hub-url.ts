// Đ-E18 (GĐ5, chốt 2026-09-24): URL của hub chat.
//
// - Staging/production: `/hubs/chat` TƯƠNG ĐỐI, cùng origin — apache chuyển `/hubs/` về API (giai-doan-5.md Mục 9.6), đúng Đ-E16
//   (trình duyệt chỉ nói chuyện với origin của mình). `connect-src 'self'` của CSP đã phủ `wss://` cùng origin (CSP Level 3).
// - Dev: FE ở :3000, API ở :5259, và luật FE cấm `rewrites` (Mục 4) — `/hubs/chat` trên :3000 rơi vào Next và 404. Nối thẳng
//   API dev; CSP dev (và CHỈ dev) mở `http://localhost:5259 ws://localhost:5259` (`buildCsp`).
//
// Chuỗi `localhost:5259` CHỈ được nằm trong nhánh `process.env.NODE_ENV === "development"` viết thẳng ở đây: Next thay biến đó
// bằng hằng lúc build và trình minify xóa nhánh chết — bản production không còn chuỗi. Để nó ở hằng cấp module hay nhận `env`
// qua tham số là chuỗi lọt vào bundle và cổng CI "Bundle production sạch" đỏ (đã gặp 2026-09-24).
export const CHAT_HUB_PATH = "/hubs/chat"

export function chatHubUrl(): string {
  if (process.env.NODE_ENV === "development") return "http://localhost:5259/hubs/chat"
  return CHAT_HUB_PATH
}
