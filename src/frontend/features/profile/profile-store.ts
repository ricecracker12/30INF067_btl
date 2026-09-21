import type { ProfileResponse } from "@/lib/api/types"

type Listener = () => void

// Hồ sơ của NGƯỜI ĐANG ĐĂNG NHẬP, giữ ở một chỗ (khuôn `lib/auth/token-store.ts`). Lý do có store: header và
// composer đều cần tên/avatar, mà gọi lại `GET /users/{me}/profile` ở mỗi chỗ là ba request cho một dữ liệu —
// và ba thời điểm khác nhau để ba chỗ đó lệch nhau sau khi người dùng sửa hồ sơ.
//
// Biến module + subscribe: React đọc qua `useSyncExternalStore`, code không phải component (load-profile.ts)
// đổi được trạng thái.

/**
 * `missing` là 404 của `GET /users/{me}/profile` — chưa onboarding (Đ-2.4). Nó là TÍN HIỆU, không phải lỗi:
 * tách khỏi `error` để guard biết "đưa đi onboarding" chứ không phải "hiện Thử lại".
 */
export type ProfileStatus = "unknown" | "missing" | "ready" | "error"

export type ProfileState = Readonly<{
  status: ProfileStatus
  /** Chỉ khác `null` khi `ready` — các trạng thái khác không có gì để hiện. */
  profile: ProfileResponse | null
}>

/** Tab vừa mở: chưa hỏi server lần nào. */
export const INITIAL_PROFILE: ProfileState = {
  status: "unknown",
  profile: null,
}

let state: ProfileState = INITIAL_PROFILE
const listeners = new Set<Listener>()

function update(next: ProfileState) {
  if (next.status === state.status && next.profile === state.profile) return
  // Object mới mỗi lần đổi: `useSyncExternalStore` so snapshot bằng `Object.is`.
  state = next
  listeners.forEach((l) => l())
}

export const profileStore = {
  getState: () => state,
  subscribe(l: Listener) {
    listeners.add(l)
    return () => {
      listeners.delete(l)
    }
  },

  /** 200 từ `GET`, hoặc `PUT` vừa trả hồ sơ mới — `PUT` trả nguyên `ProfileResponse` nên không phải gọi lại `GET` (D2). */
  setReady(profile: ProfileResponse) {
    update({ status: "ready", profile })
  },
  /** 404: chưa onboarding. Xóa hồ sơ cũ đang giữ — trạng thái này không có gì để hiện. */
  setMissing() {
    update({ status: "missing", profile: null })
  },
  /**
   * Không hỏi được server (mất mạng, 5xx). KHÔNG phải `missing` — đẩy người đã có hồ sơ vào màn onboarding
   * là bắt họ khai lại tên, và `PUT` khi đó ghi đè bio cũ (Q-D3).
   */
  markError() {
    update({ status: "error", profile: null })
  },
  /** `error` → `unknown`: bấm "Thử lại". */
  markUnknown() {
    update({ status: "unknown", profile: null })
  },
  /** Về trạng thái tab vừa mở. Test dùng giữa các ca; app gọi khi đăng xuất. */
  reset() {
    update(INITIAL_PROFILE)
  },
}
