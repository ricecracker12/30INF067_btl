import { describe, expect, it } from "vitest"

import {
  ApiError,
  hasProblemType,
  NetworkError,
  PROBLEM_TYPES,
  toApiError,
} from "./problem"

const problemResponse = (status: number, body: unknown) =>
  new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/problem+json" },
  })

describe("toApiError", () => {
  it("409 problem+json: giữ status, traceId và detail làm message", async () => {
    const err = await toApiError(
      problemResponse(409, {
        type: "https://httpstatuses.io/409",
        title: "Xung đột dữ liệu",
        status: 409,
        detail: "Email này đã được đăng ký.",
        instance: "/api/v1/auth/register",
        traceId: "9f2c7b1e4a6d4f0b8c3e5a7d9b1f3c5e",
      })
    )

    expect(err).toBeInstanceOf(ApiError)
    expect(err.status).toBe(409)
    expect(err.traceId).toBe("9f2c7b1e4a6d4f0b8c3e5a7d9b1f3c5e")
    expect(err.message).toBe("Email này đã được đăng ký.")
  })

  it("400: fieldErrors là map tên trường → mảng thông điệp", async () => {
    const err = await toApiError(
      problemResponse(400, {
        title: "Dữ liệu không hợp lệ",
        status: 400,
        traceId: "5e7a9c1b3d5f7092a4c6e8b0d2f4a6c8",
        errors: {
          email: ["Email không đúng định dạng."],
          password: ["Mật khẩu phải có ít nhất 8 ký tự."],
        },
      })
    )

    expect(err.fieldErrors.password).toEqual([
      "Mật khẩu phải có ít nhất 8 ký tự.",
    ])
    expect(Array.isArray(err.fieldErrors.email)).toBe(true)
  })

  it("500 không có detail: message lùi về title, traceId vẫn đọc được", async () => {
    const err = await toApiError(
      problemResponse(500, {
        title: "Đã xảy ra lỗi không mong muốn",
        status: 500,
        traceId: "b6d8f0a2c4e6b8d0f2a4c6e8b0d2f4a6",
      })
    )

    expect(err.message).toBe("Đã xảy ra lỗi không mong muốn")
    expect(err.traceId).toBe("b6d8f0a2c4e6b8d0f2a4c6e8b0d2f4a6")
  })

  it("502 text/html (apache lúc API khởi động lại) → ApiError(502, null), KHÔNG ném khi parse", async () => {
    const res = new Response("<html><body>502 Bad Gateway</body></html>", {
      status: 502,
      headers: { "Content-Type": "text/html" },
    })

    const err = await toApiError(res)

    expect(err.status).toBe(502)
    expect(err.problem).toBeNull()
    expect(err.message).toBe("HTTP 502")
    expect(err.fieldErrors).toEqual({})
    expect(err.traceId).toBeUndefined()
  })

  it("body JSON nhưng không phải Problem Details → problem là null, không nhận bừa", async () => {
    const res = new Response(JSON.stringify({ loi: "gì đó" }), {
      status: 400,
      headers: { "Content-Type": "application/json" },
    })

    const err = await toApiError(res)

    expect(err.problem).toBeNull()
    expect(err.message).toBe("HTTP 400")
  })

  it("JSON hỏng giữa chừng → không ném, chỉ mất phần problem", async () => {
    const res = new Response("{ khong-phai-json", {
      status: 503,
      headers: { "Content-Type": "application/problem+json" },
    })

    const err = await toApiError(res)

    expect(err.status).toBe(503)
    expect(err.problem).toBeNull()
  })
})

describe("hasProblemType (Q-E4)", () => {
  const withType = (type: string) =>
    new ApiError(503, { type, title: "t", status: 503, traceId: "x" })

  it("khớp đúng `type`, không khớp `type` kia dù cùng status", () => {
    const feed = withType(PROBLEM_TYPES.feedOverloaded)

    expect(hasProblemType(feed, PROBLEM_TYPES.feedOverloaded)).toBe(true)
    expect(hasProblemType(feed, PROBLEM_TYPES.bffSessionUnavailable)).toBe(
      false
    )
  })

  it("type mặc định theo status, body không phải Problem Details, lỗi mạng, lỗi lạ → false", () => {
    expect(
      hasProblemType(
        withType("https://httpstatuses.io/503"),
        PROBLEM_TYPES.feedOverloaded
      )
    ).toBe(false)
    expect(
      hasProblemType(new ApiError(503, null), PROBLEM_TYPES.feedOverloaded)
    ).toBe(false)
    expect(
      hasProblemType(new NetworkError("x"), PROBLEM_TYPES.feedOverloaded)
    ).toBe(false)
    expect(
      hasProblemType(new TypeError("x"), PROBLEM_TYPES.feedOverloaded)
    ).toBe(false)
  })
})
