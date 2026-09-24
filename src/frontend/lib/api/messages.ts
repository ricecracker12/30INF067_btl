import {
  ApiError,
  hasProblemType,
  NetworkError,
  PROBLEM_TYPES,
  type ProblemType,
} from "./problem"

// Đ-E6: thông điệp lỗi do FE sở hữu, ánh xạ theo (màn/endpoint, status). `detail` của server chỉ là
// dự phòng cho status chưa có trong bảng — hiện `detail` cho 401 đăng nhập là để AC-02 phụ thuộc vào
// việc server không bao giờ sửa một chữ ở nhánh "email không tồn tại", mà không test backend nào canh.

/**
 * Màn gọi API. Tách theo **endpoint**, không theo module (Q-E4, lệch B.7): `errorMessage` ánh xạ theo
 * `(ngữ cảnh, status)`, mà 403 mang nghĩa khác hẳn nhau trên các endpoint của cùng một module —
 * `POST /posts` 403 là "chưa có hồ sơ", `PATCH /posts/{id}` 403 là "không phải bài của bạn",
 * `PUT /users/me/avatar` 403 là "khóa ảnh không phải của bạn". Gộp ba cái đó thành một ngữ cảnh
 * `post` thì câu nào cũng sai với hai endpoint còn lại.
 */
export type ErrorContext =
  | "login"
  | "register"
  | "verify-email"
  | "me"
  | "profile-read"
  | "profile-write"
  | "avatar"
  | "upload"
  | "post-create"
  | "post-read"
  | "post-write"
  // GĐ4 (Q-E3, lệch B.7): năm ngữ cảnh — `relationship` cho mọi lời gọi quan hệ KHÔNG có mã riêng.
  | "relationship"
  | "friend-request"
  | "friend-respond"
  | "follow"
  | "feed"
  // GĐ5 (E1): năm ngữ cảnh — 403 mang nghĩa khác nhau trên từng endpoint của Messaging (nếp Q-E4).
  | "conversation-open"
  | "conversation-read"
  | "message-send"
  | "receipt"
  | "realtime-ticket"
  // GĐ3 (E1): bốn ngữ cảnh — 403 của `comment-create` là "chưa có hồ sơ", của `comment-delete` là "không phải của bạn"
  // (cùng lý do Q-E4 của GĐ2).
  | "comment-read"
  | "comment-create"
  | "comment-delete"
  | "reaction"

const COMMON = {
  400: "Dữ liệu không hợp lệ.",
  429: "Bạn thao tác quá nhanh. Vui lòng thử lại sau ít phút.",
  network: "Không kết nối được máy chủ.",
  unexpected: "Đã xảy ra lỗi không mong muốn.",
} as const

