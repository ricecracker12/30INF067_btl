"use client"

import { FormAlert } from "@/components/form/form-alert"
import { Skeleton } from "@/components/ui/skeleton"

import { SearchResultItem } from "./search-result-item"
import { useUserSearch } from "./use-user-search"

/** Trang kết quả lấy tối đa 20 — không cursor, không "Xem thêm" (Đ-6.19: gõ thêm chữ, không cuộn trang 3). */
export const RESULTS_LIMIT = 20

/** Trang `/search?q=` (GĐ6 E4). `q` lấy từ URL — trang ráp ở `app/` truyền vào. */
export function SearchResults({ query }: { query: string }) {
  const search = useUserSearch(query, RESULTS_LIMIT)

  return (
    <section className="flex flex-col gap-4">
      <h1 className="text-xl font-medium">
        {search.term ? `Kết quả cho “${search.term}”` : "Tìm người"}
      </h1>

      {search.tooShort && (
        <p className="text-muted-foreground">Nhập ít nhất 2 ký tự.</p>
      )}
      {!search.tooShort && search.pending && (
        <div className="flex flex-col gap-2" role="status" aria-label="Đang tìm">
          <Skeleton className="h-10" />
          <Skeleton className="h-10" />
        </div>
      )}
      <FormAlert message={search.error} />
      {!search.tooShort &&
        !search.pending &&
        !search.error &&
        search.items.length === 0 && (
          <p className="text-muted-foreground" data-testid="search-empty">
            Không tìm thấy ai tên như vậy.
          </p>
        )}
      {search.items.length > 0 && (
        <ul className="flex flex-col gap-1">
          {search.items.map((r) => (
            <li key={r.userId}>
              <SearchResultItem result={r} />
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}
