import { expectTypeOf, test } from "vitest"

import type {
  FriendPage,
  FriendRequestDirection,
  FriendRequestPage,
  FriendshipState,
  RelationshipResponse,
} from "../types"

// Pin vài hình dạng DỄ TRÔI của socialgraph-v1.yaml. Hợp đồng đổi một trong số này thì file này đỏ compile — không
// phải nút quan hệ nào đó vẽ sai lúc chạy trên staging.

test("FriendshipState đúng bốn giá trị, nhìn từ phía người gọi — thêm trạng thái thứ năm là màn E2 phải vẽ thêm nhánh", () => {
  expectTypeOf<FriendshipState>().toEqualTypeOf<
    "none" | "outgoing" | "incoming" | "friends"
  >()
})

test("RelationshipResponse: `friendship` và `following` là HAI trường độc lập, đều bắt buộc (Đ-4.5)", () => {
  expectTypeOf<RelationshipResponse>()
    .toHaveProperty("friendship")
    .toEqualTypeOf<FriendshipState>()
  expectTypeOf<RelationshipResponse>()
    .toHaveProperty("following")
    .toEqualTypeOf<boolean>()
})

test("FriendRequestDirection chỉ hai chiều — màn /friends gửi đúng một trong hai, không dựa vào mặc định", () => {
  expectTypeOf<FriendRequestDirection>().toEqualTypeOf<
    "incoming" | "outgoing"
  >()
})

test("nextCursor của hai trang là `string | null` — hết dữ liệu là null, KHÔNG suy từ độ dài trang (Đ-4.9)", () => {
  expectTypeOf<FriendPage>()
    .toHaveProperty("nextCursor")
    .toEqualTypeOf<string | null>()
  expectTypeOf<FriendRequestPage>()
    .toHaveProperty("nextCursor")
    .toEqualTypeOf<string | null>()
})
