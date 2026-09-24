import { mkdirSync } from "node:fs"
import { join } from "node:path"

import { expect, test, type Page } from "@playwright/test"

import { API, giuHanMucAuth } from "./dev-api"
import {
  dangNhapApi,
  dangNhapUi,
  donRacSauTest,
  taiKhoanCoSan,
  taoBaiApi,
  taoTaiKhoanCoHoSo,
  type TaiKhoan,
} from "./post-helpers"

// GĐ3 E6 / F3 — lát cắt bình luận + cảm xúc hai trình duyệt (giai-doan-3.md Mục 10.5, Mục 12 "Lát cắt dọc"). Chạy trên API + FE
// thật, Chrome đã cài (Đ-E8), `workers: 1`. Hai `BrowserContext` — mỗi người một cookie `__Host-sid`.
//
// Cây 3 cấp do HAI người tạo qua giao diện: B bình luận → A trả lời (cấp 2) → B trả lời (cấp 3, không còn nút Trả lời) → A xóa
// bình luận GIỮA nhánh → nhánh còn. Rồi B thả / đổi / gỡ cảm xúc trên bài và bình luận, bấm tim 10 lần liên tục không 429.
// Mọi nội dung mang hậu tố ngẫu nhiên của lượt chạy.

test.afterEach(async ({ request }) => {
  await donRacSauTest(request)
})

/** `E2E_BANG_CHUNG=<thư mục>` thì chụp ảnh ở từng mốc (F3). Không đặt thì không chụp. Không chụp ở `/me` (lộ email). */
const BANG_CHUNG = process.env.E2E_BANG_CHUNG
async function chup(page: Page, ten: string) {
  if (!BANG_CHUNG) return
  mkdirSync(BANG_CHUNG, { recursive: true })
  await page.screenshot({
    path: join(BANG_CHUNG, `gd3-${ten}.png`),
    fullPage: true,
  })
}

/**
 * `li` của ĐÚNG bình luận có nội dung `text` — không dùng `filter({ hasText })`: `li` của cha chứa cả nhánh con, nên nó cũng
 * "có chữ" của con và locator khớp hai phần tử. Neo vào đoạn văn của chính bình luận rồi lấy `li` gần nhất.
 */
const binhLuan = (page: Page, text: string) =>
  page
    .getByText(text, { exact: true })
    .locator('xpath=ancestor::li[@data-testid="comment"][1]')

async function vietBinhLuan(page: Page, text: string) {
  const box = page.getByTestId("comment-composer")
  await box.getByLabel("Viết bình luận").fill(text)
  await box.getByRole("button", { name: "Bình luận" }).click()
  await expect(binhLuan(page, text)).toHaveCount(1)
}

async function traLoi(page: Page, cha: string, text: string) {
  // Nút của CHÍNH bình luận đứng trước mọi nút của nhánh con trong DOM → `.first()`.
  await binhLuan(page, cha)
    .getByRole("button", { name: "Trả lời" })
    .first()
    .click()
  const box = page.getByTestId("reply-composer")
  await box.getByRole("textbox").fill(text)
  await box.getByRole("button", { name: "Trả lời" }).click()
  await expect(page.getByText(text, { exact: true })).toBeVisible()
}

