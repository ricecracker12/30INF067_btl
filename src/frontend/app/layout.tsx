import type { Metadata } from "next"
import { Geist_Mono, Inter } from "next/font/google"

import "./globals.css"
import { Providers } from "./providers"
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

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode
}>) {
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
        <Providers>{children}</Providers>
      </body>
    </html>
  )
}
