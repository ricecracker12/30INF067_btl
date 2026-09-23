// Query string phân trang keyset dùng chung cho mọi danh sách theo cursor: bài của một người, feed (content-v1),
// bạn bè và lời mời (socialgraph-v1). Dời khỏi `content-api.ts` ở GĐ4 E1 — hai client, một luật.

/** `cursor`/`limit` chỉ vào query string KHI CÓ — `?cursor=undefined` là 400 `errors.cursor`. */
export function pageQuery(opts: {
  cursor?: string | null
  limit?: number
}): string {
  const params = new URLSearchParams()
  // `nextCursor` là chuỗi opaque (Đ-2.11): truyền lại NGUYÊN VẸN, FE không tự dựng và không tự sửa.
  if (opts.cursor) params.set("cursor", opts.cursor)
  if (opts.limit !== undefined) params.set("limit", String(opts.limit))
  const qs = params.toString()
  return qs ? `?${qs}` : ""
}
