// Mã quyền của khu kiểm duyệt — cùng tầng 2 của moderation-v1 (Mục 6.1). Chỉ để VẼ (Đ-6.11); server vẫn chặn thật.
export const MODERATION_PERMISSIONS = {
  /** Hàng đợi, chi tiết, bỏ qua, đã xử lý. */
  queue: "report.resolve",
  /** Ẩn và khôi phục — quyền RIÊNG ngoài `report.resolve` (Đ-6.13: vai trò REVIEWER chỉ có `report.resolve`). */
  hide: "post.hide",
} as const
