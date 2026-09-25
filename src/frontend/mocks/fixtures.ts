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
  permissions: [
    "post.read.public",
    "post.read.friends",
    "post.create",
    "post.update",
    "post.delete",
    "comment.create",
    "reaction.set",
    "friend.request",
    "friend.respond",
    "message.send",
    "report.create",
  ],
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
  // GĐ3 (Đ-3.10): trường theo người xem, bắt buộc có mặt, được `null`.
  myReaction: null,
} satisfies T.PostResponse

export const postPage = {
  items: [post],
  nextCursor: null,
} satisfies T.PostPage

// --- GĐ4: socialgraph-v1.yaml + `/feed` của content-v1.yaml ---
// `example` của socialgraph-v1 dùng `0192f3c1-…2a10` cho "người kia" — trùng `userId` (người đang đăng nhập) của
// identity-v1, tức quan hệ với CHÍNH MÌNH, mà hợp đồng trả 400 cho ca đó. Nên "người kia" mặc định là tác giả trong
// `example` của `GET /feed` (`Nguyễn Văn An`), và mọi kịch bản dưới đây là id khác `userId`.

/** Người kia mặc định: tác giả bài trong `example` của `GET /feed`. Quan hệ `none`, chưa theo dõi. */
export const userIdKhac = "0192f3c0-1b2d-7e4f-8a6c-9d0e1f2a3b4c"

/**
 * Kịch bản quan hệ, chọn bằng **dữ liệu nhập** (id người kia) — nếp `SCENARIO_EMAILS` / `MEDIA_SCENARIO`: test đọc là
 * thấy nhánh nào đang chạy. Mock KHÔNG giữ trạng thái: gửi lời mời xong, `GET /relationships` vẫn trả trạng thái gốc
 * của id — ca nào cần "đọc lại ra sự thật khác" thì `server.use` riêng.
 */
export const SOCIAL_SCENARIO = {
  /** `friends`, đang theo dõi. */
  banBe: "0192f3c1-8a4e-7c31-9f2a-6b5d4e3c2a30",
  /** `incoming` — người này đã gửi lời mời cho tôi; chấp nhận được. */
  loiMoiDen: "0192f3c1-8a4e-7c31-9f2a-6b5d4e3c2a31",
  /** `outgoing` — tôi đã gửi lời mời cho người này. */
  loiMoiDi: "0192f3c1-8a4e-7c31-9f2a-6b5d4e3c2a32",
  /**
   * Đọc ra `none` nhưng gửi lời mời thì **409** — ca hai lời mời chéo nhau (US-010 AC-02): người kia vừa gửi trước
   * một nhịp. `GET` đọc lại sau 409 (qua `server.use`) mới thấy `incoming`.
   */
  daCoLoiMoi: "0192f3c1-8a4e-7c31-9f2a-6b5d4e3c2a33",
  /** Không có hồ sơ: gửi lời mời / theo dõi → **404**; `GET /relationships` → 200 `none` (không tiết lộ ai tồn tại). */
  khongTonTai: "0192f3c1-8a4e-7c31-9f2a-6b5d4e3c2a34",
} as const

export const relationship = (
  friendship: T.FriendshipState,
  following: boolean,
  otherUserId: string = userIdKhac
) =>
  ({
    userId: otherUserId,
    friendship,
    following,
  }) satisfies T.RelationshipResponse

export const userCard = {
  userId: userIdKhac,
  displayName: "Nguyễn Văn An",
  avatarUrl: null,
} satisfies T.UserCard

export const friendCard = {
  user: userCard,
  since: "2026-09-23T08:15:00Z",
} satisfies T.FriendCard

export const friendPage = {
  items: [friendCard],
  nextCursor: null,
} satisfies T.FriendPage

/** Cursor mở ra trang ngắn (Đ-4.9): `items` rỗng nhưng `nextCursor` KHÁC null — FE phải nạp tiếp, không dừng. */
export const CURSOR_TRANG_RONG = "rong-con-trang"
/** `nextCursor` mà trang rỗng ở trên trả về — trang sau đó có bài, hết dữ liệu. */
export const CURSOR_SAU_TRANG_RONG = "sau-trang-rong"
/** Cursor làm `GET /feed` trả **503** (Đ-4.10). Chỉ feed — hợp đồng socialgraph-v1 không có 503. */
export const CURSOR_QUA_TAI = "qua-tai"

