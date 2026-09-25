"use client"

import { SearchIcon } from "lucide-react"
import { useRouter } from "next/navigation"
import { useState } from "react"

import { Input } from "@/components/ui/input"

import { SearchResultItem } from "./search-result-item"
import { useUserSearch } from "./use-user-search"

/** Gợi ý dưới ô ở header (B.8 E4) — trang kết quả mới lấy đủ 20. */
export const SUGGEST_LIMIT = 8

/**
 * Ô tìm người ở header (GĐ6 E4, Mục 7.7): gõ → gợi ý ≤ 8 dưới ô; Enter → `/search?q=`. Dưới 2 ký tự chỉ nhắc, không gọi API.
 * Danh sách gợi ý là một `<ul>` thường dưới ô — không popover: không có gì để "mở", nó theo đúng nội dung ô.
 */
export function SearchBox() {
  const router = useRouter()
  const [query, setQuery] = useState("")
  const [focused, setFocused] = useState(false)
  const search = useUserSearch(query, SUGGEST_LIMIT)
  const showPanel = focused && query.trim().length > 0

  return (
    <form
      role="search"
      className="relative"
      onSubmit={(e) => {
        e.preventDefault()
        if (search.term.length === 0) return
        setFocused(false)
        router.push(`/search?q=${encodeURIComponent(search.term)}`)
      }}
    >
      <SearchIcon
        aria-hidden
        className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-muted-foreground"
      />
      <Input
        type="search"
        aria-label="Tìm người"
        placeholder="Tìm người"
        value={query}
        onChange={(e) => setQuery(e.target.value)}
        onFocus={() => setFocused(true)}
        // Trễ một nhịp: bấm vào gợi ý làm ô mất focus TRƯỚC khi click tới liên kết — ẩn ngay là nuốt cú bấm.
        onBlur={() => setTimeout(() => setFocused(false), 150)}
        className="h-8 w-36 pl-8 sm:w-48"
        data-testid="search-input"
      />
      {showPanel && (
        <div
          className="absolute top-full right-0 z-50 mt-1 w-64 rounded-lg border border-border bg-popover p-1 text-popover-foreground shadow-md"
          data-testid="search-suggestions"
        >
          {search.tooShort && (
            <p className="p-2 text-sm text-muted-foreground">
              Nhập ít nhất 2 ký tự.
            </p>
          )}
          {!search.tooShort && search.pending && (
            <p role="status" className="p-2 text-sm text-muted-foreground">
              Đang tìm…
            </p>
          )}
          {search.error && (
            <p className="p-2 text-sm text-destructive">{search.error}</p>
          )}
          {!search.pending &&
            !search.error &&
            !search.tooShort &&
            search.items.length === 0 && (
              <p className="p-2 text-sm text-muted-foreground">
                Không tìm thấy ai tên như vậy.
              </p>
            )}
          {search.items.length > 0 && (
            <ul>
              {search.items.map((r) => (
                <li key={r.userId}>
                  <SearchResultItem
                    result={r}
                    onPick={() => {
                      setFocused(false)
                      setQuery("")
                    }}
                  />
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </form>
  )
}
