// Chỗ DUY NHẤT trong code trình duyệt gọi ra NGOÀI origin: `PUT` thẳng lên R2 bằng URL đã ký
// (Đ-2.5, Đ-E17). Mọi lời gọi khác đi qua `lib/api/http.ts` tới BFF cùng origin (Đ-E14).
//
// Vì sao `XMLHttpRequest` chứ không `fetch`: `fetch` không báo được tiến trình UPLOAD (`ReadableStream`
// cho request body chưa dùng được rộng rãi, và Chrome đòi HTTP/2 + `duplex: 'half'`). E4 cần thanh tiến
// trình cho tối đa 10 ảnh; E3 chưa cần nhưng viết ở đây một lần để E4 dùng lại (Mục 17 — GĐ5 cũng thừa hưởng).
// Luật ESLint chặn `XMLHttpRequest` ở mọi nơi khác (Q-E3), override tắt đúng cho `lib/upload/**`.
//
// Module này KHÔNG biết nghiệp vụ (Đ-E13): không import `features/`, không biết avatar hay bài đăng,
// không dịch lỗi thành câu cho người dùng — chỗ gọi làm việc đó vì mỗi màn nói một câu khác.

/**
 * R2 trả lời nhưng từ chối: chữ ký sai/hết hạn (403), key sai (400), bucket lạ (404)… Kèm `status` để
 * người sửa biết nhìn vào đâu.
 *
 * `url` KHÔNG bao giờ vào thông điệp: nó mang `X-Amz-Signature`, và thông điệp lỗi đi thẳng vào console,
 * vào ảnh chụp màn hình dán vào PR (Mục 1.2 luật 7).
 */
export class R2RejectedError extends Error {
  constructor(readonly status: number) {
    super(`R2 từ chối lượt tải lên (HTTP ${status}).`)
    this.name = "R2RejectedError"
  }
}

/**
 * Không có phản hồi nào: mất mạng, **CORS sai trên bucket**, hoặc **CSP chặn** (`connect-src` thiếu host
 * R2 — Đ-E17). Đây là ca ISS-02, và trình duyệt cố ý không nói rõ cái nào trong ba — đó là lý do bước
 * `PUT` phải có câu lỗi RIÊNG, không gộp với lỗi của hai endpoint hai đầu (Mục 4 bảng ba trạng thái).
 */
export class R2NetworkError extends Error {
  constructor() {
    super("Không gọi được tới R2 (mạng, CORS hoặc CSP).")
    this.name = "R2NetworkError"
  }
}

export type PutToR2Options = {
  /** Presigned `PUT` lấy từ `UploadTicket.uploadUrl`. Không log, không lưu. */
  url: string
  /** Chính `File` người dùng chọn — GĐ2 không cắt/nén/strip EXIF (hoãn tới GĐ7). */
  file: Blob
  /** Lấy từ `UploadTicket.requiredHeaders["Content-Type"]` — giá trị ĐÃ NẰM TRONG chữ ký. */
  contentType: string
  signal?: AbortSignal
  /** Chỉ gọi khi trình duyệt biết tổng dung lượng. E3 không dùng; E4 vẽ thanh tiến trình từ đây. */
  onProgress?: (loaded: number, total: number) => void
}

/**
 * Ba kết cục, phân biệt được từ ngoài:
 *
 * - 2xx → resolve.
 * - status ≠ 2xx → `R2RejectedError` kèm status.
 * - `error`/`timeout` → `R2NetworkError`.
 *
 * Hủy (`signal`) ném `AbortError` nguyên vẹn, cùng giao kèo với `request()` của `lib/api/http.ts`: chỗ gọi
 * phân biệt "mình tự hủy" với "hỏng thật" bằng `error.name`.
 */
export function putToR2({
  url,
  file,
  contentType,
  signal,
  onProgress,
}: PutToR2Options): Promise<void> {
  return new Promise((resolve, reject) => {
    if (signal?.aborted) {
      reject(abortError())
      return
    }

    // Đ-2.5/Đ-E17: đây LÀ chỗ được phép gọi ra ngoài origin. Không có `eslint-disable` ở dòng này —
    // ngoại lệ nằm ở override `lib/upload/**` trong `eslint.config.mjs` (Q-E3), nên một directive tại
    // chỗ sẽ là directive thừa và chính ESLint kêu. Chuyển file này ra khỏi `lib/upload/` là lint đỏ.
    const xhr = new XMLHttpRequest()
    xhr.open("PUT", url)

    // ĐÚNG BẰNG `requiredHeaders` — không thêm gì. `AllowedHeaders` của bucket chỉ có `content-type`, nên
    // một header lạ (`x-requested-with`, `authorization`) làm preflight đỏ với thông điệp không liên quan.
    // `Content-Length` KHÔNG đặt tay: trình duyệt chặn header đó và tự đặt từ body; giá trị trong
    // `requiredHeaders["Content-Length"]` là để ĐỐI CHIẾU, không phải để gửi.
    xhr.setRequestHeader("Content-Type", contentType)

    // Mặc định đã là `false`; viết ra để lần sau không ai "sửa cho chắc". R2 không đặt
    // `Access-Control-Allow-Credentials`, nên gửi kèm cookie là preflight đỏ — và cookie phiên của app
    // không có việc gì ở một origin khác.
    xhr.withCredentials = false

    const done = () => {
      signal?.removeEventListener("abort", onAbort)
    }
    const onAbort = () => {
      xhr.abort()
    }
    signal?.addEventListener("abort", onAbort)

    if (onProgress) {
      xhr.upload.onprogress = (event) => {
        // `lengthComputable` sai khi body là stream không biết dài bao nhiêu — không đoán, chỉ bỏ qua.
        if (event.lengthComputable) onProgress(event.loaded, event.total)
      }
    }

    xhr.onload = () => {
      done()
      if (xhr.status >= 200 && xhr.status < 300) resolve()
      else reject(new R2RejectedError(xhr.status))
    }
    // `onerror` và `ontimeout` là CÙNG một tình huống với người dùng: không có phản hồi nào để đọc.
    xhr.onerror = () => {
      done()
      reject(new R2NetworkError())
    }
    xhr.ontimeout = () => {
      done()
      reject(new R2NetworkError())
    }
    xhr.onabort = () => {
      done()
      reject(abortError())
    }

    xhr.send(file)
  })
}

/**
 * Cùng hình dạng với thứ `fetch` ném khi `AbortSignal` bắn: `DOMException` tên `AbortError`. Chỗ gọi đã
 * có sẵn nhánh `error.name === "AbortError"` cho `request()` — hai đường hủy không nên khác nhau.
 */
function abortError(): DOMException {
  return new DOMException("Đã hủy lượt tải lên.", "AbortError")
}
