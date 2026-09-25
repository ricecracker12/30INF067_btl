"use client"

import { use } from "react"

import { SearchResults } from "@/features/search/search-results"

// Trang kết quả tìm người (GĐ6 E4). Chỉ ráp (Đ-E13). `searchParams` là Promise ở Next 16 — mở bằng `use()` (khuôn `params` của
// `users/[userId]`); `key` theo `q` để đổi từ khóa là một lượt tìm mới.
export default function SearchPage({
  searchParams,
}: {
  searchParams: Promise<{ q?: string | string[] }>
}) {
  const { q } = use(searchParams)
  const query = Array.isArray(q) ? (q[0] ?? "") : (q ?? "")
  return <SearchResults key={query} query={query} />
}
