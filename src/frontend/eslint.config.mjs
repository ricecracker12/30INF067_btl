import { defineConfig, globalIgnores } from "eslint/config"
import nextVitals from "eslint-config-next/core-web-vitals"
import nextTs from "eslint-config-next/typescript"

// Flat config: override cùng tên rule THAY hẳn options, không cộng dồn — nên pattern gốc
// phải tách ra hằng số và spread lại ở mọi override, nếu không features/** mất lệnh cấm Đ-E12.
const KIT = [
  {
    group: ["@base-ui/react", "@base-ui/react/*"],
    message: "Đ-E12: dùng components/ui, không gọi Base UI trực tiếp.",
  },
  {
    group: [
      "react-icons",
      "react-icons/*",
      "@tabler/*",
      "@heroicons/*",
      "@phosphor-icons/*",
    ],
    message: "Đ-E12: icon chỉ lucide-react.",
  },
]

// Q-E3 — `fetch` không phải cửa duy nhất ra khỏi origin: `XMLHttpRequest` cũng đi được, và E3/E4 mở nó
// ra thật (`lib/upload/r2.ts`). Tách thành hằng để override của `lib/upload/**` dựng lại danh sách này
// mà KHÔNG có XHR — flat config THAY hẳn options của rule cùng tên, không cộng dồn (luật FE Mục 10).
const RESTRICTED_GLOBALS = [
  {
    name: "localStorage",
    message: "Đ-E2: token chỉ ở memory (Mục 12).",
  },
  {
    name: "sessionStorage",
    message: "Đ-E2: token chỉ ở memory (Mục 12).",
  },
  {
    name: "fetch",
    message:
      "Đ-E2/Đ-E14: trình duyệt gọi BFF qua lib/api/http.ts; server gọi API qua lib/bff/upstream.ts.",
  },
]

const XHR_RESTRICTED = {
  name: "XMLHttpRequest",
  message:
    "Q-E3: chỉ lib/upload/r2.ts được gọi ra ngoài origin (PUT lên R2, Đ-2.5); mọi nơi khác đi qua lib/api/http.ts.",
}

const RAW_COLOR =
  "Literal[value=/\\b(bg|text|border|ring|fill|stroke|from|via|to|outline|divide)-(slate|gray|zinc|neutral|stone|red|orange|amber|yellow|lime|green|emerald|teal|cyan|sky|blue|indigo|violet|purple|fuchsia|pink|rose)-\\d{2,3}\\b/]"
const ARBITRARY_COLOR = "Literal[value=/-\\[#[0-9a-fA-F]{3,8}\\]/]"

// Tài nguyên có vòng đời (AbortController, WebSocket, observer, Worker…) KHÔNG được khởi tạo ở THAM SỐ
// của `useRef`. `<StrictMode>` của Next dev mount → unmount → mount lại trên CÙNG một instance: cleanup
// của lần mount đầu hủy tài nguyên, rồi lần mount thứ hai `useRef` trả về ĐÚNG cái vừa bị hủy, vì nó chỉ
// dùng tham số ở lần render ĐẦU TIÊN. Đã xảy ra thật ở `use-upload-queue.ts`: ba spec E2E đỏ trong khi
// 447 ca Vitest vẫn xanh, vì `render()` của Testing Library chỉ mount MỘT lần. Tạo trong effect; ref chỉ
// là hộp đựng.
//
// Chặn MỌI `new`, không liệt kê danh sách loại tài nguyên: danh sách thì loại chưa có trong đó lọt qua
// im lặng — đúng kiểu "cổng xanh giả" mà luật FE Mục 7 đã bỏ một lần ở `gen-api.mjs` (đổi 2026-09-19).
// `components/ui/**` KHÔNG bị rule này (khối cuối tắt `no-restricted-syntax` cho kit): kit sinh bởi CLI,
// không sửa tay, nên bắt nó đỏ là chặn chính `shadcn add` mà Đ-E12 bắt dùng.
const USE_REF_NEW =
  "CallExpression[callee.name='useRef'] > NewExpression," +
  "CallExpression[callee.property.name='useRef'] > NewExpression"

