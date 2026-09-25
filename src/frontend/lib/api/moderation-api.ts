import { BFF_ROUTES } from "./bff-contract"
import { request } from "./http"
import { pageQuery } from "./page-query"
import type * as T from "./types"

// Kiểu payload lấy từ hợp đồng `moderation-v1.yaml` (GĐ6 E1): yaml đổi field là các dòng dưới đỏ compile. Sáu endpoint đi qua
// proxy chung `/bff/api/*` (Đ-E14) — GĐ6 KHÔNG thêm route BFF nào. Nhật ký kiểm toán nằm ở đây, không ở `admin-api.ts`: file
// client bám nhóm Swagger 1-1 (`GET /admin/audit-logs` thuộc `moderation-v1`, Đ-6.15), dù màn đọc nó nằm ở khu quản trị.
//
// Mọi endpoint trừ `POST /reports` là đặc quyền (Đ-6.8): Redis không trả lời → 503 `revocation-unavailable`, không fail-open.

type PageOpts = { cursor?: string | null; limit?: number }

const id = (value: string) => encodeURIComponent(value)
const reports = `${BFF_ROUTES.api}/reports`

/** Bộ lọc nhật ký — chỉ trường CÓ mới vào query string (`?actorId=` rỗng là 400). */
export type AuditLogFilter = {
  actorId?: string
  action?: T.AuditAction
  targetType?: string
  targetId?: string
}

export const moderationApi = {
  /**
   * 201 báo cáo mới · 200 đã báo (còn mở) — CÙNG một câu cảm ơn cho cả hai (Mục 7.1), `request()` không phân biệt. Không thấy
   * được đối tượng → 404, cùng thân với "không tồn tại" (Đ-6.12). 429 khi quá 10 báo cáo/phút.
   */
  create: (body: T.CreateReportRequest) =>
    request<T.ReportReceipt>(reports, { method: "POST", body }),

  /** Hàng đợi — mỗi ĐỐI TƯỢNG một dòng, cũ nhất trước. Hết dữ liệu KHI VÀ CHỈ KHI `nextCursor === null`. */
  queue: (opts: PageOpts = {}, signal?: AbortSignal) => {
    const page = pageQuery(opts)
    return request<T.ReportQueuePage>(
      `${reports}?status=open${page ? `&${page.slice(1)}` : ""}`,
      { signal }
    )
  },

  /** Ảnh chụp đối tượng + báo cáo mở + lịch sử. `reporterId` không bao giờ có (Mục 8.1). */
  detail: (reportId: string, signal?: AbortSignal) =>
    request<T.ReportDetail>(`${reports}/${id(reportId)}`, { signal }),

  /**
   * Một quyết định đóng MỌI báo cáo mở của cùng đối tượng (Đ-6.13). 409 có hai `type`: `report-already-decided` (người khác vừa
   * quyết) và `moderation-target-gone`. `hide` cần thêm `post.hide` — thiếu → 403.
   */
  decide: (reportId: string, body: T.DecideReportRequest) =>
    request<T.ReportDecisionResult>(`${reports}/${id(reportId)}`, {
      method: "PATCH",
      body,
    }),

  /** Khôi phục bài/bình luận bị ẩn. Đang không ẩn → 409 `moderation-not-hidden`. Không mở lại báo cáo đã đóng. */
  restore: (
    targetType: T.ReportTargetType,
    targetId: string,
    body: T.RestoreTargetRequest = {}
  ) =>
    request<T.ModerationTargetChange>(
      `${BFF_ROUTES.api}/moderation/targets/${id(targetType)}/${id(targetId)}/restore`,
      { method: "POST", body }
    ),

  /** Nhật ký kiểm toán, `id DESC` — chỉ ADMIN (`audit.read`). */
  auditLogs: (
    filter: AuditLogFilter = {},
    opts: PageOpts = {},
    signal?: AbortSignal
  ) => {
    const params = new URLSearchParams()
    for (const [key, value] of Object.entries(filter))
      if (value) params.set(key, value)
    if (opts.cursor) params.set("cursor", opts.cursor)
    if (opts.limit !== undefined) params.set("limit", String(opts.limit))
    const qs = params.size > 0 ? `?${params.toString()}` : ""
    return request<T.AuditLogPage>(`${BFF_ROUTES.api}/admin/audit-logs${qs}`, {
      signal,
    })
  },
}