/**
 * 503 của `GET /feed` — giá trị chép từ `example` của `ServiceUnavailable`. `satisfies FeedOverloadedProblem` ghim `type`
 * vào enum của hợp đồng (Q-E4): yaml đổi `type` thì mock đỏ compile, không lặng lẽ đi nhánh 5xx chung.
 */
export const feedOverloadedProblem = () =>
  ({
    ...problem(
      503,
      "Bảng tin đang quá tải",
      "Bảng tin đang có quá nhiều người truy cập. Vui lòng thử lại."
    ),
    type: "urn:socialapp:problem:feed-overloaded",
  }) satisfies T.FeedOverloadedProblem

/** Bài trong `example` của `GET /feed`: của người khác, mức `friends`, không sửa được. */
export const feedPost = {
  postId: "0192f3c1-8a4e-7c31-9f2a-6b5d4e3c2a10",
  author: userCard,
  body: "Chiều nay trời đẹp quá.",
  privacy: "friends",
  media: [],
  commentCount: 0,
  reactionCounts: {},
  createdAt: "2026-09-23T08:15:00Z",
  editedAt: null,
  canEdit: false,
  myReaction: null,
} satisfies T.PostResponse

/**
 * `mode: "network"` → `example` của hợp đồng. `mode: "suggested"` (Đ-4.6) chứa bài `public` của NGƯỜI KHÁC cộng bài của
 * CHÍNH MÌNH — bài mẫu là của người khác (`canEdit: false`), nên đổi `privacy`: bài `friends` của người khác không lọt vào gợi ý. `nextCursor` truyền vào khi test cần trang
 * sau (ví dụ `CURSOR_TRANG_RONG` để dựng ca trang rỗng giữa chừng).
 */
export const feedPage = (mode: T.FeedMode, nextCursor: string | null = null) =>
  ({
    items: [mode === "network" ? feedPost : { ...feedPost, privacy: "public" }],
    nextCursor,
    mode,
  }) satisfies T.FeedPage

// ---- GĐ3: bình luận + cảm xúc — giá trị chép từ `example` của content-v1.yaml (luật frontend Mục 8) ----

/** Bình luận gốc trong `example` của `GET /posts/{postId}/comments`: của người khác, có 2 phản hồi, người xem đã thả like. */
export const comment = {
  commentId: "0192f3d0-1a2b-7c3d-8e4f-5a6b7c8d9e01",
  postId: "0192f3c1-8a4e-7c31-9f2a-6b5d4e3c2a10",
  parentId: null,
  depth: 1,
  status: "visible",
  author: userCard,
  body: "Ảnh đẹp quá!",
  replyCount: 2,
  reactionCounts: { like: 3 },
  myReaction: "like",
  createdAt: "2026-09-24T08:20:00Z",
  canDelete: false,
} satisfies T.CommentResponse

/** Bình luận đã xóa trong cùng `example` — giữ chỗ và nhánh, không tác giả, không nội dung (Đ-3.5). */
export const deletedComment = {
  commentId: "0192f3d0-1a2b-7c3d-8e4f-5a6b7c8d9e02",
  postId: "0192f3c1-8a4e-7c31-9f2a-6b5d4e3c2a10",
  parentId: null,
  depth: 1,
  status: "deleted",
  author: null,
  body: null,
  replyCount: 1,
  reactionCounts: {},
  myReaction: null,
  createdAt: "2026-09-24T08:25:00Z",
  canDelete: false,
} satisfies T.CommentResponse

export const commentPage = {
  items: [comment, deletedComment],
  nextCursor: null,
} satisfies T.CommentPage

// --- GĐ6: moderation-v1, admin-v1, notification-v1, profile-v1 `/search` ---
// Giá trị chép từ `example` của ba hợp đồng mới; `satisfies` bắt lệch hình dạng lúc compile. Handler GĐ6 KHÔNG nằm trong
// `handlers.ts`: mỗi màn tự `server.use(...)` đúng nhánh nó cần (nếp màn chat GĐ5) — handler mặc định cho endpoint đặc quyền là
// mời test "quên" kiểm 403.

export const reportId = "0192f3ca-6e41-7a02-b3d5-8c7e9f1a2b30"
export const reportedPostId = "0192f3c9-2b7d-7e10-8c4a-1f3e5d7b9a20"

export const reportReceipt = {
  reportId,
  status: "open",
  createdAt: "2026-09-24T08:15:42.123456Z",
} satisfies T.ReportReceipt

export const reportQueueItem = {
  reportId,
  target: { type: "post", id: reportedPostId },
  reportCount: 3,
  reasons: { spam: 2, harassment: 1 },
  firstReportedAt: "2026-09-24T08:15:42.123456Z",
} satisfies T.ReportQueueItem

