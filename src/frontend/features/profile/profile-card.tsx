"use client"

import { useState } from "react"

import { Button } from "@/components/ui/button"
import { Separator } from "@/components/ui/separator"

import { ProfileForm } from "./profile-form"
import { useProfile } from "./use-profile"

/**
 * Hồ sơ trên `/me`: xem, và mở form sửa (E2 bước 6 — cùng component với onboarding, khác nhãn nút).
 * Nằm dưới `(with-profile)` nên tới đây chắc chắn `status === "ready"`; vẫn kiểm để không phải ép kiểu.
 */
export function ProfileCard() {
  const { profile } = useProfile()
  const [editing, setEditing] = useState(false)
  const [saved, setSaved] = useState(false)

  if (!profile) return null

  return (
    <section className="flex flex-col gap-6" data-testid="profile-card">
      <div className="flex items-center justify-between gap-4">
        <h2 className="text-lg font-medium">Hồ sơ</h2>
        {!editing && (
          <Button
            variant="outline"
            size="sm"
            onClick={() => {
              setSaved(false)
              setEditing(true)
            }}
          >
            Sửa hồ sơ
          </Button>
        )}
      </div>

      {editing ? (
        <>
          <ProfileForm
            initial={profile}
            submitLabel="Lưu"
            onSaved={() => {
              setEditing(false)
              setSaved(true)
            }}
          />
          <Button variant="ghost" onClick={() => setEditing(false)}>
            Hủy
          </Button>
        </>
      ) : (
        <dl className="grid grid-cols-[max-content_1fr] gap-x-6 gap-y-3 text-sm">
          <dt className="text-muted-foreground">Tên hiển thị</dt>
          <dd data-testid="profile-display-name">{profile.displayName}</dd>
          <dt className="text-muted-foreground">Giới thiệu</dt>
          {/* `whitespace-pre-line` giữ xuống dòng người dùng gõ; bio trống hiện gạch ngang, không hiện ô rỗng. */}
          <dd className="whitespace-pre-line">{profile.bio || "—"}</dd>
        </dl>
      )}

      {saved && (
        <p role="status" className="text-sm text-muted-foreground">
          Đã lưu hồ sơ.
        </p>
      )}
      <Separator />
    </section>
  )
}
