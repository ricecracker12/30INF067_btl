"use client"

import { useCallback, useEffect, useLayoutEffect, useRef, useState } from "react"

import { messagingApi } from "@/lib/api/messaging-api"
import { errorMessage, fieldMessage } from "@/lib/api/messages"
import { ApiError, hasProblemType, PROBLEM_TYPES } from "@/lib/api/problem"
import type { ConversationResponse, MessageResponse } from "@/lib/api/types"
import { chatConnection } from "@/lib/realtime/chat"
import type { ChatConnection, ChatConnectionStatus } from "@/lib/realtime/chat-connection"
import { hubErrorCode, type HubErrorCode } from "@/lib/realtime/chat-hub-contract"

import { contentError, gapBefore, maxSeq, mergeMessages } from "./message-set"
import { markRendered, markSent } from "./latency"

// Một hội thoại đang mở (E4–E7). Ba nguồn đổ tin vào CÙNG một tập khử trùng theo `messageId` (Đ-5.17): ACK của hub, sự kiện
// `MessageReceived`, lần nạp REST. Không tin rằng hub không bao giờ rơi tin: thấy `seq` nhảy cóc, nối lại, hay đang fallback →
// lấp bằng `afterSeq`.

/** ACK không về trong 10 giây → Thất bại (Mục 7.4). Thử lại dùng CÙNG `clientMsgId` — server trả đúng tin cũ nếu đã lưu (Đ-5.5). */
export const SEND_TIMEOUT_MS = 10_000
/** Fallback (Đ-5.12): hội thoại đang mở hỏi lại mỗi 3 giây. */
export const FALLBACK_POLL_MS = 3_000
const AFTER_SEQ_PAGE = 50

export type PendingMessage = {
  clientMsgId: string
  content: string
  state: "sending" | "failed"
  /** Câu hiện dưới tin thất bại. */
  error: string | null
  /** `false`: thử lại vô ích (không còn là bạn, nội dung sai, trùng mã) — ẩn nút "Thử lại". */
  retryable: boolean
}

export type ConversationState = {
  conversation: ConversationResponse | null
  messages: MessageResponse[]
  pending: PendingMessage[]
  /** Trang đầu đang nạp. */
  loading: boolean
  loadError: unknown
  hasOlder: boolean
  loadingOlder: boolean
  /** Không gửi được nữa (BR-09) — `canSend = false` từ server hoặc lần gửi vừa nhận `not-friends`. */
  readOnly: boolean
}

type Deps = {
  connection?: ChatConnection
  status: ChatConnectionStatus
  /** Người đang đăng nhập — `app/` truyền vào (features không import chéo `features/profile`). */
  meId: string
}

type SendFailure = { error: string; retryable: boolean; notFriends: boolean }

function classify(error: unknown): SendFailure {
  const hub: HubErrorCode | null = error instanceof ApiError ? null : hubErrorCode(error)
  if (hub === "not-friends" || hasProblemType(error, PROBLEM_TYPES.notFriends))
    return { error: errorMessage("message-send", error instanceof ApiError ? error : notFriendsProblem()), retryable: false, notFriends: true }
  if (hub === "validation" || (error instanceof ApiError && error.status === 400))
    return { error: fieldMessage(error, "content", "message-send"), retryable: false, notFriends: false }
  if (hub === "conflict" || (error instanceof ApiError && error.status === 409))
    return { error: errorMessage("message-send", new ApiError(409, null)), retryable: false, notFriends: false }
  if (hub === "forbidden" || (error instanceof ApiError && error.status === 403))
    return { error: errorMessage("message-send", new ApiError(403, null)), retryable: false, notFriends: false }
  if (hub === "rate-limited")
    return { error: errorMessage("message-send", new ApiError(429, null)), retryable: true, notFriends: false }
  // unavailable, hết giờ chờ ACK, mất mạng: KHÔNG biết tin đã lưu chưa → cho thử lại cùng clientMsgId (quy tắc 5).
  return { error: "Chưa gửi được. Bấm Thử lại.", retryable: true, notFriends: false }
}

