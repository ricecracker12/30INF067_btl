import { expect, test } from "@playwright/test"

import { API, giuHanMucAuth } from "./dev-api"
import {
  dangNhapUi,
  donBai,
  taoBaiApi,
  taoTaiKhoanCoHoSo,
} from "./post-helpers"

// BR-02 + Mục 7.4 nhìn từ phía người KHÔNG được xem. Đây là ca bảo mật của khối E: server trả CÙNG một
// 404 cho "không tồn tại", "đã xóa" và "không được xem", và FE không được nói khác đi — nói khác là dùng
// status code để khai tài nguyên nào có thật.
//
// Bài `private` của A là cách dựng ca "tồn tại nhưng không được xem" mà không cần kết bạn (GĐ4).

test("tài khoản B mở bài private của A: MỘT câu, không lộ bài có tồn tại", async ({
  page,
  request,
}) => {
  // A: register + verify + login API. B: register + verify + login API + login UI.
  await giuHanMucAuth(7)
  const a = await taoTaiKhoanCoHoSo(request, "fbd-a", "An Chủ Bài")
  const idRieng = await taoBaiApi(request, a, "Bài riêng của A", "private")
  const idCong = await taoBaiApi(request, a, "Bài công khai của A", "public")

  const b = await taoTaiKhoanCoHoSo(request, "fbd-b", "Bình Người Lạ")
  await dangNhapUi(page, b)

  // 1) Bài private của A: không xem được.
  await page.goto(`/posts/${idRieng}`)
  await expect(page.getByTestId("post-not-found")).toBeVisible()
  const cauKhongXemDuoc =
    (await page.getByTestId("post-not-found").textContent()) ?? ""

  // 2) Bài KHÔNG TỒN TẠI: phải ra ĐÚNG CÙNG MỘT CÂU. Đây là cả phép thử — hai câu khác nhau ở đây nghĩa
  //    là người lạ phân biệt được "bài này có thật mà tôi không được xem" với "không có bài nào".
  await page.goto("/posts/0192f3c1-8a4e-7c31-9f2a-6b5d4e3c2a99")
  await expect(page.getByTestId("post-not-found")).toBeVisible()
  const cauKhongTonTai =
    (await page.getByTestId("post-not-found").textContent()) ?? ""
  expect(cauKhongXemDuoc).toBe(cauKhongTonTai)

  // 3) Không có nút Sửa/Xóa ở đâu cả — và bài công khai của A thì B xem được, nhưng `canEdit: false`.
  await page.goto(`/posts/${idCong}`)
  await expect(page.getByTestId("post-card")).toContainText("Bài công khai")
  await expect(page.getByRole("button", { name: "Sửa" })).toHaveCount(0)
  await expect(page.getByRole("button", { name: "Xóa" })).toHaveCount(0)

  // 4) Danh sách bài của A nhìn từ B: chỉ thấy bài công khai, KHÔNG thấy bài private.
  await page.goto(`/users/${a.userId}`)
  await expect(page.getByTestId("post-card")).toHaveCount(1)
  await expect(page.getByTestId("post-card")).toContainText("Bài công khai")

  // 5) API trực tiếp bằng token của B: 404, cùng mã với bài không tồn tại.
  const rieng = await request.get(`${API}/posts/${idRieng}`, {
    headers: b.auth,
  })
  expect(rieng.status()).toBe(404)

  await donBai(request, a, [idRieng, idCong])
})
