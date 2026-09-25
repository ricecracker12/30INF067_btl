import { authApi } from "@/lib/api/auth-api"
import { ApiError } from "@/lib/api/problem"
import type { MeResponse } from "@/lib/api/types"

type Listener = () => void

// `GET /me` của người đang đăng nhập, giữ ở MỘT chỗ (khuôn `token-store.ts`, `profile-store.ts`) — GĐ6 Đ-6.11: header, guard mềm và
// các nút theo quyền cùng đọc một nguồn, để một lần nạp lại (focus tab, vào route cần quyền) đổi mọi chỗ cùng lúc. Đó là cách
// "Admin nâng quyền X" hiện ra trên màn của X mà X không tải lại trang — bằng chứng UI của mốc 1.

/**
 * `unknown`: chưa hỏi lần nào · `ready`: có `me` · `error`: lần nạp ĐẦU hỏng. Nạp LẠI hỏng (mất mạng lúc focus) thì giữ `me` cũ:
 * đẩy người đang dùng màn quản trị ra trang "không có quyền" vì một request rớt là nói dối về quyền của họ.
 */
export type MeStatus = "unknown" | "ready" | "error"

export type MeState = Readonly<{ status: MeStatus; me: MeResponse | null }>

export const INITIAL_ME: MeState = { status: "unknown", me: null }

let state: MeState = INITIAL_ME
const listeners = new Set<Listener>()

function update(next: MeState) {
  state = next
  listeners.forEach((l) => l())
}

export const meStore = {
  getState: () => state,
  subscribe(l: Listener) {
    listeners.add(l)
    return () => {
      listeners.delete(l)
    }
  },
  /** Đăng xuất (tab này hoặc tab khác) — quyền của người cũ không được sống sang phiên sau. Test dùng giữa các ca. */
  reset() {
    inflight = null
    generation += 1
    if (state !== INITIAL_ME) update(INITIAL_ME)
  },
}

let inflight: Promise<void> | null = null
// Tăng mỗi lần `reset` — lượt nạp của phiên cũ về muộn không ghi `me` của người cũ vào phiên mới.
let generation = 0

/**
 * Nạp lại `/me`. Gọi nhiều lần đồng thời (focus + vào route cùng lúc, StrictMode) vẫn MỘT request. 401 là việc của `http.ts`
 * (phiên hết thật → `RequireAuth` về `/login`) — ở đây chỉ không biến nó thành `error`.
 */
export function refreshMe(): Promise<void> {
  const gen = generation
  inflight ??= (async () => {
    try {
      const me = await authApi.me()
      if (gen === generation) update({ status: "ready", me })
    } catch (e) {
      if (gen !== generation) return
      if (e instanceof ApiError && e.status === 401) return
      if (state.status !== "ready") update({ status: "error", me: null })
    }
  })().finally(() => {
    if (gen === generation) inflight = null
  })
  return inflight
}
