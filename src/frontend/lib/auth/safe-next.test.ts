import { describe, expect, it } from "vitest"

import { safeNext } from "./safe-next"

describe("safeNext", () => {
  it("đường dẫn nội bộ đi qua nguyên vẹn, kể cả query và hash", () => {
    expect(safeNext("/me")).toBe("/me")
    expect(safeNext("/posts/1?tab=comments#c9")).toBe(
      "/posts/1?tab=comments#c9"
    )
  })

  it("thiếu hoặc rỗng → fallback", () => {
    expect(safeNext(null)).toBe("/me")
    expect(safeNext("")).toBe("/me")
    expect(safeNext(null, "/")).toBe("/")
  })

  it.each([
    "https://evil.example",
    "//evil.example",
    "/\\evil.example",
    "\\\\evil.example",
    "/\t/evil.example",
    "/\n/evil.example",
    "javascript:alert(1)",
    "me",
  ])("chặn open redirect: %j → /me", (next) => {
    expect(safeNext(next)).toBe("/me")
  })
})
