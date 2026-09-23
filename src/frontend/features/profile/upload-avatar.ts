import { contentApi } from "@/lib/api/content-api"
import { fieldMessage, R2_PUT_FAILED } from "@/lib/api/messages"
import { profileApi } from "@/lib/api/profile-api"
import type { ImageContentType, ProfileResponse } from "@/lib/api/types"
import { putToR2 } from "@/lib/upload/r2"

// Luồng ba bước của E3 (SEQ-01 thu gọn cho avatar), tách khỏi component để test được mà không render:
//
//   1. POST /media/uploads { purpose: "avatar", files: [một file] }  → ticket
//   2. PUT  uploadUrl, body = File, header Content-Type = requiredHeaders["Content-Type"]
//   3. PUT  /users/me/avatar { mediaKey }                            → ProfileResponse có avatarUrl mới
//
// Ba bước hỏng vì BA NGUYÊN NHÂN KHÔNG LIÊN QUAN GÌ NHAU, nên mỗi bước một câu riêng — gộp thành "tải ảnh
// thất bại" là tự bịt đường sửa: bước 1 là quyền/hạn mức, bước 2 là CORS/chữ ký/CSP (ISS-02), bước 3 là
// hợp đồng (key sai dạng, key của người khác).

export type AvatarStep = "presign" | "put" | "attach"

/** Lỗi đã kèm sẵn câu cho người dùng; `step` để chỗ gọi hiện đúng chỗ và để người sửa biết nhìn vào đâu. */
export class AvatarUploadError extends Error {
  constructor(
    readonly step: AvatarStep,
    message: string,
    override readonly cause: unknown
  ) {
    super(message)
    this.name = "AvatarUploadError"
  }
}

/**
 * Một ảnh, ba bước, trả về hồ sơ mới. `signal` hủy được ở bước 2 (bước nặng nhất); hủy ném `AbortError`
 * nguyên vẹn, KHÔNG bọc thành `AvatarUploadError` — chỗ gọi không hiện lỗi cho thứ chính nó vừa hủy.
 *
 * `contentType` lấy từ `file.type`, KHÔNG đoán từ đuôi tên: giá trị khai lúc presign được ký vào URL, lệch
 * một ký tự là R2 trả 403 SignatureDoesNotMatch — trông hệt CORS sai.
 */
export async function uploadAvatar(
  file: File,
  signal?: AbortSignal
): Promise<ProfileResponse> {
  let ticket
  try {
    const tickets = await contentApi.createUploads({
      purpose: "avatar",
      files: [
        {
          // Đã qua `imageFileError` ở chỗ gọi nên `file.type` chắc chắn thuộc allowlist. Ép kiểu ở đây là
          // thu hẹp `string` → kiểu hợp đồng, không phải bỏ qua kiểm tra.
          contentType: file.type as ImageContentType,
          sizeBytes: file.size,
        },
      ],
    })
    // Hợp đồng: một ticket cho mỗi file. Mảng rỗng là hợp đồng vỡ, không phải ca người dùng — vẫn phải
    // ra một câu, vì `tickets[0]` không kiểm là `undefined` lọt xuống bước 2 thành `TypeError` khó đọc.
    ticket = tickets[0]
    if (!ticket) throw new Error("Presign trả về danh sách ticket rỗng.")
  } catch (error) {
    throw new AvatarUploadError(
      "presign",
      fieldMessage(error, "files", "upload"),
      error
    )
  }

  try {
    await putToR2({
      url: ticket.uploadUrl,
      file,
      // Giá trị ĐÃ NẰM TRONG chữ ký — dùng lại cái server ký, không dựng lại từ `file.type`.
      contentType: ticket.requiredHeaders["Content-Type"],
      signal,
    })
  } catch (error) {
    if (error instanceof Error && error.name === "AbortError") throw error
    throw new AvatarUploadError("put", R2_PUT_FAILED, error)
  }

  try {
    return await profileApi.setAvatar({ mediaKey: ticket.mediaKey })
  } catch (error) {
    throw new AvatarUploadError(
      "attach",
      fieldMessage(error, "mediaKey", "avatar"),
      error
    )
  }
}
