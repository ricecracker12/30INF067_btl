import { MeProfile } from "@/features/auth/me-profile"

// Chỉ ráp (Đ-E13). Gọi API ở client trong `MeProfile` (Đ-E11) — token nằm trong memory của tab.
export default function MePage() {
  return <MeProfile />
}
