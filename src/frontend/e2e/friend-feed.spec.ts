import { expect, test, type Page } from "@playwright/test"

import { giuHanMucAuth } from "./dev-api"
import {
  dangNhapUi,
  donRacSauTest,
  taoBaiApi,
  taoTaiKhoanCoHoSo,
} from "./post-helpers"

// GĐ4 E6 — lát cắt hai tài khoản (Mục 10.6 dòng 3): thứ Vitest không nói được vì nó không có backend thật. Chạy trên API
// + FE dev thật, Chrome đã cài (Đ-E8), `workers: 1`. Hai `BrowserContext` — một context cho hai người thì cookie
// `__Host-sid` của người sau đè người trước.
//
// Feed gợi ý trên DB dev chứa bài công khai của MỌI lượt chạy trước: không khẳng định bài nào đứng đầu, chỉ khẳng định bài
// có hậu tố ngẫu nhiên CÓ MẶT — nó mới nhất nên nằm trang đầu. Trang đang mở không tự nạp lại: mọi khẳng định sau một thay
// đổi quan hệ đi sau `goto("/")` (lỗi của test, không phải của app, nếu quên).

test.afterEach(async ({ request }) => {
  await donRacSauTest(request)
})

const feed = (page: Page) => page.getByTestId("feed")
const bai = (page: Page, body: string) =>
  feed(page).getByText(body, { exact: true })

async function moTrangChu(page: Page) {
  await page.goto("/")
  await expect(feed(page)).toBeVisible()
}

test("hai tài khoản: gợi ý → kết bạn → chấp nhận → bài friends hiện → hủy kết bạn → bài friends biến mất", async ({
  browser,
  request,
}) => {
  // B: register + verify + login API. A: như B. Rồi login UI của cả hai → 8 lượt `/auth/*`.
  await giuHanMucAuth(8)
  const tag = Math.random().toString(36).slice(2, 8)
  const B = await taoTaiKhoanCoHoSo(request, "ff-b", `Bình ${tag}`)
  const P = `Bài công khai của Bình ${tag}`
  const F = `Bài bạn bè của Bình ${tag}`
  await taoBaiApi(request, B, P, "public")
  await taoBaiApi(request, B, F, "friends")

  const A = await taoTaiKhoanCoHoSo(request, "ff-a", `An ${tag}`)
  // Đ-4.6 (đổi 2026-09-23): người chưa có kết nối vẫn thấy bài của CHÍNH MÌNH, mọi mức.
  const RIENG_A = `Bài riêng tư của An ${tag}`
  await taoBaiApi(request, A, RIENG_A, "private")

  const ctxA = await browser.newContext()
  const ctxB = await browser.newContext()
  try {
    const pa = await ctxA.newPage()
    const pb = await ctxB.newPage()
    await dangNhapUi(pa, A)

    // 1. A mới tinh: nhãn gợi ý; thấy P và bài riêng tư của mình; KHÔNG thấy F.
    await moTrangChu(pa)
    await expect(pa.getByTestId("feed-suggested")).toBeVisible()
    await expect(bai(pa, P)).toBeVisible()
    await expect(bai(pa, RIENG_A)).toBeVisible()
    await expect(bai(pa, F)).toHaveCount(0)

    // 2. A bấm tên B trên bài P → hồ sơ B → Kết bạn → "Đã gửi lời mời" (vẽ theo phản hồi 201, Đ-4.16).
    await feed(pa)
      .getByRole("link", { name: `Bình ${tag}` })
      .first()
      .click()
    await pa.waitForURL(new RegExp(`/users/${B.userId}$`))
    await pa.getByTestId("nut-ket-ban").click()
    await expect(pa.getByTestId("nhan-da-gui")).toBeVisible()

    // 3. B đăng nhập ở context riêng → /friends → Chấp nhận A → A sang mục Bạn bè.
    await dangNhapUi(pb, B)
    await pb.goto("/friends")
    const loiMoiCuaA = pb
      .getByTestId("section-incoming")
      .locator(`[data-user-id="${A.userId}"]`)
    await loiMoiCuaA.getByRole("button", { name: "Chấp nhận" }).click()
    await expect(loiMoiCuaA).toHaveCount(0)
    await expect(
      pb.getByTestId("section-friends").locator(`[data-user-id="${A.userId}"]`)
    ).toBeVisible()

    // 4. A: trang chủ → KHÔNG còn nhãn gợi ý (dấu nguồn cache trang đầu, FEED-07b); thấy CẢ P và F.
    await moTrangChu(pa)
    await expect(pa.getByTestId("feed-suggested")).toHaveCount(0)
    await expect(bai(pa, P)).toBeVisible()
    await expect(bai(pa, F)).toBeVisible()

    // 5. A: hồ sơ B → Hủy kết bạn → xác nhận → trang chủ → F biến mất ngay (IFriendshipReader không cache, Đ-4.3).
    await pa.goto(`/users/${B.userId}`)
    await pa.getByTestId("nut-huy-ket-ban").click()
    await pa
      .getByRole("alertdialog")
      .getByRole("button", { name: "Hủy kết bạn" })
      .click()
    await expect(pa.getByTestId("nut-ket-ban")).toBeVisible()
    await moTrangChu(pa)
    await expect(bai(pa, F)).toHaveCount(0)
    // Hết kết nối → về gợi ý (Đ-4.6): P có thể hiện lại dưới nhãn gợi ý — không khẳng định P vắng.
    await expect(pa.getByTestId("feed-suggested")).toBeVisible()
  } finally {
    await ctxA.close()
    await ctxB.close()
  }
})
