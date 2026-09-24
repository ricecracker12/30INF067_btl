import { act, render, screen } from "@testing-library/react"
import { StrictMode } from "react"
import { describe, expect, it } from "vitest"

import { createChatConnection } from "./chat-connection"
import { fakeDeps, flush } from "./fake-hub"
import { useChatConnection } from "./use-chat-connection"

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
