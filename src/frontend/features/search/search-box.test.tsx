import { render, screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { delay, http, HttpResponse } from "msw"
import { StrictMode } from "react"
import { beforeEach, describe, expect, it, vi } from "vitest"

import { BFF_URL } from "@/lib/api/config"
import type { SearchPage } from "@/lib/api/types"
import { searchResult } from "@/mocks/fixtures"
import { server } from "@/mocks/node"

import { SearchBox } from "./search-box"
import { SearchResults } from "./search-results"

// GĐ6 E4 — ô tìm: debounce 300 ms, hủy request cũ, KHÔNG gửi dưới 2 ký tự (Đ-6.21); trạng thái rỗng; Enter → trang kết quả.

const push = vi.fn()
const router = { push }
vi.mock("next/navigation", () => ({ useRouter: () => router }))

const SEARCH = `${BFF_URL}/api/search`
const CHO = { timeout: 5000 }

type Seen = { q: string | null; limit: string | null; request: Request }

/** Ghi mọi lượt gọi `/search`; trả kết quả theo `q` (mặc định: một người tên như `q`). */
function phucVu(answer: (q: string) => SearchPage | Promise<SearchPage> = (q) => ({
  items: [{ ...searchResult, displayName: `Người ${q}` }],
})) {
  const seen: Seen[] = []
  server.use(
    http.get(SEARCH, async ({ request }) => {
      const u = new URL(request.url).searchParams
      seen.push({ q: u.get("q"), limit: u.get("limit"), request })
      return HttpResponse.json(await answer(u.get("q") ?? ""))
    })
  )
  return seen
}

beforeEach(() => push.mockReset())

describe("SearchBox", () => {
  it("dưới 2 ký tự: nhắc 'Nhập ít nhất 2 ký tự', KHÔNG gọi API", async () => {
    const seen = phucVu()
    const user = userEvent.setup()
    render(<SearchBox />)
    await user.type(screen.getByTestId("search-input"), " n ")
    expect(screen.getByText("Nhập ít nhất 2 ký tự.")).toBeInTheDocument()
    await new Promise((r) => setTimeout(r, 400))
    expect(seen).toHaveLength(0)
  })

  it("gõ liền 'nguyen' → MỘT request sau debounce, limit 8 (không một request mỗi phím)", async () => {
    const seen = phucVu()
    const user = userEvent.setup()
    render(<SearchBox />)
    await user.type(screen.getByTestId("search-input"), "nguyen")

    expect(await screen.findByText("Người nguyen", {}, CHO)).toBeInTheDocument()
    expect(seen.map((s) => s.q)).toEqual(["nguyen"])
    expect(seen[0].limit).toBe("8")
  })

  it("gõ tiếp khi request cũ đang bay → request cũ bị HỦY, kết quả cũ về muộn không đè", async () => {
    const seen = phucVu(async (q) => {
      if (q === "ngu") await delay(800)
      return { items: [{ ...searchResult, displayName: `Người ${q}` }] }
    })
    const user = userEvent.setup()
    render(<SearchBox />)
    const input = screen.getByTestId("search-input")
    await user.type(input, "ngu")
    await waitFor(() => expect(seen).toHaveLength(1), CHO)
    await user.type(input, "y")

    expect(await screen.findByText("Người nguy", {}, CHO)).toBeInTheDocument()
    expect(seen[0].request.signal.aborted).toBe(true)
    await new Promise((r) => setTimeout(r, 900))
    expect(screen.queryByText("Người ngu")).toBeNull()
  })

  it("không ai khớp → 'Không tìm thấy ai tên như vậy.'", async () => {
    phucVu(() => ({ items: [] }))
    const user = userEvent.setup()
    render(<SearchBox />)
    await user.type(screen.getByTestId("search-input"), "zz")
    expect(
      await screen.findByText("Không tìm thấy ai tên như vậy.", {}, CHO)
    ).toBeInTheDocument()
  })

  it("Enter → /search?q= (từ khóa đã trim, mã hóa)", async () => {
    phucVu()
    const user = userEvent.setup()
    render(<SearchBox />)
    await user.type(screen.getByTestId("search-input"), " Nguyễn Văn {Enter}")
    expect(push).toHaveBeenCalledWith(
      `/search?q=${encodeURIComponent("Nguyễn Văn")}`
    )
  })

  it("400 errors.q → câu CỦA SERVER", async () => {
    server.use(
      http.get(SEARCH, () =>
        HttpResponse.json(
          {
            type: "https://httpstatuses.io/400",
            title: "Dữ liệu không hợp lệ",
            status: 400,
            traceId: "t",
            errors: { q: ["Từ khóa tối đa 50 ký tự."] },
          },
          { status: 400, headers: { "Content-Type": "application/problem+json" } }
        )
      )
    )
    const user = userEvent.setup()
    render(<SearchBox />)
    await user.type(screen.getByTestId("search-input"), "ab")
    expect(
      await screen.findByText("Từ khóa tối đa 50 ký tự.", {}, CHO)
    ).toBeInTheDocument()
  })

  it("StrictMode: mount → unmount → mount vẫn ra kết quả (controller của lần mount đầu đã hủy không bị dùng lại)", async () => {
    phucVu()
    const user = userEvent.setup()
    render(
      <StrictMode>
        <SearchBox />
      </StrictMode>
    )
    await user.type(screen.getByTestId("search-input"), "an")
    expect(await screen.findByText("Người an", {}, CHO)).toBeInTheDocument()
  })
})

describe("SearchResults", () => {
  it("trang kết quả lấy 20, mỗi người dẫn tới hồ sơ", async () => {
    const seen = phucVu()
    render(<SearchResults query="nguyen" />)
    const link = await screen.findByTestId("search-result", {}, CHO)
    expect(link).toHaveAttribute("href", `/users/${searchResult.userId}`)
    expect(seen[0].limit).toBe("20")
  })

  it("q quá ngắn → nhắc, không gọi", async () => {
    const seen = phucVu()
    render(<SearchResults query="a" />)
    expect(screen.getByText("Nhập ít nhất 2 ký tự.")).toBeInTheDocument()
    await new Promise((r) => setTimeout(r, 400))
    expect(seen).toHaveLength(0)
  })
})
