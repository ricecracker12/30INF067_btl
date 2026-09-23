"use client"

import { useCallback, useEffect, useRef, useState } from "react"

// Danh sách theo cursor keyset — khuôn DÙNG CHUNG của feed (GĐ4 E4) và ba danh sách của `/friends` (E3), không biết
// nghiệp vụ (Q-E8). Chép từ `features/post/use-post-page.ts` của GĐ2, giữ nguyên ba luật của cursor:
//
//  1. **FE không bao giờ dựng, sửa, hay diễn giải cursor** — truyền lại NGUYÊN VẸN chuỗi vừa nhận.
//  2. **Hết dữ liệu KHI VÀ CHỈ KHI `nextCursor === null`** (Đ-2.11, Đ-4.9). Không một dòng nào so độ dài trang với
//     `limit`: server lọc lại quyền xem ở mọi response, nên trang ngắn — kể cả trang RỖNG — vẫn có thể còn trang sau.
//  3. **Nối trang bằng id, không `concat` thẳng.** StrictMode chạy effect hai lần; observer bắn liên tiếp.
//
// Khác bản GĐ2: `error` là lỗi THÔ (màn tự chọn câu và cách vẽ — feed cần tách 503 quá tải bằng `type`), trả kèm
// trang đầu (`head`, để feed đọc `mode`), và đếm số trang liên tiếp không thêm được gì (`emptyStreak`, trần tự nạp).
// `use-post-page.ts` CHƯA chuyển sang đây — nợ có địa chỉ (Q-E8, GĐ5).

type Page<T> = { items: T[]; nextCursor: string | null }

/** Kết quả của MỘT lượt xem, gắn `key`. Đổi `key` là dữ liệu cũ tự hết hiệu lực — không `setState` đồng bộ trong effect. */
type Loaded<T, P extends Page<T>> = {
  key: string
  /** `null` khi chính trang đầu hỏng. */
  head: P | null
  items: T[]
  nextCursor: string | null
  error: unknown
  emptyStreak: number
  lastAdded: number
}

export type CursorPagesState<T, P extends Page<T>> = {
  items: T[]
  /** Trang ĐẦU của lượt xem hiện tại — trang sau không ghi đè (nhãn theo trang đầu không nhảy giữa chừng). */
  head: P | null
  /** `null` = hết trang (hoặc chưa nạp xong trang đầu). Không bao giờ là chuỗi rỗng. */
  nextCursor: string | null
  /** Đang bay một lượt gọi — trang đầu hay trang sau đều tính. */
  pending: boolean
  /** Lỗi thô của lượt gọi gần nhất, `null` khi lượt gần nhất thành công. */
  error: unknown
  /** Trang đầu đã về (dù rỗng, dù lỗi) — để màn phân biệt "đang nạp" với "không có gì". */
  loaded: boolean
  /** Số trang liên tiếp (tính cả trang đầu) không thêm được mục mới nào. Về 0 ngay khi một trang có mục. */
  emptyStreak: number
  /**
   * Số mục trang SAU gần nhất thêm được — trang đầu luôn 0. Để màn báo cho trình đọc màn hình "đã tải thêm N" khi danh
   * sách tự nối dài mà không có nút nào được bấm.
   */
  lastAdded: number
  /** Nạp trang kế với `nextCursor` hiện tại — cũng là "thử lại" sau khi trang sau hỏng. Hết trang / đang bay: bỏ qua. */
  loadMore: () => void
  /** Nạp lại từ đầu (lượt xem mới). */
  reload: () => void
  /** Thay một mục tại chỗ, hoặc gỡ nó đi khi `next` là `null`. */
  replaceItem: (id: string, next: T | null) => void
}

type Options<T, P extends Page<T>> = {
  /** Định danh lượt xem (ví dụ `"feed"`, `"friends:outgoing"`). `null` = chưa biết xem gì, không gọi. */
  key: string | null
  fetchPage: (cursor: string | null, signal?: AbortSignal) => Promise<P>
  getId: (item: T) => string
}

