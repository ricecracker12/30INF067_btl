// Đ-E15 — Content-Security-Policy có nonce theo từng request. Hàm thuần: proxy.ts gọi, Vitest kiểm từng chỉ thị.
//
// Vì sao nonce: Next chèn script INLINE để nạp React lúc hydrate. CSP không có 'unsafe-inline' mà thiếu nonce thì chặn
// luôn script của chính app; có 'unsafe-inline' thì XSS inline (`<img onerror>`, `<script>` chèn vào trang) chạy được —
// mất lý do có CSP. Nonce sinh mới mỗi request, Next tự gắn vào script của nó; script kẻ tấn công chèn không có nonce.

export const NONCE_HEADER = "x-nonce"

/** 16 byte ngẫu nhiên (128 bit), base64 — đủ để không đoán được, sinh mới cho MỖI request. */
export function createNonce(): string {
  const bytes = new Uint8Array(16)
  crypto.getRandomValues(bytes)
  return btoa(String.fromCharCode(...bytes))
}

export function buildCsp(nonce: string, { dev }: { dev: boolean }): string {
  const directives: [string, ...string[]][] = [
    ["default-src", "'self'"],
    // KHÔNG 'strict-dynamic' (mẫu của Next có): nó tin mọi <script> do script đã tin tạo bằng createElement, và Chrome áp
    // cả cho script INLINE — đã thử: có nó thì `<script>` inline chèn động chạy được, bỏ nó thì bị chặn. App không có
    // script bên thứ ba; mọi chunk của Next cùng origin nên 'self' là đủ. Dev: React dùng eval để dựng lại stack lỗi phía
    // server — production KHÔNG có.
    [
      "script-src",
      "'self'",
      `'nonce-${nonce}'`,
      ...(dev ? ["'unsafe-eval'"] : []),
    ],
    // Dev: Next chèn style inline (hot reload) không mang nonce. Production: CSS là file tĩnh 'self'.
    [
      "style-src",
      "'self'",
      ...(dev ? ["'unsafe-inline'"] : [`'nonce-${nonce}'`]),
    ],
    ["img-src", "'self'", "blob:", "data:"],
    ["font-src", "'self'"],
    // Trình duyệt chỉ nói chuyện với BFF cùng origin (Đ-E14): script lạ không gửi được dữ liệu ra domain khác bằng fetch.
    ["connect-src", "'self'"],
    ["object-src", "'none'"],
    // Chặn chèn <base href> để đổi đích của mọi đường dẫn tương đối.
    ["base-uri", "'self'"],
    // Chặn form bị chèn gửi dữ liệu (mật khẩu đang gõ) ra ngoài.
    ["form-action", "'self'"],
    // Không cho trang khác nhúng app (clickjacking) — cùng ý X-Frame-Options: DENY, chuẩn mới hơn.
    ["frame-ancestors", "'none'"],
  ]
  // Dev chạy http://localhost: nâng mọi request lên https là làm hỏng chính dev server.
  if (!dev) directives.push(["upgrade-insecure-requests"])

  return directives.map((d) => d.join(" ")).join("; ")
}
