// @vitest-environment node
import { expect, test } from "vitest"

import { planJobs } from "./gen-api.mjs"

// Kiểm phần lập kế hoạch của gen:api mà không chạy openapi-typescript. Đây là chỗ cổng codegen đã
// từng chỉ chạy với MỘT hợp đồng (identity) — đường `lib/api/<nhóm>/` với nhiều module phải được
// khẳng định ở đây, mỗi lần chạy test, chứ không chỉ một lần thử tay lúc thêm module thứ hai.

test("mỗi hợp đồng ra một file dưới lib/api/<nhóm>/, bỏ hậu tố phiên bản", () => {
  const jobs = planJobs([
    "Profile/Presentation/profile-v1.yaml",
    "Identity/Presentation/identity-v1.yaml",
    "Content/Presentation/content-v1.yaml",
  ])

  expect([...jobs.keys()]).toEqual([
    "lib/api/content/schema.d.ts",
    "lib/api/identity/schema.d.ts",
    "lib/api/profile/schema.d.ts",
  ])
  expect(jobs.get("lib/api/identity/schema.d.ts")?.yaml).toMatch(
    /backend[\\/]Modules[\\/]Identity[\\/]Presentation[\\/]identity-v1\.yaml$/
  )
})

test("nhận đường dẫn kiểu Windows (globSync trả dấu \\ trên Windows)", () => {
  const jobs = planJobs(["Identity\\Presentation\\identity-v1.yaml"])
  expect([...jobs.keys()]).toEqual(["lib/api/identity/schema.d.ts"])
})

test("không có hợp đồng nào thì ném — một số không không phải là một lần qua", () => {
  expect(() => planJobs([])).toThrow(/Không thấy hợp đồng nào/)
})

test("hai hợp đồng cùng đích thì ném và nêu cả hai nguồn, trước khi sinh file nào", () => {
  expect(
    () =>
      planJobs([
        "Content/Presentation/content-v1.yaml",
        "Content/Presentation/content-v2.yaml",
      ])
    // Không dùng cờ `s` (dotAll): tsconfig target ES2017, cờ đó là ES2018 → tsc TS1501.
  ).toThrow(/content-v1\.yaml[\s\S]*content-v2\.yaml/)
})
