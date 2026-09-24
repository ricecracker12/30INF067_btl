// FR-007 phía client cho ô bình luận (Đ-3.14, Đ-E5). Ngưỡng và thông điệp chép ĐÚNG `CommentPolicy` của server — client
// nới hơn hoặc bằng server, không bao giờ chặt hơn, và một lỗi không hiện hai cách nói.
//
// Đo `.length` (UTF-16) như `string.Length` của .NET — chép nguyên cách của `post.ts`: không đếm byte, không đếm code
// point, KHÔNG trim trước khi đo. Chuỗi toàn khoảng trắng thì là rỗng. Lưu nguyên văn — xuống dòng người dùng gõ là nội dung.

/** Bằng đúng `CommentPolicy.MaxBodyLength` và `varchar(1000)` của cột. */
export const MAX_COMMENT_LENGTH = 1000

/** Chép nguyên `CommentPolicy.Empty`. */
export const COMMENT_EMPTY = "Bình luận không được để trống."

/** Chép nguyên `CommentPolicy.BodyTooLong`. */
export const COMMENT_TOO_LONG = `Bình luận không được vượt quá ${MAX_COMMENT_LENGTH} ký tự.`

/** Cùng thứ tự với server: rỗng trước, dài sau. `undefined` = hợp lệ. */
export function commentBodyError(body: string): string | undefined {
  if (body.trim() === "") return COMMENT_EMPTY
  if (body.length > MAX_COMMENT_LENGTH) return COMMENT_TOO_LONG
  return undefined
}
