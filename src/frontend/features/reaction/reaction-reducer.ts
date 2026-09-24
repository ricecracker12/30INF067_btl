import type { ReactionSummary, ReactionType } from "@/lib/api/types"

// Optimistic update TUẦN TỰ THEO ĐỐI TƯỢNG (Đ-3.13) — hàm thuần, test không cần render.
//
// Mỗi đối tượng (một bài, một bình luận) giữ ba giá trị:
//   - `confirmed` — tóm tắt server trả lần cuối (con số THẬT);
//   - `desired`   — lần bấm cuối của người dùng (thứ giao diện hiện NGAY);
//   - `inFlight`  — đang có đúng MỘT request bay hay không.
//
// Luật:
//   1. Bấm → `desired` đổi ngay; số đếm hiện ra tính lại từ `confirmed` (không cộng dồn trên số đã đoán).
//   2. Không có request bay và `desired` khác `confirmed.myReaction` → gửi `desired`.
//   3. Đang bay → KHÔNG gửi thêm; chờ.
//   4. Về thành công → `confirmed` = server; `desired` vẫn khác → gửi tiếp MỘT request (quay lại 2).
//   5. Về lỗi → `desired` = `confirmed.myReaction` (rollback); màn hiện lỗi.
//
// Vì sao: bấm tim–bỏ–tim–bỏ trong một giây mà gửi hết thì response về không theo thứ tự (giao diện dừng ở trạng thái sai) và mười
// lần như vậy là 429 lúc "chỉ bấm tim". Luật trên cho TỐI ĐA HAI request cho một chuỗi bấm bất kỳ, trạng thái cuối luôn là lần bấm
// cuối. React 19 `useOptimistic` không diễn đạt được bước 3–4 (gộp các lần bấm trong khi đang bay) nên không dùng.

export type ReactionState = {
  confirmed: ReactionSummary
  desired: ReactionType | null
  inFlight: boolean
}

/** Việc phải làm sau một bước: `null` = không gửi gì; còn lại = gửi request đưa đối tượng về `type` (`null` trong đó = gỡ). */
export type Send = { type: ReactionType | null } | null

export type Step = { state: ReactionState; send: Send }

export function initialReaction(summary: ReactionSummary): ReactionState {
  return { confirmed: summary, desired: summary.myReaction, inFlight: false }
}

/** Người dùng chọn `next` (`null` = gỡ). */
export function press(state: ReactionState, next: ReactionType | null): Step {
  const s = { ...state, desired: next }
  if (s.inFlight || s.desired === s.confirmed.myReaction)
    return { state: s, send: null }
  return { state: { ...s, inFlight: true }, send: { type: s.desired } }
}

/** Request vừa bay về thành công với tóm tắt THẬT của server. */
export function settled(state: ReactionState, summary: ReactionSummary): Step {
  const s = { ...state, confirmed: summary }
  if (s.desired === summary.myReaction)
    return { state: { ...s, inFlight: false }, send: null }
  return { state: { ...s, inFlight: true }, send: { type: s.desired } }
}

/** Request vừa bay về lỗi — quay về con số server đã xác nhận. */
export function failed(state: ReactionState): ReactionState {
  return { ...state, desired: state.confirmed.myReaction, inFlight: false }
}

/**
 * Thứ giao diện hiện: số đếm của `confirmed`, bỏ lượt của chính mình theo server rồi cộng lượt theo `desired`. Loại về 0 thì
 * mất khóa — cùng hợp đồng với server (Đ-3.8), không bao giờ hiện "0 like".
 */
export function view(state: ReactionState): ReactionSummary {
  const counts: Record<string, number> = { ...state.confirmed.reactionCounts }
  const mine = state.confirmed.myReaction
  if (mine !== null) {
    const n = (counts[mine] ?? 0) - 1
    if (n > 0) counts[mine] = n
    else delete counts[mine]
  }
  if (state.desired !== null)
    counts[state.desired] = (counts[state.desired] ?? 0) + 1
  return { reactionCounts: counts, myReaction: state.desired }
}

export function total(summary: ReactionSummary): number {
  return Object.values(summary.reactionCounts).reduce((a, b) => a + b, 0)
}
