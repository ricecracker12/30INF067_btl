import { describe, expect, it } from "vitest"

import {
  createChatConnection,
  FALLBACK_RETRY_MS,
  NotConnectedError,
} from "./chat-connection"
import { fakeDeps, flush } from "./fake-hub"

// GĐ5 E2 (giai-doan-5.md Mục 10.6): máy trạng thái của kết nối chat với hub giả — connecting → connected → reconnecting →
// fallback → connected; MỖI lần bắt tay xin đúng một vé; đăng xuất → dừng.

async function started(options?: Parameters<typeof fakeDeps>[0]) {
  const f = fakeDeps(options)
  const connection = createChatConnection(f.deps)
  connection.setEnabled(true)
  const release = connection.acquire()
  await flush()
  return { ...f, connection, release }
}

describe("chat-connection", () => {
  it("chưa có phiên thì không kết nối dù có màn cần", async () => {
    const f = fakeDeps()
    const connection = createChatConnection(f.deps)
    connection.acquire()
    await flush()

    expect(connection.getStatus()).toBe("idle")
    expect(f.hubs).toHaveLength(0)
  })

  it("có phiên + có màn → connected, xin ĐÚNG MỘT vé (Đ-5.16)", async () => {
    const { connection, state } = await started()

    expect(connection.getStatus()).toBe("connected")
    expect(state.tickets).toBe(1)
  })

  it("server đóng (tuổi thọ 15 phút) → nối lại ngay bằng vé MỚI, báo onReconnected", async () => {
    const { connection, hubs, clock, state } = await started()
    let reconnected = 0
    connection.onReconnected(() => (reconnected += 1))

    hubs[0].serverClose()
    expect(connection.getStatus()).toBe("reconnecting")
    await clock.advance(0)

    expect(connection.getStatus()).toBe("connected")
    expect(state.tickets).toBe(2)
    expect(reconnected).toBe(1)
  })

  it("hỏng 3 lần liền → fallback; 30 giây sau thử lại và về connected", async () => {
    const f = fakeDeps({ startOutcomes: [new Error("1"), new Error("2"), new Error("3")] })
    const connection = createChatConnection(f.deps)
    connection.setEnabled(true)
    connection.acquire()
    await f.clock.advance(10_000)

    expect(connection.getStatus()).toBe("fallback")
    expect(f.hubs[0].starts).toBe(3)

    await f.clock.advance(FALLBACK_RETRY_MS)
    expect(connection.getStatus()).toBe("connected")
    expect(f.state.tickets).toBe(4)
  })

  it("vé 503 (Redis chết) → fallback NGAY, không thử lại ba lần", async () => {
    const unavailable = new Error("503 realtime-unavailable")
    const f = fakeDeps({ unavailable: (e) => e === unavailable, startOutcomes: [unavailable] })
    const connection = createChatConnection(f.deps)
    connection.setEnabled(true)
    connection.acquire()
    await f.clock.advance(0)

    expect(connection.getStatus()).toBe("fallback")
    expect(f.hubs[0].starts).toBe(1)
  })

  it("đăng xuất (setEnabled(false)) → dừng hub, idle, không tự nối lại", async () => {
    const { connection, hubs, clock } = await started()

    connection.setEnabled(false)
    await clock.advance(60_000)

    expect(connection.getStatus()).toBe("idle")
    expect(hubs[0].stopped).toBe(1)
    hubs[0].serverClose() // đóng do chính mình dừng → không được nối lại
    await clock.advance(60_000)
    expect(connection.getStatus()).toBe("idle")
  })

  it("nhả rồi lấy lại ngay (StrictMode) → KHÔNG dừng, không kết nối thứ hai", async () => {
    const { connection, hubs, clock, release, state } = await started()

    release()
    const again = connection.acquire()
    await clock.advance(0)

    expect(connection.getStatus()).toBe("connected")
    expect(hubs).toHaveLength(1)
    expect(hubs[0].stopped).toBe(0)
    expect(state.tickets).toBe(1)
    again()
  })

  it("người dùng cuối cùng rời → dừng sau một nhịp", async () => {
    const { connection, hubs, clock, release } = await started()

    release()
    await clock.advance(0)

    expect(connection.getStatus()).toBe("idle")
    expect(hubs[0].stopped).toBe(1)
  })

  it("sự kiện hub tới listener; gửi khi chưa connected → NotConnectedError (màn chuyển REST)", async () => {
    const f = fakeDeps()
    const connection = createChatConnection(f.deps)
    await expect(
      connection.sendMessage({ conversationId: "c", content: "x", clientMsgId: "m" })
    ).rejects.toBeInstanceOf(NotConnectedError)

    connection.setEnabled(true)
    connection.acquire()
    await flush()
    const got: string[] = []
    connection.onMessage((m) => got.push(m.messageId))
    f.hubs[0].emit("MessageReceived", { messageId: "m-1" })

    await connection.sendMessage({ conversationId: "c", content: "x", clientMsgId: "m" })
    expect(got).toEqual(["m-1"])
    expect(f.hubs[0].invoked[0]).toEqual({
      method: "SendMessage",
      args: [{ conversationId: "c", content: "x", clientMsgId: "m" }],
    })
  })
})
