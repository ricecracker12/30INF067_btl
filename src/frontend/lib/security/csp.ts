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

/**
 * Đ-E17 bước 3 — kiểm DẠNG của host R2. Cùng cạm bẫy đã đốt một lần ở `APP_ORIGIN` và
 * `Cors__AllowedOrigins`: lệch một ký tự (`/` cuối, có path) thì trình duyệt chặn IM LẶNG, và triệu
 * chứng trông hệt ký sai chữ ký — mất cả buổi để phân biệt. Thà đỏ lúc khởi động.
 *
 * Hàm THUẦN: không đọc `process.env`, để Vitest kiểm được mọi nhánh mà không dựng môi trường.
 *
 * `name` truyền vào chỉ để ghép thông điệp. File này KHÔNG chứa chuỗi `R2__`: `app/layout.tsx` import
 * `NONCE_HEADER` từ đây, nên nếu về sau có một client component nào import module này thì cả module
 * vào bundle trình duyệt — và cổng CI bundle grep đúng chuỗi `R2__` sẽ đỏ với thông điệp không liên
 * quan gì tới nguyên nhân thật. Tên biến nằm ở `proxy.ts`, chỗ không bao giờ ra trình duyệt.
 */
export function checkR2Host(
  raw: string | undefined | null,
  { dev, name }: { dev: boolean; name: string }
): { host: string | null; problem: string | null } {
  const value = raw?.trim()
  // Thiếu biến ở dev là hợp lệ: chưa làm E3/E4 thì chưa cần R2. Ngoài dev, chỗ gọi tự từ chối chạy.
  if (!value) return { host: null, problem: null }

  const sai = (vi_sao: string) => ({
    host: null,
    problem: `${name} '${value}' ${vi_sao} — phải có dạng scheme://host[:port], không path, không dấu / ở cuối`,
  })

  let u: URL
  try {
    u = new URL(value)
  } catch {
    return sai("không phải URL tuyệt đối")
  }
  // http chỉ chấp nhận ở dev (R2 thật luôn https); production dính http là hạ cấp bảo mật lặng lẽ.
  if (u.protocol !== "https:" && !(dev && u.protocol === "http:"))
    return sai("phải là https")
  // `u.origin` bỏ path, query, và dấu `/` cuối — so lại với chuỗi gốc là bắt được cả ba thứ đó một lần.
  if (u.origin !== value) return sai("có path, query hoặc dấu / ở cuối")

  return { host: value, problem: null }
}

/** Đ-E18: nguồn hub chat ở dev — cùng origin với nhánh dev của `chatHubUrl()` (`lib/realtime/hub-url.ts`). Module server, không vào bundle trình duyệt. */
const DEV_REALTIME = ["http://localhost:5259", "ws://localhost:5259"]

export function buildCsp(
  nonce: string,
  { dev, r2Host }: { dev: boolean; r2Host: string | null }
): string {
  // Host R2 vào ĐÚNG HAI chỉ thị dưới. `null` (chưa cấu hình) → không chỉ thị nào có chuỗi rỗng thừa.
  const r2 = (host: string | null) => (host ? [host] : [])

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
    // Đ-E17: ảnh của người dùng phục vụ từ presigned GET trên R2 (Đ-2.9) — không proxy qua origin mình.
    ["img-src", "'self'", "blob:", "data:", ...r2(r2Host)],
    ["font-src", "'self'"],
    // Trình duyệt chỉ nói chuyện với BFF cùng origin (Đ-E14) — TRỪ lượt `PUT` thẳng lên R2 (Đ-2.5, Đ-E17):
    // upload đi vòng qua Next server là nhân đôi băng thông VPS cho mỗi ảnh, đúng thứ Đ-2.5 tránh.
    // Đ-E18 (GĐ5): CHỈ dev — hub chat nối thẳng API dev (FE :3000, API :5259; luật FE cấm `rewrites`). Staging/production
    // nối `/hubs/chat` cùng origin: `'self'` đã phủ `wss://` cùng origin (CSP Level 3), không thêm gì.
    ["connect-src", "'self'", ...r2(r2Host), ...(dev ? DEV_REALTIME : [])],
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
