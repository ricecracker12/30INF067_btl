// Kiểm phía client cho ô chữ của GĐ6 (Đ-E5): ngưỡng và câu chép ĐÚNG validator của server — client nới hơn hoặc bằng, không bao
// giờ chặt hơn, và một lỗi không hiện hai cách nói. Đo sau `trim` (server coi chuỗi toàn khoảng trắng là vắng mặt và đo sau trim):
// đo trước trim là CHẶT hơn server với chuỗi có khoảng trắng hai đầu.
//
// Nguồn: `CreateReportRequestValidator`, `DecideReportRequestValidator` (Moderation), `AccountChange`, `RoleModels` (Identity).

/** `detail` của báo cáo, `note` của quyết định/khôi phục, `reason` của khóa tài khoản — cùng trần 500. */
export const MAX_MODERATION_TEXT = 500

/** Chép `CreateReportRequestValidator.ReasonCodeInvalid` — server nói câu này cả khi thiếu lý do. */
export const REPORT_REASON_INVALID = "Lý do báo cáo không hợp lệ."
export const REPORT_DETAIL_REQUIRED = "Vui lòng mô tả lý do."
export const REPORT_DETAIL_TOO_LONG = "Mô tả tối đa 500 ký tự."
export const DECISION_NOTE_REQUIRED = "Vui lòng ghi chú cách đã xử lý."
export const DECISION_NOTE_TOO_LONG = "Ghi chú tối đa 500 ký tự."
export const LOCK_REASON_REQUIRED = "Lý do khóa là bắt buộc."
export const LOCK_REASON_TOO_LONG = "Lý do khóa tối đa 500 ký tự."

/** `^[A-Z][A-Z0-9_]{2,29}$` — schema `RoleCode` (Đ-6.9). */
export const ROLE_CODE_PATTERN = /^[A-Z][A-Z0-9_]{2,29}$/
export const ROLE_CODE_INVALID =
  "Mã vai trò gồm 3–30 ký tự A–Z, 0–9, _ và bắt đầu bằng chữ cái in hoa."
export const MAX_ROLE_DISPLAY_NAME = 50
export const ROLE_DISPLAY_NAME_INVALID = `Tên hiển thị phải từ 1 đến ${MAX_ROLE_DISPLAY_NAME} ký tự.`

/** Chữ bắt buộc hay tùy chọn, trần 500 sau trim. `undefined` = hợp lệ. */
function text(
  value: string,
  required: boolean,
  missing: string,
  tooLong: string
): string | undefined {
  const t = value.trim()
  if (required && t === "") return missing
  if (t.length > MAX_MODERATION_TEXT) return tooLong
  return undefined
}

/** `detail` bắt buộc khi lý do là `other` (Đ-6.12). */
export const reportDetailError = (detail: string, isOther: boolean) =>
  text(detail, isOther, REPORT_DETAIL_REQUIRED, REPORT_DETAIL_TOO_LONG)

/** `note` bắt buộc khi quyết định là `resolve` (Đ-6.13). */
export const decisionNoteError = (note: string, required: boolean) =>
  text(note, required, DECISION_NOTE_REQUIRED, DECISION_NOTE_TOO_LONG)

export const lockReasonError = (reason: string) =>
  text(reason, true, LOCK_REASON_REQUIRED, LOCK_REASON_TOO_LONG)

export function roleCodeError(code: string): string | undefined {
  return ROLE_CODE_PATTERN.test(code) ? undefined : ROLE_CODE_INVALID
}

export function roleDisplayNameError(name: string): string | undefined {
  const t = name.trim()
  return t.length >= 1 && t.length <= MAX_ROLE_DISPLAY_NAME
    ? undefined
    : ROLE_DISPLAY_NAME_INVALID
}

/** Chuỗi rỗng sau trim → không gửi trường (hợp đồng: `detail`/`note` tùy chọn). */
export function optionalText(value: string): string | undefined {
  const t = value.trim()
  return t === "" ? undefined : t
}