const eslintConfig = defineConfig([
  ...nextVitals,
  ...nextTs,
  // Override default ignores of eslint-config-next.
  globalIgnores([
    // Default ignores of eslint-config-next:
    ".next/**",
    "out/**",
    "build/**",
    "next-env.d.ts",
    // File sinh tự động — không lint (E2, Đ-E7). Glob chứ không liệt kê từng module: thêm
    // module mà quên thêm dòng thì file sinh bị lint, và không ai sửa được nó (đổi 2026-09-19).
    "lib/api/**/schema.d.ts",
  ]),
  {
    files: ["**/*.{ts,tsx}"],
    rules: {
      "no-restricted-globals": ["error", ...RESTRICTED_GLOBALS, XHR_RESTRICTED],
      "no-restricted-properties": [
        "error",
        { object: "window", property: "localStorage", message: "Đ-E2" },
        { object: "window", property: "sessionStorage", message: "Đ-E2" },
        {
          object: "document",
          property: "cookie",
          message: "Đ-E2: cookie refresh là HttpOnly, JS không đụng",
        },
      ],
      "react/no-danger": "error",
      "no-restricted-imports": ["error", { patterns: KIT }],
      "no-restricted-syntax": [
        "error",
        {
          selector: RAW_COLOR,
          message:
            "Đ-E12: dùng token (bg-primary, text-muted-foreground, text-destructive…), không màu thô.",
        },
        { selector: ARBITRARY_COLOR, message: "Đ-E12: không mã màu tùy ý." },
        {
          selector: USE_REF_NEW,
          message:
            "Tài nguyên có vòng đời không khởi tạo bằng useRef(new Thing()) — StrictMode mount lại trả về đúng cái đã hủy. Tạo trong effect, ref chỉ là hộp đựng.",
        },
      ],
    },
  },
  // Đ-E13 — ranh giới bốn tầng. Mỗi feature tự import trong thư mục mình bằng đường dẫn
  // tương đối; đụng tới feature khác thì phải đi qua alias và bị chặn ở đây.
  {
    files: ["features/**"],
    rules: {
      "no-restricted-imports": [
        "error",
        {
          patterns: [
            ...KIT,
            {
              group: ["@/features/*"],
              message:
                "Đ-E13: features/ không import chéo nhau — đẩy phần dùng chung xuống components/ hoặc lib/.",
            },
          ],
        },
      ],
    },
  },
  {
    files: ["lib/**", "components/**"],
    rules: {
      "no-restricted-imports": [
        "error",
        {
          patterns: [
            ...KIT,
            {
              group: ["@/features", "@/features/*", "@/app", "@/app/*"],
              message:
                "Đ-E13: lib/ và components/ không biết nghiệp vụ, không import ngược lên.",
            },
          ],
        },
      ],
    },
  },
  // Q-E3 — ngoại lệ DUY NHẤT của lệnh cấm XHR, và phải đứng SAU khối chung. Danh sách dựng lại từ
  // `RESTRICTED_GLOBALS` mà bỏ XHR: `lib/upload/` vẫn không được `fetch` ra ngoài origin, vẫn không
  // được Web Storage. Một dòng `eslint-disable` tại chỗ trong `r2.ts` nhắc lại vì sao (Đ-2.5).
  {
    files: ["lib/upload/**"],
    rules: {
      "no-restricted-globals": ["error", ...RESTRICTED_GLOBALS],
    },
  },
  // PHẢI đứng cuối: `components/ui/**` khớp cả `components/**` ở trên, mà flat config lấy
  // khối SAU. Đảo lên trước là kit bị cấm import `@base-ui/react` — tức là cấm chính thứ
  // nó được phép dùng.
  {
    files: ["components/ui/**"],
    rules: {
      "no-restricted-imports": "off",
      "no-restricted-syntax": "off",
    },
  },
])

export default eslintConfig
