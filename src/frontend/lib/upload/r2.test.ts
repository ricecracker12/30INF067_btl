import { http, HttpResponse } from "msw"
import { describe, expect, it } from "vitest"

import { r2Host } from "@/mocks/fixtures"
import { server } from "@/mocks/node"

import { putToR2, R2NetworkError, R2RejectedError } from "./r2"

// Ba kết cục của bước 2, mỗi cái một nguyên nhân khác hẳn — đó là lý do duy nhất `r2.ts` phân biệt chúng
// thay vì ném chung một lỗi (Mục 4 bảng ba trạng thái).

const UPLOAD_URL = `${r2Host}/socialmedia-dev/avatars/anh.jpg?X-Amz-Signature=gia`

function anhNho() {
  // Nội dung không quan trọng — mock không đọc byte; dung lượng > 0 để giống file thật.
  return new File(["0123456789"], "anh.jpg", { type: "image/jpeg" })
}

/** Ghi lại request THẬT SỰ đi lên host R2 — thứ duy nhất chứng minh header nào được gửi. */
function recordPut() {
  const seen: Request[] = []
  server.use(
    http.put(`${r2Host}/*`, ({ request }) => {
      seen.push(request)
      return new HttpResponse(null, { status: 200 })
    })
  )
  return seen
}

describe("putToR2", () => {
  it("2xx là xong — không ném gì", async () => {
    await expect(
      putToR2({
        url: UPLOAD_URL,
        file: anhNho(),
        contentType: "image/jpeg",
      })
    ).resolves.toBeUndefined()
  })

  it("gửi ĐÚNG Content-Type đã ký, và KHÔNG tự đặt Content-Length", async () => {
    const seen = recordPut()

    await putToR2({
      url: UPLOAD_URL,
      file: anhNho(),
      contentType: "image/png",
    })

    expect(seen).toHaveLength(1)
    expect(seen[0].method).toBe("PUT")
    expect(seen[0].headers.get("content-type")).toBe("image/png")
    // Trình duyệt chặn `Content-Length` đặt bằng tay; nó tự đặt từ body. Giá trị trong `requiredHeaders`
    // là để ĐỐI CHIẾU, không để gửi — đặt tay là cảnh báo ở console và header không đi.
    expect(seen[0].headers.get("x-requested-with")).toBeNull()
    expect(seen[0].headers.get("authorization")).toBeNull()
  })

  it("status ≠ 2xx là R2 TỪ CHỐI — kèm status, và thông điệp KHÔNG chứa chữ ký", async () => {
    server.use(
      http.put(
        `${r2Host}/*`,
        () => new HttpResponse("SignatureDoesNotMatch", { status: 403 })
      )
    )

    const error = await putToR2({
      url: UPLOAD_URL,
      file: anhNho(),
      contentType: "image/jpeg",
    }).catch((e: unknown) => e)

    expect(error).toBeInstanceOf(R2RejectedError)
    expect((error as R2RejectedError).status).toBe(403)
    // Luật Mục 1.2 #7: `uploadUrl` mang `X-Amz-Signature`, không bao giờ vào thông điệp lỗi.
    expect((error as Error).message).not.toContain("X-Amz-Signature")
    expect((error as Error).message).not.toContain(r2Host)
  })

  it("không có phản hồi nào là MẠNG/CORS/CSP — lỗi khác hẳn ca R2 từ chối (ISS-02)", async () => {
    server.use(http.put(`${r2Host}/*`, () => HttpResponse.error()))

    const error = await putToR2({
      url: UPLOAD_URL,
      file: anhNho(),
      contentType: "image/jpeg",
    }).catch((e: unknown) => e)

    expect(error).toBeInstanceOf(R2NetworkError)
    expect(error).not.toBeInstanceOf(R2RejectedError)
  })

  it("hủy ném AbortError nguyên vẹn — cùng giao kèo với request() của http.ts", async () => {
    const controller = new AbortController()
    controller.abort()

    const error = await putToR2({
      url: UPLOAD_URL,
      file: anhNho(),
      contentType: "image/jpeg",
      signal: controller.signal,
    }).catch((e: unknown) => e)

    expect((error as Error).name).toBe("AbortError")
    expect(error).not.toBeInstanceOf(R2NetworkError)
  })

  // KHÔNG có ca "hủy GIỮA CHỪNG" ở đây: XHR giả của msw (`@mswjs/interceptors` 0.41.9) không cài `abort()`
  // — `grep -c abort lib/node/interceptors/XMLHttpRequest/index.mjs` ra **0**, nên `xhr.abort()` không làm
  // gì và request vẫn chạy tới `onload`. Viết ca đó ở đây là viết một test xanh nhờ mock, không nhờ code.
  // Nhánh `onabort` chỉ nghiệm thu được trên trình duyệt thật; E4 (hủy một ảnh đang lên) phải nhớ điều này.
})
