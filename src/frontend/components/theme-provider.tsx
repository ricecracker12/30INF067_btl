"use client"

import * as React from "react"
import { ThemeProvider as NextThemesProvider } from "next-themes"

// E1 bước 2: template sinh kèm `ThemeHotkey` (phím `d` đổi sáng/tối ở mọi nơi ngoài ô
// nhập) — đã bỏ. Mạng xã hội có nhiều phím tắt, bấm nhầm `d` là đổi cả giao diện.
function ThemeProvider({
  children,
  ...props
}: React.ComponentProps<typeof NextThemesProvider>) {
  return (
    <NextThemesProvider
      attribute="class"
      defaultTheme="system"
      enableSystem
      disableTransitionOnChange
      {...props}
    >
      {children}
    </NextThemesProvider>
  )
}

export { ThemeProvider }
