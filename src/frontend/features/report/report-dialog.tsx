"use client"

import { FlagIcon } from "lucide-react"
import { useState } from "react"

import { FormAlert } from "@/components/form/form-alert"
import { TextareaField } from "@/components/form/textarea-field"
import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog"
import {
  Field,
  FieldError,
  FieldLabel,
  FieldTitle,
} from "@/components/ui/field"
import { RadioGroup, RadioGroupItem } from "@/components/ui/radio-group"
import { Spinner } from "@/components/ui/spinner"
import { errorMessage, validationErrors } from "@/lib/api/messages"
import { moderationApi } from "@/lib/api/moderation-api"
import { ApiError } from "@/lib/api/problem"
import type { ReasonCode, ReportTargetType } from "@/lib/api/types"
import { REASON_CODES, REASON_LABEL } from "@/lib/moderation/reasons"
import {
  optionalText,
  REPORT_REASON_INVALID,
  reportDetailError,
} from "@/lib/validation/moderation"

/** CÙNG một câu cho 201 (mới) và 200 (đã báo, còn mở) — người báo không cần biết mình đã báo rồi hay chưa (Mục 7.1). */
export const REPORT_THANKS = "Cảm ơn bạn. Chúng tôi sẽ xem xét báo cáo này."

const TARGET_NAME: Record<ReportTargetType, string> = {
  post: "bài viết",
  comment: "bình luận",
  user: "người dùng",
}

type Props = {
  targetType: ReportTargetType
  targetId: string
  /** Nút mở gọn (chỉ biểu tượng) — dùng trong hàng cảm xúc của bình luận. */
  compact?: boolean
}

type Fields = { reasonCode?: string; detail?: string }

/**
 * Nút "Báo cáo" + hộp thoại (GĐ6 E5, UC-18, Mục 7.1): năm lý do bằng nhãn tiếng Việt, "Khác" bắt buộc mô tả. Không biết mình đang
 * nằm trên bài, bình luận hay hồ sơ nào — `app/` ghép vào slot của màn người khác (Đ-6.20), `features/post`/`comment`/`profile`
 * không import feature này.
 *
 * Không hiện gì lộ luật kiểm duyệt: 404 (không thấy được hoặc không tồn tại) là MỘT câu "Nội dung này không còn nữa".
 */
export function ReportButton({ targetType, targetId, compact }: Props) {
  const [open, setOpen] = useState(false)
  const [reason, setReason] = useState<ReasonCode | null>(null)
  const [detail, setDetail] = useState("")
  const [fields, setFields] = useState<Fields>({})
  const [formError, setFormError] = useState<string | null>(null)
  const [pending, setPending] = useState(false)
  const [done, setDone] = useState(false)

  function reset() {
    setReason(null)
    setDetail("")
    setFields({})
    setFormError(null)
    setDone(false)
  }

  async function submit() {
    if (pending) return
    const isOther = reason === "other"
    const next: Fields = {
      reasonCode: reason ? undefined : REPORT_REASON_INVALID,
      detail: reportDetailError(detail, isOther),
    }
    setFields(next)
    setFormError(null)
    if (next.reasonCode || next.detail || !reason) return

    setPending(true)
    try {
      await moderationApi.create({
        targetType,
        targetId,
        reasonCode: reason,
        detail: optionalText(detail),
      })
      setDone(true)
    } catch (e) {
      if (e instanceof ApiError && e.status === 400) {
        const v = validationErrors(e, ["reasonCode", "detail"] as const)
        setFields(v.fields)
        // `errors.targetId` (báo cáo nội dung của chính mình) không có ô nào — câu server hiện cấp form.
        const own = e.fieldErrors.targetId?.[0]
        setFormError(own ?? v.formMessage)
      } else setFormError(errorMessage("report-create", e))
    } finally {
      setPending(false)
    }
  }

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        if (!next && pending) return
        if (next) reset()
        setOpen(next)
      }}
    >
      <DialogTrigger
        render={
          <Button
            variant="ghost"
            size={compact ? "icon-xs" : "sm"}
            aria-label={compact ? `Báo cáo ${TARGET_NAME[targetType]}` : undefined}
            data-testid="report-button"
          />
        }
      >
        <FlagIcon data-icon={compact ? undefined : "inline-start"} aria-hidden />
        {!compact && "Báo cáo"}
      </DialogTrigger>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Báo cáo {TARGET_NAME[targetType]}</DialogTitle>
          {!done && (
            <DialogDescription>
              Người bị báo cáo không biết ai đã báo cáo họ.
            </DialogDescription>
          )}
        </DialogHeader>

        {done ? (
          <p role="status" data-testid="report-thanks">
            {REPORT_THANKS}
          </p>
        ) : (
          <form
            id="report-form"
            className="flex flex-col gap-4"
            onSubmit={(e) => {
              e.preventDefault()
              void submit()
            }}
          >
            <FormAlert message={formError} />
            <Field data-invalid={fields.reasonCode ? true : undefined}>
              <FieldTitle id="report-reason-label">Lý do</FieldTitle>
              <RadioGroup
                value={reason}
                aria-labelledby="report-reason-label"
                onValueChange={(value) => {
                  setReason(value as ReasonCode)
                  setFields((f) => ({ ...f, reasonCode: undefined }))
                }}
              >
                {REASON_CODES.map((code) => (
                  <FieldLabel key={code} htmlFor={`report-reason-${code}`}>
                    <Field orientation="horizontal">
                      <RadioGroupItem
                        id={`report-reason-${code}`}
                        value={code}
                        disabled={pending}
                      />
                      <FieldTitle>{REASON_LABEL[code]}</FieldTitle>
                    </Field>
                  </FieldLabel>
                ))}
              </RadioGroup>
              {fields.reasonCode && <FieldError>{fields.reasonCode}</FieldError>}
            </Field>

            <TextareaField
              label={reason === "other" ? "Mô tả (bắt buộc)" : "Mô tả thêm (không bắt buộc)"}
              value={detail}
              rows={3}
              disabled={pending}
              error={fields.detail}
              onChange={(e) => {
                setDetail(e.target.value)
                setFields((f) => ({ ...f, detail: undefined }))
              }}
            />
          </form>
        )}

        <DialogFooter>
          {done ? (
            <Button onClick={() => setOpen(false)}>Đóng</Button>
          ) : (
            <Button
              type="submit"
              form="report-form"
              disabled={pending}
              aria-busy={pending || undefined}
            >
              {pending && <Spinner data-icon="inline-start" aria-hidden />}
              Gửi báo cáo
            </Button>
          )}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
