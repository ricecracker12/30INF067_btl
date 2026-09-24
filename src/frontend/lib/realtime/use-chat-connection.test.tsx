import { act, render, renderHook, screen } from "@testing-library/react"
import { StrictMode } from "react"
import { describe, expect, it, vi } from "vitest"

import { createChatConnection } from "./chat-connection"
import { fakeDeps, flush } from "./fake-hub"
import type { ChatConnectionStatus } from "./chat-connection"
import { useChatConnection, useOnConnected } from "./use-chat-connection"

function Status({ connection }: { connection: ReturnType<typeof createChatConnection> }) {
  return <p>{useChatConnection(connection)}</p>
}

describe("useChatConnection", () => {
  // Ca <StrictMode> DUY NHẤT của hook sở hữu kết nối (luật FE Mục 9): Next dev mount → unmount → mount lại. Khẳng định TRẠNG THÁI
  // CUỐI — một kết nối, một vé, connected — không đếm số lần effect chạy.
  it("<StrictMode>: mount → unmount → mount lại vẫn MỘT kết nối, MỘT vé", async () => {
    const f = fakeDeps()
    const connection = createChatConnection(f.deps)
    connection.setEnabled(true)

    render(
      <StrictMode>
        <Status connection={connection} />
      </StrictMode>
    )
    await act(async () => {
      await f.clock.advance(0)
      await flush()
    })

    expect(screen.getByText("connected")).toBeInTheDocument()
    expect(f.hubs).toHaveLength(1)
    expect(f.state.tickets).toBe(1)
    expect(f.hubs[0].stopped).toBe(0)
  })

  it("hai người dùng (badge + màn chat) chung MỘT kết nối; người cuối rời thì dừng", async () => {
    const f = fakeDeps()
    const connection = createChatConnection(f.deps)
    connection.setEnabled(true)

    const badge = render(<Status connection={connection} />)
    const chat = render(<Status connection={connection} />)
    await act(async () => {
      await f.clock.advance(0)
    })
    expect(f.hubs).toHaveLength(1)

    chat.unmount()
    await act(async () => {
      await f.clock.advance(0)
    })
    expect(connection.getStatus()).toBe("connected")

    badge.unmount()
    await act(async () => {
      await f.clock.advance(0)
    })
    expect(connection.getStatus()).toBe("idle")
    expect(f.hubs[0].stopped).toBe(1)
  })
})

describe("useOnConnected", () => {
  // Badge + danh sách hội thoại nạp lúc gắn, hub nối SAU — tin tới trong khe đó phải được nạp lại khi hub vừa nối (staging
  // 2026-09-24: badge của B đứng 0 vì `onReconnected` không bắn ở lần kết nối đầu).
  it("gọi ở lần kết nối ĐẦU và mỗi lần nối lại; không gọi khi đã connected lúc gắn hay khi trạng thái không đổi", () => {
    const onConnected = vi.fn()
    const { rerender } = renderHook(({ s }) => useOnConnected(s, onConnected), {
      initialProps: { s: "idle" as ChatConnectionStatus },
    })
    rerender({ s: "connecting" })
    expect(onConnected).not.toHaveBeenCalled()
    rerender({ s: "connected" })
    expect(onConnected).toHaveBeenCalledTimes(1)
    rerender({ s: "connected" })
    rerender({ s: "reconnecting" })
    rerender({ s: "connected" })
    expect(onConnected).toHaveBeenCalledTimes(2)

    const already = vi.fn()
    renderHook(() => useOnConnected("connected", already), { wrapper: StrictMode })
    expect(already).not.toHaveBeenCalled()
  })
})
