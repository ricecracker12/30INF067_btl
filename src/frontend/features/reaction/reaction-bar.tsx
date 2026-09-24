"use client"

import { ChevronDown } from "lucide-react"
import { useState } from "react"

import { Button } from "@/components/ui/button"
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@/components/ui/popover"
import type { ReactionTarget } from "@/lib/api/content-api"
import type { ReactionSummary } from "@/lib/api/types"

import { REACTION_META, REACTION_ORDER } from "./reaction-meta"
import { total } from "./reaction-reducer"
import { useReaction } from "./use-reaction"

// Thanh cảm xúc của MỘT đối tượng (bài hoặc bình luận — Đ-3.13). Không biết đối tượng nằm ở màn nào: `app/` ghép nó vào slot
// `footer` của card bài, và vào `renderReactions` của cây bình luận. Không import feature nào khác (Đ-E13).
//
// Nút chính bật/tắt "Thích" (hoặc gỡ loại đang chọn); nút mũi tên mở bảng sáu loại. Số đếm và nút sáng đổi NGAY khi bấm
// (optimistic), gửi tuần tự tối đa hai request cho một chuỗi bấm, lỗi thì quay về số server đã xác nhận.

type Props = {
  target: ReactionTarget
  /** `reactionCounts` + `myReaction` lúc đối tượng được nạp. */
  initial: ReactionSummary
  /** Bình luận dùng bản gọn: không nhãn chữ trên nút chính. */
  compact?: boolean
}

export function ReactionBar({ target, initial, compact }: Props) {
  const { summary, error, choose } = useReaction(target, initial)
  const [open, setOpen] = useState(false)

  const mine = summary.myReaction
  const current = REACTION_META[mine ?? "like"]
  const count = total(summary)
  const present = REACTION_ORDER.filter((t) => summary.reactionCounts[t])

  return (
    <div
      className="flex flex-wrap items-center gap-1"
      data-testid="reaction-bar"
      data-target={`${target.kind}:${target.id}`}
    >
      <Button
        variant={mine ? "secondary" : "ghost"}
        size="sm"
        aria-pressed={mine !== null}
        aria-label={mine ? `Bỏ cảm xúc ${current.label}` : "Thích"}
        onClick={() => choose(mine ? null : "like")}
      >
        <current.Icon data-icon="inline-start" aria-hidden />
        {!compact && current.label}
      </Button>

      <Popover open={open} onOpenChange={setOpen}>
        <PopoverTrigger
          render={
            <Button variant="ghost" size="icon-sm" aria-label="Chọn cảm xúc">
              <ChevronDown aria-hidden />
            </Button>
          }
        />
        <PopoverContent className="w-auto flex-row gap-1 p-2" align="start">
          {REACTION_ORDER.map((type) => {
            const { label, Icon } = REACTION_META[type]
            return (
              <Button
                key={type}
                variant={mine === type ? "secondary" : "ghost"}
                size="sm"
                aria-pressed={mine === type}
                onClick={() => {
                  // Chọn lại đúng loại đang có là gỡ — cùng hành vi với nút chính.
                  choose(mine === type ? null : type)
                  setOpen(false)
                }}
              >
                <Icon data-icon="inline-start" aria-hidden />
                {label}
              </Button>
            )
          })}
        </PopoverContent>
      </Popover>

      <span
        className="flex items-center gap-1 text-sm text-muted-foreground"
        data-testid="reaction-count"
        title={present
          .map((t) => `${REACTION_META[t].label}: ${summary.reactionCounts[t]}`)
          .join(" · ")}
      >
        {present.slice(0, 3).map((t) => {
          const { Icon } = REACTION_META[t]
          return <Icon key={t} className="size-3.5" aria-hidden />
        })}
        {count} cảm xúc
      </span>

      {error && (
        <p role="alert" className="w-full text-xs text-destructive">
          {error}
        </p>
      )}
    </div>
  )
}
