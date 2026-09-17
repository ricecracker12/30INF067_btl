"use client"

import type { ReactNode } from "react"

import { ThemeProvider } from "@/components/theme-provider"
// Nạp để nối nhánh 401 của `request()` với coordinator (`configureRefresh`) cho MỌI trang, kể cả trang ngoài (app)
// chưa import session.ts. Không cần Provider cho phiên: `useSession()` đọc thẳng store (E6).
import "@/lib/auth/session"

// Không có mock trình duyệt (đổi Đ-E7 ngày 2026-09-17): dev luôn gọi API thật. MSW chỉ còn trong
// Vitest qua `msw/node`.
export function Providers({ children }: { children: ReactNode }) {
  return <ThemeProvider>{children}</ThemeProvider>
}
