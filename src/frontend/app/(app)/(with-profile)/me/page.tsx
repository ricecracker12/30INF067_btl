import { MeProfile } from "@/features/auth/me-profile"
import { AvatarCard } from "@/features/profile/avatar-card"
import { ProfileCard } from "@/features/profile/profile-card"

// Chỉ ráp (Đ-E13). Nằm dưới `(with-profile)` (Q-E6): tới được đây nghĩa là đã có hồ sơ, nên `ProfileCard`
// không phải có nhánh "chưa onboarding". Gọi API ở client (Đ-E11).
export default function MePage() {
  return (
    <div className="flex flex-col gap-8">
      <ProfileCard />
      <AvatarCard />
      <MeProfile />
    </div>
  )
}
