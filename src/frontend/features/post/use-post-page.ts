"use client"

import { useCallback, useEffect, useRef, useState } from "react"

import { contentApi } from "@/lib/api/content-api"
import { errorMessage } from "@/lib/api/messages"
import type { PostResponse } from "@/lib/api/types"

// Cuộn theo cursor keyset của Đ-2.11. Khuôn này là **di sản cho GĐ4 (feed) và GĐ5 (lịch sử hội thoại)**
// (Mục 17): sai ở đây thì GĐ4 viết lại phần cuộn vô hạn, đúng thứ Đ-2.11 sinh ra để tránh.
//
// Ba luật của cursor, không luật nào thương lượng được:
//
//  1. **FE không bao giờ dựng, sửa, hay diễn giải cursor.** Nó là `base64url("{created_at:O}|{post_id}")`
//     nhưng đó là chuyện của server — FE truyền lại NGUYÊN VẸN chuỗi vừa nhận.
//  2. **Hết dữ liệu là `nextCursor === null`**, không phải chuỗi rỗng. Coi `""` là "còn trang" là một vòng
//     lặp gọi API không có điểm dừng.
//  3. **Nối trang bằng `postId`, không `concat` thẳng.** StrictMode chạy effect hai lần, và người dùng bấm
//     "Xem thêm" hai lần trước khi lô đầu về — cả hai cho cùng một lô, tức cùng `key` React hai lần.

/** Mặc định của hợp đồng. Hằng một chỗ: `limit` ngoài `1..50` là 400 `errors.limit` (Đ-2.11). */
export const PAGE_SIZE = 20

/**
 * Kết quả của MỘT lượt xem danh sách, gắn `key = "{userId}:{attempt}"`.
 *
 * Gom vào một object có `key` chứ không rải thành bốn `useState` là để **không gọi `setState` đồng bộ
 * trong effect** (`react-hooks/set-state-in-effect` — set đồng bộ gây render dây chuyền). Đổi `userId`
 * hay bấm "Thử lại" chỉ đổi `key`; dữ liệu cũ tự hết hiệu lực vì `key` không khớp nữa, không ai phải xóa
 * nó bằng tay. Nhờ vậy bài của người này không bao giờ nháy lên trên hồ sơ người kia.
 */
type Loaded = {
  key: string
  items: PostResponse[]
  nextCursor: string | null
  error: string | null
}

export type PostPageState = {
  items: PostResponse[]
  /** `null` = hết trang (hoặc chưa nạp xong trang đầu). Không bao giờ là chuỗi rỗng. */
  nextCursor: string | null
  /** Đang bay một lượt gọi — trang đầu hay trang sau đều tính. */
  pending: boolean
  error: string | null
  /** Trang đầu đã về (dù rỗng, dù lỗi) — để màn phân biệt "đang nạp" với "không có bài nào". */
  loaded: boolean
  /** Nạp trang kế. Không còn trang, hoặc đang bay một lượt, thì không làm gì. */
  loadMore: () => void
  /** Nạp lại từ đầu. Dùng cho nút "Thử lại"/"Tải lại". */
  reload: () => void
  /**
   * Thay một bài tại chỗ (E6 sửa bài), hoặc gỡ nó đi khi `next` là `null` (E6 xóa bài — để lại card cũ
   * là để lại một liên kết chết, vì `GET /posts/{id}` sau xóa mềm trả 404 kể cả với tác giả).
   */
  replaceItem: (postId: string, next: PostResponse | null) => void
}

/**
 * Bài của một người, mới nhất trước. `userId === null` nghĩa là màn chưa biết đang xem bài của ai —
 * không gọi gì cho tới khi có id.
 */
