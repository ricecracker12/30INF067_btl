import type { NotificationResponse } from "@/lib/api/types"
import { reasonLabel } from "@/lib/moderation/reasons"

// Câu hiển thị và đích điều hướng của một thông báo (GĐ6 Mục 8.3). Server KHÔNG trả câu — FE ghép từ `type` + `actor` +
// `actorCount`, để đổi chữ không phải đổi hợp đồng và DB không chứa tên người (Đ-6.16). Hàm thuần: test không cần render.

type N = Pick<
  NotificationResponse,
  "type" | "actor" | "actorCount" | "target" | "reasonCode"
>

/** `actor` là người của sự kiện MỚI NHẤT; `null` khi người đó không còn hồ sơ (và luôn `null` với `moderation`). */
function subject(n: N): string {
  const who = n.actor?.displayName ?? "Một người dùng"
  // `actorCount` đếm người KHÁC NHAU trong đợt (Đ-6.16) — "An và 3 người khác" là 4 người.
  return n.actorCount > 1 ? `${who} và ${n.actorCount - 1} người khác` : who
}

export function notificationText(n: N): string {
  switch (n.type) {
    case "comment":
      return `${subject(n)} đã bình luận về bài viết của bạn.`
    case "reply":
      return `${subject(n)} đã trả lời bình luận của bạn.`
    case "reaction":
      return n.target.type === "comment"
        ? `${subject(n)} đã bày tỏ cảm xúc về bình luận của bạn.`
        : `${subject(n)} đã bày tỏ cảm xúc về bài viết của bạn.`
    case "friend_request":
      return `${subject(n)} đã gửi lời mời kết bạn.`
    case "friend_accepted":
      return `${subject(n)} đã chấp nhận lời mời kết bạn của bạn.`
    case "message":
      return `${subject(n)} đã nhắn tin cho bạn.`
    case "tag":
      return `${subject(n)} đã nhắc đến bạn trong một bình luận.`
    case "moderation": {
      // KHÔNG tên Moderator, không tên người báo (B.10 mục 7) — `actor` luôn null ở loại này.
      const thing = n.target.type === "comment" ? "Bình luận" : "Bài viết"
      const reason = n.reasonCode ? ` (lý do: ${reasonLabel(n.reasonCode)})` : ""
      return `${thing} của bạn đã bị ẩn vì vi phạm tiêu chuẩn cộng đồng${reason}.`
    }
  }
}

/**
 * Bấm thông báo đi đâu. Đích đã mất / bị ẩn / đổi quyền xem thì MÀN ĐÍCH trả 404 và tự nói (Đ-6.16: không rút lại thông báo) —
 * ở đây không đoán trước. Bình luận không có trang riêng: về bài chứa nó.
 */
export function notificationHref(n: N): string {
  const { target } = n
  switch (target.type) {
    case "post":
      return `/posts/${encodeURIComponent(target.id)}`
    case "comment":
      return target.postId
        ? `/posts/${encodeURIComponent(target.postId)}`
        : "/notifications"
    case "conversation":
      return `/messages/${encodeURIComponent(target.id)}`
    case "user":
      // Lời mời đến: màn trả lời lời mời là `/friends` (E2E-02), không phải hồ sơ người mời.
      return n.type === "friend_request"
        ? "/friends"
        : `/users/${encodeURIComponent(target.id)}`
  }
}

/** Badge chuông: số NHÓM chưa đọc, "9+" từ 10 (Mục 8.3). */
export function badgeText(total: number): string {
  return total >= 10 ? "9+" : String(total)
}
