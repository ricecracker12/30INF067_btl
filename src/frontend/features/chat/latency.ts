// Công cụ đo p95 gửi→nhận (E8, giai-doan-5.md Mục 10.7) — BẬT bằng `?latency=1`, người dùng thường không thấy gì.
//
// Đo trên HAI trình duyệt cùng một máy (cùng đồng hồ): tab gửi ghi `t0` theo `clientMsgId` lúc bấm gửi; tab nhận ghi `t1` sau
// khi tin được VẼ (requestAnimationFrame kép — khung hình kế tiếp đã có tin trên màn). Mẫu nằm ở `window.__chatLatency`; kịch bản
// Playwright (`e2e/chat-latency.spec.ts`) đọc cả hai tab và tính p50/p95/p99. Không thêm trường nào vào hợp đồng.
//
// Phần nặng (ghi, xuất) nằm ở `latency-probe.ts`, nạp bằng import ĐỘNG chỉ khi bật — không vào bundle của người dùng thường.

let enabled: boolean | null = null

export function latencyEnabled(): boolean {
  if (enabled === null)
    enabled = typeof window !== "undefined" && new URLSearchParams(window.location.search).get("latency") === "1"
  return enabled
}

export function markSent(clientMsgId: string) {
  if (!latencyEnabled()) return
  const t0 = performance.timeOrigin + performance.now()
  void import("./latency-probe").then((p) => p.recordSent(clientMsgId, t0))
}

export function markRendered(clientMsgId: string) {
  if (!latencyEnabled()) return
  void import("./latency-probe").then((p) => p.recordRendered(clientMsgId))
}