export function useCursorPages<T, P extends Page<T>>({
  key: viewKey,
  fetchPage,
  getId,
}: Options<T, P>): CursorPagesState<T, P> {
  const [data, setData] = useState<Loaded<T, P> | null>(null)
  // Trang SAU: `loadMore` chạy trong sự kiện (bấm nút, callback observer) nên set state đồng bộ ở đó là hợp lệ.
  const [pendingMore, setPendingMore] = useState(false)
  const [attempt, setAttempt] = useState(0)

  const key = viewKey === null ? null : `${viewKey}:${attempt}`
  const current = data !== null && data.key === key ? data : null

  // `fetchPage`/`getId` là closure mới mỗi render — giữ bản mới nhất trong ref để effect trang đầu KHÔNG chạy lại mỗi
  // render. Ghi trong effect, không lúc render (render bị hủy vẫn kịp ghi đè — lý do như `currentRef` dưới).
  const fetchRef = useRef(fetchPage)
  const getIdRef = useRef(getId)
  const currentRef = useRef<Loaded<T, P> | null>(null)
  useEffect(() => {
    fetchRef.current = fetchPage
    getIdRef.current = getId
    currentRef.current = current
  })

  const pendingRef = useRef(false)
  // Khởi tạo LƯỜI — cổng `USE_REF_NEW` cấm `useRef(new Set())`.
  const seenRef = useRef<Set<string> | null>(null)
  const seen = useCallback(() => (seenRef.current ??= new Set<string>()), [])
  // Controller của LƯỢT XEM: trang đầu hủy từ cleanup, trang sau dùng chung — rời màn là hủy cả lượt đang bay.
  const pageAbortRef = useRef<AbortController | null>(null)
  // Tăng mỗi lượt xem mới — lượt gọi của lượt cũ về muộn bị bỏ, không ghi đè danh sách mới.
  const runRef = useRef(0)

  const run = useCallback(
    async (
      pageKey: string,
      cursor: string | null,
      runId: number,
      signal?: AbortSignal
    ) => {
      pendingRef.current = true
      try {
        const page = await fetchRef.current(cursor, signal)
        if (runId !== runRef.current) return

        // Lọc theo `seen()` chứ không theo `items` của lần render trước: hai lượt về sát nhau thì lượt sau vẫn thấy
        // mảng cũ và lô trùng lọt qua.
        const daThay = seen()
        const them = page.items.filter(
          (it) => !daThay.has(getIdRef.current(it))
        )
        them.forEach((it) => daThay.add(getIdRef.current(it)))

        setData((prev) => {
          const same = prev !== null && prev.key === pageKey
          return {
            key: pageKey,
            head: same && cursor !== null ? prev.head : page,
            items:
              same && them.length === 0
                ? prev.items
                : [...(same ? prev.items : []), ...them],
            // Hợp đồng: hết dữ liệu là `null`. `?? null` để một `undefined` bất ngờ không thành "còn trang".
            nextCursor: page.nextCursor ?? null,
            error: null,
            emptyStreak:
              them.length === 0 ? (same ? prev.emptyStreak : 0) + 1 : 0,
            lastAdded: cursor === null ? 0 : them.length,
          }
        })
      } catch (e) {
        if (runId !== runRef.current) return
        if ((e as Error).name === "AbortError") return
        setData((prev) => {
          const same = prev !== null && prev.key === pageKey
          return {
            key: pageKey,
            head: same ? prev.head : null,
            // Giữ những gì đã có: trang SAU hỏng thì danh sách cũ vẫn đúng, và `nextCursor` giữ nguyên để "Thử lại"
            // nạp lại ĐÚNG lô vừa hỏng.
            items: same ? prev.items : [],
            nextCursor: same ? prev.nextCursor : null,
            error: e,
            emptyStreak: same ? prev.emptyStreak : 0,
            lastAdded: same ? prev.lastAdded : 0,
          }
        })
      } finally {
        if (runId === runRef.current) {
          pendingRef.current = false
          setPendingMore(false)
        }
      }
    },
    [seen]
  )

  // Trang đầu: mỗi lần `key` đổi. Không `setState` nào ở đây — dữ liệu cũ tự hết hiệu lực vì `key` không khớp.
  useEffect(() => {
    if (key === null) return

    const runId = ++runRef.current
    // BẮT BUỘC: nạp lại sau khi đã có dữ liệu thì lô trang đầu NẰM SẴN trong set cũ và bị gạt hết — màn trắng.
    seenRef.current = new Set()
    pendingRef.current = false
    const controller = new AbortController()
    pageAbortRef.current = controller

    void run(key, null, runId, controller.signal)
    return () => controller.abort()
  }, [key, run])

  const loadMore = useCallback(() => {
    const now = currentRef.current
    if (pendingRef.current || !now || now.nextCursor === null) return
    setPendingMore(true)
    void run(
      now.key,
      now.nextCursor,
      runRef.current,
      pageAbortRef.current?.signal
    )
  }, [run])

  const reload = useCallback(() => setAttempt((n) => n + 1), [])

  const replaceItem = useCallback(
    (id: string, next: T | null) => {
      if (next === null) seen().delete(id)
      setData((prev) =>
        prev === null
          ? prev
          : {
              ...prev,
              items:
                next === null
                  ? prev.items.filter((it) => getIdRef.current(it) !== id)
                  : prev.items.map((it) =>
                      getIdRef.current(it) === id ? next : it
                    ),
            }
      )
    },
    [seen]
  )

  return {
    items: current?.items ?? [],
    head: current?.head ?? null,
    nextCursor: current?.nextCursor ?? null,
    pending: (key !== null && current === null) || pendingMore,
    error: current?.error ?? null,
    loaded: current !== null,
    emptyStreak: current?.emptyStreak ?? 0,
    lastAdded: current?.lastAdded ?? 0,
    loadMore,
    reload,
    replaceItem,
  }
}
