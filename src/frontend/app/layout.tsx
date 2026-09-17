import type { Metadata } from "next"
import { headers } from "next/headers"
import { Geist_Mono, Inter } from "next/font/google"

import "./globals.css"
import { Providers } from "./providers"
import { NONCE_HEADER } from "@/lib/security/csp"
import { cn } from "@/lib/utils"

// Đ-E12 mục 6: font chỉ Inter, subset latin + vietnamese — thiếu `vietnamese` thì
// chữ có dấu vẽ bằng font dự phòng, lệch nét.
const inter = Inter({
  subsets: ["latin", "vietnamese"],
  variable: "--font-sans",
})

const fontMono = Geist_Mono({
  subsets: ["latin"],
  variable: "--font-mono",
})

export const metadata: Metadata = {
  title: "SocialApp",
}

// Đ-E15: đọc nonce do proxy.ts sinh cho request này. Gọi `headers()` làm MỌI trang render động — bắt buộc với CSP có
// nonce (trang tĩnh dựng lúc build, khi đó chưa có request nào để có nonce).
export default async function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode
}>) {
  const nonce = (await headers()).get(NONCE_HEADER) ?? undefined
  return (
    <html
      lang="vi"
      suppressHydrationWarning
      className={cn(
        "antialiased",
        fontMono.variable,
        "font-sans",
        inter.variable
      )}
    >
      <body>
        <Providers nonce={nonce}>{children}</Providers>
      </body>
    </html>
  )
}
