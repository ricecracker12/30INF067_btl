"use client"

import Link from "next/link"
import { useRef, useState, type ReactNode } from "react"

import { Avatar, AvatarFallback, AvatarImage } from "@/components/ui/avatar"
import { Badge } from "@/components/ui/badge"
import { Card, CardContent } from "@/components/ui/card"
import { contentApi } from "@/lib/api/content-api"
import type { PostMedia, PostResponse } from "@/lib/api/types"

import { PRIVACY_LABEL } from "./privacy"

// Một bài trong danh sách và ở trang chi tiết — cùng component, khác ở chỗ `href` (card trong danh sách
// dẫn tới chi tiết, card ở chi tiết thì không tự dẫn tới chính nó).
//
// Hàng dưới cùng là SLOT `footer` (GĐ3 Đ-3.13): thanh cảm xúc và số bình luận thuộc `features/reaction`, `features/comment` —
// card không import chúng (Đ-E13 cấm import chéo), `app/` ghép vào đây cùng tiền lệ slot `actions` của E6.

const dateTime = new Intl.DateTimeFormat("vi-VN", {
  dateStyle: "medium",
  timeStyle: "short",
  timeZone: "Asia/Ho_Chi_Minh",
})

type Props = {
  post: PostResponse
  /** `true` ở trang chi tiết: không bọc liên kết tới chính trang đang mở. */
  standalone?: boolean
  /** Nút Sửa/Xóa của E6 — card không tự dựng, vì quyền đến từ `post.canEdit` và hành động là của màn. */
  actions?: ReactNode
  /** Bài vừa được nạp lại (ảnh hết hạn) — màn cập nhật vào danh sách nó đang cầm. */
  onRefreshed?: (post: PostResponse) => void
  /** Hàng tương tác dưới bài (GĐ3): thanh cảm xúc, số bình luận — do `app/` ghép. */
  footer?: ReactNode
}

export function PostCard({
  post,
  standalone,
  actions,
  onRefreshed,
  footer,
}: Props) {
  return (
    <Card data-testid="post-card" data-post-id={post.postId}>
      <CardContent className="flex flex-col gap-4">
        <div className="flex items-start justify-between gap-4">
          <AuthorLine post={post} />
          {actions}
        </div>

        {post.body && (
          // `whitespace-pre-line` giữ xuống dòng người dùng gõ; `break-words` để một chuỗi dài không
          // phá bố cục card.
          <p className="break-words whitespace-pre-line">{post.body}</p>
        )}

        {post.media.length > 0 && (
          <MediaGrid
            postId={post.postId}
            media={post.media}
            onRefreshed={onRefreshed}
          />
        )}

        <div className="flex flex-wrap items-center gap-4 text-sm text-muted-foreground">
          {footer}
          {!standalone && (
            <Link
              href={`/posts/${post.postId}`}
              className="ml-auto underline underline-offset-4"
            >
              Xem bài
            </Link>
          )}
        </div>
      </CardContent>
    </Card>
  )
}

function AuthorLine({ post }: { post: PostResponse }) {
  return (
    <div className="flex items-center gap-3">
      <Avatar>
        {post.author.avatarUrl && (
          <AvatarImage
            src={post.author.avatarUrl}
            alt={`Ảnh đại diện của ${post.author.displayName}`}
          />
        )}
        <AvatarFallback>
          {post.author.displayName.trim().charAt(0).toUpperCase()}
        </AvatarFallback>
      </Avatar>

      <div className="flex flex-col gap-0.5">
        <Link
          href={`/users/${post.author.userId}`}
          className="text-sm font-medium underline-offset-4 hover:underline"
        >
          {post.author.displayName}
        </Link>
        <div className="flex items-center gap-2 text-xs text-muted-foreground">
          <time dateTime={post.createdAt}>
            {dateTime.format(new Date(post.createdAt))}
          </time>
          <span aria-hidden>·</span>
          <span>{PRIVACY_LABEL[post.privacy]}</span>
          {/* Nhãn theo `editedAt`, không theo so sánh `createdAt !== updatedAt` — server đóng dấu, FE đọc. */}
          {post.editedAt != null && (
            <Badge variant="secondary" data-testid="edited-badge">
              đã chỉnh sửa
            </Badge>
          )}
        </div>
      </div>
    </div>
  )
}

/**
 * Lưới ảnh theo `position` (0..9) — thứ tự do server trả, sắp lại tại chỗ để không phụ thuộc vào thứ tự
 * phần tử trong mảng.
 *
 * `media[].url` là **presigned GET 15 phút** (Đ-2.9). Tab mở lâu hơn thế thì ảnh vỡ mà **không có lỗi nào
 * trong log** — trình duyệt chỉ bắn `error` trên chính thẻ `<img>`. Gặp `error` thì nạp lại bài ĐÚNG MỘT
 * LẦN cho mỗi lần mở màn: lần đầu là URL hết hạn (nạp lại được URL mới), lần sau là R2 hỏng thật — nạp
 * lại nữa chỉ là vòng lặp request. Đây cũng là bẫy GĐ4 phải nhớ ở tầng cache.
 */
function MediaGrid({
  postId,
  media,
  onRefreshed,
}: {
  postId: string
  media: PostMedia[]
  onRefreshed?: (post: PostResponse) => void
}) {
  const refreshed = useRef(false)
  const [broken, setBroken] = useState(false)

  const sorted = [...media].sort((a, b) => a.position - b.position)

  async function onImageError() {
    if (refreshed.current) {
      setBroken(true)
      return
    }
    refreshed.current = true
    try {
      onRefreshed?.(await contentApi.getPost(postId))
    } catch {
      // Nạp lại hỏng (404 vì bài vừa bị xóa, mất mạng) thì không có gì để hiện thêm — ảnh vỡ là tất cả
      // thông tin người dùng cần, và một FormAlert ở đây là báo lỗi cho thứ họ không làm.
      setBroken(true)
    }
  }

  return (
    <div className="grid grid-cols-2 gap-2" data-testid="post-media">
      {sorted.map((m) => (
        // `key` theo `url` chứ không theo `position`: sau khi nạp lại, URL mới phải làm React thay thẻ
        // `<img>` thật — giữ nguyên thẻ cũ thì trình duyệt dùng lại ảnh hỏng trong cache.
        //
        // `next/image` KHÔNG dùng được ở đây, và không phải vì lười: optimizer của Next TẢI ẢNH VỀ SERVER
        // Next rồi phục vụ lại, tức byte ảnh đi qua origin của app — đúng thứ Đ-2.5 cấm. Nó còn đòi khai
        // host R2 trong `remotePatterns`, mà URL presigned đổi chữ ký mỗi 15 phút nên cache của optimizer
        // là cache của những URL đã chết.
        // eslint-disable-next-line @next/next/no-img-element -- Đ-2.5: byte ảnh không đi qua origin app.
        <img
          key={m.url}
          src={m.url}
          alt=""
          width={m.width ?? undefined}
          height={m.height ?? undefined}
          loading="lazy"
          data-testid="post-image"
          className="aspect-square w-full rounded-md border border-border object-cover"
          onError={() => void onImageError()}
        />
      ))}
      {broken && (
        <p
          role="status"
          className="col-span-2 text-sm text-muted-foreground"
          data-testid="media-broken"
        >
          Không tải được ảnh của bài này.
        </p>
      )}
    </div>
  )
}
