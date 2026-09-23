import { describe, expect, it } from "vitest"

import {
  BODY_TOO_LONG,
  MAX_BODY_LENGTH,
  MAX_MEDIA_COUNT,
  POST_EMPTY,
  PRIVACY_REQUIRED,
  TOO_MANY_MEDIA,
  bodyToSend,
  postContentError,
  validatePost,
} from "./post"

// BR-01 ở client phải ra CÙNG KEY với `PostContentPolicy.Validate` của server. Lệch key nghĩa là hai bên
// chỉ vào hai ô khác nhau cho cùng một bài, và người dùng sửa ô sai.

describe("postContentError — ba mệnh đề BR-01", () => {
  it("bài hợp lệ: có chữ, không ảnh", () => {
    expect(postContentError("Chào buổi sáng.", 0)).toBeUndefined()
  })

  it("bài hợp lệ: không chữ, có ảnh", () => {
    expect(postContentError("", 1)).toBeUndefined()
  })

  it("rỗng cả chữ lẫn ảnh → key `body`, KHÔNG phải `mediaKeys`", () => {
    // Người dùng đang đứng ở ô soạn chữ; bảo họ "thiếu ảnh" là chỉ sai đường thoát dễ nhất.
    expect(postContentError("", 0)).toEqual({
      field: "body",
      message: POST_EMPTY,
    })
  })

  it("chuỗi toàn khoảng trắng tính là rỗng, như `string.IsNullOrWhiteSpace` của server", () => {
    expect(postContentError("   \n\t ", 0)).toEqual({
      field: "body",
      message: POST_EMPTY,
    })
  })

  it("khoảng trắng KHÔNG bị trim trước khi đo độ dài, như server", () => {
    // 5000 ký tự thật + hai khoảng trắng = 5002 > ngưỡng. Server đo `request.Body.Length` trên chuỗi thô
    // (`NormalizeBody` chỉ đổi chuỗi trắng thành `null`), nên client trim trước khi đo là NỚI hơn server
    // đúng hai ký tự — và ca đó ra 400 mà màn không đoán được vì sao.
    expect(postContentError(` ${"a".repeat(MAX_BODY_LENGTH)} `, 0)).toEqual({
      field: "body",
      message: BODY_TOO_LONG,
    })
  })

  it.each([
    [MAX_BODY_LENGTH - 1, undefined],
    [MAX_BODY_LENGTH, undefined],
    [MAX_BODY_LENGTH + 1, BODY_TOO_LONG],
  ])("độ dài %i ký tự → %s", (length, message) => {
    const result = postContentError("a".repeat(length), 0)
    expect(result?.message).toBe(message)
  })

  it.each([
    [MAX_MEDIA_COUNT - 1, undefined],
    [MAX_MEDIA_COUNT, undefined],
    [MAX_MEDIA_COUNT + 1, TOO_MANY_MEDIA],
  ])("%i ảnh → %s", (count, message) => {
    const result = postContentError("", count)
    expect(result?.message).toBe(message)
  })

  it("ẢNH TRƯỚC, CHỮ SAU: 11 ảnh kèm bài quá dài vẫn ra `mediaKeys`", () => {
    // Cùng thứ tự với `PostContentPolicy.Validate`. Kiểm chữ trước thì 11 ảnh KHÔNG kèm chữ (hợp lệ với
    // mệnh đề "không rỗng") sẽ ra `body` — sai ô.
    expect(
      postContentError("a".repeat(MAX_BODY_LENGTH + 1), MAX_MEDIA_COUNT + 1)
    ).toEqual({ field: "mediaKeys", message: TOO_MANY_MEDIA })
  })

  it("11 ảnh KHÔNG kèm chữ vẫn ra `mediaKeys`, không rơi vào mệnh đề rỗng", () => {
    expect(postContentError("", MAX_MEDIA_COUNT + 1)).toEqual({
      field: "mediaKeys",
      message: TOO_MANY_MEDIA,
    })
  })
})

describe("validatePost — BR-01 cộng privacy", () => {
  it("chưa chọn mức riêng tư → `privacy`, dùng đúng câu của server", () => {
    expect(
      validatePost({ body: "Chào.", mediaCount: 0, privacy: null })
    ).toEqual({ privacy: PRIVACY_REQUIRED })
  })

  it("bài hợp lệ và đã chọn mức riêng tư → không lỗi nào", () => {
    expect(
      validatePost({ body: "Chào.", mediaCount: 2, privacy: "friends" })
    ).toEqual({})
  })

  it("hai lỗi độc lập cùng lúc: BR-01 và privacy là hai luật khác nhau của server", () => {
    expect(validatePost({ body: "", mediaCount: 0, privacy: null })).toEqual({
      body: POST_EMPTY,
      privacy: PRIVACY_REQUIRED,
    })
  })
})

describe("bodyToSend", () => {
  it("chuỗi rỗng và chuỗi toàn khoảng trắng đều thành `null`, như `NormalizeBody`", () => {
    expect(bodyToSend("")).toBeNull()
    expect(bodyToSend("   ")).toBeNull()
  })

  it("giữ NGUYÊN chuỗi thô, KHÔNG trim — server cũng không trim", () => {
    expect(bodyToSend("  Chào phố cổ.  ")).toBe("  Chào phố cổ.  ")
  })
})
