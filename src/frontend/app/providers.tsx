"use client"

import { useEffect, useState, type ReactNode } from "react"

import { ThemeProvider } from "@/components/theme-provider"

// Mock CHỈ chạy ở `next dev`. Hai điều kiện phải giống hệt nhau ở đây và ở chỗ gác `import()` bên
// dưới, nếu không trang treo ở khung trắng: `ready` chờ một worker không bao giờ khởi động.
//
// Viết nguyên văn `process.env.…` để Next thay chuỗi lúc build. Điều kiện `NODE_ENV` là thứ bundler
// gấp được thành hằng false trong bản production → cắt luôn cả cây MSW khỏi `.next/static`
// (Turbopack KHÔNG cắt nếu chỉ gác bằng `NEXT_PUBLIC_API_MOCKING` — xem "Thực tế thi công" của E2).
const mocking =
  process.env.NODE_ENV === "development" &&
  process.env.NEXT_PUBLIC_API_MOCKING === "enabled"

export function Providers({ children }: { children: ReactNode }) {
  const [ready, setReady] = useState(!mocking)

  useEffect(() => {
    if (process.env.NODE_ENV !== "development") return
    if (process.env.NEXT_PUBLIC_API_MOCKING !== "enabled") return
    void import("@/mocks/browser")
      .then(({ startWorker }) => startWorker())
      .then(() => setReady(true))
  }, [])

  // PHẢI chờ worker.start trước khi render: màn /me gọi refresh ngay khi mount, chạy trước worker
  // là request đi thẳng ra mạng thật.
  if (!ready) return null

  // AuthProvider thêm ở E6.
  return <ThemeProvider>{children}</ThemeProvider>
}
