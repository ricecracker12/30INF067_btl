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

const RAW_COLOR =
  "Literal[value=/\\b(bg|text|border|ring|fill|stroke|from|via|to|outline|divide)-(slate|gray|zinc|neutral|stone|red|orange|amber|yellow|lime|green|emerald|teal|cyan|sky|blue|indigo|violet|purple|fuchsia|pink|rose)-\\d{2,3}\\b/]"
const ARBITRARY_COLOR = "Literal[value=/-\\[#[0-9a-fA-F]{3,8}\\]/]"

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
    // File sinh tự động — không lint (E2, Đ-E7).
    "lib/api/schema.d.ts",
  ]),
  {
    files: ["**/*.{ts,tsx}"],
    rules: {
      "no-restricted-globals": [
        "error",
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
      ],
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
