import { expect, type APIRequestContext, type Page } from "@playwright/test"

import { API, taoTaiKhoanDaXacMinh } from "./dev-api"

// Dùng chung cho các spec của GĐ2 (E8). Đặt ở đây thay vì chép vào bốn file: mỗi bản chép là một chỗ để
// lệch khi hợp đồng đổi — và bốn spec đều cần đúng một thứ, "một tài khoản đã có hồ sơ".

/** Ảnh THẬT trong repo (Q-E8). Không sinh lúc chạy: ảnh sinh động dễ không phải JPEG hợp lệ, và lỗi khi
 * đó trông hệt lỗi CORS — tức là nghi phạm sai. */
export const ANH_JPG = "e2e/fixtures/anh-nho.jpg"
export const ANH_PNG = "e2e/fixtures/anh-nho.png"

export type TaiKhoan = {
  email: string
  password: string
  userId: string
  /** Bearer token cho các lời gọi API trực tiếp của spec — KHÔNG bao giờ đi qua trình duyệt (Đ-E14). */
  auth: { Authorization: string }
}

/** Đăng nhập qua API để lấy token cho phần dựng dữ liệu của spec. Tốn 1 lượt `/auth/*`. */
export async function dangNhapApi(
  request: APIRequestContext,
  email: string,
  password: string
): Promise<TaiKhoan> {
  const res = await request.post(`${API}/auth/login`, {
    data: { email, password },
  })
  expect(res.status(), await res.text()).toBe(200)
  const { accessToken } = (await res.json()) as { accessToken: string }
  const auth = { Authorization: `Bearer ${accessToken}` }

  const me = await request.get(`${API}/me`, { headers: auth })
  expect(me.status()).toBe(200)
  const { userId } = (await me.json()) as { userId: string }

  return { email, password, userId, auth }
}

/**
 * Tài khoản đã xác minh **và đã có hồ sơ** — điều kiện để vào được `(with-profile)`. Tốn **3** lượt
 * `/auth/*` (register + verify + login); người gọi khai vào `giuHanMucAuth`.
 */
export async function taoTaiKhoanCoHoSo(
  request: APIRequestContext,
  prefix: string,
  displayName: string
): Promise<TaiKhoan> {
  const { email, password } = await taoTaiKhoanDaXacMinh(request, prefix)
  const tk = await dangNhapApi(request, email, password)

  const res = await request.put(`${API}/users/me/profile`, {
    headers: tk.auth,
    data: { displayName, bio: null },
  })
  expect(res.status(), await res.text()).toBe(200)
  return tk
}

/** Đăng nhập trên UI (qua BFF) và chờ tới màn đã đăng nhập. Tốn 1 lượt `/auth/*`. */
export async function dangNhapUi(page: Page, tk: TaiKhoan) {
  await page.goto("/login?next=%2Fme")
  await page.getByLabel("Email").fill(tk.email)
  await page.getByLabel("Mật khẩu").fill(tk.password)
  await page.getByRole("button", { name: "Đăng nhập" }).click()
  await page.waitForURL(/\/me$/)
}

/** Tạo một bài qua API — nhanh hơn gõ trên UI khi bài chỉ là **tiền đề** của điều đang kiểm. */
export async function taoBaiApi(
  request: APIRequestContext,
  tk: TaiKhoan,
  body: string,
  privacy: "public" | "friends" | "private" = "public"
): Promise<string> {
  const res = await request.post(`${API}/posts`, {
    headers: tk.auth,
    data: { body, privacy, mediaKeys: [] },
  })
  expect(res.status(), await res.text()).toBe(201)
  return ((await res.json()) as { postId: string }).postId
}

/**
 * Dọn bài sau spec (cạm bẫy Mục 9: "E2E để lại bài rác trên bucket `-dev`").
 *
 * Xóa mềm là tất cả FE/API làm được — object trên R2 do worker dọn sau (Đ-2.13). 403 ở đây là bài đã
 * xóa rồi, không phải lỗi của spec, nên không `expect` gì.
 */
export async function donBai(
  request: APIRequestContext,
  tk: TaiKhoan,
  postIds: readonly string[]
) {
  for (const id of postIds) {
    await request.delete(`${API}/posts/${id}`, { headers: tk.auth })
  }
}
