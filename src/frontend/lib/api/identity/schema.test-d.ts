import { expectTypeOf, test } from "vitest"

import type { MeResponse, RoleCode } from "../types"

// Quyết định 1 của GĐ1 (claim `role` mang `code` chuỗi) giữ nguyên. GĐ1–GĐ5 là union ba giá trị; từ GĐ6 vai trò là dữ liệu
// (Đ-6.9) nên `RoleCode` là chuỗi mở — so quyền bằng `MeResponse.permissions`, không bằng tên vai trò (Đ-6.11).
test("RoleCode là chuỗi mở từ GĐ6 — vai trò tự tạo cũng đi qua /me", () => {
  expectTypeOf<RoleCode>().toEqualTypeOf<string>()
})

test("MeResponse có permissions bắt buộc (Đ-6.11)", () => {
  expectTypeOf<MeResponse>()
    .toHaveProperty("permissions")
    .toEqualTypeOf<string[]>()
})

test("MeResponse có cả role lẫn roleDisplayName (quyết định 3)", () => {
  expectTypeOf<MeResponse>()
    .toHaveProperty("roleDisplayName")
    .toEqualTypeOf<string>()
})
