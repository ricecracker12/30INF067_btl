import type { MessageResponse } from "@/lib/api/types"

import {
  HUB_EVENTS,
  HUB_METHODS,
  type ReceiptUpdated,
  type SendMessageArgs,
  type SendMessageResult,
  type SendReceiptArgs,
} from "./chat-hub-contract"

// MỘT kết nối hub cho cả app (Đ-5.17): badge ở header và màn chat cùng dùng, mà `features/` không import chéo nhau — nên nó ở
// `lib/realtime/`. Trạng thái phát bằng module store + `useSyncExternalStore` (khuôn `lib/auth/token-store.ts`).
//
// Vòng đời, theo đúng thứ tự các quyết định:
//  - Đ-5.16: WebSockets + skipNegotiation — `ticket()` chạy ĐÚNG MỘT lần mỗi lần bắt tay (kể cả tự nối lại → vé mới).
//  - Đ-5.10: server cắt sau 15 phút / khi thu hồi → `onclose` → tự nối lại ngay (vé mới, BFF phải còn phiên mới xin được).
//  - Đ-5.12: hỏng 3 lần liền, hoặc vé 503 → `fallback` (màn gửi bằng REST, hỏi lại 3 giây), thử nối lại mỗi 30 giây.
//  - Đăng xuất (tab này hoặc tab khác qua BroadcastChannel → token-store) → dừng ngay, không đợi server.
//
// Không dùng `withAutomaticReconnect` của SignalR: lịch của nó không có "chuyển fallback sau 3 lần" và không cho biết khi nào
// bỏ cuộc. Tự lập lịch ở đây — một chỗ, test được bằng hub giả.

export type ChatConnectionStatus =
  | "idle"
  | "connecting"
  | "connected"
  | "reconnecting"
  | "fallback"

/** Phần của `HubConnection` (@microsoft/signalr) mà kết nối dùng — để test cắm hub giả, không mock module toàn cục. */
export interface HubLike {
  start(): Promise<void>
  stop(): Promise<void>
  invoke<T>(method: string, ...args: unknown[]): Promise<T>
  on(event: string, handler: (...args: never[]) => void): void
  onclose(callback: (error?: Error) => void): void
}

export type ChatConnectionDeps = {
  /** Dựng hub; `ticket` được hub gọi MỖI lần bắt tay (accessTokenFactory). */
  createHub: (ticket: () => Promise<string>) => HubLike
  /** Xin vé qua BFF. Ném lỗi 503 `realtime-unavailable` khi Redis chết. */
  issueTicket: () => Promise<string>
  /** Vé 503 → chuyển thẳng fallback, không thử lại ngay (không có Redis thì thử bao nhiêu cũng hỏng). */
  isUnavailable: (error: unknown) => boolean
  setTimer?: (fn: () => void, ms: number) => unknown
  clearTimer?: (handle: unknown) => void
}

/** Lịch thử lại sau lần hỏng thứ 1, 2 (ms). Lần hỏng thứ 3 liên tiếp → fallback. */
export const RETRY_DELAYS_MS = [0, 2_000, 5_000] as const
export const FAILURES_BEFORE_FALLBACK = 3
export const FALLBACK_RETRY_MS = 30_000

type Listener = () => void

export class NotConnectedError extends Error {
  constructor() {
    super("Kênh thời gian thực chưa kết nối.")
    this.name = "NotConnectedError"
  }
}

