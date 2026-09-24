import { act, renderHook, waitFor } from "@testing-library/react"
import { http, HttpResponse } from "msw"
import { describe, expect, it } from "vitest"

import { BFF_URL } from "@/lib/api/config"
import type { ConversationResponse, MessagePage, MessageResponse } from "@/lib/api/types"
import { createChatConnection, type ChatConnectionStatus } from "@/lib/realtime/chat-connection"
import { fakeDeps } from "@/lib/realtime/fake-hub"
import { server } from "@/mocks/node"

import { useConversation } from "./use-conversation"

// GĐ5 E4–E7 (giai-doan-5.md Mục 10.6): tập tin khử trùng, lấp chỗ hở, Thử lại cùng clientMsgId, not-friends khóa ô soạn, không có
// hub thì gửi REST. REST qua msw (`/bff/api/...`), hub qua hub giả — không mock module `@microsoft/signalr`.

const CID = "0192f3c1-9b2d-7e40-8a11-3c5d7e9f1a20"
const ME = "0192f3c1-8a4e-7c31-9f2a-6b5d4e3c2a10"
const PEER = "0192f3c1-7f3e-7b20-8e19-5a4c3b2a1f09"
const BASE = `${BFF_URL}/api/conversations/${CID}`
const CHO = { timeout: 5000 }

const msg = (seq: number, from = PEER, clientMsgId = `cm-${seq}`): MessageResponse => ({
  messageId: `m-${seq}`,
  conversationId: CID,
  senderId: from,
  seq,
  content: `tin ${seq}`,
  clientMsgId,
  createdAt: "2026-09-24T10:00:00Z",
})

const detail = (over: Partial<ConversationResponse> = {}): ConversationResponse => ({
  conversationId: CID,
  peer: { userId: PEER, displayName: "Trần Bình", avatarUrl: null },
  lastMessage: null,
  unreadCount: 0,
  peerDeliveredSeq: 0,
  peerSeenSeq: 0,
  canSend: true,
  ...over,
})

/** REST: chi tiết + lịch sử + biên nhận; ghi lại mọi afterSeq và mọi body gửi tin. */
function rest(opts: { history?: MessageResponse[]; detail?: ConversationResponse; send?: (body: { clientMsgId: string; content: string }) => Response } = {}) {
  const afterSeqs: number[] = []
  const sends: Array<{ clientMsgId: string; content: string }> = []
  server.use(
    http.get(BASE, () => HttpResponse.json(opts.detail ?? detail())),
    http.get(`${BASE}/messages`, ({ request }) => {
      const after = new URL(request.url).searchParams.get("afterSeq")
      if (after !== null) {
        afterSeqs.push(Number(after))
        const items = (opts.history ?? []).filter((m) => m.seq > Number(after))
        return HttpResponse.json({ items, nextCursor: null } satisfies MessagePage)
      }
      const items = [...(opts.history ?? [])].sort((a, b) => b.seq - a.seq)
      return HttpResponse.json({ items, nextCursor: null } satisfies MessagePage)
    }),
    http.post(`${BASE}/receipts`, () => new HttpResponse(null, { status: 204 })),
    http.post(`${BASE}/messages`, async ({ request }) => {
      const body = (await request.json()) as { clientMsgId: string; content: string }
      sends.push(body)
      return opts.send?.(body) ?? HttpResponse.json(msg(99, ME, body.clientMsgId), { status: 201 })
    })
  )
  return { afterSeqs, sends }
}

async function connected() {
  const f = fakeDeps()
  const connection = createChatConnection(f.deps)
  connection.setEnabled(true)
  connection.acquire()
  await act(async () => {
    await f.clock.advance(0)
  })
  return { f, connection }
}

function mount(connection: ReturnType<typeof createChatConnection>, status: ChatConnectionStatus) {
  return renderHook(({ s }) => useConversation(CID, { connection, status: s, meId: ME }), {
    initialProps: { s: status },
  })
}