const BY_CONTEXT: Record<ErrorContext, Partial<Record<number, string>>> = {
  login: {
    // MỘT câu cho cả "email không tồn tại" lẫn "sai mật khẩu" (AC-02). Không thêm "sai lần thứ N" —
    // FE không biết, đoán là lộ thông tin.
    401: "Email hoặc mật khẩu không đúng.",
    // GĐ1 không có endpoint gửi lại mail → không có nút "Gửi lại".
    403: "Tài khoản chưa xác minh email. Vui lòng mở liên kết trong thư chúng tôi đã gửi.",
    // Không đếm ngược: FE không biết `locked_until`.
    423: "Tài khoản tạm khóa do đăng nhập sai nhiều lần. Vui lòng thử lại sau 15 phút.",
  },
  register: {
    // Ngoại lệ CÓ Ý THỨC của hợp đồng so với AC-02: không báo trùng thì người dùng không biết vì sao
    // đăng ký hỏng. Form hiện câu này dưới trường email, kèm link "Đăng nhập".
    409: "Email này đã được đăng ký.",
  },
  "verify-email": {
    // Cùng câu cho token sai dạng (validator, có `errors.token`) và token không tồn tại (không có
    // `errors`) — người dùng làm cùng một việc: mở lại đúng liên kết.
    400: "Liên kết xác minh không hợp lệ. Hãy mở lại đúng liên kết trong thư, không sao chép thiếu ký tự.",
    // 410 có HAI nghĩa: hết hạn hoặc đã dùng. Câu phải đúng cho cả hai — người bấm link lần hai là ca phổ
    // biến nhất, và họ đăng nhập được. GĐ1 không có endpoint gửi lại mail → không nhắc "gửi lại".
    410: "Liên kết xác minh đã hết hạn hoặc đã được sử dụng. Nếu bạn đã xác minh trước đó, hãy đăng nhập.",
  },
  // `/me` không có mã riêng: 401 là việc của interceptor (E7), còn lại dùng nhánh chung.
  me: {},

  // --- GĐ2 (Q-E4) ---

  // 404 của `GET /users/{userId}/profile` KHÔNG phải lỗi khi userId là chính mình: đó là tín hiệu
  // onboarding (Đ-2.4) và màn E2 phân nhánh trước khi tới đây. Để trống có chủ đích — đặt một câu
  // ở đây là mời màn khác hiện "không tìm thấy hồ sơ" cho người vừa đăng ký.
  "profile-read": {},

  // `PUT /users/me/profile` chỉ có 400 (theo `errors.displayName`/`errors.bio`) — nhánh chung đủ.
  "profile-write": {},

  // 403 = `mediaKey` nằm dưới tiền tố của người khác, HOẶC người gọi chưa có hồ sơ (Q-D9). Hai ca
  // không phân biệt được từ ngoài, nên một câu phải đúng cho cả hai: việc người dùng làm là chọn lại ảnh.
  avatar: {
    403: "Ảnh này không thuộc về bạn. Hãy chọn lại ảnh.",
  },

  // `POST /media/uploads` 403 = thiếu quyền `post.create` (Đ-2.6). Không nêu tên quyền — người dùng
  // không làm gì được với chuỗi đó.
  upload: {
    403: "Tài khoản của bạn chưa được phép đăng bài.",
  },

  "post-create": {
    403: "Bạn cần hoàn tất hồ sơ trước khi đăng bài.",
    // 409: `mediaKey` đã gắn vào một bài khác (BR-03). Ảnh đó không dùng lại được.
    409: "Ảnh này đã được dùng trong một bài khác. Hãy chọn lại ảnh.",
  },

  "post-read": {
    404: "Không tìm thấy bài viết.",
  },

  // MỘT câu cho cả "bài của người khác" lẫn "bài đã xóa mềm" — hợp đồng trả 403 cho cả hai, và FE
  // không được tiết lộ bài có tồn tại hay không (B.7 E6, Mục 12 nhóm Bảo mật).
  "post-write": {
    403: "Không tìm thấy bài viết, hoặc bạn không có quyền với bài này.",
  },

  // --- GĐ4 (Q-E3) ---

  // `GET /relationships/{id}`, `GET /friends`, `GET /friends/requests`, ba `DELETE` (lời mời, bạn bè, theo dõi):
  // không có mã riêng (chỉ 400/401/429/5xx) — nhánh chung đủ. Để trống CÓ CHỦ ĐÍCH: mượn `friend-request` cho một
  // lời gọi `GET` là mời người sau thêm câu 409 "đã có lời mời" vào một màn đọc.
  relationship: {},

  // `POST /friends/requests`. 404 và 409 chép NGUYÊN VĂN `detail` của socialgraph-v1.yaml — một lỗi không hiện hai
  // cách nói (Đ-E5). 403 = thiếu quyền `friend.request`; không nêu tên quyền, như `upload`.
  "friend-request": {
    403: "Tài khoản của bạn chưa được phép kết bạn.",
    404: "Không tìm thấy người dùng.",
    409: "Đã có lời mời hoặc quan hệ bạn bè giữa hai người.",
  },

  // `POST /friends/requests/{id}/accept`. MỘT câu cho mọi lý do (Đ-4.14): không có lời mời · lời mời của chính mình
  // · đã là bạn · người thứ ba — server cố ý không phân biệt, FE không được đoán.
  "friend-respond": {
    403: "Lời mời này không còn hiệu lực.",
  },

  // `PUT /follows/{id}`. 403 dùng chung quyền `friend.request` (Đ-4.12) nhưng người dùng đang bấm "Theo dõi" — nói
  // đúng việc họ vừa làm.
  follow: {
    403: "Tài khoản của bạn chưa được phép theo dõi người khác.",
    404: "Không tìm thấy người dùng.",
  },

  // `GET /feed`: câu 503 "quá tải" KHÔNG nằm ở đây mà ở `BY_TYPE` (Q-E4, chốt 2026-09-23) — 503 không mang
  // `feed-overloaded` (trang HTML của apache, proxy hỏng) là lỗi hệ thống thật, đi nhánh `>= 500` như mọi màn.
  feed: {},

  // --- GĐ5 (E1) ---

  // `POST /conversations`. 403 `not-friends` đi qua BY_TYPE; 403 còn lại = thiếu quyền `message.send`. 404 chép `detail`.
  "conversation-open": {
    403: "Tài khoản của bạn chưa được phép nhắn tin.",
    404: "Không tìm thấy người dùng.",
  },

  // `GET /conversations/{id}`, `…/messages`, danh sách, badge. 403 = không phải thành viên HOẶC không tồn tại — MỘT câu cho cả
  // hai, không tiết lộ hội thoại có thật hay không (Mục 6.1).
  "conversation-read": {
    403: "Không tìm thấy cuộc trò chuyện, hoặc bạn không có quyền xem.",
  },

  // `POST …/messages` (fallback REST) — 403 `not-friends` đi qua BY_TYPE. 409 là bug client (dùng lại clientMsgId cho nội dung
  // khác, Đ-5.5): người dùng không làm gì được ngoài gửi lại như tin mới.
  "message-send": {
    403: "Bạn không thể gửi tin vào cuộc trò chuyện này.",
    409: "Tin nhắn bị trùng mã. Hãy gửi lại như một tin mới.",
  },

  // `POST …/receipts` — lỗi biên nhận không hiện cho người dùng (màn nuốt, lần xem sau gửi lại mốc lớn hơn). Để trống.
  receipt: {},

  // `POST /realtime/tickets` — 503 đi qua BY_TYPE (fallback). 429: xin vé quá nhanh (20/phút) — vẫn chat được qua fallback.
  "realtime-ticket": {},

  // --- GĐ3 (E1) ---

  // `GET /posts/{id}/comments`, `GET /comments/{id}/replies`. 404 = bài/bình luận không có HOẶC không được xem — MỘT câu,
  // không tiết lộ thứ nào có thật (Đ-3.3).
  "comment-read": {
    404: "Không tìm thấy bài viết hoặc bình luận.",
  },

  // `POST /posts/{id}/comments`. 403 = chưa có hồ sơ (kiểm TRƯỚC BR-02, Mục 6.1). 404 = bài không còn xem được.
  "comment-create": {
    403: "Bạn cần hoàn tất hồ sơ trước khi bình luận.",
    404: "Bài viết không còn tồn tại hoặc bạn không còn quyền xem.",
  },

  // `DELETE /comments/{id}`. 403 = không phải của bạn HOẶC đã xóa — một câu (Đ-3.5).
  "comment-delete": {
    403: "Bình luận không còn tồn tại, hoặc không phải của bạn.",
  },

  // `PUT`/`DELETE …/reactions/me`. 404 = đối tượng không còn (bị xóa, hoặc bài vừa đổi sang riêng tư).
  reaction: {
    404: "Nội dung này không còn tồn tại.",
  },
}