export const reportDetail = {
  reportId,
  target: {
    type: "post",
    id: reportedPostId,
    status: "published",
    author: { userId, displayName: "Bình Minh", avatarUrl: null },
    body: "Mua ngay khóa học làm giàu, inbox để nhận ưu đãi!",
    media: [],
    postId: reportedPostId,
    createdAt: "2026-09-24T07:00:00.000000Z",
    editedAt: null,
  },
  openReports: [
    {
      reportId,
      reasonCode: "spam",
      detail: null,
      createdAt: "2026-09-24T08:15:42.123456Z",
    },
  ],
  history: [],
} satisfies T.ReportDetail

export const auditLogItem = {
  id: 1024,
  actorId: userId,
  actor: { userId, displayName: "Kiểm duyệt viên", avatarUrl: null },
  action: "report.hide",
  targetType: "post",
  targetId: reportedPostId,
  metadata: { reportIds: [reportId], reasonCode: "spam", note: null },
  ip: "203.0.113.7",
  createdAt: "2026-09-25T08:15:42.123456Z",
} satisfies T.AuditLogItem

export const adminUser = {
  userId,
  email: "an.nguyen@example.com",
  displayName: "Nguyễn Văn An",
  roleCode: "USER",
  roleDisplayName: "Người dùng",
  status: "active",
  emailVerified: true,
  lockedUntil: null,
  createdAt: "2026-09-08T03:10:22Z",
} satisfies T.AdminUser

/** 18 mã theo `PermissionCodes.All` — `role.manage` cuối, không trao được qua API (L-D19). */
export const permissionCodes = [
  "post.read.public",
  "post.read.friends",
  "post.create",
  "post.update",
  "post.delete",
  "comment.create",
  "reaction.set",
  "friend.request",
  "friend.respond",
  "message.send",
  "report.create",
  "report.resolve",
  "post.hide",
  "user.lock",
  "user.unlock",
  "role.assign",
  "audit.read",
  "role.manage",
] as const

export const permissionInfos = permissionCodes.map(
  (code) =>
    ({
      code,
      description: `Mô tả ${code}`,
      assignable: code !== "role.manage",
    }) satisfies T.PermissionInfo
)

export const roleSummaries = [
  {
    roleId: 1,
    code: "USER",
    displayName: "Người dùng",
    isSystem: true,
    editable: true,
    userCount: 8421,
    permissions: me.permissions,
  },
  {
    roleId: 2,
    code: "MODERATOR",
    displayName: "Kiểm duyệt viên",
    isSystem: true,
    editable: true,
    userCount: 4,
    permissions: [...me.permissions, "report.resolve", "post.hide"],
  },
  {
    roleId: 3,
    code: "ADMIN",
    displayName: "Quản trị viên",
    isSystem: true,
    editable: false,
    userCount: 2,
    permissions: [...permissionCodes],
  },
  {
    roleId: 4,
    code: "REVIEWER",
    displayName: "Người rà soát",
    isSystem: false,
    editable: true,
    userCount: 0,
    permissions: ["report.resolve"],
  },
] satisfies T.RoleSummary[]

export const notificationComment = {
  notificationId: "0192f3d0-4a1b-7c2d-8e3f-9a0b1c2d3e4f",
  type: "comment",
  actor: { userId, displayName: "Nguyễn Văn An", avatarUrl: null },
  actorCount: 4,
  target: { type: "post", id: reportedPostId, postId: reportedPostId },
  reasonCode: null,
  isRead: false,
  createdAt: "2026-09-25T07:02:11.482913Z",
  updatedAt: "2026-09-25T08:15:42.123456Z",
} satisfies T.NotificationResponse

export const notificationModeration = {
  notificationId: "0192f3cf-11aa-7b22-9c33-4d44e55f6a77",
  type: "moderation",
  actor: null,
  actorCount: 1,
  target: {
    type: "post",
    id: "0192f3c2-5e6f-7a80-9b1c-2d3e4f5a6b7c",
    postId: "0192f3c2-5e6f-7a80-9b1c-2d3e4f5a6b7c",
  },
  reasonCode: "spam",
  isRead: true,
  createdAt: "2026-09-24T21:40:03.000001Z",
  updatedAt: "2026-09-24T21:40:03.000001Z",
} satisfies T.NotificationResponse

export const searchResult = {
  userId: userIdKhac,
  displayName: "Nguyễn Văn An",
  avatarUrl: null,
} satisfies T.SearchResult
