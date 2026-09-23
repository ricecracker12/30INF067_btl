// Chặn open redirect qua `?next=` (frontend-rules Mục 5): chỉ nhận đường dẫn CÙNG origin.
//
// Mặc định `"/"` — trang chủ là feed (GĐ4 Q-E1, chốt 2026-09-23; trước đó `"/me"`): đăng nhập xong thấy feed, người mới
// onboarding xong thấy feed gợi ý — đường gặp người khác của Đ-4.6.
//
// Không dừng ở kiểm tiền tố `//` và `/\`: trình duyệt bỏ tab/xuống dòng trong URL và coi `\` như `/`,
// nên `/\t/evil.example` hay `/%09/…` sau khi chuẩn hóa vẫn thành `//evil.example`. Cách chắc là để
// chính bộ phân tích URL giải trên một origin giả rồi so origin.
const PROBE_ORIGIN = "http://safe-next.invalid"

export function safeNext(next: string | null, fallback = "/"): string {
  if (!next || !next.startsWith("/")) return fallback

  let url: URL
  try {
    url = new URL(next, PROBE_ORIGIN)
  } catch {
    return fallback
  }
  if (url.origin !== PROBE_ORIGIN) return fallback

  return `${url.pathname}${url.search}${url.hash}`
}
