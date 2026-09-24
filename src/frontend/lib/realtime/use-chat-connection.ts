"use client"

import { useEffect, useSyncExternalStore } from "react"

import { tokenStore } from "@/lib/auth/token-store"

import { chatConnection } from "./chat"
import type { ChatConnection, ChatConnectionStatus } from "./chat-connection"

// Nối kết nối chat với phiên: authenticated → được chạy; anonymous (đăng xuất ở tab này, tab khác qua BroadcastChannel, hay
// phiên hết hạn) → dừng NGAY (Đ-5.10 cơ chế 3). Đăng ký một lần cho cả module — không phụ thuộc màn nào đang mở.
let wired = false
function wireSession(connection: ChatConnection) {
  if (wired) return
  wired = true
  const sync = () => connection.setEnabled(tokenStore.getSession().status === "authenticated")
  tokenStore.subscribe(sync)
  sync()
}

/**
 * Giữ kết nối chat sống khi màn đang mở (đếm người dùng — badge ở header + màn chat là HAI người dùng của MỘT kết nối) và trả
 * trạng thái. `connection` chỉ để test cắm bản giả; app dùng mặc định.
 */
export function useChatConnection(
  connection: ChatConnection = chatConnection
): ChatConnectionStatus {
  useEffect(() => {
    if (connection === chatConnection) wireSession(connection)
    return connection.acquire()
  }, [connection])

  return useSyncExternalStore(
    connection.subscribeStatus,
    connection.getStatus,
    () => "idle"
  )
}