describe("useConversation", () => {
  it("một tin tới qua REST, sự kiện hub và lần nạp lại → hiện MỘT lần, sắp theo seq", async () => {
    rest({ history: [msg(1), msg(2)] })
    const { f, connection } = await connected()
    const { result } = mount(connection, "connected")
    await waitFor(() => expect(result.current.loading).toBe(false), CHO)

    act(() => {
      f.hubs[0].emit("MessageReceived", msg(2))
      f.hubs[0].emit("MessageReceived", msg(3))
      f.hubs[0].emit("MessageReceived", msg(3))
    })

    expect(result.current.messages.map((m) => m.seq)).toEqual([1, 2, 3])
  })

  it("seq nhảy cóc (có tới 2, nhận 5) → gọi afterSeq=2 và lấp 3, 4", async () => {
    const { afterSeqs } = rest({ history: [msg(1), msg(2), msg(3), msg(4), msg(5)].slice(0, 2) })
    const { f, connection } = await connected()
    const { result } = mount(connection, "connected")
    await waitFor(() => expect(result.current.loading).toBe(false), CHO)
    server.use(
      http.get(`${BASE}/messages`, ({ request }) => {
        const after = Number(new URL(request.url).searchParams.get("afterSeq"))
        afterSeqs.push(after)
        return HttpResponse.json({ items: [msg(3), msg(4), msg(5)].filter((m) => m.seq > after), nextCursor: null })
      })
    )

    act(() => f.hubs[0].emit("MessageReceived", msg(5)))

    await waitFor(() => expect(result.current.messages.map((m) => m.seq)).toEqual([1, 2, 3, 4, 5]), CHO)
    // Mọi lượt lấp đều từ mốc 2 — một lượt của lần kết nối đầu (hub connected khi trang đầu vừa nạp) + lượt do chỗ hở.
    expect(afterSeqs.length).toBeGreaterThan(0)
    expect(afterSeqs.every((s) => s === 2)).toBe(true)
  })

  it("chưa kết nối hub → gửi bằng REST; tin optimistic thay bằng tin server theo clientMsgId", async () => {
    const { sends } = rest()
    const f = fakeDeps()
    const connection = createChatConnection(f.deps) // không bật — status idle
    const { result } = mount(connection, "fallback")
    await waitFor(() => expect(result.current.loading).toBe(false), CHO)

    act(() => {
      expect(result.current.send("xin chào")).toBeNull()
    })
    expect(result.current.pending).toHaveLength(1)

    await waitFor(() => expect(result.current.pending).toHaveLength(0), CHO)
    expect(sends).toHaveLength(1)
    expect(result.current.messages.map((m) => m.clientMsgId)).toEqual([sends[0].clientMsgId])
  })

  it("lỗi mạng → Thất bại + Thử lại gửi CÙNG clientMsgId; lần hai server trả đúng tin cũ → MỘT tin (AC-03)", async () => {
    let attempt = 0
    const { sends } = rest({
      send: (body) => {
        attempt += 1
        return attempt === 1 ? HttpResponse.error() : HttpResponse.json(msg(7, ME, body.clientMsgId), { status: 200 })
      },
    })
    const f = fakeDeps()
    const connection = createChatConnection(f.deps)
    const { result } = mount(connection, "fallback")
    await waitFor(() => expect(result.current.loading).toBe(false), CHO)

    act(() => void result.current.send("mất mạng giữa chừng"))
    await waitFor(() => expect(result.current.pending[0]?.state).toBe("failed"), CHO)
    expect(result.current.pending[0].retryable).toBe(true)

    act(() => result.current.retry(result.current.pending[0].clientMsgId))
    await waitFor(() => expect(result.current.pending).toHaveLength(0), CHO)

    expect(sends).toHaveLength(2)
    expect(sends[1].clientMsgId).toBe(sends[0].clientMsgId)
    expect(result.current.messages).toHaveLength(1)
  })

  it("403 not-friends → hội thoại chỉ đọc, tin thất bại KHÔNG cho thử lại (AC-04)", async () => {
    rest({
      send: () =>
        HttpResponse.json(
          { type: "urn:socialapp:problem:not-friends", title: "Không phải bạn bè", status: 403, traceId: "t", detail: "x" },
          { status: 403, headers: { "Content-Type": "application/problem+json" } }
        ),
    })
    const f = fakeDeps()
    const connection = createChatConnection(f.deps)
    const { result } = mount(connection, "fallback")
    await waitFor(() => expect(result.current.loading).toBe(false), CHO)

    act(() => void result.current.send("sau khi hủy kết bạn"))
    await waitFor(() => expect(result.current.readOnly).toBe(true), CHO)

    expect(result.current.pending[0].retryable).toBe(false)
    expect(result.current.pending[0].error).toBe("Hai bạn không còn là bạn bè. Hội thoại chỉ đọc.")
  })

  it("canSend = false từ server → chỉ đọc ngay khi mở", async () => {
    rest({ detail: detail({ canSend: false }) })
    const f = fakeDeps()
    const { result } = mount(createChatConnection(f.deps), "idle")
    await waitFor(() => expect(result.current.loading).toBe(false), CHO)

    expect(result.current.readOnly).toBe(true)
  })

  it("tin tới trong khe \"trang đầu đã nạp, hub CHƯA kết nối\" → khi hub vừa connected thì lấp bằng afterSeq (lỗi thật trên staging)", async () => {
    const { afterSeqs } = rest({ history: [msg(1)] })
    const f = fakeDeps()
    const connection = createChatConnection(f.deps)
    const { result, rerender } = mount(connection, "connecting")
    await waitFor(() => expect(result.current.loading).toBe(false), CHO)
    // Tin 2 lưu ở server TRONG KHE: không có MessageReceived nào tới tab này.
    server.use(
      http.get(`${BASE}/messages`, ({ request }) => {
        const after = Number(new URL(request.url).searchParams.get("afterSeq"))
        afterSeqs.push(after)
        return HttpResponse.json({ items: [msg(1), msg(2)].filter((m) => m.seq > after), nextCursor: null })
      })
    )

    rerender({ s: "connected" })

    await waitFor(() => expect(result.current.messages.map((m) => m.seq)).toEqual([1, 2]), CHO)
    expect(afterSeqs).toEqual([1])
  })

  it("ReceiptUpdated của người kia nâng mốc — mốc cũ về muộn KHÔNG kéo lùi", async () => {
    rest({ history: [msg(1, ME), msg(2, ME)] })
    const { f, connection } = await connected()
    const { result } = mount(connection, "connected")
    await waitFor(() => expect(result.current.loading).toBe(false), CHO)

    act(() => {
      f.hubs[0].emit("ReceiptUpdated", { conversationId: CID, userId: PEER, deliveredSeq: 2, seenSeq: 2 })
      f.hubs[0].emit("ReceiptUpdated", { conversationId: CID, userId: PEER, deliveredSeq: 1, seenSeq: 1 })
    })

    expect(result.current.conversation?.peerSeenSeq).toBe(2)
    expect(result.current.conversation?.peerDeliveredSeq).toBe(2)
  })
})
