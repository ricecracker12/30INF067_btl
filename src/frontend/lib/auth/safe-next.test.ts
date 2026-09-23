import { describe, expect, it } from "vitest"

import { safeNext } from "./safe-next"

describe("safeNext", () => {
  it("đường dẫn nội bộ đi qua nguyên vẹn, kể cả query và hash", () => {
    expect(safeNext("/me")).toBe("/me")
    expect(safeNext("/posts/1?tab=comments#c9")).toBe(
      "/posts/1?tab=comments#c9"
    )
  })

  // Mặc định "/" (GĐ4 Q-E1) — đổi CÓ CHỦ ĐÍCH từ "/me": trang chủ là feed.
  it("thiếu hoặc rỗng → fallback, mặc định là trang chủ \"/\"", () => {
    expect(safeNext(null)).toBe("/")
    expect(safeNext("")).toBe("/")
    expect(safeNext(null, "/me")).toBe("/me")
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
  ])("chặn open redirect: %j → /", (next) => {
    expect(safeNext(next)).toBe("/")
  })
})
