"use client"

import Link from "next/link"
import { useState, type ChangeEvent, type FormEvent } from "react"

import { FormAlert } from "@/components/form/form-alert"
import { Alert, AlertDescription } from "@/components/ui/alert"
import { Button } from "@/components/ui/button"
import {
  Field,
  FieldContent,
  FieldDescription,
  FieldError,
  FieldLabel,
  FieldTitle,
} from "@/components/ui/field"
import { Progress } from "@/components/ui/progress"
import { RadioGroup, RadioGroupItem } from "@/components/ui/radio-group"
import { Spinner } from "@/components/ui/spinner"
import { Textarea } from "@/components/ui/textarea"
import { contentApi } from "@/lib/api/content-api"
import { errorMessage, validationErrors } from "@/lib/api/messages"
import { ApiError } from "@/lib/api/problem"
import type { PostPrivacy, PostResponse } from "@/lib/api/types"
import type { FieldErrors } from "@/lib/validation/auth"
import { IMAGE_ACCEPT_ATTRIBUTE, MAX_IMAGE_BYTES } from "@/lib/validation/media"
import {
  MAX_BODY_LENGTH,
  MAX_MEDIA_COUNT,
  bodyToSend,
  validatePost,
  type PostField,
} from "@/lib/validation/post"

import { PRIVACY_DESCRIPTION, PRIVACY_LABEL, PRIVACY_ORDER } from "./privacy"
import { useUploadQueue, type UploadItem } from "./use-upload-queue"

// Màn soạn bài (E4) — bước 1–5 của SEQ-01 trên trình duyệt. Composer KHÔNG tự biết cách tải ảnh: hàng đợi
// nằm trong `use-upload-queue.ts`, ở đây chỉ còn form và cách hiện bốn trạng thái của từng dòng ảnh.
//
// Ba chỗ hiện lỗi, cố ý tách — chúng sửa bằng ba việc khác nhau:
// - dưới ô chọn ảnh: lỗi của CHÍNH LƯỢT CHỌN (sai loại, quá nặng, quá 10 ảnh) — chặn ở client, chưa gọi API;
// - trên từng dòng ảnh: lỗi của MỘT ảnh (presign hoặc `PUT`) — chỉ ảnh đó có nút "Thử lại";
// - `FormAlert` + lỗi dưới từng ô: lỗi của `POST /posts` — cả bài, sau khi mọi ảnh đã xong.

const FIELDS = [
  "body",
  "mediaKeys",
  "privacy",
] as const satisfies readonly PostField[]

const MAX_IMAGE_MB = MAX_IMAGE_BYTES / (1024 * 1024)

