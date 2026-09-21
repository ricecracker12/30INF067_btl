import { NextResponse, type NextRequest } from "next/server"

import {
  buildCsp,
  checkR2Host,
  createNonce,
  NONCE_HEADER,
} from "@/lib/security/csp"

/**
 * Tên biến SERVER chứa host R2 (Đ-E17, sửa 2026-09-20). Dùng LẠI `R2__Endpoint` mà API đã có trong
 * `deploy/.env` thay vì sinh biến thứ hai mang cùng giá trị — hai biến một giá trị là hai chỗ để lệch,
 * và `env_file: [./.env]` đã đưa nó vào container frontend sẵn rồi.
 *
 * `__` là quy ước binding của .NET, không phải của Next — ở đây nó chỉ là một tên chuỗi, `process.env`
 * không diễn giải gì thêm. Hằng số này đặt trong `proxy.ts` (middleware, không bao giờ vào bundle
 * trình duyệt) chứ không trong `csp.ts`: cổng CI bundle grep đúng chuỗi `R2__` trên `.next/static`.
 * Đã đo: với cách này `R2__` chỉ nằm trong `.next/server`, cổng xanh.
 */
export const R2_HOST_ENV = "R2__Endpoint"

// Đ-E15 — proxy.ts CHỈ gắn Content-Security-Policy có nonce theo từng request. KHÔNG có logic đăng nhập ở đây (Đ-E3:
// guard ở client, API tự chặn dữ liệu) — thêm kiểm phiên vào file này là một chỗ chặn thứ hai lệch với guard.
//
// Nonce đi hai đường: header response (trình duyệt thi hành) và header REQUEST (Next đọc lúc render để gắn nonce vào
// script của nó; root layout đọc để đưa cho next-themes).
/**
 * Đ-E17 — host R2 cho `connect-src`/`img-src`. Đọc ở đây chứ không suy từ `uploadUrl` mà API trả lúc chạy:
 * CSP phải có mặt TRƯỚC khi trang render, còn `uploadUrl` chỉ có sau khi người dùng đã chọn ảnh.
 *
 * Đọc LƯỜI trong `proxy()`, không ở tầng module: `next build` nạp file này mà không có biến nào (Q-E1) —
 * kiểm lúc nạp là build CI đỏ, đúng lỗi mà `lib/bff/config.ts` đã gặp một lần.
 */
function r2HostOf(env: NodeJS.ProcessEnv, dev: boolean): string | null {
  const { host, problem } = checkR2Host(env[R2_HOST_ENV], {
    dev,
    name: R2_HOST_ENV,
  })
  if (problem) throw new Error(problem)
  // CHỈ production mới bắt buộc có biến, cùng ngưỡng với `lib/bff/config.ts` — `NODE_ENV` trong Vitest là
  // `test`, lấy `!dev` làm ngưỡng thì mọi test cũ của Đ-E15 đỏ vì thiếu một biến chúng không liên quan.
  // Thiếu biến trên production thì từ chối phục vụ: cho chạy tiếp là upload chết trên staging với triệu
  // chứng (PUT bị chặn, không có response) trông HỆT CORS sai trên bucket — xem Đ-E17.
  if (!host && env.NODE_ENV === "production")
    throw new Error(
      `Thiếu biến môi trường ${R2_HOST_ENV} khi chạy production: không dựng được CSP cho R2, ` +
        `mọi lượt tải ảnh lên sẽ bị trình duyệt chặn. App từ chối phục vụ thay vì chạy với cấu hình thiếu (Đ-E17).`
    )
  return host
}

export function proxy(request: NextRequest) {
  const dev = process.env.NODE_ENV === "development"
  const nonce = createNonce()
  const csp = buildCsp(nonce, { dev, r2Host: r2HostOf(process.env, dev) })

  const requestHeaders = new Headers(request.headers)
  requestHeaders.set(NONCE_HEADER, nonce)
  requestHeaders.set("Content-Security-Policy", csp)

  const response = NextResponse.next({ request: { headers: requestHeaders } })
  response.headers.set("Content-Security-Policy", csp)
  return response
}

export const config = {
  matcher: [
    {
      // Bỏ qua: /bff/* (JSON của BFF, không phải trang), file tĩnh và ảnh tối ưu của Next, favicon.
      source: "/((?!bff|_next/static|_next/image|favicon.ico).*)",
      // Prefetch của next/link không phải lượt tải trang — không cần nonce, đỡ sinh thừa.
      missing: [
        { type: "header", key: "next-router-prefetch" },
        { type: "header", key: "purpose", value: "prefetch" },
      ],
    },
  ],
}
