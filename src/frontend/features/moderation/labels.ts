import type {
  ReportHistoryEntry,
  ReportQueueItem,
  ReportTargetType,
  TargetSnapshot,
} from "@/lib/api/types"

// Nhãn tiếng Việt của khu kiểm duyệt. `Record<…>` theo kiểu sinh: hợp đồng thêm giá trị là file này đỏ compile.

export const TARGET_TYPE_LABEL: Record<ReportTargetType, string> = {
  post: "Bài viết",
  comment: "Bình luận",
  user: "Người dùng",
}

export const TARGET_STATUS_LABEL: Record<TargetSnapshot["status"], string> = {
  published: "Đang hiển thị",
  hidden: "Đã bị ẩn",
  deleted: "Đã xóa",
  active: "Đang hoạt động",
  disabled: "Đã bị khóa",
}

export const OUTCOME_LABEL: Record<ReportHistoryEntry["outcome"], string> = {
  resolved: "Đã xử lý",
  dismissed: "Đã bỏ qua",
}

/** Lý do được báo nhiều nhất — dòng hàng đợi và lựa chọn MẶC ĐỊNH khi ẩn (Mục 7.2 bước 3). Hòa thì theo thứ tự khóa của server. */
export function topReason(reasons: ReportQueueItem["reasons"]): string | null {
  let best: string | null = null
  let count = -1
  for (const [code, n] of Object.entries(reasons))
    if (n > count) {
      best = code
      count = n
    }
  return best
}

const relative = new Intl.RelativeTimeFormat("vi", { numeric: "always" })

/** "Chờ bao lâu" của dòng hàng đợi — tính từ báo cáo mở cũ nhất. */
export function waitingLabel(since: string, now: number = Date.now()): string {
  const minutes = Math.max(0, Math.round((now - Date.parse(since)) / 60_000))
  if (minutes < 60) return relative.format(-minutes, "minute")
  const hours = Math.round(minutes / 60)
  if (hours < 48) return relative.format(-hours, "hour")
  return relative.format(-Math.round(hours / 24), "day")
}