export function PostComposer() {
  const queue = useUploadQueue()

  const [body, setBody] = useState("")
  // `null` = CHƯA CHỌN, không phải "mặc định công khai". Không đặt sẵn giá trị nào: bài đăng nhầm mức
  // riêng tư không rút lại được, và một mặc định im lặng là thứ người dùng không bao giờ thấy mình đã chọn.
  const [privacy, setPrivacy] = useState<PostPrivacy | null>(null)
  const [fieldErrors, setFieldErrors] = useState<FieldErrors<PostField>>({})
  const [formError, setFormError] = useState<string | null>(null)
  const [pending, setPending] = useState(false)
  const [created, setCreated] = useState<PostResponse | null>(null)

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    // Cờ `pending` chặn gửi đôi: `POST /posts` KHÔNG idempotent, và lần hai nhận 409 vì chính bài vừa tạo
    // đã giữ những `mediaKey` đó (BR-03).
    if (pending) return

    setFormError(null)
    setCreated(null)

    const clientErrors = validatePost({
      body,
      mediaCount: queue.items.length,
      privacy,
    })
    setFieldErrors(clientErrors)
    if (Object.keys(clientErrors).length > 0) return
    // `validatePost` đã bắt `null` ở trên; dòng này chỉ để thu hẹp kiểu cho lời gọi bên dưới.
    if (privacy === null) return

    setPending(true)
    try {
      const post = await contentApi.create({
        body: bodyToSend(body),
        privacy,
        // Chỉ ảnh `xong` mới có mặt — nút Đăng đã tắt khi còn ảnh dở, nhưng `mediaKeys()` vẫn lọc lại:
        // gửi key của ảnh chưa lên là 400 "Ảnh chưa được tải lên xong…" (server `HEAD` không thấy object).
        mediaKeys: queue.mediaKeys(),
      })
      setCreated(post)
      setBody("")
      setPrivacy(null)
      queue.reset()
    } catch (error) {
      // Mọi 400 hiển thị theo key của `errors` — KỂ CẢ khi client đã kiểm qua (Đ-E5): server là bên quyết
      // định, và nó có những câu client không có (trùng ảnh, ảnh chưa lên xong, khóa sai dạng).
      if (error instanceof ApiError && error.status === 400) {
        const { fields, formMessage } = validationErrors(error, FIELDS)
        setFieldErrors(fields)
        setFormError(formMessage)
      } else {
        setFormError(errorMessage("post-create", error))
      }
    } finally {
      setPending(false)
    }
  }

  function onPick(event: ChangeEvent<HTMLInputElement>) {
    const files = Array.from(event.target.files ?? [])
    // Mở hộp thoại rồi bấm Hủy: `files` rỗng, không phải lỗi.
    if (files.length === 0) return

    // Cho chọn LẠI ĐÚNG những file vừa chọn sau khi bị từ chối: `input type=file` không bắn `change` khi
    // giá trị không đổi.
    event.target.value = ""

    setFieldErrors((prev) => ({
      ...prev,
      mediaKeys: undefined,
      body: undefined,
    }))
    void queue.addFiles(files)
  }

  function clearFieldError(field: PostField) {
    if (fieldErrors[field]) {
      setFieldErrors((prev) => ({ ...prev, [field]: undefined }))
    }
  }

  return (
    // noValidate: thông điệp lấy từ Đ-E5, không để trình duyệt tự hiện câu tiếng Anh.
    <form noValidate onSubmit={onSubmit} className="flex flex-col gap-6">
      <FormAlert message={formError} />
      {created && <CreatedAlert post={created} />}

      <Field data-invalid={fieldErrors.body ? true : undefined}>
        <FieldLabel htmlFor="post-body">Nội dung</FieldLabel>
        <Textarea
          id="post-body"
          name="body"
          rows={5}
          placeholder="Bạn đang nghĩ gì?"
          value={body}
          aria-invalid={fieldErrors.body ? true : undefined}
          aria-describedby={fieldErrors.body ? "post-body-error" : undefined}
          onChange={(e) => {
            setBody(e.target.value)
            clearFieldError("body")
          }}
        />
        {/* Đếm để người dùng thấy giới hạn TRƯỚC khi bị chặn — không phải luật mới, chỉ là hiển thị. */}
        <p className="text-sm text-muted-foreground">
          {body.length}/{MAX_BODY_LENGTH}
        </p>
        {fieldErrors.body && (
          <FieldError id="post-body-error">{fieldErrors.body}</FieldError>
        )}
      </Field>

      <MediaPicker
        queue={queue}
        listError={fieldErrors.mediaKeys}
        disabled={pending}
        onPick={onPick}
      />

      <Field data-invalid={fieldErrors.privacy ? true : undefined}>
        {/* `FieldLabel` là `<label>` thật, mà `role="radiogroup"` không gắn được vào một `<label>` —
            tên nhóm đi qua `aria-labelledby` để trình đọc màn hình đọc "Mức riêng tư" trước ba lựa chọn. */}
        <FieldTitle id="post-privacy-label">Mức riêng tư</FieldTitle>
        <RadioGroup
          value={privacy}
          aria-labelledby="post-privacy-label"
          aria-invalid={fieldErrors.privacy ? true : undefined}
          aria-describedby={
            fieldErrors.privacy ? "post-privacy-error" : undefined
          }
          onValueChange={(value) => {
            setPrivacy(value as PostPrivacy)
            clearFieldError("privacy")
          }}
        >
          {PRIVACY_ORDER.map((value) => (
            <FieldLabel key={value} htmlFor={`privacy-${value}`}>
              <Field orientation="horizontal">
                <FieldContent>
                  <FieldTitle>{PRIVACY_LABEL[value]}</FieldTitle>
                  <FieldDescription>
                    {PRIVACY_DESCRIPTION[value]}
                  </FieldDescription>
                </FieldContent>
                <RadioGroupItem
                  id={`privacy-${value}`}
                  value={value}
                  disabled={pending}
                />
              </Field>
            </FieldLabel>
          ))}
        </RadioGroup>
        {fieldErrors.privacy && (
          <FieldError id="post-privacy-error">{fieldErrors.privacy}</FieldError>
        )}
      </Field>

      <Button
        type="submit"
        // Nút Đăng chỉ bật khi MỌI ảnh `xong`: gửi lúc còn ảnh đang lên là 400 "Ảnh chưa được tải lên
        // xong…", mà người dùng không làm gì sai — họ chỉ bấm sớm.
        disabled={pending || !queue.allDone}
        aria-busy={pending || undefined}
      >
        {pending && <Spinner data-icon="inline-start" aria-hidden />}
        Đăng bài
      </Button>
    </form>
  )
}

