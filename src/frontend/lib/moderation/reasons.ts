import type { ReasonCode } from "@/lib/api/types"

// Năm lý do chuẩn hóa của báo cáo (PTTK ENT-12, Đ-6.12) — nhãn tiếng Việt MỘT chỗ. Bốn feature cùng hiện lý do (hộp thoại báo
// cáo, hàng đợi kiểm duyệt, câu thông báo `moderation`, biểu ngữ bài bị ẩn) mà `features/` không import chéo nhau (Đ-E13), nên
// bảng nằm ở `lib/`. `Record<ReasonCode, …>`: hợp đồng thêm lý do thứ sáu là file này đỏ compile, không phải nhãn trống lúc chạy.

export const REASON_LABEL: Record<ReasonCode, string> = {
  spam: "Spam hoặc quảng cáo",
  harassment: "Quấy rối hoặc bắt nạt",
  nudity: "Ảnh khỏa thân hoặc nội dung tình dục",
  violence: "Bạo lực hoặc đe dọa",
  other: "Lý do khác",
}

/** Thứ tự hiện trong hộp thoại — "Khác" cuối cùng. */
export const REASON_CODES = Object.keys(REASON_LABEL) as ReasonCode[]

/** Chuỗi lạ (server thêm lý do mà FE chưa sinh lại kiểu) → hiện nguyên mã, không hiện ô trống. */
export function reasonLabel(code: string): string {
  return (REASON_LABEL as Record<string, string>)[code] ?? code
}
