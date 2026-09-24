import {
  HttpTransportType,
  HubConnectionBuilder,
  LogLevel,
} from "@microsoft/signalr"

import { messagingApi } from "@/lib/api/messaging-api"
import { hasProblemType, PROBLEM_TYPES } from "@/lib/api/problem"

import { createChatConnection, type HubLike } from "./chat-connection"
import { chatHubUrl } from "./hub-url"

// Bản ráp thật của kết nối chat: SignalR JS + vé qua BFF. CHỈ `lib/realtime/**` được import `@microsoft/signalr` (ESLint, Đ-5.17).

function createHub(ticket: () => Promise<string>): HubLike {
  return new HubConnectionBuilder()
    .withUrl(chatHubUrl(), {
      // Đ-5.16: bỏ negotiate — nếu không, accessTokenFactory chạy HAI lần (negotiate + WebSocket), vé dùng một lần bị tiêu ở
      // lượt negotiate và mọi kết nối 401 với triệu chứng trông như "vé sai". Kiểm ở C0/F1: tab Network 1 lần xin vé / kết nối.
      transport: HttpTransportType.WebSockets,
      skipNegotiation: true,
      accessTokenFactory: ticket,
    })
    // Không log của client ra console: URL WebSocket mang vé trên query (Đ-E16).
    .configureLogging(LogLevel.None)
    .build()
}

/** Kết nối chat DUY NHẤT của tab. Màn dùng qua `useChatConnection()` — không gọi `acquire` tay. */
export const chatConnection = createChatConnection({
  createHub,
  issueTicket: async () => (await messagingApi.ticket()).ticket,
  isUnavailable: (error) => hasProblemType(error, PROBLEM_TYPES.realtimeUnavailable),
})
