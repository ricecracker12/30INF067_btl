import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

// `config.ts` tính API_BASE_URL NGAY LÚC NẠP MODULE, nên mỗi ca phải nạp lại module sau khi
// dựng env — không thể set env rồi đọc lại hằng số đã tính.
async function loadConfig() {
  vi.resetModules()
  return import("./config")
}

beforeEach(() => {
  vi.unstubAllEnvs()
})
afterEach(() => {
  vi.unstubAllEnvs()
  vi.resetModules()
})

describe("API_BASE_URL", () => {
  it("có biến: dùng đúng giá trị và bỏ dấu / ở cuối", async () => {
    vi.stubEnv("NEXT_PUBLIC_API_BASE_URL", "https://mxh.banhgao.net/api/v1/")

    const { API_BASE_URL } = await loadConfig()

    // Bỏ / cuối vì request() nối thẳng `${API_BASE_URL}${path}` — không bỏ là ra //auth/login.
    expect(API_BASE_URL).toBe("https://mxh.banhgao.net/api/v1")
  })

  it("có biến là đường dẫn tương đối (staging sau apache): giữ nguyên", async () => {
    vi.stubEnv("NEXT_PUBLIC_API_BASE_URL", "/api/v1")

    const { API_BASE_URL } = await loadConfig()

    expect(API_BASE_URL).toBe("/api/v1")
  })

  it("thiếu biến ngoài production: lùi về API dev local (Đ-E1)", async () => {
    vi.stubEnv("NEXT_PUBLIC_API_BASE_URL", "")
    vi.stubEnv("NODE_ENV", "development")

    const { API_BASE_URL } = await loadConfig()

    expect(API_BASE_URL).toBe("http://localhost:5259/api/v1")
  })

  it("thiếu biến khi build production: NÉM LỖI và nêu tên biến", async () => {
    vi.stubEnv("NEXT_PUBLIC_API_BASE_URL", "")
    vi.stubEnv("NODE_ENV", "production")

    await expect(loadConfig()).rejects.toThrow(/NEXT_PUBLIC_API_BASE_URL/)
  })
})
