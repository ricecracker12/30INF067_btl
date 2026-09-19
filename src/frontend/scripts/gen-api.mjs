// Sinh type từ MỌI hợp đồng OpenAPI của backend — danh sách module SUY RA từ glob, không gõ tay.
//
// Vì sao không dùng một script `gen:api:<module>` cho mỗi module (luật frontend Mục 7 trước 2026-09-19):
// cách đó có BA danh sách phải khớp nhau mà không gì ép chúng khớp — hợp đồng nào tồn tại (backend),
// sinh file cho hợp đồng nào (package.json), và cổng CI kiểm file nào (ci.yml). Thiếu một dòng ở
// danh sách 2 thì file sinh KHÔNG BAO GIỜ tồn tại nên cũng không bao giờ lệch, và cổng xanh giả;
// thiếu ở danh sách 3 thì `git status --porcelain -- <path không tồn tại>` trả rỗng + exit 0, cổng
// xanh vĩnh viễn. Suy từ glob thì danh sách 2 biến mất; cổng CI kiểm cả worktree nên danh sách 3
// cũng biến mất. Thêm module = thả file .yaml vào, không sửa gì ở đây lẫn ở ci.yml.
//
// Chạy: pnpm gen:api    (từ src/frontend)
// Phần lập kế hoạch (planJobs) là hàm thuần, có unit test ở scripts/gen-api.test.ts.

import { execFileSync } from "node:child_process"
import { globSync, mkdirSync } from "node:fs"
import path from "node:path"
import { pathToFileURL } from "node:url"

/** Gốc chứa hợp đồng, tương đối với src/frontend. */
export const CONTRACT_ROOT = "../backend/Modules"

/** Một hợp đồng cho mỗi nhóm Swagger, tên file trùng tên nhóm (giai-doan-2.md Mục 8). */
export const CONTRACT_GLOB = "*/Presentation/*-v1.yaml"

/**
 * Lập kế hoạch sinh — hàm THUẦN, không chạm đĩa, để unit test không cần openapi-typescript.
 *
 * Đích suy từ TÊN NHÓM Swagger (tên file bỏ hậu tố phiên bản), không từ tên thư mục module:
 * `Identity/Presentation/identity-v1.yaml` → `lib/api/identity/schema.d.ts`. Tên nhóm đã buộc phải duy
 * nhất toàn app (mỗi nhóm là một `SwaggerDoc` trong Program.cs), nên một module có hai nhóm vẫn ra hai
 * file riêng thay vì ghi đè nhau. Với mọi module đã lên kế hoạch, tên nhóm trùng tên module.
 *
 * @param {readonly string[]} contracts đường dẫn tương đối với CONTRACT_ROOT, dạng
 *   `<Module>/Presentation/<nhóm>-v1.yaml`; nhận cả dấu `\` của Windows.
 * @returns {Map<string, { yaml: string, source: string }>} đích → { đường dẫn yaml để chạy, nguồn để báo lỗi }
 */
export function planJobs(contracts) {
  // "0 hợp đồng" KHÔNG phải kết quả sạch — cùng luật với RunConfiguration.TreatNoTestsAsError=true ở
  // cổng Contract/AuthZ của .NET. Đổi chỗ thư mục hay gõ hỏng glob phải ĐỎ ngay, chứ không phải sinh
  // 0 file rồi báo thành công và để cổng codegen xanh trên hư không.
  if (contracts.length === 0) {
    throw new Error(
      `Không thấy hợp đồng nào khớp ${CONTRACT_ROOT}/${CONTRACT_GLOB} — kiểm lại đường dẫn trong scripts/gen-api.mjs`
    )
  }

  /** @type {Map<string, { yaml: string, source: string }>} */
  const jobs = new Map()

  for (const relative of [...contracts].sort()) {
    const file = relative.split(/[\\/]/).at(-1) ?? relative
    const group = file.replace(/\.yaml$/, "").replace(/-v\d+$/, "")
    const out = `lib/api/${group}/schema.d.ts`

    // Hai file cùng tên nhóm (ví dụ content-v1.yaml và content-v2.yaml cạnh nhau) sẽ cùng đổ vào một
    // đích và cái sau ghi đè cái trước mà không ai biết. Thà đỏ ở đây — TRƯỚC khi sinh file nào — còn
    // hơn để type của bản kia lặng lẽ biến mất.
    const previous = jobs.get(out)
    if (previous) {
      throw new Error(
        `Hai hợp đồng cùng sinh ra ${out}: ${previous.source} và ${relative}\n` +
          "Mỗi nhóm Swagger một tên file duy nhất; đổi phiên bản hợp đồng thì thay file, không đặt cạnh nhau."
      )
    }
    jobs.set(out, {
      yaml: path.join(CONTRACT_ROOT, relative),
      source: relative,
    })
  }

  return jobs
}

function main() {
  /** @type {Map<string, { yaml: string, source: string }>} */
  let jobs
  try {
    jobs = planJobs(globSync(CONTRACT_GLOB, { cwd: CONTRACT_ROOT }))
  } catch (error) {
    console.error(error instanceof Error ? error.message : String(error))
    process.exit(1)
  }

  // Gọi thẳng bin bằng process.execPath, KHÔNG gọi "pnpm exec": trên Windows `pnpm` là shim .cmd nên
  // execFileSync không có shell sẽ ENOENT, còn bật shell:true thì mở ra chuyện trích dẫn đường dẫn.
  const cli = path.resolve("node_modules/openapi-typescript/bin/cli.js")

  for (const [out, { yaml, source }] of jobs) {
    mkdirSync(path.dirname(out), { recursive: true })
    execFileSync(process.execPath, [cli, yaml, "-o", out], { stdio: "inherit" })
    console.log(
      `${path.posix.join(CONTRACT_ROOT, source.split(path.sep).join("/"))} → ${out}`
    )
  }
}

// Chỉ chạy khi được gọi trực tiếp (`node scripts/gen-api.mjs`), không chạy khi test import planJobs.
if (
  process.argv[1] &&
  import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href
) {
  main()
}
