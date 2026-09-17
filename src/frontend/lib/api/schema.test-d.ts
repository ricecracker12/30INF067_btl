import { expectTypeOf, test } from "vitest"

import type { MeResponse, RoleCode } from "./types"

test("RoleCode là union chuỗi đúng hợp đồng (quyết định 1)", () => {
  expectTypeOf<RoleCode>().toEqualTypeOf<"USER" | "MODERATOR" | "ADMIN">()
})

test("MeResponse có cả role lẫn roleDisplayName (quyết định 3)", () => {
  expectTypeOf<MeResponse>()
    .toHaveProperty("roleDisplayName")
    .toEqualTypeOf<string>()
})
