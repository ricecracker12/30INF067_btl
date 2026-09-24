import { expectTypeOf, test } from "vitest"

import type {
  CommentPage,
  CommentResponse,
  CreateCommentRequest,
  FeedMode,
  FeedPage,
  PostAuthor,
  PostMedia,
  PostPage,
  PostPrivacy,
  PostResponse,
  ReactionSummary,
  ReactionType,
} from "../types"

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

// --- GĐ4: `GET /feed` ---

test("FeedPage.nextCursor là `string | null` — trang ngắn, kể cả rỗng, vẫn có thể còn trang sau (Đ-4.9)", () => {
  expectTypeOf<FeedPage>()
    .toHaveProperty("nextCursor")
    .toEqualTypeOf<string | null>()
})

test("FeedPage.mode là `network | suggested` và bắt buộc — nhãn gợi ý đọc từ đây, không suy từ items (Đ-4.6)", () => {
  expectTypeOf<FeedMode>().toEqualTypeOf<"network" | "suggested">()
  expectTypeOf<FeedPage>().toHaveProperty("mode").toEqualTypeOf<FeedMode>()
})

test("FeedPage.items là PostResponse — GĐ3 thêm field vào bài thì feed tự có, không khai lại", () => {
  expectTypeOf<FeedPage>()
    .toHaveProperty("items")
    .toEqualTypeOf<PostResponse[]>()
})

// --- GĐ3: bình luận + cảm xúc ---

test("PostResponse.myReaction bắt buộc có mặt, được null — trường theo người xem (Đ-3.10)", () => {
  expectTypeOf<PostResponse>()
    .toHaveProperty("myReaction")
    .toEqualTypeOf<ReactionType | null>()
})

test("ReactionType là union sáu loại chữ thường, khớp CHECK ck_reactions_type", () => {
  expectTypeOf<ReactionType>().toEqualTypeOf<
    "like" | "love" | "haha" | "wow" | "sad" | "angry"
  >()
})

test("CommentResponse.author và body nullable — bình luận đã xóa không lộ ai viết, viết gì (Đ-3.5)", () => {
  expectTypeOf<CommentResponse>()
    .toHaveProperty("author")
    .toEqualTypeOf<PostAuthor | null>()
  expectTypeOf<CommentResponse>()
    .toHaveProperty("body")
    .toEqualTypeOf<string | null>()
  expectTypeOf<CommentResponse>()
    .toHaveProperty("myReaction")
    .toEqualTypeOf<ReactionType | null>()
})

test("CommentPage.nextCursor là `string | null` — cùng quy ước cursor với danh sách bài", () => {
  expectTypeOf<CommentPage>()
    .toHaveProperty("nextCursor")
    .toEqualTypeOf<string | null>()
})

test("ReactionSummary.reactionCounts là object KHÔNG nullable — FE đối chiếu optimistic với số thật (Đ-3.7)", () => {
  expectTypeOf<ReactionSummary>()
    .toHaveProperty("reactionCounts")
    .toEqualTypeOf<{ [key: string]: number }>()
})

test("CreateCommentRequest không có depth — server tính cấp từ cha (Đ-3.4)", () => {
  expectTypeOf<CreateCommentRequest>().not.toHaveProperty("depth")
})
