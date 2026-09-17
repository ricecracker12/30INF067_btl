import { NextResponse, type NextRequest } from "next/server"

import { buildCsp, createNonce, NONCE_HEADER } from "@/lib/security/csp"

// Đ-E15 — proxy.ts CHỈ gắn Content-Security-Policy có nonce theo từng request. KHÔNG có logic đăng nhập ở đây (Đ-E3:
// guard ở client, API tự chặn dữ liệu) — thêm kiểm phiên vào file này là một chỗ chặn thứ hai lệch với guard.
//
// Nonce đi hai đường: header response (trình duyệt thi hành) và header REQUEST (Next đọc lúc render để gắn nonce vào
// script của nó; root layout đọc để đưa cho next-themes).
export function proxy(request: NextRequest) {
  const nonce = createNonce()
  const csp = buildCsp(nonce, { dev: process.env.NODE_ENV === "development" })

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