/**
 * Câu theo `type` của Problem Details (GĐ4 Q-E4) — chạy TRƯỚC `BY_CONTEXT`, cho MỌI ngữ cảnh: cùng một status mang hai
 * nghĩa mà người dùng làm hai việc khác nhau. Cả hai KHÔNG kèm `traceId` (không phải lỗi hệ thống) và KHÔNG đếm ngược
 * (Đ-4.10: không hứa thời điểm, không đọc `Retry-After`).
 */
const BY_TYPE: Record<ProblemType, string> = {
  [PROBLEM_TYPES.feedOverloaded]:
    "Bảng tin đang quá tải. Vui lòng thử lại sau ít phút.",
  // BFF mất Redis phiên: không phải "bạn bị đăng xuất" — phiên vẫn còn, chỉ tạm không đọc được.
  [PROBLEM_TYPES.bffSessionUnavailable]:
    "Dịch vụ đăng nhập tạm thời gián đoạn. Vui lòng thử lại sau ít phút.",
  // GĐ5 — câu chép NGUYÊN VĂN `detail` của messaging-v1.yaml (Đ-E5: một lỗi không hiện hai cách nói).
  [PROBLEM_TYPES.notFriends]: "Hai bạn không còn là bạn bè. Hội thoại chỉ đọc.",
  [PROBLEM_TYPES.realtimeUnavailable]:
    "Kênh thời gian thực tạm thời không sẵn sàng. Tin nhắn vẫn gửi được.",
}

/** Thông điệp cấp form cho một lỗi bất kỳ ném ra từ `request()`. */
export function errorMessage(context: ErrorContext, error: unknown): string {
  if (error instanceof NetworkError) return COMMON.network
  if (!(error instanceof ApiError)) return COMMON.unexpected

  const byType = Object.values(PROBLEM_TYPES).find((t) =>
    hasProblemType(error, t)
  )
  if (byType) return BY_TYPE[byType]

  const known = BY_CONTEXT[context][error.status]
  if (known) return known
  if (error.status === 400) return COMMON[400]
  if (error.status === 429) return COMMON[429]

  // 5xx: không đoán nguyên nhân; traceId là thứ duy nhất backend cần để tìm log. 502 HTML của
  // apache không có traceId → chỉ câu chung.
  if (error.status >= 500) {
    return error.traceId
      ? `${COMMON.unexpected} Mã tra cứu: ${error.traceId}`
      : COMMON.unexpected
  }

  return error.problem?.detail ?? COMMON.unexpected
}

