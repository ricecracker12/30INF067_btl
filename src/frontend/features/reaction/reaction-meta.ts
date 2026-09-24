import {
  FaceAngry,
  FaceGrinning,
  FaceSlightlyFrowning,
  Heart,
  Sparkles,
  ThumbsUp,
  type LucideIcon,
} from "lucide-react"

import type { ReactionType } from "@/lib/api/types"

// Sáu loại cảm xúc — phân biệt bằng ICON + NHÃN, không bằng sáu màu (luật frontend Mục 1 #5: màu chỉ từ token). Thứ tự là thứ tự
// hiện trong bảng chọn. `satisfies Record<ReactionType, …>`: hợp đồng thêm/bớt một loại là lỗi compile ở đây.

export const REACTION_ORDER = [
  "like",
  "love",
  "haha",
  "wow",
  "sad",
  "angry",
] as const satisfies readonly ReactionType[]

export const REACTION_META = {
  like: { label: "Thích", Icon: ThumbsUp },
  love: { label: "Yêu thích", Icon: Heart },
  haha: { label: "Haha", Icon: FaceGrinning },
  wow: { label: "Wow", Icon: Sparkles },
  sad: { label: "Buồn", Icon: FaceSlightlyFrowning },
  angry: { label: "Phẫn nộ", Icon: FaceAngry },
} satisfies Record<ReactionType, { label: string; Icon: LucideIcon }>
