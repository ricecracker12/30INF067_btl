import { setupWorker } from "msw/browser"

import { API_BASE_URL } from "@/lib/api/config"

import { handlers } from "./handlers"

export const worker = setupWorker(...handlers)

// StrictMode của React chạy effect HAI lần ở dev (Next bật mặc định cho App Router). Gọi
// `worker.start()` lần hai thì MSW ném "cannot configure an already enabled network" ra console —
// đo được bằng e2e/smoke.spec.ts. Giữ một promise cấp module, không dùng `useRef`: StrictMode dựng
// lại component nên ref cũng mất (cùng cạm bẫy với Đ-E10).
let dangKhoiDong: Promise<void> | null = null

export function startWorker(): Promise<void> {
  dangKhoiDong ??= khoiDong()
  return dangKhoiDong
}

async function khoiDong() {
  // Gốc API ở dạng tuyệt đối, để so với url của request. Dev là http://localhost:5259/api/v1;
  // nếu ai đó đặt đường dẫn tương đối thì nối vào origin hiện tại.
  const apiBase = new URL(API_BASE_URL, location.origin).toString()

  await worker.start({
    // KHÔNG dùng thẳng `onUnhandledRequest: 'error'`: Next dev tự gọi endpoint nội bộ CÙNG ORIGIN
    // (`/__nextjs_original-stack-frames`, HMR, RSC) và mỗi cái thành một lỗi đỏ trong console —
    // đo được bằng e2e/smoke.spec.ts lượt bật mock. Cũng không hạ xuống 'bypass': gõ sai path API
    // sẽ lặng lẽ đi ra mạng thật và lỗi lộ rất muộn.
    // Cách ở giữa: chỉ báo lỗi cho request tới CHÍNH gốc API — đúng thứ mock chịu trách nhiệm.
    onUnhandledRequest(request, print) {
      if (request.url.startsWith(apiBase)) print.error()
    },
  })
}
