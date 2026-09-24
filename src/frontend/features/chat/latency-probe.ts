// Phần ghi của công cụ đo p95 (E8) — chỉ nạp khi `?latency=1` (xem `latency.ts`). Thời điểm là `performance.timeOrigin + now()`
// (ms, đồng hồ tường của máy) để hai tab CÙNG máy so được với nhau.

export type LatencySamples = {
  sent: Record<string, number>
  rendered: Record<string, number>
}

declare global {
  interface Window {
    __chatLatency?: LatencySamples
  }
}

function samples(): LatencySamples {
  window.__chatLatency ??= { sent: {}, rendered: {} }
  return window.__chatLatency
}

export function recordSent(clientMsgId: string, t0: number) {
  samples().sent[clientMsgId] ??= t0
}

/** `t1` sau khi tin đã được VẼ: hai lần requestAnimationFrame = khung hình chứa tin đã commit lên màn. */
export function recordRendered(clientMsgId: string) {
  requestAnimationFrame(() =>
    requestAnimationFrame(() => {
      samples().rendered[clientMsgId] ??= performance.timeOrigin + performance.now()
    })
  )
}
