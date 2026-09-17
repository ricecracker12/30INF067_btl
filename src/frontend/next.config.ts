import type { NextConfig } from "next"

// Đ-E14 — header bảo mật cho MỌI response của FE (trang lẫn /bff/*). Content-Security-Policy KHÔNG đặt ở đây: nó cần
// nonce mới cho từng request, nên nằm ở proxy.ts (Đ-E15).
const securityHeaders = [
  // Không cho trình duyệt đoán kiểu nội dung (JSON bị hiểu thành HTML/script).
  { key: "X-Content-Type-Options", value: "nosniff" },
  // Không cho trang khác nhúng app vào iframe (clickjacking — lừa bấm "Đăng xuất", "Xóa bài"…).
  { key: "X-Frame-Options", value: "DENY" },
  // Không gửi URL (có thể chứa ?token= của /verify-email, ?next=…) sang site khác.
  { key: "Referrer-Policy", value: "same-origin" },
  { key: "Cross-Origin-Opener-Policy", value: "same-origin" },
  {
    key: "Permissions-Policy",
    value: "camera=(), microphone=(), geolocation=(), payment=()",
  },
  // HSTS chỉ khi chạy production (sau HTTPS của apache/Cloudflare). Không includeSubDomains: không áp lên subdomain khác
  // của banhgao.net mà app này không quản.
  ...(process.env.NODE_ENV === "production"
    ? [{ key: "Strict-Transport-Security", value: "max-age=31536000" }]
    : []),
]

const nextConfig: NextConfig = {
  // E8: `.next/standalone` chỉ mang file runtime cần (server.js + node_modules đã lọc) — image không cần pnpm, không cần
  // devDependencies.
  output: "standalone",
  // Không quảng cáo "X-Powered-By: Next.js" — bớt thông tin cho người dò phiên bản có lỗ hổng.
  poweredByHeader: false,
  headers() {
    return [{ source: "/:path*", headers: securityHeaders }]
  },
}

export default nextConfig
