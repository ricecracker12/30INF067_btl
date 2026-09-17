// E8 — kiểm cấu hình BFF (Đ-E14) NGAY LÚC SERVER KHỞI ĐỘNG, cùng tinh thần "thiếu cấu hình = từ chối chạy" của API.
// Không có bước này, container thiếu SESSION_ENCRYPTION_KEY vẫn khởi động và báo `healthy` (/login render được), chỉ lỗi ở
// request /bff đầu tiên — deploy trông như thành công. Next gọi `register` một lần trước khi nhận request, và KHÔNG gọi
// lúc `next build` (build vẫn không cần biến nào). Development có mặc định local nên không bao giờ thoát ở đây.
export async function register() {
  // Chỉ runtime Node (Route Handler của BFF chạy ở đây); Edge không có Redis hay node:crypto.
  if (process.env.NEXT_RUNTIME !== "nodejs") return
  const { assertBffConfigOrExit } = await import("./lib/bff/startup")
  assertBffConfigOrExit()
}
