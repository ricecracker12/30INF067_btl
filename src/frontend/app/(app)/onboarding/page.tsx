import { Onboarding } from "@/features/profile/onboarding"

// Nằm trong `(app)` (có `RequireAuth`) nhưng NGOÀI `(with-profile)` (không có `RequireProfile`) — Q-E2.
// Chỉ ráp (Đ-E13).
export default function OnboardingPage() {
  return <Onboarding />
}
