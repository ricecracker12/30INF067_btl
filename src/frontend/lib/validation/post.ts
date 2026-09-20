// BR-01 phía client cho màn soạn bài (Đ-E5). Ngưỡng, THỨ TỰ KIỂM và thông điệp chép đúng
// `PostContentPolicy.Validate` + `CreatePostRequestValidator` — client nới hơn hoặc bằng server, không
// bao giờ chặt hơn.
//
// Luật của MỘT FILE ẢNH (loại, dung lượng) không nằm ở đây mà ở `lib/validation/media.ts`: nó là luật của
// một file, không phải của một bài, và avatar (E3) cần đúng nó mà không cần ba luật dưới đây.
//
// Đo `.length` (UTF-16) như `string.Length` của .NET, KHÔNG đếm byte và KHÔNG trim trước khi đo — khác
// mật khẩu của GĐ1, nơi server đếm byte UTF-8.

import type { PostPrivacy } from "@/lib/api/types"

import type { FieldErrors } from "./auth"

/** Bằng đúng `PostContentPolicy.MaxBodyLength` và `varchar(5000)` của cột. */
export const MAX_BODY_LENGTH = 5000

/** Bằng đúng `PostContentPolicy.MaxMediaCount` và CHECK `ck_posts_media_count`. */
export const MAX_MEDIA_COUNT = 10

/** Đúng ba key hợp đồng trả trong `errors`, và đúng ba ô người dùng đang đứng. */
export type PostField = "body" | "mediaKeys" | "privacy"

export const BODY_TOO_LONG = `Nội dung bài không được vượt quá ${MAX_BODY_LENGTH} ký tự.`
export const TOO_MANY_MEDIA = `Một bài chỉ được đính kèm tối đa ${MAX_MEDIA_COUNT} ảnh.`
export const POST_EMPTY = "Bài đăng phải có nội dung hoặc ít nhất một ảnh."
/** Chép nguyên `CreatePostRequestValidator.PrivacyRequired`. */
export const PRIVACY_REQUIRED = "Mức riêng tư là bắt buộc."

/**
 * Ba mệnh đề BR-01, trả về ĐÚNG MỘT key — cùng quyết định với `PostContentPolicy.Validate`, và vì cùng
 * thứ tự nên client và server không bao giờ chỉ vào hai ô khác nhau cho cùng một bài.
 *
 * **Ảnh trước, chữ sau.** 11 ảnh không kèm chữ thỏa mệnh đề "không rỗng", nên kiểm chữ trước sẽ ra
 * `body` — sai ô. Mệnh đề "rỗng cả chữ lẫn ảnh" lại mang key `body` chứ không phải `mediaKeys`: người
 * dùng đang ở ô soạn chữ, và đường thoát dễ nhất là gõ một chữ.
 */
export function postContentError(
  body: string,
  mediaCount: number
): { field: "body" | "mediaKeys"; message: string } | undefined {
  if (mediaCount > MAX_MEDIA_COUNT)
    return { field: "mediaKeys", message: TOO_MANY_MEDIA }

  if (body.length > MAX_BODY_LENGTH)
    return { field: "body", message: BODY_TOO_LONG }

  if (mediaCount === 0 && body.trim() === "")
    return { field: "body", message: POST_EMPTY }

  return undefined
}

/**
 * Toàn bộ những gì client kiểm được trước khi gửi — BR-01 cộng `privacy`, KHÔNG thêm luật nào server
 * không có (Đ-E5: chặt hơn là chặn nhầm người dùng hợp lệ mà không có đường thoát).
 *
 * `privacy === null` là "chưa chọn". Kiểm ở client vì server cũng bắt buộc (`NotNull`, cùng câu), không
 * phải vì FE tự đặt thêm luật; và khi gửi thì KHÔNG tự điền mặc định nào (E4 bước 5).
 *
 * `mediaCount` là số ảnh ĐÃ ĐÍNH KÈM trong composer, kể cả ảnh đang lên — quá 10 phải kêu ngay lúc chọn,
 * không đợi tải xong mười một lượt rồi mới nói.
 */
export function validatePost(input: {
  body: string
  mediaCount: number
  privacy: PostPrivacy | null
}): FieldErrors<PostField> {
  const errors: FieldErrors<PostField> = {}

  const content = postContentError(input.body, input.mediaCount)
  if (content) errors[content.field] = content.message

  if (input.privacy === null) errors.privacy = PRIVACY_REQUIRED

  return errors
}

/**
 * Giá trị `body` gửi lên. Chép `PostService.NormalizeBody`: toàn khoảng trắng thành `null`, còn lại giữ
 * NGUYÊN chuỗi thô — không trim. Bài chỉ có ảnh gửi `null` chứ không gửi `""`: cột là `varchar` nullable
 * và `ck_posts_not_empty` đọc `NULL`, chuỗi rỗng thì lọt CHECK mà không mang nghĩa gì.
 */
export function bodyToSend(raw: string): string | null {
  return raw.trim() === "" ? null : raw
}
