import { expect, test } from "@playwright/test"

import { giuHanMucAuth } from "./dev-api"
import {
  ANH_JPG,
  ANH_PNG,
  dangNhapUi,
  donBai,
  taoTaiKhoanCoHoSo,
} from "./post-helpers"

// E4 trên trình duyệt thật với **ảnh thật** và **bucket R2 thật** (`socialmedia-dev`). Đây là ca duy nhất
// chứng minh được ISS-02 đã đóng: ba nghi phạm của bước `PUT` (CORS bucket, chữ ký, CSP) chỉ tồn tại ở
// trình duyệt thật và cho CÙNG một triệu chứng. Không mock nào thay được (Mục 1.2 luật 4).

test("đăng bài với 2 ảnh thật: PUT thẳng lên R2, bài hiện đủ 2 ảnh", async ({
  page,
  request,
}) => {
  await giuHanMucAuth(4)
  const tk = await taoTaiKhoanCoHoSo(request, "create", "An Đăng Bài")
  const daTao: string[] = []

  const denR2: string[] = []
  const quaBff: string[] = []
  page.on("request", (r) => {
    const u = new URL(r.url())
    if (u.hostname.includes("r2.cloudflarestorage.com"))
      denR2.push(`${r.method()} ${u.hostname}`)
    else if (u.pathname.startsWith("/bff/"))
      quaBff.push(`${r.method()} ${u.pathname}`)
  })

  await dangNhapUi(page, tk)
  await page.goto("/compose")

  await page.getByLabel("Nội dung").fill("Hai ảnh thật từ e2e/fixtures.")
  await page.getByRole("radio", { name: /Công khai/ }).click()
  await page.getByLabel(/^Ảnh \(tối đa/).setInputFiles([ANH_JPG, ANH_PNG])

  const dong = page.getByTestId("upload-row")
  await expect(dong).toHaveCount(2)
  await expect
    .poll(
      async () =>
        (
          await dong.evaluateAll((els) =>
            els.map((e) => (e as HTMLElement).dataset.status)
          )
        ).filter((s) => s === "xong").length,
      { timeout: 60_000 }
    )
    .toBe(2)

  const dang = page.getByRole("button", { name: "Đăng bài" })
  await expect(dang).toBeEnabled()
  await dang.click()
  await expect(page.getByTestId("post-created")).toBeVisible({
    timeout: 30_000,
  })

  // Đ-2.5: byte ảnh đi THẲNG lên R2, không qua origin của app. `MAX_PROXY_BODY` = 1 MB là chỗ nó cố ý vỡ.
  expect(denR2.filter((r) => r.startsWith("PUT"))).toHaveLength(2)
  expect(quaBff.filter((r) => r.includes("/media/uploads"))).toHaveLength(1)

  // Bài hiện trên /me với đủ 2 ảnh, và ảnh TẢI ĐƯỢC (presigned GET + `img-src` của CSP đều đúng).
  await page.goto("/me")
  const card = page.getByTestId("post-card").first()
  await expect(card).toContainText("Hai ảnh thật")
  await expect(card.getByTestId("post-image")).toHaveCount(2)
  await expect
    .poll(async () =>
      card
        .getByTestId("post-image")
        .first()
        .evaluate((img) => (img as HTMLImageElement).naturalWidth)
    )
    .toBeGreaterThan(0)

  const postId = await card.getAttribute("data-post-id")
  if (postId) daTao.push(postId)
  // Dọn: bài rác trên bucket `-dev` phình dần nếu spec nào cũng để lại (cạm bẫy Mục 9).
  await donBai(request, tk, daTao)
})
