import { render, screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { delay, http, HttpResponse } from "msw"
import { StrictMode } from "react"
import { beforeEach, describe, expect, it } from "vitest"

import { BFF_URL } from "@/lib/api/config"
import type { FriendshipState } from "@/lib/api/types"
import { relationship, SOCIAL_SCENARIO, userIdKhac } from "@/mocks/fixtures"
import { server } from "@/mocks/node"
import { fakeSession } from "@/mocks/session"

import { RelationshipButtons } from "./relationship-buttons"

// E2 — nút quan hệ vẽ theo SỰ THẬT của server (Đ-4.16): bấm → khóa cả cụm → vẽ theo phản hồi. `friendship` và `following`
// là hai sự thật độc lập (Đ-4.5) — mỗi ca đổi một hàng thì khẳng định luôn hàng kia KHÔNG đổi.

const API = `${BFF_URL}/api`
const REL = `${API}/relationships/:userId`

/** Chờ tay 5s: vài ca nối GET → ghi → GET lại; chạy cả bộ thì 1s mặc định không đủ (luật frontend Mục 9). */
const CHO = { timeout: 5000 }

/** Mọi request thật sự đi ra, dạng "METHOD /đường" — thứ duy nhất chứng minh method + path đúng hợp đồng. */
function ghiRequest() {
  const seen: string[] = []
  server.events.on("request:start", ({ request }) => {
    seen.push(
      `${request.method} ${new URL(request.url).pathname.replace("/bff/api", "")}`
    )
  })
  return seen
}

/** `GET /relationships` trả lần lượt từng quan hệ trong danh sách (phần tử cuối lặp lại mãi). */
function quanHe(...lan: [FriendshipState, boolean][]) {
  let i = 0
  server.use(
    http.get(REL, ({ params }) => {
      const [f, following] = lan[Math.min(i++, lan.length - 1)]
      return HttpResponse.json(
        relationship(f, following, String(params.userId))
      )
    })
  )
}

const nut = (testId: string) => screen.queryByTestId(testId)
const cho = (testId: string) => screen.findByTestId(testId, undefined, CHO)

function moNut(userId = userIdKhac, displayName?: string) {
  const user = userEvent.setup()
  const view = render(
    <RelationshipButtons userId={userId} displayName={displayName} />
  )
  return { user, ...view }
}

beforeEach(() => {
  fakeSession.start()
})

describe("RelationshipButtons — vẽ theo trạng thái", () => {
  it.each<[FriendshipState, string[]]>([
    ["none", ["nut-ket-ban"]],
    ["outgoing", ["nhan-da-gui", "nut-huy-loi-moi"]],
    ["incoming", ["nut-chap-nhan", "nut-tu-choi"]],
    ["friends", ["nhan-ban-be", "nut-huy-ket-ban"]],
  ])(
    "friendship = %s → đúng nhóm nút, và hàng theo dõi theo `following` (cả hai giá trị)",
    async (f, coMat) => {
      const TAT_CA = [
        "nut-ket-ban",
        "nhan-da-gui",
        "nut-huy-loi-moi",
        "nut-chap-nhan",
        "nut-tu-choi",
        "nhan-ban-be",
        "nut-huy-ket-ban",
      ]
      for (const following of [false, true]) {
        quanHe([f, following])
        const { unmount } = moNut()
        await cho("relationship-buttons")

        for (const id of TAT_CA)
          expect(nut(id) !== null, `${f}/${following}: ${id}`).toBe(
            coMat.includes(id)
          )
        // Đ-4.5: hàng theo dõi KHÔNG suy từ `friendship`.
        expect(nut("nut-theo-doi") !== null).toBe(!following)
        expect(nut("nut-bo-theo-doi") !== null).toBe(following)
        unmount()
      }
    }
  )

  it("đang nạp → skeleton, không nút nào bấm được; lỗi nạp → câu chung + Thử lại → nạp lại", async () => {
    let lan = 0
    server.use(
      http.get(REL, async ({ params }) => {
        lan++
        await delay(20)
        if (lan === 1)
          return HttpResponse.json(
            {
              type: "t",
              title: "Đã xảy ra lỗi không mong muốn",
              status: 500,
              traceId: "abc",
            },
            {
              status: 500,
              headers: { "Content-Type": "application/problem+json" },
            }
          )
        return HttpResponse.json(
          relationship("none", false, String(params.userId))
        )
      })
    )
    const { user } = moNut()

    expect(screen.getByTestId("relationship-loading")).toBeInTheDocument()
    expect(screen.queryAllByRole("button")).toHaveLength(0)

    expect(await screen.findByRole("alert", undefined, CHO)).toHaveTextContent(
      "Mã tra cứu: abc"
    )
    await user.click(screen.getByRole("button", { name: "Thử lại" }))

    expect(await cho("nut-ket-ban")).toBeInTheDocument()
    expect(lan).toBe(2)
  })
})

describe("RelationshipButtons — mỗi nút đúng method + path, vẽ theo phản hồi", () => {
  it("Kết bạn → POST /friends/requests {userId} → 'Đã gửi lời mời' + Hủy lời mời (theo body 201)", async () => {
    quanHe(["none", false])
    const bodies: unknown[] = []
    server.events.on("request:start", async ({ request }) => {
      if (request.method === "POST") bodies.push(await request.clone().json())
    })
    const seen = ghiRequest()
    const { user } = moNut()

    await user.click(await cho("nut-ket-ban"))

    expect(await cho("nhan-da-gui")).toBeInTheDocument()
    expect(nut("nut-huy-loi-moi")).toBeInTheDocument()
    expect(seen).toContain("POST /friends/requests")
    expect(bodies).toEqual([{ userId: userIdKhac }])
    // Hàng theo dõi không đổi (Đ-4.5): kết bạn không tạo dòng theo dõi.
    expect(nut("nut-theo-doi")).toBeInTheDocument()
  })

  it("Hủy lời mời → DELETE /friends/requests/{id} → 204 → Kết bạn", async () => {
    quanHe(["outgoing", false])
    const seen = ghiRequest()
    const { user } = moNut()

    await user.click(await cho("nut-huy-loi-moi"))

    expect(await cho("nut-ket-ban")).toBeInTheDocument()
    expect(seen).toContain(`DELETE /friends/requests/${userIdKhac}`)
  })

  it("Chấp nhận → POST /friends/requests/{id}/accept → 'Bạn bè' (theo body 200)", async () => {
    const { user } = moNut(SOCIAL_SCENARIO.loiMoiDen)
    const seen = ghiRequest()

    await user.click(await cho("nut-chap-nhan"))

    expect(await cho("nhan-ban-be")).toBeInTheDocument()
    expect(seen).toContain(
      `POST /friends/requests/${SOCIAL_SCENARIO.loiMoiDen}/accept`
    )
  })

  it("Từ chối → DELETE /friends/requests/{id} (CÙNG endpoint với hủy) → Kết bạn", async () => {
    const { user } = moNut(SOCIAL_SCENARIO.loiMoiDen)
    const seen = ghiRequest()

    await user.click(await cho("nut-tu-choi"))

    expect(await cho("nut-ket-ban")).toBeInTheDocument()
    expect(seen).toContain(
      `DELETE /friends/requests/${SOCIAL_SCENARIO.loiMoiDen}`
    )
  })

  it("Hủy kết bạn → hộp thoại nêu tên → xác nhận → DELETE /friends/{id} → Kết bạn; vẫn đang theo dõi (Đ-4.5)", async () => {
    const { user } = moNut(SOCIAL_SCENARIO.banBe, "Nguyễn Văn An")
    const seen = ghiRequest()

    await user.click(await cho("nut-huy-ket-ban"))
    const hop = await screen.findByRole("alertdialog", undefined, CHO)
    expect(hop).toHaveTextContent(
      "Bài chỉ dành cho bạn bè của Nguyễn Văn An sẽ không còn hiện với bạn."
    )
    await user.click(within(hop).getByRole("button", { name: "Hủy kết bạn" }))

    expect(await cho("nut-ket-ban")).toBeInTheDocument()
    expect(seen).toContain(`DELETE /friends/${SOCIAL_SCENARIO.banBe}`)
    expect(nut("nut-bo-theo-doi")).toBeInTheDocument()
    await waitFor(
      () => expect(screen.queryByRole("alertdialog")).toBeNull(),
      CHO
    )
  })

  it("hộp thoại Hủy kết bạn → 'Không' → 0 request DELETE, vẫn là bạn", async () => {
    const { user } = moNut(SOCIAL_SCENARIO.banBe)
    const seen = ghiRequest()

    await user.click(await cho("nut-huy-ket-ban"))
    const hop = await screen.findByRole("alertdialog", undefined, CHO)
    // Không có tên → câu vẫn giữ, chỉ bỏ tên.
    expect(hop).toHaveTextContent("bạn bè của người này")
    await user.click(within(hop).getByRole("button", { name: "Không" }))

    await waitFor(
      () => expect(screen.queryByRole("alertdialog")).toBeNull(),
      CHO
    )
    expect(seen.filter((s) => s.startsWith("DELETE"))).toEqual([])
    expect(nut("nhan-ban-be")).toBeInTheDocument()
  })

  it("Theo dõi → PUT /follows/{id} (không POST) → Bỏ theo dõi; Bỏ theo dõi → DELETE → Theo dõi; hàng kết bạn không đổi", async () => {
    quanHe(["none", false])
    const seen = ghiRequest()
    const { user } = moNut()

    await user.click(await cho("nut-theo-doi"))
    await user.click(await cho("nut-bo-theo-doi"))

    expect(await cho("nut-theo-doi")).toBeInTheDocument()
    expect(seen.filter((s) => s.includes("/follows/"))).toEqual([
      `PUT /follows/${userIdKhac}`,
      `DELETE /follows/${userIdKhac}`,
    ])
    expect(nut("nut-ket-ban")).toBeInTheDocument()
  })
})

describe("RelationshipButtons — lỗi (Q-E3): đọc lại khi màn vẽ quan hệ đã cũ", () => {
  it("409 Kết bạn → câu NGUYÊN VĂN hợp đồng + GET thứ hai + nút Chấp nhận (người kia vừa gửi trước)", async () => {
    quanHe(["none", false], ["incoming", false])
    const seen = ghiRequest()
    const { user } = moNut(SOCIAL_SCENARIO.daCoLoiMoi)

    await user.click(await cho("nut-ket-ban"))

    expect(await cho("nut-chap-nhan")).toBeInTheDocument()
    expect(screen.getByRole("alert")).toHaveTextContent(
      "Đã có lời mời hoặc quan hệ bạn bè giữa hai người."
    )
    expect(
      seen.filter((s) => s.startsWith("GET /relationships/"))
    ).toHaveLength(2)
  })

  it("403 Chấp nhận → 'Lời mời này không còn hiệu lực.' + đọc lại (lời mời đã bị hủy trong lúc màn mở)", async () => {
    // Màn vẽ `incoming`, nhưng server (mock) coi id này là `none` → accept 403; lượt đọc lại ra `none`.
    quanHe(["incoming", false], ["none", false])
    const seen = ghiRequest()
    const { user } = moNut()

    await user.click(await cho("nut-chap-nhan"))

    expect(await cho("nut-ket-ban")).toBeInTheDocument()
    expect(screen.getByRole("alert")).toHaveTextContent(
      "Lời mời này không còn hiệu lực."
    )
    expect(
      seen.filter((s) => s.startsWith("GET /relationships/"))
    ).toHaveLength(2)
  })

  it("404 Kết bạn (không có hồ sơ) → 'Không tìm thấy người dùng.', KHÔNG đọc lại, giữ nút cũ", async () => {
    const seen = ghiRequest()
    const { user } = moNut(SOCIAL_SCENARIO.khongTonTai)

    await user.click(await cho("nut-ket-ban"))

    expect(await screen.findByRole("alert", undefined, CHO)).toHaveTextContent(
      "Không tìm thấy người dùng."
    )
    expect(nut("nut-ket-ban")).toBeInTheDocument()
    expect(
      seen.filter((s) => s.startsWith("GET /relationships/"))
    ).toHaveLength(1)
  })

  it("404 Theo dõi → câu của ngữ cảnh follow, giữ nút Theo dõi", async () => {
    const { user } = moNut(SOCIAL_SCENARIO.khongTonTai)

    await user.click(await cho("nut-theo-doi"))

    expect(await screen.findByRole("alert", undefined, CHO)).toHaveTextContent(
      "Không tìm thấy người dùng."
    )
    expect(nut("nut-theo-doi")).toBeInTheDocument()
  })
})

describe("RelationshipButtons — khóa cả cụm (Đ-4.16)", () => {
  it("đang chờ → MỌI nút disabled, nút vừa bấm aria-busy; bấm lần hai / bấm nút kia → không request thứ hai", async () => {
    quanHe(["none", false])
    let posts = 0
    // Chốt do TEST mở, không `delay`: phản hồi về đúng lúc test cho phép — trễ theo mili giây thì các cú bấm của
    // userEvent (chuỗi pointer event) có lúc chậm hơn, phản hồi về trước và ca đo sai thứ.
    let moChot: () => void = () => {}
    const chot = new Promise<void>((r) => (moChot = r))
    server.use(
      http.post(`${API}/friends/requests`, async ({ request }) => {
        posts++
        await chot
        const { userId } = (await request.json()) as { userId: string }
        return HttpResponse.json(relationship("outgoing", false, userId), {
          status: 201,
        })
      })
    )
    const seen = ghiRequest()
    const { user } = moNut()

    const ketBan = await cho("nut-ket-ban")
    await user.click(ketBan)

    // KHÔNG optimistic: trong lúc chờ vẫn là "Kết bạn", chưa phải "Đã gửi lời mời".
    expect(nut("nhan-da-gui")).toBeNull()
    expect(ketBan).toBeDisabled()
    expect(ketBan).toHaveAttribute("aria-busy", "true")
    expect(nut("nut-theo-doi")).toBeDisabled()
    await user.click(ketBan)
    await user.click(nut("nut-theo-doi")!)
    moChot()

    expect(await cho("nhan-da-gui")).toBeInTheDocument()
    expect(posts).toBe(1)
    expect(seen.filter((s) => s.includes("/follows/"))).toEqual([])
  })

  it("sang hồ sơ khác khi thao tác đang bay → hồ sơ mới vẽ nút của NÓ, phản hồi cũ bị bỏ (không kẹt skeleton)", async () => {
    let moChot: () => void = () => {}
    const chot = new Promise<void>((r) => (moChot = r))
    let daTraLoi = false
    server.use(
      http.post(`${API}/friends/requests`, async ({ request }) => {
        await chot
        daTraLoi = true
        const { userId } = (await request.json()) as { userId: string }
        return HttpResponse.json(relationship("outgoing", false, userId), {
          status: 201,
        })
      })
    )
    const { user, rerender } = moNut(userIdKhac)
    await user.click(await cho("nut-ket-ban"))

    rerender(<RelationshipButtons userId={SOCIAL_SCENARIO.banBe} />)

    expect(await cho("nhan-ban-be")).toBeInTheDocument()
    moChot()
    await waitFor(() => expect(daTraLoi).toBe(true), CHO)
    await delay(30)
    // Phản hồi 201 của người cũ đã về: không ghi đè, không kéo màn về skeleton.
    expect(nut("nhan-ban-be")).toBeInTheDocument()
    expect(nut("nhan-da-gui")).toBeNull()
    expect(nut("relationship-loading")).toBeNull()
  })
})

describe("RelationshipButtons — StrictMode (luật frontend Mục 9, L5)", () => {
  // Next dev mount → unmount → mount lại: `AbortController` của lượt đọc đầu bị hủy. Khẳng định TRẠNG THÁI CUỐI, KHÔNG
  // đếm số `GET` — dưới StrictMode số request tăng gấp đôi một cách hợp lệ.
  it("vẽ đúng nút sau lần mount thứ hai và bấm được", async () => {
    const user = userEvent.setup()
    render(
      <StrictMode>
        <RelationshipButtons userId={SOCIAL_SCENARIO.loiMoiDen} />
      </StrictMode>
    )

    await user.click(await cho("nut-chap-nhan"))

    expect(await cho("nhan-ban-be")).toBeInTheDocument()
  })
})
