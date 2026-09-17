import "@testing-library/jest-dom/vitest"

import { cleanup } from "@testing-library/react"
import { afterAll, afterEach, beforeAll } from "vitest"

import { server } from "@/mocks/node"
import { mockControls } from "@/mocks/session"
import { fakeApi } from "@/mocks/upstream"

// `globals: false` nên auto-cleanup của Testing Library không tự đăng ký — không dọn thì DOM của
// test trước còn lại, `getByLabelText` thấy hai phần tử và đỏ vô cớ.
afterEach(cleanup)

// Handler ở mocks/ (Đ-E7). `onUnhandledRequest: 'error'` để gõ sai path là đỏ ngay,
// thay vì test lặng lẽ đi ra mạng thật.
beforeAll(() => {
  server.listen({ onUnhandledRequest: "error" })
})
afterEach(() => {
  server.resetHandlers()
  server.events.removeAllListeners()
  mockControls.reset()
  fakeApi.reset()
})
afterAll(() => {
  server.close()
})

// Base UI cần vài API trình duyệt mà jsdom chưa có. Polyfill đặt ở đây, không rải trong từng test.
if (!("ResizeObserver" in globalThis)) {
  globalThis.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  }
}
