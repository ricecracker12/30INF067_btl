import { expect, test, type APIRequestContext, type Browser } from "@playwright/test"

import { API, giuHanMucAuth } from "./dev-api"
import { focusLai, taoAdmin, timBaoCao } from "./gd6-helpers"
import {
  dangNhapUi,
  donRacSauTest,
  taoBaiApi,
  taoTaiKhoanCoHoSo,
  type TaiKhoan,
} from "./post-helpers"

// GĐ6 E10 — lát cắt kiểm duyệt / quản trị / thông báo / tìm kiếm (Mục 10.5, E2E-01..06) trên API + FE dev thật, Chrome đã cài
// (Đ-E8), `workers: 1`. Mỗi người một `BrowserContext` (cookie `__Host-sid` không đè nhau). Chạy lại trên staging ở F2 với tài
// khoản thật (`E2E_ADMIN_*`).
//
// DB dev có dữ liệu của mọi lượt chạy trước: không khẳng định vị trí, chỉ khẳng định thứ mang hậu tố ngẫu nhiên của lượt này.

test.afterEach(async ({ request }) => {
  await donRacSauTest(request)
})

// Hai–bốn người, mỗi người một context, cộng lượt biên dịch đầu của route mới trên `next dev` — 30 giây mặc định hết giữa chừng
// (đo 2026-09-25: E2E-06 đỏ ở bước đăng nhập UI khi trang `/me` đã vẽ xong mà sự kiện `load` chưa tới).
test.describe.configure({ timeout: 120_000 })

const tag = () => Math.random().toString(36).slice(2, 8)

async function moNguoi(browser: Browser, tk: TaiKhoan) {
  const ctx = await browser.newContext()
  const page = await ctx.newPage()
  await dangNhapUi(page, tk)
  return { ctx, page }
}

async function doiVaiTroApi(request: APIRequestContext, admin: TaiKhoan, userId: string, roleCode: string) {
  const res = await request.put(`${API}/admin/users/${userId}/role`, { headers: admin.auth, data: { roleCode } })
  expect(res.status(), await res.text()).toBe(200)
}

test("E2E-06 ⭐ nâng rồi hạ quyền có hiệu lực ở request kế tiếp, KHÔNG đăng nhập lại (mốc 1)", async ({ browser, request }) => {
  // Admin 3 + X 3 + người báo 3 = 9; rồi đăng nhập UI Admin + X = 2. Khai TÁCH: một lần khai quá 10 là 429 dù có chờ.
  await giuHanMucAuth(9)
  const t = tag()
  const admin = await taoAdmin(request, `Quản trị ${t}`)
  const X = await taoTaiKhoanCoHoSo(request, "gd6-x", `Xuân ${t}`)
  const R = await taoTaiKhoanCoHoSo(request, "gd6-r", `Rà ${t}`)
  // Một báo cáo để X có thứ bấm ở bước hạ quyền.
  const postId = await taoBaiApi(request, R, `Bài để báo cáo ${t}`, "public")
  const rep = await request.post(`${API}/reports`, {
    headers: X.auth,
    data: { targetType: "post", targetId: postId, reasonCode: "spam" },
  })
  expect(rep.status(), await rep.text()).toBe(201)

  await giuHanMucAuth(2)
  const a = await moNguoi(browser, admin)
  const x = await moNguoi(browser, X)
  try {
    await x.page.goto("/")
    await expect(x.page.getByRole("link", { name: "Trang của tôi" })).toBeVisible()
    await expect(x.page.getByTestId("nav-moderation")).toHaveCount(0)

    // Admin nâng X lên MODERATOR BẰNG GIAO DIỆN.
    await a.page.goto(`/admin/users/${X.userId}`)
    await a.page.getByRole("button", { name: "Đổi vai trò" }).click()
    await a.page.getByRole("radio", { name: "Kiểm duyệt viên" }).click()
    await a.page.getByRole("button", { name: "Lưu vai trò" }).click()
    await expect(a.page.getByTestId("admin-user-role")).toHaveText("Kiểm duyệt viên")

    // X focus lại tab → liên kết hiện, vào được hàng đợi — cùng tab, không tải lại, không đăng nhập lại.
    await focusLai(x.page)
    await x.page.getByTestId("nav-moderation").click()
    await expect(x.page.getByRole("heading", { name: "Hàng đợi kiểm duyệt" })).toBeVisible()
    const reportId = await timBaoCao(request, admin, postId)
    await x.page.goto(`/moderation/${reportId}`)
    await expect(x.page.getByTestId("decision-panel")).toBeVisible()

    // Admin hạ X về USER (qua API cho gọn — đường UI đã đi ở trên).
    await doiVaiTroApi(request, admin, X.userId, "USER")

    // X bấm một thao tác kiểm duyệt → 403 → trang "không có quyền"; X VẪN đăng nhập.
    await x.page.getByRole("button", { name: "Bỏ qua" }).click()
    await x.page.getByRole("button", { name: /Xác nhận/ }).click()
    await expect(x.page.getByTestId("no-permission")).toBeVisible()
    await expect(x.page.getByTestId("nav-moderation")).toHaveCount(0)
    await expect(x.page).not.toHaveURL(/\/login/)
    await x.page.getByRole("link", { name: "Trang của tôi" }).click()
    await expect(x.page).toHaveURL(/\/me$/)
  } finally {
    await a.ctx.close()
    await x.ctx.close()
  }
})