export function createChatConnection(deps: ChatConnectionDeps) {
  const setTimer = deps.setTimer ?? ((fn, ms) => setTimeout(fn, ms))
  const clearTimer =
    deps.clearTimer ?? ((h) => clearTimeout(h as ReturnType<typeof setTimeout>))

  let status: ChatConnectionStatus = "idle"
  let hub: HubLike | null = null
  let users = 0
  let enabled = false // phiên đang authenticated
  let failures = 0
  let everConnected = false
  let timer: unknown = null
  let stopTimer: unknown = null
  let generation = 0 // tăng mỗi lần dừng hẳn — lượt start cũ về muộn bị bỏ

  const statusListeners = new Set<Listener>()
  const messageListeners = new Set<(m: MessageResponse) => void>()
  const receiptListeners = new Set<(r: ReceiptUpdated) => void>()
  const reconnectedListeners = new Set<() => void>()

  function setStatus(next: ChatConnectionStatus) {
    if (next === status) return
    status = next
    statusListeners.forEach((l) => l())
  }

  function clearRetry() {
    if (timer !== null) clearTimer(timer)
    timer = null
  }

  function ensureHub(): HubLike {
    if (hub) return hub
    const created = deps.createHub(() => deps.issueTicket())
    created.on(HUB_EVENTS.messageReceived, (m: MessageResponse) =>
      messageListeners.forEach((l) => l(m))
    )
    created.on(HUB_EVENTS.receiptUpdated, (r: ReceiptUpdated) =>
      receiptListeners.forEach((l) => l(r))
    )
    created.onclose(() => {
      // Đóng do chính mình dừng (đăng xuất, hết người dùng) → im lặng. Đóng do server (tuổi thọ 15 phút, thu hồi, mạng rớt) →
      // nối lại ngay, có vé mới.
      if (hub !== created || !shouldRun()) return
      setStatus("reconnecting")
      failures = 0
      schedule(0)
    })
    hub = created
    return created
  }

  const shouldRun = () => enabled && users > 0

  function schedule(ms: number) {
    clearRetry()
    const gen = generation
    timer = setTimer(() => {
      timer = null
      if (gen === generation) void connect()
    }, ms)
  }

  async function connect() {
    if (!shouldRun()) return
    const gen = generation
    if (status !== "reconnecting" && status !== "fallback") setStatus("connecting")
    try {
      await ensureHub().start()
      if (gen !== generation || !shouldRun()) {
        await hub?.stop()
        return
      }
      failures = 0
      const wasConnectedBefore = everConnected
      everConnected = true
      setStatus("connected")
      // Nối LẠI (không phải lần đầu) → màn lấp chỗ hở bằng `afterSeq` + làm mới danh sách/badge (Đ-5.17).
      if (wasConnectedBefore) reconnectedListeners.forEach((l) => l())
    } catch (error) {
      if (gen !== generation || !shouldRun()) return
      failures += 1
      if (deps.isUnavailable(error) || failures >= FAILURES_BEFORE_FALLBACK) {
        setStatus("fallback")
        schedule(FALLBACK_RETRY_MS)
        return
      }
      setStatus(everConnected ? "reconnecting" : "connecting")
      schedule(RETRY_DELAYS_MS[failures] ?? RETRY_DELAYS_MS.at(-1)!)
    }
  }

  function stopNow() {
    generation += 1
    clearRetry()
    failures = 0
    everConnected = false
    setStatus("idle")
    const h = hub
    hub = null // hub mới ở lần chạy sau — handler cũ không bắn vào lượt mới
    void h?.stop().catch(() => undefined)
  }

  function reevaluate() {
    if (shouldRun()) {
      if (stopTimer !== null) {
        clearTimer(stopTimer)
        stopTimer = null
      }
      if (status === "idle") void connect()
    } else if (status !== "idle" && stopTimer === null) {
      // Hoãn một nhịp: StrictMode unmount → mount lại ngay trong cùng tác vụ — dừng rồi mở lại là hai kết nối, hai vé.
      stopTimer = setTimer(() => {
        stopTimer = null
        if (!shouldRun()) stopNow()
      }, 0)
    }
  }

  return {
    getStatus: () => status,
    subscribeStatus(l: Listener) {
      statusListeners.add(l)
      return () => void statusListeners.delete(l)
    },

    /** Có phiên (authenticated) hay không — `lib/realtime/use-chat-connection.ts` nối với token-store. */
    setEnabled(value: boolean) {
      enabled = value
      if (!value) {
        if (stopTimer !== null) clearTimer(stopTimer)
        stopTimer = null
        if (status !== "idle" || hub) stopNow()
        return
      }
      reevaluate()
    },

    /** Một màn cần kết nối. Trả hàm nhả. Kết nối sống khi còn ít nhất một người dùng VÀ còn phiên. */
    acquire() {
      users += 1
      reevaluate()
      let released = false
      return () => {
        if (released) return
        released = true
        users -= 1
        reevaluate()
      }
    },

    onMessage(l: (m: MessageResponse) => void) {
      messageListeners.add(l)
      return () => void messageListeners.delete(l)
    },
    onReceipt(l: (r: ReceiptUpdated) => void) {
      receiptListeners.add(l)
      return () => void receiptListeners.delete(l)
    },
    onReconnected(l: () => void) {
      reconnectedListeners.add(l)
      return () => void reconnectedListeners.delete(l)
    },

    /** Gửi qua hub. Chưa `connected` → ném `NotConnectedError` (màn tự chuyển REST). Lỗi hub → đọc bằng `hubErrorCode`. */
    sendMessage(args: SendMessageArgs): Promise<SendMessageResult> {
      if (status !== "connected" || !hub) return Promise.reject(new NotConnectedError())
      return hub.invoke<SendMessageResult>(HUB_METHODS.sendMessage, args)
    },
    sendReceipt(args: SendReceiptArgs): Promise<void> {
      if (status !== "connected" || !hub) return Promise.reject(new NotConnectedError())
      return hub.invoke<void>(HUB_METHODS.sendReceipt, args)
    },
  }
}

export type ChatConnection = ReturnType<typeof createChatConnection>
