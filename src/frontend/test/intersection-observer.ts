// Harness, không phải ca test (luật frontend Mục 9): jsdom KHÔNG có `IntersectionObserver`. Stub này ghi lại mọi
// observer đang sống, và KHÔNG tự bắn gì — ca nào cần "cuộn tới đáy" thì gọi `kichHoatGiaoNhau` bằng tay. Nhờ vậy
// test điều khiển được đúng thời điểm sentinel vào/ra khung nhìn, kể cả ca "vẫn nằm trong khung nhìn sau khi trang về"
// (Q-E5) mà observer thật không bắn lại.

type Stub = {
  callback: IntersectionObserverCallback
  elements: Set<Element>
  options: IntersectionObserverInit | undefined
  self: IntersectionObserver
}

const live = new Set<Stub>()

class StubIntersectionObserver implements IntersectionObserver {
  readonly root = null
  readonly rootMargin: string
  readonly thresholds: ReadonlyArray<number> = [0]
  readonly scrollMargin = "0px"
  private readonly stub: Stub

  constructor(
    callback: IntersectionObserverCallback,
    options?: IntersectionObserverInit
  ) {
    this.rootMargin = options?.rootMargin ?? "0px"
    this.stub = { callback, elements: new Set(), options, self: this }
    live.add(this.stub)
  }

  observe(el: Element) {
    this.stub.elements.add(el)
  }

  unobserve(el: Element) {
    this.stub.elements.delete(el)
  }

  disconnect() {
    this.stub.elements.clear()
    live.delete(this.stub)
  }

  takeRecords(): IntersectionObserverEntry[] {
    return []
  }
}

export function installIntersectionObserverStub() {
  globalThis.IntersectionObserver = StubIntersectionObserver
}

/** Gỡ mọi observer còn sống giữa hai ca — observer của ca trước không được bắn vào ca sau. */
export function resetIntersectionObservers() {
  live.clear()
}

/** Số observer đang sống và đang theo dõi ít nhất một phần tử — ca StrictMode kiểm "không rò observer". */
export function soObserverDangTheoDoi() {
  return [...live].filter((s) => s.elements.size > 0).length
}

/**
 * Bắn một lượt giao nhau cho MỌI phần tử đang được theo dõi. Gọi trong `act()` — callback thường `setState`.
 * Observer thật bắn khi trạng thái giao nhau ĐỔI; stub bắn khi được gọi, nên ca test tự quyết thời điểm.
 */
export function kichHoatGiaoNhau(isIntersecting: boolean) {
  for (const s of [...live]) {
    const entries = [...s.elements].map(
      (target) =>
        ({
          target,
          isIntersecting,
          intersectionRatio: isIntersecting ? 1 : 0,
          time: performance.now(),
          boundingClientRect: target.getBoundingClientRect(),
          intersectionRect: target.getBoundingClientRect(),
          rootBounds: null,
        }) satisfies IntersectionObserverEntry
    )
    if (entries.length > 0) s.callback(entries, s.self)
  }
}
