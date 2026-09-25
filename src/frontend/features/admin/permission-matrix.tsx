"use client"

import { Checkbox } from "@/components/ui/checkbox"
import { Field, FieldContent, FieldDescription, FieldLabel, FieldTitle } from "@/components/ui/field"
import type { PermissionInfo } from "@/lib/api/types"

type Props = {
  permissions: PermissionInfo[]
  selected: ReadonlySet<string>
  onToggle: (code: string, checked: boolean) => void
  /** ADMIN (`editable: false`) — chỉ đọc: ADMIN có mọi quyền bằng lối tắt tầng 2, không dòng `role_permissions` nào (Đ-6.9). */
  readOnly?: boolean
  disabled?: boolean
  idPrefix: string
}

/**
 * Ma trận 18 quyền × MỘT vai trò (GĐ6 E8). Mã không `assignable` (`role.manage` — L-D19) luôn khóa: server trả 400 nếu gửi, và
 * muốn trao toàn quyền thì gán vai trò ADMIN. Nhãn là `code` + `description` của server — FE không tự đặt tên quyền.
 */
export function PermissionMatrix({
  permissions,
  selected,
  onToggle,
  readOnly,
  disabled,
  idPrefix,
}: Props) {
  return (
    <ul className="flex flex-col gap-2" data-testid="permission-matrix">
      {permissions.map((p) => {
        const id = `${idPrefix}-${p.code}`
        return (
          <li key={p.code}>
            <FieldLabel htmlFor={id}>
              <Field orientation="horizontal">
                <Checkbox
                  id={id}
                  checked={selected.has(p.code)}
                  disabled={readOnly || disabled || !p.assignable}
                  onCheckedChange={(checked) => onToggle(p.code, checked === true)}
                />
                <FieldContent>
                  <FieldTitle>
                    <code>{p.code}</code>
                  </FieldTitle>
                  {(p.description || !p.assignable) && (
                    <FieldDescription>
                      {p.description}
                      {!p.assignable && " (chỉ Quản trị viên có — không trao qua vai trò)"}
                    </FieldDescription>
                  )}
                </FieldContent>
              </Field>
            </FieldLabel>
          </li>
        )
      })}
    </ul>
  )
}
