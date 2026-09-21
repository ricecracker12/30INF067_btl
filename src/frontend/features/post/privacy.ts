import type { PostPrivacy } from "@/lib/api/types"

// Ba mức riêng tư, một nguồn chữ duy nhất: composer (E4) chọn, card (E5) hiển thị, form sửa (E6) chọn lại.
// Chép nhãn ra ba chỗ là ba chỗ để lệch — và lệch ở đây nghĩa là cùng một bài được gọi hai tên khác nhau
// trên hai màn.

/**
 * Nhãn `friends` **nói thật** về GĐ2 (Đ-2.9, `AlwaysStrangers`): kết bạn chưa có nên bài "bạn bè" hiện
 * chỉ tác giả xem được. Viết "Bạn bè xem được" là hứa một thứ chưa chạy, và người dùng chỉ phát hiện khi
 * bạn họ bảo không thấy bài.
 */
export const PRIVACY_LABEL: Record<PostPrivacy, string> = {
  public: "Công khai",
  friends: "Bạn bè",
  private: "Chỉ mình tôi",
}

export const PRIVACY_DESCRIPTION: Record<PostPrivacy, string> = {
  public: "Ai cũng xem được bài này.",
  friends: "Hiện chỉ mình bạn xem được, tính năng kết bạn sẽ có ở bản sau.",
  private: "Chỉ bạn xem được bài này.",
}

/** Thứ tự hiển thị trong `radio-group`: nới nhất trước, chặt nhất sau. */
export const PRIVACY_ORDER = [
  "public",
  "friends",
  "private",
] as const satisfies readonly PostPrivacy[]