/**
 * Ô chọn ảnh cộng danh sách trạng thái. `listError` là lỗi `errors.mediaKeys` do SERVER trả cho cả bài
 * (trùng ảnh, khóa sai dạng); `queue.fileError` là lỗi của lượt chọn, chặn ở client.
 */
function MediaPicker({
  queue,
  listError,
  disabled,
  onPick,
}: {
  queue: ReturnType<typeof useUploadQueue>
  listError?: string
  disabled: boolean
  onPick: (event: ChangeEvent<HTMLInputElement>) => void
}) {
  const error = queue.fileError ?? listError

  return (
    <Field data-invalid={error ? true : undefined}>
      <FieldLabel htmlFor="post-media">
        Ảnh (tối đa {MAX_MEDIA_COUNT})
      </FieldLabel>
      <input
        id="post-media"
        name="media"
        type="file"
        multiple
        // Chỉ là gợi ý cho hộp thoại của hệ điều hành — `imageFileError` trong hàng đợi mới là chỗ chặn thật.
        accept={IMAGE_ACCEPT_ATTRIBUTE}
        disabled={disabled || queue.items.length >= MAX_MEDIA_COUNT}
        aria-invalid={error ? true : undefined}
        aria-describedby={error ? "post-media-error" : undefined}
        onChange={onPick}
        className="text-sm file:mr-4 file:rounded-md file:border file:border-border file:bg-secondary file:px-3 file:py-1.5 file:text-sm file:text-secondary-foreground"
      />
      <FieldDescription>
        JPEG, PNG hoặc WebP, tối đa {MAX_IMAGE_MB} MB mỗi ảnh. Ảnh được tải lên
        ngay khi chọn; thứ tự trong danh sách là thứ tự hiển thị.
      </FieldDescription>
      {error && <FieldError id="post-media-error">{error}</FieldError>}

      {queue.items.length > 0 && (
        <ul className="flex flex-col gap-3" data-testid="upload-queue">
          {queue.items.map((item) => (
            <UploadRow
              key={item.id}
              item={item}
              disabled={disabled}
              onRetry={() => void queue.retryItem(item.id)}
              onRemove={() => queue.removeItem(item.id)}
            />
          ))}
        </ul>
      )}
    </Field>
  )
}

const STATUS_LABEL: Record<UploadItem["status"], string> = {
  cho: "chờ",
  "dang-gui": "đang gửi",
  xong: "xong",
  loi: "lỗi",
}

/**
 * Một dòng ảnh. Nút "Thử lại" chỉ có ở dòng `loi` — đó là cả điểm của hàng đợi: một ảnh hỏng không kéo
 * chín ảnh kia theo, và người dùng không phải chọn lại cả lô vì một lượt `PUT` rớt mạng.
 */
function UploadRow({
  item,
  disabled,
  onRetry,
  onRemove,
}: {
  item: UploadItem
  disabled: boolean
  onRetry: () => void
  onRemove: () => void
}) {
  const status =
    item.status === "dang-gui"
      ? `${STATUS_LABEL["dang-gui"]} (${item.percent}%)`
      : STATUS_LABEL[item.status]

  return (
    <li
      className="flex flex-col gap-2 rounded-md border border-border p-3"
      data-testid="upload-row"
      data-status={item.status}
    >
      <div className="flex items-center justify-between gap-3">
        <span className="truncate text-sm">{item.file.name}</span>
        <span className="shrink-0 text-sm text-muted-foreground" role="status">
          {status}
        </span>
      </div>

      {item.status === "dang-gui" && (
        <Progress
          value={item.percent}
          aria-label={`Tiến trình tải ${item.file.name}`}
        />
      )}

      {item.status === "loi" && item.error && (
        <p className="text-sm text-destructive">{item.error}</p>
      )}

      <div className="flex gap-2">
        {item.status === "loi" && (
          <Button
            type="button"
            variant="outline"
            size="sm"
            disabled={disabled}
            onClick={onRetry}
          >
            Thử lại
          </Button>
        )}
        <Button
          type="button"
          variant="ghost"
          size="sm"
          disabled={disabled}
          onClick={onRemove}
        >
          Bỏ ảnh
        </Button>
      </div>
    </li>
  )
}

/**
 * Bài vừa đăng. Composer ở lại màn thay vì chuyển sang bài: trang chi tiết `/posts/{postId}` là việc của
 * E5, và đưa người dùng tới một route chưa tồn tại là đổi một lỗi 400 lấy một trang 404.
 */
function CreatedAlert({ post }: { post: PostResponse }) {
  return (
    <Alert data-testid="post-created">
      <AlertDescription>
        Đã đăng bài.{" "}
        <Link href={`/posts/${post.postId}`} className="underline">
          Xem bài
        </Link>
      </AlertDescription>
    </Alert>
  )
}
