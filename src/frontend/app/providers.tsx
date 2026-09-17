"use client"

import type { ReactNode } from "react"

import { ThemeProvider } from "@/components/theme-provider"

// Không có mock trình duyệt (đổi Đ-E7 ngày 2026-09-17): dev luôn gọi API thật. MSW chỉ còn trong
// Vitest qua `msw/node`.
export function Providers({ children }: { children: ReactNode }) {
  // AuthProvider thêm ở E6.
  return <ThemeProvider>{children}</ThemeProvider>
}
