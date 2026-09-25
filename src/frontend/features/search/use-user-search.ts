"use client"

import { useEffect, useState } from "react"

import { fieldMessage } from "@/lib/api/messages"
import { profileApi } from "@/lib/api/profile-api"
import type { SearchResult } from "@/lib/api/types"

/** Gõ tới đâu ra tới đó nhưng không mỗi phím một request (Đ-6.21). */
export const SEARCH_DEBOUNCE_MS = 300
/** Ngưỡng dưới của server sau `trim` (Đ-6.19) — client KHÔNG chặt hơn (Đ-E5); trên 50 để server trả `errors.q`. */
export const SEARCH_MIN_LENGTH = 2

type Loaded =
  | { key: string; items: SearchResult[] }
  | { key: string; error: string }

export type UserSearchState = {
  /** Từ khóa sau `trim` — thứ thật sự gửi đi. */
  term: string
  /** Dưới 2 ký tự: màn hiện "Nhập ít nhất 2 ký tự", KHÔNG gọi API. */
  tooShort: boolean
  /** Đang đợi debounce hoặc đang bay. */
  pending: boolean
  items: SearchResult[]
  error: string | null
}

/**
 * Tìm người theo tên (GĐ6 Đ-6.19, Đ-6.21). Mỗi lượt gõ: đợi 300 ms, rồi gọi với `AbortController` TẠO TRONG EFFECT (luật frontend #14)
 * — gõ tiếp thì cleanup vừa hủy timer vừa hủy request cũ, kết quả cũ về muộn không đè kết quả mới. Kết quả gắn `key` = từ khóa
 * (khuôn "key + suy ra"): không `setState` đồng bộ trong effect.
 */
export function useUserSearch(query: string, limit: number): UserSearchState {
  const term = query.trim()
  const tooShort = term.length < SEARCH_MIN_LENGTH
  const key = `${term}:${limit}`
  const [data, setData] = useState<Loaded | null>(null)

  useEffect(() => {
    if (term.length < SEARCH_MIN_LENGTH) return
    const controller = new AbortController()
    const timer = setTimeout(() => {
      profileApi.search(term, limit, controller.signal).then(
        (page) => setData({ key, items: page.items }),
        (e: unknown) => {
          if ((e as Error).name === "AbortError") return
          // 400 đọc ĐÚNG câu server dưới `errors.q` (Đ-E5) — vd. quá 50 ký tự.
          setData({ key, error: fieldMessage(e, "q", "search") })
        }
      )
    }, SEARCH_DEBOUNCE_MS)
    return () => {
      clearTimeout(timer)
      controller.abort()
    }
  }, [term, limit, key])

  const current = !tooShort && data?.key === key ? data : null
  return {
    term,
    tooShort,
    pending: !tooShort && current === null,
    items: current && "items" in current ? current.items : [],
    error: current && "error" in current ? current.error : null,
  }
}
