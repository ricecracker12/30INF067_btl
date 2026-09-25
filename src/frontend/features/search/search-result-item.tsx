import Link from "next/link"

import { Avatar, AvatarFallback, AvatarImage } from "@/components/ui/avatar"
import type { SearchResult } from "@/lib/api/types"

/** Một người trong kết quả — bấm là sang hồ sơ (nút quan hệ nằm ở đó, Đ-6.19 không trả trạng thái quan hệ). */
export function SearchResultItem({
  result,
  onPick,
}: {
  result: SearchResult
  onPick?: () => void
}) {
  return (
    <Link
      href={`/users/${encodeURIComponent(result.userId)}`}
      onClick={onPick}
      data-testid="search-result"
      className="flex items-center gap-3 rounded-lg p-2 text-sm hover:bg-muted"
    >
      <Avatar className="size-8">
        {result.avatarUrl && (
          <AvatarImage
            src={result.avatarUrl}
            alt={`Ảnh đại diện của ${result.displayName}`}
          />
        )}
        <AvatarFallback>
          {result.displayName.trim().charAt(0).toUpperCase()}
        </AvatarFallback>
      </Avatar>
      <span>{result.displayName}</span>
    </Link>
  )
}
