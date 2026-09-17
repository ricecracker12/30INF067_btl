// @vitest-environment node
import { afterEach, describe, expect, it, vi } from "vitest"

// config.ts nhớ cấu hình đã đọc cho process.env — mỗi ca nạp lại module để đọc env mới.
async function loadStartup() {
  vi.resetModules()
  return import("./startup")
}

afterEach(() => {
  vi.unstubAllEnvs()
  vi.restoreAllMocks()
})

describe("assertBffConfigOrExit — kiểm cấu hình lúc server khởi động (E8)", () => {
  it("production thiếu SESSION_ENCRYPTION_KEY → in lỗi nêu tên biến và THOÁT mã 1", async () => {
    vi.stubEnv("NODE_ENV", "production")
    vi.stubEnv("API_INTERNAL_URL", "http://api:8080/api/v1")
    vi.stubEnv("REDIS_URL", "redis://redis:6379")
    vi.stubEnv("APP_ORIGIN", "https://mxh.banhgao.net")
    vi.stubEnv("SESSION_ENCRYPTION_KEY", "")
    const exit = vi
      .spyOn(process, "exit")
      .mockImplementation((() => undefined) as never)
    const error = vi.spyOn(console, "error").mockImplementation(() => {})

    const { assertBffConfigOrExit } = await loadStartup()
    assertBffConfigOrExit()

    expect(exit).toHaveBeenCalledWith(1)
    expect(error.mock.calls[0][0]).toMatch(/SESSION_ENCRYPTION_KEY/)
  })

  it("production đủ biến → không thoát", async () => {
    vi.stubEnv("NODE_ENV", "production")
    vi.stubEnv("API_INTERNAL_URL", "http://api:8080/api/v1")
    vi.stubEnv("REDIS_URL", "redis://redis:6379")
    vi.stubEnv("APP_ORIGIN", "https://mxh.banhgao.net")
    vi.stubEnv("SESSION_ENCRYPTION_KEY", Buffer.alloc(32, 1).toString("base64"))
    const exit = vi
      .spyOn(process, "exit")
      .mockImplementation((() => undefined) as never)

    const { assertBffConfigOrExit } = await loadStartup()
    assertBffConfigOrExit()

    expect(exit).not.toHaveBeenCalled()
  })

  it("development không biến nào → dùng mặc định local, không thoát", async () => {
    vi.stubEnv("NODE_ENV", "development")
    const exit = vi
      .spyOn(process, "exit")
      .mockImplementation((() => undefined) as never)

    const { assertBffConfigOrExit } = await loadStartup()
    assertBffConfigOrExit()

    expect(exit).not.toHaveBeenCalled()
  })
})
