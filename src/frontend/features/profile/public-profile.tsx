"use client"

import { useEffect, useState, type ReactNode } from "react"

import { FormAlert } from "@/components/form/form-alert"
import { Avatar, AvatarFallback, AvatarImage } from "@/components/ui/avatar"
import { Button } from "@/components/ui/button"
import { Card, CardContent } from "@/components/ui/card"
import { Skeleton } from "@/components/ui/skeleton"
import { errorMessage } from "@/lib/api/messages"
import { ApiError } from "@/lib/api/problem"
import { profileApi } from "@/lib/api/profile-api"
import type { ProfileResponse } from "@/lib/api/types"

/**
 * Hồ sơ công khai của MỘT NGƯỜI KHÁC — `GET /users/{userId}/profile`.
 *
 * Không dùng `profileStore`: store giữ hồ sơ của **người đang đăng nhập** (header, composer, `/me` đều
 * đọc nó). Ghi hồ sơ người khác vào đó là đổi tên hiển thị của chính mình trên thanh đầu trang.
 *
 * **404 ở đây KHÔNG phải tín hiệu onboarding** như ở E2 — đó chỉ đúng khi `userId` là chính mình. Với
 * người khác, 404 nghĩa là không có hồ sơ nào để xem: người dùng không tồn tại, hoặc tồn tại mà chưa
 * onboarding. Một câu cho cả hai, cùng lý do với 404 của bài: FE không được khai tài khoản nào có thật.
 */
type Loaded =
  | { key: string; profile: ProfileResponse }
  | { key: string; notFound: true }
  | { key: string; error: string }

type Props = {
  userId: string
  /**
   * Nút cạnh tên (GĐ4 E5: nút quan hệ). `app/` truyền vào — `features/profile` không import `features/friend` (Đ-E13).
   * CHỈ gọi khi hồ sơ đã nạp được: người không tồn tại (404) hay lỗi nạp thì không có nút Kết bạn nào để bấm vào hư không.
   * Dạng HÀM nhận hồ sơ đã nạp — để `app/` lấy tên người kia cho hộp thoại Hủy kết bạn mà không nạp hồ sơ lần hai.
   */
  actions?: (profile: ProfileResponse) => ReactNode
}

export function PublicProfile({ userId, actions }: Props) {
  const [data, setData] = useState<Loaded | null>(null)
  const [attempt, setAttempt] = useState(0)

  // Cùng khuôn "key + suy ra" của `PostDetail`: không `setState` đồng bộ trong effect, và hồ sơ người
  // này không nháy lên khi điều hướng sang hồ sơ người kia.
  const key = `${userId}:${attempt}`
  const current = data?.key === key ? data : null

  useEffect(() => {
    const controller = new AbortController()
    const pageKey = `${userId}:${attempt}`

    profileApi.get(userId, controller.signal).then(
      (res) => setData({ key: pageKey, profile: res }),
      (e: unknown) => {
        if ((e as Error).name === "AbortError") return
        if (e instanceof ApiError && e.status === 404)
          setData({ key: pageKey, notFound: true })
        else setData({ key: pageKey, error: errorMessage("profile-read", e) })
      }
    )
    return () => controller.abort()
  }, [userId, attempt])

  if (current === null) {
    return (
      <div className="flex items-center gap-4" aria-hidden>
        <Skeleton className="size-16 rounded-full" />
        <div className="flex flex-1 flex-col gap-2">
          <Skeleton className="h-5 w-40" />
          <Skeleton className="h-4 w-64" />
        </div>
      </div>
    )
  }

  if ("notFound" in current) {
    return (
      <Card data-testid="profile-not-found">
        <CardContent>
          <p>Không tìm thấy người dùng này.</p>
        </CardContent>
      </Card>
    )
  }

  if ("error" in current) {
    return (
      <div className="flex flex-col gap-4">
        <FormAlert message={current.error} />
        <Button
          variant="outline"
          className="self-start"
          onClick={() => setAttempt((n) => n + 1)}
        >
          Thử lại
        </Button>
      </div>
    )
  }

  const profile = current.profile

  return (
    <section className="flex items-start gap-4" data-testid="public-profile">
      <Avatar className="size-16">
        {profile.avatarUrl && (
          <AvatarImage
            src={profile.avatarUrl}
            alt={`Ảnh đại diện của ${profile.displayName}`}
          />
        )}
        <AvatarFallback className="text-xl">
          {profile.displayName.trim().charAt(0).toUpperCase()}
        </AvatarFallback>
      </Avatar>

      <div className="flex flex-col gap-1">
        <h1 className="text-xl font-medium" data-testid="public-display-name">
          {profile.displayName}
        </h1>
        {/* `whitespace-pre-line` giữ xuống dòng người dùng gõ; bio trống thì không hiện ô rỗng. */}
        {profile.bio && (
          <p className="text-sm whitespace-pre-line text-muted-foreground">
            {profile.bio}
          </p>
        )}
        {actions && (
          <div className="pt-2" data-testid="public-profile-actions">
            {actions(profile)}
          </div>
        )}
      </div>
    </section>
  )
}
