import { useSyncExternalStore } from "react"

import { INITIAL_SESSION, tokenStore, type Session } from "./token-store"

/**
 * Phiên hiện tại cho component (Đ-E2: React đọc store qua `useSyncExternalStore`). Không cần Context/Provider —
 * store là biến module, mọi component đọc cùng một nguồn. Server không có phiên → snapshot server là `unknown`,
 * khớp HTML lúc hydrate.
 */
export function useSession(): Session {
  return useSyncExternalStore(
    tokenStore.subscribe,
    tokenStore.getSession,
    () => INITIAL_SESSION
  )
}
