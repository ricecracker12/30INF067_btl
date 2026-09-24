import type { ChatConnectionDeps, HubLike } from "./chat-connection"

// Hub giả cho Vitest (GĐ5 E2) — cắm qua `createChatConnection(deps)`, KHÔNG mock module `@microsoft/signalr` toàn cục (giai-doan-5.md
// Mục 10.6). Đồng hồ tay: `advance()` chạy timer đến hạn theo thứ tự — không phụ thuộc fake timers của Vitest.

export type FakeHub = HubLike & {
  /** Số lần `start()` được gọi. */
  starts: number
  /** Kết quả của lần `start()` kế tiếp: `"ok"` hoặc một lỗi. */
  nextStart: Array<"ok" | Error>
  handlers: Map<string, (...args: never[]) => void>
  invoked: Array<{ method: string; args: unknown[] }>
  /** Server đóng kết nối (tuổi thọ 15 phút, thu hồi, mạng rớt). */
  serverClose(): void
  emit(event: string, payload: unknown): void
  stopped: number
}

export function createFakeHub(
  ticket: () => Promise<string>,
  outcomes: Array<"ok" | Error> = []
): FakeHub {
  let onclose: ((e?: Error) => void) | null = null
  const hub: FakeHub = {
    starts: 0,
    stopped: 0,
    nextStart: outcomes,
    handlers: new Map(),
    invoked: [],
    async start() {
      hub.starts += 1
      await ticket() // accessTokenFactory: MỘT lần mỗi lần bắt tay (Đ-5.16)
      const outcome = hub.nextStart.shift() ?? "ok"
      if (outcome !== "ok") throw outcome
    },
    async stop() {
      hub.stopped += 1
    },
    async invoke<T>(method: string, ...args: unknown[]) {
      hub.invoked.push({ method, args })
      return undefined as T
    },
    on(event, handler) {
      hub.handlers.set(event, handler)
    },
    onclose(callback) {
      onclose = callback
    },
    serverClose() {
      onclose?.(new Error("server closed"))
    },
    emit(event, payload) {
      ;(hub.handlers.get(event) as ((p: unknown) => void) | undefined)?.(payload)
    },
  }
  return hub
}

export function createManualClock() {
  let now = 0
  let seq = 0
  const timers = new Map<number, { at: number; fn: () => void }>()
  return {
    setTimer: (fn: () => void, ms: number) => {
      seq += 1
      timers.set(seq, { at: now + ms, fn })
      return seq
    },
    clearTimer: (handle: unknown) => void timers.delete(handle as number),
    /** Tiến đồng hồ, chạy mọi timer đến hạn (kể cả timer do timer đặt ra), chờ microtask giữa các lượt. */
    async advance(ms: number) {
      const until = now + ms
      for (;;) {
        await flush()
        const due = [...timers.entries()]
          .filter(([, t]) => t.at <= until)
          .sort((a, b) => a[1].at - b[1].at)[0]
        if (!due) break
        timers.delete(due[0])
        now = due[1].at
        due[1].fn()
      }
      now = until
      await flush()
    },
    pending: () => timers.size,
  }
}

export async function flush() {
  for (let i = 0; i < 10; i++) await Promise.resolve()
}

/** Deps đầy đủ cho `createChatConnection` với hub giả + đồng hồ tay; đếm số vé đã xin. */
/** `startOutcomes`: kết quả lần lượt của các lần `start()` (dùng chung mọi hub) — xếp TRƯỚC khi kết nối chạy. */
export function fakeDeps(
  options: { unavailable?: (e: unknown) => boolean; startOutcomes?: Array<"ok" | Error> } = {}
) {
  const outcomes = options.startOutcomes ?? []
  const clock = createManualClock()
  const hubs: FakeHub[] = []
  const state = { tickets: 0 }
  const deps: ChatConnectionDeps = {
    createHub: (ticket) => {
      const hub = createFakeHub(ticket, outcomes)
      hubs.push(hub)
      return hub
    },
    issueTicket: async () => {
      state.tickets += 1
      return `ve-${state.tickets}`
    },
    isUnavailable: options.unavailable ?? (() => false),
    setTimer: clock.setTimer,
    clearTimer: clock.clearTimer,
  }
  return { deps, clock, hubs, state }
}
