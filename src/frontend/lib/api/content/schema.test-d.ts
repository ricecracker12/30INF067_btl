import { expectTypeOf, test } from "vitest"

import type { PostMedia, PostPage, PostPrivacy, PostResponse } from "../types"

// Pin vài hình dạng DỄ TRÔI của content-v1.yaml. Hợp đồng đổi một trong số này thì file này đỏ
// compile — không phải màn nào đó đỏ lúc chạy trên staging.

test("PostPage.nextCursor là `string | null` — hết dữ liệu là null, KHÔNG phải chuỗi rỗng (Đ-2.11)", () => {
  expectTypeOf<PostPage>()
    .toHaveProperty("nextCursor")
    .toEqualTypeOf<string | null>()
})

test("reactionCounts là object, KHÔNG nullable — GĐ2 luôn `{}` rỗng (Đ-2.12)", () => {
  expectTypeOf<PostResponse>()
    .toHaveProperty("reactionCounts")
    .toEqualTypeOf<{ [key: string]: number }>()
})

test("canEdit do server tính và luôn có mặt — FE không tự so author.userId (Mục 8.2)", () => {
  expectTypeOf<PostResponse>()
    .toHaveProperty("canEdit")
    .toEqualTypeOf<boolean>()
})

test("PostPrivacy là union ba mức chữ thường, khớp CHECK ck_posts_privacy", () => {
  expectTypeOf<PostPrivacy>().toEqualTypeOf<"public" | "friends" | "private">()
})

test("PostMedia có `url` đã ký và KHÔNG có `mediaKey` — key là chi tiết nội bộ (Đ-2.9)", () => {
  expectTypeOf<PostMedia>().toHaveProperty("url").toEqualTypeOf<string>()
  expectTypeOf<PostMedia>().not.toHaveProperty("mediaKey")
})