test("E2E-01 báo cáo → Moderator ẩn → tác giả thấy thông báo + biểu ngữ; người báo mở lại link → không còn", async ({
  browser,
  request,
}) => {
  // Admin 3 + A 3 + C 3 = 9; rồi bốn lượt đăng nhập UI (A, Admin, C, A lần hai).
  await giuHanMucAuth(9)
  const t = tag()
  const admin = await taoAdmin(request, `Quản trị ${t}`)
  const A = await taoTaiKhoanCoHoSo(request, "gd6-a", `An ${t}`)
  const C = await taoTaiKhoanCoHoSo(request, "gd6-c", `Chi ${t}`)
  const postId = await taoBaiApi(request, C, `Bài vi phạm ${t}`, "public")

  await giuHanMucAuth(4)
  const a = await moNguoi(browser, A)
  try {
    // A báo cáo bằng giao diện.
    await a.page.goto(`/posts/${postId}`)
    await a.page.getByTestId("report-button").click()
    await a.page.getByRole("radio", { name: "Spam hoặc quảng cáo" }).click()
    await a.page.getByRole("button", { name: "Gửi báo cáo" }).click()
    await expect(a.page.getByTestId("report-thanks")).toBeVisible()
  } finally {
    await a.ctx.close()
  }

  // Moderator (Admin có mọi quyền) ẩn bằng giao diện.
  const reportId = await timBaoCao(request, admin, postId)
  const m = await moNguoi(browser, admin)
  try {
    await m.page.goto(`/moderation/${reportId}`)
    await expect(m.page.getByTestId("target-snapshot")).toContainText(`Bài vi phạm ${t}`)
    await m.page.getByRole("button", { name: "Ẩn nội dung" }).click()
    await m.page.getByRole("button", { name: /Xác nhận/ }).click()
    await expect(m.page).toHaveURL(/\/moderation\?notice=done/)
    await expect(m.page.locator(`[data-report-id="${reportId}"]`)).toHaveCount(0)
  } finally {
    await m.ctx.close()
  }

  // C: thông báo `moderation` (event sau COMMIT, xử lý bất đồng bộ) → bấm → biểu ngữ, không nút Sửa.
  const c = await moNguoi(browser, C)
  try {
    await expect(async () => {
      await c.page.goto("/notifications")
      await expect(
        c.page.getByText("Bài viết của bạn đã bị ẩn vì vi phạm tiêu chuẩn cộng đồng (lý do: Spam hoặc quảng cáo).")
      ).toBeVisible({ timeout: 2_000 })
    }).toPass({ timeout: 20_000 })
    await c.page.getByTestId("notification-item").first().click()
    await expect(c.page).toHaveURL(new RegExp(`/posts/${postId}$`))
    await expect(c.page.getByTestId("post-hidden-banner")).toBeVisible()
    await expect(c.page.getByRole("button", { name: "Sửa" })).toHaveCount(0)
  } finally {
    await c.ctx.close()
  }

  // A mở lại link bài → 404, cùng màn "không tìm thấy" (không lộ lý do).
  const a2 = await browser.newContext()
  try {
    const page = await a2.newPage()
    await dangNhapUi(page, A)
    await page.goto(`/posts/${postId}`)
    await expect(page.getByTestId("post-not-found")).toBeVisible()
  } finally {
    await a2.close()
  }
})