test("hai tài khoản: cây 3 cấp → cấp 3 không Trả lời → xóa giữa nhánh vẫn còn nhánh → thả/đổi/gỡ cảm xúc → bấm tim 10 lần không 429", async ({
  browser,
  request,
}) => {
  test.setTimeout(180_000)
  const tag = Math.random().toString(36).slice(2, 8)
  const coSan = taiKhoanCoSan()
  let A: TaiKhoan
  let B: TaiKhoan
  if (coSan) {
    // Staging: login API hai người + login UI hai người → 4 lượt `/auth/*`.
    await giuHanMucAuth(4)
    A = await dangNhapApi(request, coSan.E2E_A_EMAIL, coSan.E2E_A_PASSWORD)
    B = await dangNhapApi(request, coSan.E2E_B_EMAIL, coSan.E2E_B_PASSWORD)
  } else {
    // register + verify + login API (3 lượt mỗi người) + login UI của cả hai → 8 lượt `/auth/*`.
    await giuHanMucAuth(8)
    A = await taoTaiKhoanCoHoSo(request, "gd3-a", `An ${tag}`)
    B = await taoTaiKhoanCoHoSo(request, "gd3-b", `Bình ${tag}`)
  }
  // Bài public của A — tiền đề, tạo qua API. Tài khoản có sẵn (staging) không vào danh sách dọn tự động → xóa ở `finally`.
  const postId = await taoBaiApi(request, A, `Bài GĐ3 ${tag}`, "public")

  const ctxA = await browser.newContext()
  const ctxB = await browser.newContext()
  try {
    const pa = await ctxA.newPage()
    const pb = await ctxB.newPage()
    await dangNhapUi(pa, A)
    await dangNhapUi(pb, B)

    // 1. B bình luận (cấp 1).
    const C1 = `B hỏi ${tag}`
    await pb.goto(`/posts/${postId}`)
    await vietBinhLuan(pb, C1)
    await expect(pb.getByTestId("comment-total")).toHaveText("1 bình luận")

    // 2. A trả lời (cấp 2).
    const C2 = `A trả lời ${tag}`
    await pa.goto(`/posts/${postId}`)
    await expect(binhLuan(pa, C1)).toHaveCount(1)
    await traLoi(pa, C1, C2)

    // 3. B nạp lại, mở nhánh, trả lời A (cấp 3) → bình luận cấp 3 KHÔNG có nút Trả lời (BR-08).
    const C3 = `B trả lời cấp 3 ${tag}`
    await pb.reload()
    await binhLuan(pb, C1)
      .getByRole("button", { name: "Xem 1 phản hồi" })
      .click()
    await expect(binhLuan(pb, C2)).toHaveCount(1)
    await traLoi(pb, C2, C3)
    const cap3 = pb
      .locator(`[data-testid="comment"][data-depth="3"]`)
      .filter({ hasText: C3 })
    await expect(cap3).toHaveCount(1)
    await expect(cap3.getByRole("button", { name: "Trả lời" })).toHaveCount(0)
    await chup(pb, "1-cay-3-cap")

    // 4. A xóa bình luận GIỮA nhánh (cấp 2 của chính A) → "đã bị xóa", nhánh con vẫn mở được.
    await pa.reload()
    await binhLuan(pa, C1)
      .getByRole("button", { name: "Xem 1 phản hồi" })
      .click()
    const cua_A = binhLuan(pa, C2)
    await cua_A.getByRole("button", { name: "Xóa" }).first().click()
    await pa
      .getByRole("alertdialog")
      .getByRole("button", { name: "Xóa bình luận" })
      .click()
    await expect(pa.getByTestId("comment-removed")).toHaveText(
      "Bình luận đã bị xóa."
    )
    await expect(pa.getByText(C2, { exact: true })).toHaveCount(0)

    // B nạp lại: dòng đã xóa vẫn đúng chỗ, cấp 3 của B vẫn đọc được. Response của nhánh KHÔNG mang body/author của dòng đã xóa (F2).
    await pb.reload()
    // Nhánh của C1 chứa chính dòng đã xóa — soi response của ĐÚNG lượt mở này.
    const repliesRes = pb.waitForResponse((r) =>
      /\/bff\/api\/comments\/[0-9a-f-]{36}\/replies/.test(r.url())
    )
    await binhLuan(pb, C1)
      .getByRole("button", { name: "Xem 1 phản hồi" })
      .click()
    const body = (await (await repliesRes).json()) as {
      items: { status: string; body: string | null; author: unknown }[]
    }
    const daXoa = body.items.find((c) => c.status === "deleted")
    expect(daXoa).toBeDefined()
    expect(daXoa!.body).toBeNull()
    expect(daXoa!.author).toBeNull()
    // Dòng đã xóa vẫn giữ "Xem 1 phản hồi" → mở ra thấy cấp 3 của B.
    await pb
      .getByTestId("comment-removed")
      .locator("..")
      .getByRole("button", { name: "Xem 1 phản hồi" })
      .click()
    await expect(pb.getByText(C3, { exact: true })).toBeVisible()
    await chup(pb, "2-xoa-giua-nhanh")

    // 5. B thả / đổi / gỡ cảm xúc trên BÀI, rồi thả trên BÌNH LUẬN của mình.
    const reactOfPost = pb.locator(
      `[data-testid="reaction-bar"][data-target="post:${postId}"]`
    )
    await reactOfPost
      .getByRole("button", { name: "Thích", exact: true })
      .click()
    await expect(reactOfPost.getByTestId("reaction-count")).toHaveText(
      /1 cảm xúc/
    )
    await reactOfPost.getByRole("button", { name: "Chọn cảm xúc" }).click()
    await pb.getByRole("dialog").getByRole("button", { name: "Haha" }).click()
    await expect(
      reactOfPost.getByRole("button", { name: "Bỏ cảm xúc Haha" })
    ).toHaveAttribute("aria-pressed", "true")
    await reactOfPost.getByRole("button", { name: "Bỏ cảm xúc Haha" }).click()
    await expect(reactOfPost.getByTestId("reaction-count")).toHaveText(
      /0 cảm xúc/
    )

    await binhLuan(pb, C1)
      .getByRole("button", { name: "Thích", exact: true })
      .first()
      .click()
    await expect(
      binhLuan(pb, C1).getByTestId("reaction-count").first()
    ).toHaveText(/1 cảm xúc/)

    // 6. Bấm tim 10 lần liên tục: không 429, trạng thái cuối đúng lần bấm cuối (chẵn → không thả), nạp lại vẫn vậy.
    const statuses: number[] = []
    pb.on("response", (r) => {
      if (r.url().includes(`/bff/api/posts/${postId}/reactions/me`))
        statuses.push(r.status())
    })
    for (let i = 0; i < 10; i++)
      await reactOfPost.getByRole("button", { name: /Thích/ }).click()
    await expect(
      reactOfPost.getByRole("button", { name: "Thích", exact: true })
    ).toHaveAttribute("aria-pressed", "false")
    await pb.waitForTimeout(1_500) // chuỗi request tuần tự về hết
    expect(statuses).not.toContain(429)
    console.log(
      `[GĐ3 E6] 10 lần bấm tim → ${statuses.length} request (${statuses.join(",")})`
    )
    await pb.reload()
    const sauReload = pb.locator(
      `[data-testid="reaction-bar"][data-target="post:${postId}"]`
    )
    await expect(
      sauReload.getByRole("button", { name: "Thích", exact: true })
    ).toHaveAttribute("aria-pressed", "false")
    await expect(sauReload.getByTestId("reaction-count")).toHaveText(
      /0 cảm xúc/
    )
    await chup(pb, "3-cam-xuc")

    // 7. Feed của A: bài có thanh cảm xúc + số bình luận (2 còn hiển thị: C1 và C3) dẫn tới chi tiết.
    await pa.goto(`/users/${A.userId}`)
    const theBai = pa
      .getByTestId("post-card")
      .filter({ hasText: `Bài GĐ3 ${tag}` })
    await expect(theBai.getByTestId("comment-count")).toHaveText(/2 bình luận/)
  } finally {
    await ctxA.close()
    await ctxB.close()
    await request.delete(`${API}/posts/${postId}`, { headers: A.auth })
  }
})
