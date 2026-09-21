// Giá trị chép từ `example` của identity-v1.yaml. `satisfies` bắt lệch HÌNH DẠNG lúc compile —
// hợp đồng đổi field là file này đỏ, không phải mock im lặng trả sai.
import type * as T from "@/lib/api/types"

export const userId = "0192f3c1-8a4e-7c31-9f2a-6b5d4e3c2a10"

export const me = {
  userId,
  email: "an.nguyen@example.com",
  role: "USER",
  roleDisplayName: "Người dùng",
  emailVerifiedAt: "2026-09-08T03:14:07Z",
  status: "active",
  createdAt: "2026-09-08T03:10:22Z",
} satisfies T.MeResponse

export const registerResponse = {
  userId,
  email: "an.nguyen@example.com",
} satisfies T.RegisterResponse

export const verifyEmailResponse = {
  email: "an.nguyen@example.com",
  verifiedAt: "2026-09-08T03:14:07Z",
} satisfies T.VerifyEmailResponse

function traceId() {
  return crypto.randomUUID().replaceAll("-", "")
}

export const problem = (status: number, title: string, detail?: string) =>
  ({
    type: `https://httpstatuses.io/${status}`,
    title,
    status,
    ...(detail && { detail }),
    traceId: traceId(),
  }) satisfies T.ProblemDetails

/** 400 của hợp đồng: `errors` là map tên trường → danh sách thông điệp. */
export const validationProblem = (errors: Record<string, string[]>) =>
  ({
    ...problem(400, "Dữ liệu không hợp lệ", "Dữ liệu đầu vào không hợp lệ"),
    errors,
  }) satisfies T.ProblemDetails

// --- GĐ2: profile-v1.yaml + content-v1.yaml ---
// Giá trị chép từ `example` của hai hợp đồng; chỗ hợp đồng không có `example` (PostResponse, PostPage)
// thì dựng giá trị hợp lệ tối thiểu — `satisfies` vẫn bắt lệch hình dạng lúc compile.

/** Người dùng CHƯA onboarding: `GET /users/{id}/profile` trả 404 — tín hiệu của E2, không phải lỗi. */
export const userIdChuaCoHoSo = "0192f3c1-8a4e-7c31-9f2a-6b5d4e3c2a11"

export const profile = {
  userId,
  displayName: "An Nguyễn",
  bio: "Sinh viên năm 3, thích chụp ảnh phố.",
  avatarUrl: null,
  createdAt: "2026-09-20T02:10:22Z",
  updatedAt: "2026-09-20T02:10:22Z",
} satisfies T.ProfileResponse

export const mediaKey = `posts/${userId}/01a0b811f70376b7812581bf3209feca.jpg`
export const avatarKey = `avatars/${userId}/01a0b811f70376b7812581bf3209feca.jpg`

/**
 * Host R2 giả. Origin RIÊNG, khác `BFF_URL` — đó là cả ý nghĩa của Đ-2.5: byte ảnh không đi qua origin
 * của app. Test của E3/E4 chặn `PUT` ở đây để dựng nhánh "R2 từ chối" và nhánh "mạng/CORS/CSP".
 */
export const r2Host = "https://r2.example.test"

/** `uploadUrl` thật mang chữ ký — fixture để chuỗi giả, không bao giờ chép chữ ký thật vào repo. */
export const uploadTicket = {
  mediaKey,
  uploadUrl: `${r2Host}/socialmedia-dev/posts/anh.jpg?X-Amz-Signature=gia`,
  expiresIn: 600,
  requiredHeaders: {
    "Content-Type": "image/jpeg",
    "Content-Length": "1048576",
  },
} satisfies T.UploadTicket

/**
 * Kịch bản của luồng ảnh, chọn bằng **dữ liệu nhập** chứ không bằng cờ ẩn — đúng nếp `SCENARIO_EMAILS`
 * của GĐ1: test đọc là thấy nhánh nào đang chạy, và mock **giống server hơn** một `server.use` trả cùng
 * một mã cho mọi request.
 *
 * `sizeBytes` là chỗ móc tự nhiên nhất: nó đi từ `imageFileError` của client, qua thân presign, tới
 * `mediaKey` server ký — tức là một con số duy nhất lái được cả chuỗi ba bước.
 */
export const MEDIA_SCENARIO = {
  /**
   * File có đúng dung lượng này làm `POST /media/uploads` cấp một `mediaKey` **đã gắn vào bài khác**;
   * `POST /posts` sau đó trả **409** (BR-03). Dựng được cả chuỗi thay vì chặn thẳng ở bước cuối — nhờ
   * vậy ca test chứng minh luôn rằng client gửi lại đúng key nó vừa nhận.
   */
  sizeBytesKeyDaDung: 4_242,
} as const

/** `mediaKey` mà server coi là đã dùng ở bài khác — cấp bởi kịch bản `sizeBytesKeyDaDung`. */
export const mediaKeyDaDung = `posts/${userId}/anh-da-dung.jpg`

export const postId = "0192f3c1-8a4e-7c31-9f2a-6b5d4e3c2a20"
/** Bài không đọc được (không tồn tại / BR-02 không đạt / đã xóa mềm) — hợp đồng trả 404 cho cả ba. */
export const postIdKhongDocDuoc = "0192f3c1-8a4e-7c31-9f2a-6b5d4e3c2a21"
/** Bài không sửa/xóa được — hợp đồng trả 403 cho cả "của người khác" lẫn "đã xóa mềm". */
export const postIdKhongSuaDuoc = "0192f3c1-8a4e-7c31-9f2a-6b5d4e3c2a22"

export const post = {
  postId,
  author: { userId, displayName: "An Nguyễn", avatarUrl: null },
  body: "Chiều nay ở phố cổ.",
  privacy: "public",
  media: [],
  commentCount: 0,
  // GĐ2 luôn `{}` rỗng, KHÔNG `null` (Đ-2.12) — pin ở đây để E5 không dựng nhánh `?? {}` vô nghĩa.
  reactionCounts: {},
  createdAt: "2026-09-20T02:10:22Z",
  editedAt: null,
  canEdit: true,
} satisfies T.PostResponse

export const postPage = {
  items: [post],
  nextCursor: null,
} satisfies T.PostPage