test("E2E-02 lời mời kết bạn → chuông của người nhận sáng khi tab lấy lại focus → bấm → /friends", async ({
  browser,
  request,
}) => {
  await giuHanMucAuth(7)
  const t = tag()
  const A = await taoTaiKhoanCoHoSo(request, "gd6-mo", `Mời ${t}`)
  const B = await taoTaiKhoanCoHoSo(request, "gd6-nhan", `Nhận ${t}`)
  const b = await moNguoi(browser, B)
  try {
    await b.page.goto("/")
    await expect(b.page.getByTestId("notification-bell")).toBeVisible()
    await expect(b.page.getByTestId("notification-badge")).toHaveCount(0)

    const res = await request.post(`${API}/friends/requests`, { headers: A.auth, data: { userId: B.userId } })
    expect(res.status(), await res.text()).toBe(201)

    // Event xử lý bất đồng bộ: focus lại cho tới khi badge sáng (hỏi lại 30 giây cũng sẽ tới — focus chỉ để khỏi chờ).
    await expect(async () => {
      await focusLai(b.page)
      await expect(b.page.getByTestId("notification-badge")).toHaveText("1", { timeout: 1_000 })
    }).toPass({ timeout: 20_000 })

    await b.page.getByTestId("notification-bell").click()
    await b.page.getByText(`Mời ${t} đã gửi lời mời kết bạn.`).click()
    await expect(b.page).toHaveURL(/\/friends$/)
    await expect(b.page.getByTestId("notification-badge")).toHaveCount(0)
  } finally {
    await b.ctx.close()
  }
})

test("E2E-03 gõ tên không dấu → gợi ý → Enter → trang kết quả → hồ sơ", async ({ browser, request }) => {
  await giuHanMucAuth(7)
  const t = tag()
  const N = await taoTaiKhoanCoHoSo(request, "gd6-ng", `Nguyễn Văn ${t}`)
  const A = await taoTaiKhoanCoHoSo(request, "gd6-tim", `Tìm ${t}`)
  const a = await moNguoi(browser, A)
  try {
    await a.page.goto("/")
    const o = a.page.getByTestId("search-input")
    await o.fill(`nguyen van ${t}`)
    await expect(a.page.getByTestId("search-suggestions").getByText(`Nguyễn Văn ${t}`)).toBeVisible()
    await o.press("Enter")
    await expect(a.page).toHaveURL(/\/search\?q=/)
    await a.page.getByTestId("search-result").filter({ hasText: `Nguyễn Văn ${t}` }).click()
    await expect(a.page).toHaveURL(new RegExp(`/users/${N.userId}$`))
  } finally {
    await a.ctx.close()
  }
})

