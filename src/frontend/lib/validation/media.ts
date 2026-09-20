// Validation client cho MỘT file ảnh sắp tải lên (Đ-E5). Ngưỡng và thông điệp chép ĐÚNG từ
// `CreateUploadsRequestValidator` (`FileNotAllowed` + `IsAllowed`) — client nới hơn hoặc bằng server,
// không bao giờ chặt hơn.
//
// Vì sao kiểm ở client dù server đã kiểm: bước này chặn một vòng `POST /media/uploads` + một vòng `PUT`
// lên R2 cho một file chắc chắn hỏng. Nó KHÔNG thay server kiểm — 400 từ server vẫn hiện theo key
// `errors.files` (Đ-E5), kể cả khi client đã kiểm qua.
//
// E3 dùng cho avatar (một ảnh), E4 dùng lại cho từng ảnh của composer.

import type { ImageContentType } from "@/lib/api/types"

/**
 * Allowlist Đ-2.8, đúng bằng `MediaAttachment.AllowedContentTypes` và CHECK `ck_media_content_type`.
 * `satisfies` gắn vào kiểu sinh từ hợp đồng: thêm loại ở `.yaml` mà quên ở đây thì đỏ compile.
 */
export const ACCEPTED_IMAGE_TYPES = [
  "image/jpeg",
  "image/png",
  "image/webp",
] as const satisfies readonly ImageContentType[]

/** 10 MB — cùng con số với `MediaAttachment.MaxSizeBytes` và CHECK `ck_media_size`. */
export const MAX_IMAGE_BYTES = 10 * 1024 * 1024

/**
 * MỘT câu cho cả "sai loại" lẫn "quá nặng" lẫn "file rỗng", chép nguyên
 * `CreateUploadsRequestValidator.FileNotAllowed`. Gộp ba nhánh là quyết định của server (nó cố ý không
 * nói file thứ mấy sai); client nói khác đi là một lỗi hiện hai cách nói.
 */
export const IMAGE_NOT_ALLOWED =
  "Chỉ nhận ảnh JPEG, PNG hoặc WebP, tối đa 10 MB mỗi ảnh."

/** Thu hẹp `string` của `File.type` về kiểu hợp đồng — chỗ DUY NHẤT được phép ép kiểu đó. */
export function isImageContentType(value: string): value is ImageContentType {
  return (ACCEPTED_IMAGE_TYPES as readonly string[]).includes(value)
}

/**
 * `undefined` nghĩa là gửi được. Nhận `{ type, size }` chứ không nhận `File` để test không phải dựng
 * `File` thật cho mọi ngưỡng.
 *
 * `size === 0` bị chặn ở đây vì server chặn (`sizeBytes is >= 1`): object rỗng không phải ảnh, và để lọt
 * xuống thì người dùng nhận 400 sau hai vòng mạng thay vì một câu ngay tại chỗ.
 */
export function imageFileError(file: {
  type: string
  size: number
}): string | undefined {
  if (!isImageContentType(file.type)) return IMAGE_NOT_ALLOWED
  if (file.size < 1 || file.size > MAX_IMAGE_BYTES) return IMAGE_NOT_ALLOWED
  return undefined
}

/**
 * Giá trị cho `accept` của `<input type="file">`. Chỉ là GỢI Ý cho hộp thoại chọn file của hệ điều hành —
 * người dùng vẫn chọn được file khác (macOS cho gõ thẳng tên, kéo-thả bỏ qua `accept`), nên
 * `imageFileError` vẫn phải chạy sau khi chọn.
 */
export const IMAGE_ACCEPT_ATTRIBUTE = ACCEPTED_IMAGE_TYPES.join(",")
