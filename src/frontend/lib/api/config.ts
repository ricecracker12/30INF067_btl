// Đ-E14 (thay Đ-E1) — trình duyệt CHỈ gọi BFF cùng origin (`/bff/*` của chính Next server). Không còn gốc API cho trình
// duyệt: không biến NEXT_PUBLIC_API_BASE_URL, không CORS, không cookie khác site — và không build nào nhúng sai địa chỉ
// API được. Gốc API .NET là cấu hình SERVER (`API_INTERNAL_URL`, lib/bff/config.ts).

export const BFF_BASE_PATH = "/bff"

/**
 * URL tuyệt đối tới BFF. Trình duyệt: origin của trang. Vitest (Node): origin của jsdom — `fetch` của Node không nhận
 * đường dẫn tương đối.
 */
export const BFF_URL: string =
  typeof window === "undefined"
    ? BFF_BASE_PATH
    : `${window.location.origin}${BFF_BASE_PATH}`