test("E2E-04 Admin khóa X đang mở tab → tab của X về /login; X đăng nhập → 'Tài khoản đã bị khóa'", async ({
  browser,
  request,
}) => {
  await giuHanMucAuth(9)
  const t = tag()
  const admin = await taoAdmin(request, `Quản trị ${t}`)
  const X = await taoTaiKhoanCoHoSo(request, "gd6-khoa", `Khóa ${t}`)
  const a = await moNguoi(browser, admin)
  const x = await moNguoi(browser, X)
  try {
    await x.page.goto("/")
    await expect(x.page.getByRole("link", { name: "Trang của tôi" })).toBeVisible()

    await a.page.goto(`/admin/users/${X.userId}`)
    await a.page.getByRole("button", { name: "Khóa tài khoản" }).click()
    await a.page.getByLabel("Lý do (ghi vào nhật ký kiểm toán)").fill(`E2E-04 ${t}`)
    await a.page.getByRole("button", { name: "Khóa", exact: true }).click()
    await expect(a.page.getByTestId("admin-user-status")).toHaveText("Đã bị khóa")

    // Request kế tiếp của X (focus → /me) → 401 → BFF refresh → 401 → phiên hết → /login.
    await focusLai(x.page)
    await expect(x.page).toHaveURL(/\/login/)
    await x.page.getByLabel("Email").fill(X.email)
    await x.page.getByLabel("Mật khẩu").fill(X.password)
    await x.page.getByRole("button", { name: "Đăng nhập" }).click()
    await expect(x.page.getByText("Tài khoản đã bị khóa. Liên hệ quản trị viên.")).toBeVisible()
  } finally {
    await a.ctx.close()
    await x.ctx.close()
  }
})

test("E2E-05 gỡ post.create của USER → hộp thoại hiện số tài khoản → USER đăng bài lỗi; trả lại → đăng được, không tải lại", async ({
  browser,
  request,
}) => {
  await giuHanMucAuth(9)
  const t = tag()
  const admin = await taoAdmin(request, `Quản trị ${t}`)
  const U = await taoTaiKhoanCoHoSo(request, "gd6-u", `Dùng ${t}`)
  const roles = (await (await request.get(`${API}/admin/roles`, { headers: admin.auth })).json()) as {
    roleId: number
    code: string
    permissions: string[]
  }[]
  const userRole = roles.find((r) => r.code === "USER")!
  const a = await moNguoi(browser, admin)
  const u = await moNguoi(browser, U)
  try {
    await a.page.goto(`/admin/roles/${userRole.roleId}`)
    await a.page.getByRole("checkbox", { name: /post\.create/ }).click()
    await a.page.getByRole("button", { name: "Lưu quyền" }).click()
    const dialog = a.page.getByTestId("confirm-permissions")
    await expect(dialog).toContainText("tài khoản bị ảnh hưởng ngay lập tức")
    await expect(dialog.getByTestId("confirm-removed")).toContainText("post.create")
    await dialog.getByRole("button", { name: "Tiếp tục" }).click()
    await expect(a.page.getByText(/Đã lưu\. Quyền mới có hiệu lực/)).toBeVisible()

    await u.page.goto("/compose")
    await u.page.getByLabel("Nội dung").fill(`Bài khi mất quyền ${t}`)
    await u.page.getByRole("radio", { name: /Công khai/ }).click()
    await u.page.getByRole("button", { name: "Đăng bài" }).click()
    // Câu THIẾU QUYỀN, không phải "hãy tạo hồ sơ" — 403 chưa có hồ sơ mang `type` riêng từ content-v1 1.3.0-gd6.
    await expect(u.page.getByText("Tài khoản của bạn chưa được phép đăng bài.")).toBeVisible()
    await expect(u.page.getByTestId("post-created")).toHaveCount(0)

    // Trả lại quyền bằng giao diện, rồi USER bấm Đăng LẦN NỮA trên cùng trang — không tải lại.
    await a.page.getByRole("checkbox", { name: /post\.create/ }).click()
    await a.page.getByRole("button", { name: "Lưu quyền" }).click()
    await a.page.getByTestId("confirm-permissions").getByRole("button", { name: "Tiếp tục" }).click()
    await expect(a.page.getByText(/Đã lưu\. Quyền mới có hiệu lực/)).toBeVisible()

    await u.page.getByRole("button", { name: "Đăng bài" }).click()
    await expect(u.page.getByTestId("post-created")).toBeVisible({ timeout: 30_000 })
  } finally {
    // Lưới an toàn: vai trò USER của CẢ DB dev phải về đúng tập ban đầu dù test đỏ ở đâu.
    await request.put(`${API}/admin/roles/${userRole.roleId}/permissions`, {
      headers: admin.auth,
      data: { permissions: userRole.permissions, confirm: true },
    })
    await a.ctx.close()
    await u.ctx.close()
  }
})
