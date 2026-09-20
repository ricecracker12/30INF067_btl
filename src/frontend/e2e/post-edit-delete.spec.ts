import { expect, test } from "@playwright/test"

import { giuHanMucAuth } from "./dev-api"
import {
  dangNhapUi,
  donBai,
  taoBaiApi,
  taoTaiKhoanCoHoSo,
} from "./post-helpers"

// E6 trên trình duyệt thật. Hai điều chỉ đo được ở đây: nhãn "đã chỉnh sửa" đến từ `editedAt` mà SERVER
// đóng dấu, và URL của bài vừa xóa phải ra trang "không tìm thấy" — không phải lỗi 500, và không phải
// một trang trắng.

test("sửa → nhãn 'đã chỉnh sửa'; xóa → mở lại URL cũ ra trang không tìm thấy", async ({
  page,
  request,
}) => {
  await giuHanMucAuth(4)
  const tk = await taoTaiKhoanCoHoSo(request, "edit", "An Sửa Xóa")
  const idSua = await taoBaiApi(request, tk, "Bài sẽ sửa")
  const idXoa = await taoBaiApi(request, tk, "Bài sẽ xóa")

  const patches: string[] = []
  page.on("request", (r) => {
    if (r.method() === "PATCH") patches.push(r.postData() ?? "")
  })

  await dangNhapUi(page, tk)

  // --- SỬA: chỉ đổi mức riêng tư ---
  await page.goto(`/posts/${idSua}`)
  await page.getByRole("button", { name: "Sửa" }).click()
  await page.getByRole("radio", { name: /Chỉ mình tôi/ }).click()
  await page.getByRole("button", { name: "Lưu" }).click()

  await expect(page.getByTestId("edited-badge")).toBeVisible()
  await expect(page.getByTestId("post-card")).toContainText("Chỉ mình tôi")
  // Gửi cả hai trường không sai hợp đồng, nhưng `edited_at` bị đóng dấu cho thay đổi không tồn tại.
  expect(patches).toEqual([JSON.stringify({ privacy: "private" })])

  // Tải lại: nhãn đến từ `editedAt` của server, không phải state trong trang.
  await page.reload()
  await expect(page.getByTestId("edited-badge")).toBeVisible()

  // --- XÓA: rời trang, rồi mở lại URL cũ ---
  await page.goto(`/posts/${idXoa}`)
  await page.getByRole("button", { name: "Xóa" }).click()
  await page.getByRole("button", { name: "Xóa bài" }).click()
  await page.waitForURL(/\/me$/)

  const lai = await page.goto(`/posts/${idXoa}`)
  // Trang render BÌNH THƯỜNG (200); 404 là của API và FE dịch thành một câu.
  expect(lai?.status()).toBe(200)
  await expect(page.getByTestId("post-not-found")).toBeVisible()
  await expect(page.getByText("Không tìm thấy bài viết.")).toBeVisible()
  // 404 là câu trả lời cuối cùng — không mời người dùng thử lại vô ích.
  await expect(page.getByRole("button", { name: "Thử lại" })).toHaveCount(0)

  await donBai(request, tk, [idSua, idXoa])
})
