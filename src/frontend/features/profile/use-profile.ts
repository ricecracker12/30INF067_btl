import { useSyncExternalStore } from "react"

import {
  INITIAL_PROFILE,
  profileStore,
  type ProfileState,
} from "./profile-store"

/**
 * Hồ sơ hiện tại cho component (Đ-E2: React đọc store qua `useSyncExternalStore`). Không Context/Provider —
 * store là biến module, mọi component đọc cùng một nguồn. Server không có hồ sơ → snapshot server là
 * `unknown`, khớp HTML lúc hydrate.
 */
export function useProfile(): ProfileState {
  return useSyncExternalStore(
    profileStore.subscribe,
    profileStore.getState,
    () => INITIAL_PROFILE
  )
}