function notFriendsProblem(): ApiError {
  return new ApiError(403, {
    type: PROBLEM_TYPES.notFriends,
    title: "Không phải bạn bè",
    status: 403,
    traceId: "",
  })
}

function withTimeout<T>(promise: Promise<T>, ms: number): Promise<T> {
  return new Promise<T>((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error("timeout")), ms)
    promise.then(
      (v) => {
        clearTimeout(timer)
        resolve(v)
      },
      (e) => {
        clearTimeout(timer)
        reject(e)
      }
    )
  })
}

export function useConversation(conversationId: string, { connection = chatConnection, status, meId }: Deps) {
  const [state, setState] = useState<ConversationState>({
    conversation: null,
    messages: [],
    pending: [],
    loading: true,
    loadError: null,
    hasOlder: false,
    loadingOlder: false,
    readOnly: false,
  })
  const olderCursor = useRef<string | null>(null)
  const stateRef = useRef(state)
  const statusRef = useRef(status)
  const sentSeen = useRef(0)
  const filling = useRef(false)
  const refill = useRef(false)
  // Handler đọc ref — đồng bộ bằng LAYOUT effect (luật FE Mục 9: useEffect để hở một khe mà cú bấm đọc ref cũ).
  useLayoutEffect(() => {
    stateRef.current = state
    statusRef.current = status
  })

  const patch = useCallback((fn: (s: ConversationState) => ConversationState) => setState(fn), [])

  const addMessages = useCallback(
    (incoming: MessageResponse[]) =>
      patch((s) => {
        const messages = mergeMessages(s.messages, incoming)
        const ids = new Set(incoming.map((m) => m.clientMsgId))
        const pending = s.pending.filter((p) => !ids.has(p.clientMsgId))
        return messages === s.messages && pending.length === s.pending.length ? s : { ...s, messages, pending }
      }),
    [patch]
  )

  /**
   * Lấp chỗ hở phía trên mốc hiện có (nối lại, fallback, seq nhảy cóc). Một lượt một lúc — nhưng yêu cầu tới khi đang bận KHÔNG bị
   * bỏ: đánh dấu `refill`, xong lượt hiện tại thì lấp thêm một lượt từ mốc MỚI NHẤT (lượt đang chạy có thể đã hỏi trước khi tin
   * mới được lưu — bỏ yêu cầu là mất chỗ hở).
   */
  const fillFrom = useCallback(
    async (afterSeq: number) => {
      if (filling.current) {
        refill.current = true
        return
      }
      filling.current = true
      try {
        let from = afterSeq
        for (;;) {
          refill.current = false
          for (let i = 0; i < 5; i++) {
            const page = await messagingApi.history(conversationId, { afterSeq: from, limit: AFTER_SEQ_PAGE })
            addMessages(page.items)
            if (page.items.length < AFTER_SEQ_PAGE) break
            from = page.items[page.items.length - 1].seq
          }
          if (!refill.current) break
          from = Math.max(from, maxSeq(stateRef.current.messages))
        }
      } catch {
        // Lượt lấp hỏng: lượt sau (sự kiện kế, hỏi lại 3 giây, nối lại) thử lại — không báo lỗi giữa màn.
      } finally {
        filling.current = false
      }
    },
    [conversationId, addMessages]
  )

  const refreshDetail = useCallback(async () => {
    try {
      const conversation = await messagingApi.get(conversationId)
      patch((s) => ({
        ...s,
        conversation: mergeMarks(s.conversation, conversation),
        readOnly: conversation.canSend === false,
      }))
    } catch {
      // Giữ chi tiết cũ; màn vẫn đọc được.
    }
  }, [conversationId, patch])

  // --- Trang đầu ---
  useEffect(() => {
    const controller = new AbortController()
    Promise.all([
      messagingApi.get(conversationId, controller.signal),
      messagingApi.history(conversationId, {}, controller.signal),
    ])
      .then(([conversation, page]) => {
        olderCursor.current = page.nextCursor
        setState((s) => ({
          ...s,
          conversation,
          messages: mergeMessages(s.messages, page.items),
          loading: false,
          loadError: null,
          hasOlder: page.nextCursor !== null,
          readOnly: conversation.canSend === false,
        }))
      })
      .catch((error: unknown) => {
        if ((error as Error).name === "AbortError") return
        setState((s) => ({ ...s, loading: false, loadError: error }))
      })
    return () => controller.abort()
  }, [conversationId])

  // --- Biên nhận ---
  const sendReceipt = useCallback(
    async (kind: "delivered" | "seen", upToSeq: number) => {
      const args = { conversationId, kind, upToSeq }
      try {
        if (statusRef.current === "connected") await connection.sendReceipt(args)
        else await messagingApi.receipt(conversationId, { kind, upToSeq })
      } catch {
        // Biên nhận hỏng không hiện cho người dùng (ngữ cảnh `receipt` để trống) — lần sau gửi mốc lớn hơn là đủ.
      }
    },
    [conversationId, connection]
  )

  /** "Đã xem" CHỈ khi hội thoại đang mở VÀ tab đang hiển thị (Mục 7.2) — để quên tab thì không "xem" hộ. */
  const markSeen = useCallback(() => {
    if (typeof document !== "undefined" && document.visibilityState !== "visible") return
    const top = maxSeq(stateRef.current.messages)
    if (top > sentSeen.current) {
      sentSeen.current = top
      void sendReceipt("seen", top)
    }
  }, [sendReceipt])

  useEffect(() => {
    markSeen()
  }, [state.messages, markSeen])

  useEffect(() => {
    const onVisible = () => markSeen()
    document.addEventListener("visibilitychange", onVisible)
    return () => document.removeEventListener("visibilitychange", onVisible)
  }, [markSeen])

  // --- Sự kiện hub ---
  useEffect(() => {
    const offMessage = connection.onMessage((m) => {
      if (m.conversationId !== conversationId) return
      const gap = gapBefore(stateRef.current.messages, m)
      addMessages([m])
      markRendered(m.clientMsgId)
      if (gap !== null) void fillFrom(gap)
      if (m.senderId !== meId && document.visibilityState !== "visible")
        void sendReceipt("delivered", m.seq)
    })
    const offReceipt = connection.onReceipt((r) => {
      if (r.conversationId !== conversationId || r.userId === meId) return
      patch((s) =>
        s.conversation
          ? {
              ...s,
              conversation: {
                ...s.conversation,
                peerDeliveredSeq: Math.max(s.conversation.peerDeliveredSeq, r.deliveredSeq),
                peerSeenSeq: Math.max(s.conversation.peerSeenSeq, r.seenSeq),
              },
            }
          : s
      )
    })
    const offReconnected = connection.onReconnected(() => {
      void fillFrom(maxSeq(stateRef.current.messages))
      void refreshDetail()
    })
    return () => {
      offMessage()
      offReceipt()
      offReconnected()
    }
  }, [connection, conversationId, meId, addMessages, fillFrom, patch, refreshDetail, sendReceipt])

  // --- Hub vừa connected (KỂ CẢ lần đầu) → lấp chỗ hở ---
  // Khe "trang đầu đã nạp, hub CHƯA kết nối": tin gửi trong khe này không có MessageReceived nào tới tab (hub chưa có kết nối để
  // đẩy). `onReconnected` chỉ bắn khi nối LẠI, nên lần kết nối ĐẦU cũng phải lấp — lộ ra trên staging 2026-09-24 (local quá nhanh).
  const loading = state.loading
  useEffect(() => {
    if (status !== "connected" || loading) return
    void fillFrom(maxSeq(stateRef.current.messages))
  }, [status, loading, fillFrom])

  // --- Fallback: hỏi lại 3 giây (Đ-5.12) ---
  useEffect(() => {
    if (status !== "fallback") return
    const timer = setInterval(() => {
      void fillFrom(maxSeq(stateRef.current.messages))
      void refreshDetail()
    }, FALLBACK_POLL_MS)
    return () => clearInterval(timer)
  }, [status, fillFrom, refreshDetail])

  // --- Gửi ---
  const deliver = useCallback(
    async (p: { clientMsgId: string; content: string }) => {
      markSent(p.clientMsgId)
      try {
        const message =
          statusRef.current === "connected"
            ? (await withTimeout(connection.sendMessage({ conversationId, ...p }), SEND_TIMEOUT_MS)).message
            : await messagingApi.send(conversationId, p)
        addMessages([message])
      } catch (error) {
        const failure = classify(error)
        patch((s) => ({
          ...s,
          readOnly: s.readOnly || failure.notFriends,
          pending: s.pending.map((x) =>
            x.clientMsgId === p.clientMsgId
              ? { ...x, state: "failed", error: failure.error, retryable: failure.retryable }
              : x
          ),
        }))
      }
    },
    [connection, conversationId, addMessages, patch]
  )

  /** Gửi tin mới (optimistic). Trả lỗi client để ô soạn hiện, hoặc `null` khi đã nhận. */
  const send = useCallback(
    (content: string): string | null => {
      const invalid = contentError(content)
      if (invalid) return invalid
      const clientMsgId = crypto.randomUUID()
      patch((s) => ({
        ...s,
        pending: [...s.pending, { clientMsgId, content, state: "sending", error: null, retryable: true }],
      }))
      void deliver({ clientMsgId, content })
      return null
    },
    [deliver, patch]
  )

  /** Thử lại — CÙNG `clientMsgId` (Đ-5.5): lần trước đã lưu thì server trả đúng tin cũ, không sinh bản thứ hai. */
  const retry = useCallback(
    (clientMsgId: string) => {
      const p = stateRef.current.pending.find((x) => x.clientMsgId === clientMsgId)
      if (!p || !p.retryable) return
      patch((s) => ({
        ...s,
        pending: s.pending.map((x) => (x.clientMsgId === clientMsgId ? { ...x, state: "sending", error: null } : x)),
      }))
      void deliver({ clientMsgId, content: p.content })
    },
    [deliver, patch]
  )

  const discard = useCallback(
    (clientMsgId: string) =>
      patch((s) => ({ ...s, pending: s.pending.filter((x) => x.clientMsgId !== clientMsgId) })),
    [patch]
  )

  const loadOlder = useCallback(async () => {
    const cursor = olderCursor.current
    if (cursor === null || stateRef.current.loadingOlder) return
    patch((s) => ({ ...s, loadingOlder: true }))
    try {
      const page = await messagingApi.history(conversationId, { cursor })
      olderCursor.current = page.nextCursor
      patch((s) => ({
        ...s,
        messages: mergeMessages(s.messages, page.items),
        hasOlder: page.nextCursor !== null,
        loadingOlder: false,
      }))
    } catch {
      patch((s) => ({ ...s, loadingOlder: false }))
    }
  }, [conversationId, patch])

  return { ...state, send, retry, discard, loadOlder }
}

/** Mốc là TUYỆT ĐỐI và chỉ tăng — bản chi tiết về muộn không được kéo lùi mốc vừa nhận qua hub. */
function mergeMarks(
  current: ConversationResponse | null,
  next: ConversationResponse
): ConversationResponse {
  if (!current) return next
  return {
    ...next,
    peerDeliveredSeq: Math.max(current.peerDeliveredSeq, next.peerDeliveredSeq),
    peerSeenSeq: Math.max(current.peerSeenSeq, next.peerSeenSeq),
  }
}