export function useUserPosts(userId: string | null): PostPageState {
  const [data, setData] = useState<Loaded | null>(null)
  // Trang SAU: `loadMore` chạy trong sự kiện bấm nút, nên set state đồng bộ ở đó là hợp lệ.
  const [pendingMore, setPendingMore] = useState(false)
  const [attempt, setAttempt] = useState(0)

  const key = `${userId ?? ""}:${attempt}`

  // Dữ liệu của lượt xem KHÁC thì coi như chưa có gì — không cần xóa state, chỉ cần không đọc nó.
  const current = data?.key === key ? data : null

  // Bản sao đồng bộ để `loadMore` (trong sự kiện bấm) thấy cursor mới nhất, chứ không phải giá trị của
  // lần render đang treo; và để chặn gọi đôi.
  const currentRef = useRef<Loaded | null>(null)
  currentRef.current = current
  const pendingRef = useRef(false)
  const seenRef = useRef(new Set<string>())
  // Tăng mỗi lượt xem mới — lượt gọi của `userId` cũ về muộn sẽ bị bỏ, không ghi đè danh sách mới.
  const runRef = useRef(0)

  const fetchPage = useCallback(
    async (
      id: string,
      pageKey: string,
      cursor: string | null,
      run: number,
      signal?: AbortSignal
    ) => {
      pendingRef.current = true
      try {
        const page = await contentApi.listUserPosts(
          id,
          // `cursor` chỉ vào query string KHI CÓ — `?cursor=undefined` là 400 `errors.cursor`.
          { cursor, limit: PAGE_SIZE },
          signal
        )
        if (run !== runRef.current) return

        // Khử trùng theo `postId`. `seenRef` chứ không lọc theo `items` của lần render trước: hai lượt
        // gọi trùng về sát nhau thì lượt thứ hai vẫn thấy mảng cũ và lô trùng lọt qua.
        const them = page.items.filter((p) => !seenRef.current.has(p.postId))
        them.forEach((p) => seenRef.current.add(p.postId))

        setData((prev) => {
          const base = prev?.key === pageKey ? prev.items : []
          return {
            key: pageKey,
            items: them.length === 0 ? base : [...base, ...them],
            // Hợp đồng: hết dữ liệu là `null`. `?? null` để một `undefined` bất ngờ không thành "còn trang".
            nextCursor: page.nextCursor ?? null,
            error: null,
          }
        })
      } catch (e) {
        if (run !== runRef.current) return
        if ((e as Error).name === "AbortError") return
        setData((prev) => ({
          key: pageKey,
          // Giữ nguyên những gì đã có: trang SAU hỏng thì danh sách cũ vẫn đúng, xóa nó đi là phạt người
          // dùng vì một lô lỗi.
          items: prev?.key === pageKey ? prev.items : [],
          nextCursor: prev?.key === pageKey ? prev.nextCursor : null,
          error: errorMessage("post-read", e),
        }))
      } finally {
        if (run === runRef.current) {
          pendingRef.current = false
          setPendingMore(false)
        }
      }
    },
    []
  )

  // Trang đầu: mỗi lần `key` đổi (`userId` khác, hoặc bấm Thử lại). Không `setState` nào ở đây — `seenRef`
  // và `runRef` là ref, và dữ liệu cũ đã tự hết hiệu lực vì `key` không khớp.
  useEffect(() => {
    if (userId === null) return

    const run = ++runRef.current
    seenRef.current = new Set()
    pendingRef.current = false
    const controller = new AbortController()

    void fetchPage(userId, `${userId}:${attempt}`, null, run, controller.signal)
    // Rời trang hoặc đổi người: hủy lượt đang bay.
    return () => controller.abort()
  }, [userId, attempt, fetchPage])

  const loadMore = useCallback(() => {
    const now = currentRef.current
    // Hết trang, hoặc còn một lượt đang bay: không gọi. Bấm "Xem thêm" hai lần nhanh là hai lô giống hệt.
    if (
      userId === null ||
      pendingRef.current ||
      !now ||
      now.nextCursor === null
    )
      return
    setPendingMore(true)
    void fetchPage(userId, now.key, now.nextCursor, runRef.current)
  }, [userId, fetchPage])

  const reload = useCallback(() => setAttempt((n) => n + 1), [])

  const replaceItem = useCallback(
    (postId: string, next: PostResponse | null) => {
      if (next === null) seenRef.current.delete(postId)
      setData((prev) =>
        prev === null
          ? prev
          : {
              ...prev,
              items:
                next === null
                  ? prev.items.filter((p) => p.postId !== postId)
                  : prev.items.map((p) => (p.postId === postId ? next : p)),
            }
      )
    },
    []
  )

  return {
    items: current?.items ?? [],
    nextCursor: current?.nextCursor ?? null,
    // Trang đầu chưa về là đang bay; trang sau thì `pendingMore` nói.
    pending: (userId !== null && current === null) || pendingMore,
    error: current?.error ?? null,
    loaded: current !== null,
    loadMore,
    reload,
    replaceItem,
  }
}