/**
 * Tách lỗi 400 theo trường (Đ-E5: mọi 400 hiển thị theo key của `errors`). Trả về lỗi cho các
 * trường màn đang có, và `formMessage` khi còn lỗi không gắn được vào trường nào (`body`, key lạ,
 * hoặc 400 không có `errors`) — không để lỗi nào biến mất lặng lẽ.
 */
export function validationErrors<K extends string>(
  error: ApiError,
  fields: readonly K[]
): { fields: Partial<Record<K, string>>; formMessage: string | null } {
  const result: Partial<Record<K, string>> = {}
  let unmatched = false

  for (const [key, messages] of Object.entries(error.fieldErrors)) {
    const field = fields.find((f) => f === key)
    const first = messages[0]
    if (field && first) result[field] = first
    else unmatched = true
  }

  const matchedAny = Object.keys(result).length > 0
  return {
    fields: result,
    formMessage: unmatched || !matchedAny ? COMMON[400] : null,
  }
}

/**
 * Câu cho bước `PUT` thẳng lên R2 — CỐ ĐỊNH, không đi qua `errorMessage`: R2 không phải API của mình, nó
 * không trả Problem Details, và trình duyệt cố ý không cho biết là mạng, CORS hay CSP (ca ISS-02).
 *
 * Nằm ở đây chứ không ở màn nào, dù `r2.ts` ghi "mỗi màn nói một câu khác": câu này không phải của màn
 * mà của ĐƯỜNG TRUYỀN — avatar (E3) và composer (E4) hỏng vì cùng ba nghi phạm đó và người dùng làm cùng
 * một việc. Chép sang feature thứ hai là hai chỗ để lệch nhau.
 */
export const R2_PUT_FAILED =
  "Không tải được ảnh lên. Kiểm tra kết nối rồi thử lại."

/**
 * Các key mà `errors` của Problem Details dùng ở các hợp đồng (GĐ1, GĐ2, GĐ4). Union chứ không `string`: `fieldMessage`
 * đọc `error.fieldErrors[key]`, nên một key gõ nhầm (`"file"` thay vì `"files"`) im lặng lùi về bảng chung
 * và người dùng mất đúng câu server muốn nói. Thêm key mới ở `.yaml` thì thêm một dòng ở đây.
 */
export type FieldErrorKey =
  | "body"
  | "files"
  | "mediaKey"
  | "mediaKeys"
  | "privacy"
  | "displayName"
  | "bio"
  | "token"
  | "email"
  | "password"
  | "cursor"
  | "limit"
  // GĐ4 — socialgraph-v1: tự gửi lời mời / tự theo dõi → `errors.userId`; `direction` lạ → `errors.direction`.
  | "userId"
  | "direction"
  // GĐ5 — messaging-v1: nội dung tin, mã tin phía client, tham số lịch sử và biên nhận.
  | "content"
  | "clientMsgId"
  | "afterSeq"
  | "kind"
  | "upToSeq"
  // GĐ3 — content-v1: cha của phản hồi (Đ-3.4) và loại cảm xúc.
  | "parentId"
  | "type"

/**
 * 400 của một endpoint đọc ĐÚNG CÂU SERVER dưới key của `errors` trước, RỒI MỚI lùi về bảng
 * `errorMessage` (Đ-E5: mọi 400 hiển thị theo key của `errors`).
 *
 * Vì sao cần: `errorMessage` ánh xạ theo `(ngữ cảnh, status)` nên mọi 400 ra đúng một câu
 * `"Dữ liệu không hợp lệ."`, che mất những câu server duy nhất người dùng dùng được — "Ảnh chưa được tải
 * lên xong…", "Một ảnh không được đính kèm hai lần.". Dùng khi màn chỉ có MỘT chỗ hiện lỗi cho cả lời gọi
 * (bước presign, bước gắn ảnh); form nhiều trường dùng `validationErrors` để chia về từng ô.
 */
export function fieldMessage(
  error: unknown,
  key: FieldErrorKey,
  context: ErrorContext
): string {
  if (error instanceof ApiError && error.status === 400) {
    const first = error.fieldErrors[key]?.[0]
    if (first) return first
  }
  return errorMessage(context, error)
}
