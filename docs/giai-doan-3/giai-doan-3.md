# GĐ 3 — Tương tác: bình luận 3 cấp + cảm xúc (UC-06, UC-07) · lịch gốc Ngày 13–15, làm sau GĐ4

> Nguồn: [`ke-hoach-trien-khai.md`](../ke-hoach-trien-khai.md) mục "GĐ 3 — Tương tác", nhịp cổng mở / cổng đóng ở Mục 0C,
> và báo cáo PTTK v5.0 (Mục 5.5 schema ENT-03/ENT-05, 6.7.2 ma trận quyền, FR-007, FR-008, BR-05, BR-08).
> Nền móng: [`giai-doan-2.md`](../giai-doan-2/giai-doan-2.md) — GĐ3 **tiêu thụ** module Content, khung bảng `comments` +
> `reactions`, cursor keyset, BR-02 lúc đọc và khuôn tầng 3 của GĐ2. Không dựng lại thứ nào.
>
> **Ba mốc không lùi được của giai đoạn này:**
> 1. **Bộ đếm khớp bản ghi thật dưới tải đồng thời.** `posts.reaction_counts` và `comment_count` phải bằng đúng
>    `GROUP BY` trên bảng gốc sau 50 request song song. Lệch một đơn vị là lỗi không bao giờ tự lành (Đ-3.8).
> 2. **Bình luận thừa kế BR-02 của bài.** Không một đường nào (đọc, viết, thả cảm xúc) chạm được vào bài mà người gọi
>    không được xem — kể cả qua `commentId` của một bình luận nằm trong bài đó (Đ-3.3).
> 3. **Hợp đồng `content-v1` mở lại theo kiểu chỉ-thêm.** Không đổi, không xóa một trường nào mà GĐ2 đã đóng băng;
>    cổng `API contract` và codegen FE là thứ chứng minh (Đ-3.10).

## Tài liệu này có hai phần

| Phần | Trả lời câu hỏi | Đọc khi |
|---|---|---|
| **A — Thiết kế và quyết định** | *Cái gì* và *vì sao* | Trước khi gõ dòng đầu tiên; lúc review PR |
| **B — Kế hoạch triển khai** | *Ai làm gì, theo thứ tự nào* | Lúc chia việc; lúc kiểm tiến độ |

Hướng dẫn thi công từng bước (file nào, lệnh nào, cạm bẫy nào) nằm ở các file `huong-dan-khoi-*.md` cùng thư mục,
**viết khi khối đó bắt đầu** — đúng nếp GĐ1 và GĐ2, không viết trước rồi để lệch với thực tế thi công.

> **Trạng thái các quyết định:** mười bốn quyết định ở Mục 3 là **đề xuất**, viết ngày 2026-09-21 trên nền code nhánh
> `loveart1210` (sau PR #20). Cổng mở chốt hoặc sửa từng cái; cái nào sửa thì ghi ngày và lý do ngay dưới quyết định đó,
> đúng cách GĐ2 ghi "Chốt 2026-09-19 (`Q-…`)".

---

# Phần A — Thiết kế và quyết định

## 0. Thuật ngữ

| Từ | Nghĩa trong tài liệu này |
|---|---|
| **bình luận gốc** | Bình luận trả lời thẳng vào bài: `parent_id IS NULL`, `depth = 1` |
| **phản hồi** | Bình luận trả lời một bình luận khác: `depth` 2 hoặc 3 |
| **nhánh** | Một bình luận cùng toàn bộ phản hồi bên dưới nó |
| **đối tượng (target)** | Thứ nhận cảm xúc: một bài (`post`) hoặc một bình luận (`comment`) |
| **bộ đếm** | Cột phi chuẩn hóa: `posts.comment_count`, `posts.reaction_counts`, `comments.reply_count`, `comments.reaction_counts` |
| **`myReaction`** | Cảm xúc của **chính người gọi** trên đối tượng, `null` nếu chưa thả. Tính theo từng người xem — không bao giờ cache chung |
| **optimistic update** | FE đổi giao diện ngay khi bấm, gửi request sau, và quay về trạng thái đã được server xác nhận nếu request lỗi |
| **tầng 1/2/3** | AuthN · RBAC `[RequirePermission]` · ownership ở service — `giai-doan-1.md` Mục 6 |

## 1. Mục tiêu giai đoạn

### Phát biểu một câu

> **Người dùng thật trên staging bình luận và trả lời tới 3 cấp dưới một bài họ được xem, xóa bình luận của mình mà
> nhánh con vẫn còn, thả / đổi / gỡ đúng một cảm xúc trên bài hay bình luận — và mọi con số hiển thị luôn khớp với dữ
> liệu thật, kể cả khi nhiều người bấm cùng lúc.**

### Mục tiêu chính thức và khối nào gánh

| Mã | Mục tiêu | Đạt bằng | Kiểm bằng |
|---|---|---|---|
| **FR-007** | Bình luận ≤ 1000 ký tự, trả lời tối đa 3 cấp, xóa giữ nhánh | A1–A3, D1–D4 | CMT-01..10 |
| **BR-08** | Tối đa 3 cấp bình luận | A1 (hàm thuần) + A2 (CHECK) + D3 | CMT-03, CMT-04 |
| **FR-008** | Thả / đổi / gỡ cảm xúc trên bài và bình luận, cập nhật bộ đếm | C1–C4, D5–D6 | REACT-01..08 |
| **BR-05** | Một người, một đối tượng, một cảm xúc | PK ba cột (có từ GĐ2) + C2 | REACT-02, COUNT-02 |
| **BR-02** | Bình luận và cảm xúc thừa kế quyền xem bài, đánh giá lúc đọc | D0 (một hàm chung) | READ-CMT-01..03, READ-REACT-01..02 |
| **GOAL-03** | Không IDOR ở bình luận và cảm xúc | Khuôn tầng 3 của GĐ2 | TC-A03-comment + 6 dòng matrix mới |

### Vì sao GĐ3 nặng hơn vẻ ngoài

Kế hoạch gốc cho GĐ3 hai ngày và gọi nó là "phần mở rộng, cắt xuống mức tối thiểu được". Ba thứ làm nó khác một bài
CRUD thứ hai:

1. **Đây là lần đầu hệ thống có bộ đếm phi chuẩn hóa bị nhiều người ghi đồng thời.** GĐ2 ghi `media_count` đúng một lần
   lúc tạo bài, do đúng một người. GĐ3 thì 50 người cùng thả tim vào một bài. Cách viết "đọc lên, cộng một, ghi xuống"
   của EF là mất cập nhật ngay lập tức, và không test đơn luồng nào bắt được.
2. **Đây là lần đầu có tài nguyên con thừa kế quyền của tài nguyên cha.** Bình luận không có mức riêng tư riêng — nó
   hiện được khi và chỉ khi bài hiện được. Mọi endpoint nhận `commentId` phải lần ngược về bài để hỏi BR-02; quên ở một
   endpoint là lộ nội dung bài `private` qua một đường không ai nhìn.
3. **Đây là lần đầu frontend phải nói dối người dùng một cách có kiểm soát.** Optimistic update hiện kết quả trước khi
   server xác nhận; làm sai thì con số nhảy lung tung khi mạng chậm, hoặc tự bắn 429 khi người dùng bấm liên tục.

### GĐ3 đứng ở đâu trong lịch

**Chốt 2026-09-21: GĐ3 làm SAU GĐ4**, đúng thứ tự của kế hoạch gốc, và **một người** làm cả backend lẫn frontend. Lý do
chọn thứ tự: GĐ4 nằm trên đường găng (GĐ5 cần `friendships` cho BR-09) và chứa GOAL-01, còn GĐ3 thì cắt được và không
giai đoạn nào chờ nó.

Hệ quả cho tài liệu này — GĐ3 khởi động trên nền GĐ4 đã xong:

| GĐ4 để lại | GĐ3 dùng thế nào |
|---|---|
| `IFriendshipReader` thật | Bài `friends` của bạn thật sự hiện ra → `READ-CMT-*`, `READ-REACT-*` kiểm được **cả hai** nhánh: người lạ (404) và bạn (thấy) |
| Một hàm hydrate batch cho mọi đường trả `PostResponse` (GĐ4 `C3`) | `myReaction` thêm **vào đúng hàm đó** (Đ-3.11) → feed, trang cá nhân, chi tiết bài có trường mới cùng lúc |
| Cache feed chỉ lưu `post_id`, test `FEED-13` hai người xem (GĐ4 Đ-4.9) | Mở rộng `FEED-13` thêm khẳng định `myReaction`; không đụng cache |
| Trang chủ ráp `FeedList` + **`PostItem`** ở `app/(app)/(with-profile)/page.tsx` qua `renderPost(post, onChanged)` (GĐ4 Đ-4.16, lệch L4 — `PostItem` vì feed có bài của chính mình, cần Sửa/Xóa) | Cắm thanh cảm xúc vào đúng dòng `renderPost` đó (Đ-3.13); không sửa `features/feed/` |
| `hooks/use-cursor-pages.ts` — hook phân trang cursor dùng chung, không biết nghiệp vụ (GĐ4 Q-E8) | Danh sách bình luận / phản hồi theo cursor dùng lại hook này; ref mà handler đọc đồng bộ bằng `useLayoutEffect` (luật frontend Mục 9) |
| Feed gợi ý có bài của **chính mình** mọi mức (GĐ4 Đ-4.6 đổi 2026-09-23) | Thanh cảm xúc trên bài của mình xuất hiện cả khi người dùng chưa có kết nối — `READ-REACT-*` không phụ thuộc chế độ feed |
| Báo cáo k6 sơ bộ | Chạy lại lượt lạnh sau khi `myReaction` vào đường hydrate — so với mốc GĐ4 (`F5`) |

*Bàn giao GĐ4 → GĐ3 (2026-09-23, GĐ4 `F4`, sau merge PR #21 `0d0a093`):* ba chỗ cắm — `PostHydrator` (chữ ký ở hướng dẫn
GĐ4 B+C+D Mục 11/18), `FEED-13` hai người xem, dòng `renderPost` của trang chủ. Hai việc phải làm lại: k6 lượt (2) sau khi
`myReaction` vào đường hydrate; `READ-CMT-*` / `READ-REACT-*` với bài `friends` giữa hai người là **bạn thật** (quan hệ tạo qua
API `POST /friends/requests` + `accept` — đã có trên `develop`). E2E staging hai tài khoản của GĐ4 (`F2`) đạt cùng ngày — GĐ4 xong.

---|---|---|
| **Sau GĐ4** (đúng lịch gốc) | Feed đã có; bình luận/cảm xúc gắn thẳng vào card của feed | E-lane thêm slot tương tác vào card feed (Đ-3.13), không sửa `features/feed/` từ bên trong |
| **Trước GĐ4** | Bài `friends` vẫn chỉ tác giả thấy (`AlwaysStrangers`, Đ-2.9) nên bình luận trên bài `friends` cũng vậy | Không gì — Đ-3.3 đi qua `PostVisibility`, GĐ4 đổi một dòng DI thì bình luận tự đúng theo. GĐ4 phải đọc hai bẫy ở Mục 14 |

Chọn thứ tự nào là việc của buổi cổng mở, không phải của tài liệu này. Đổi thứ tự so với lịch gốc thì ghi vào Mục 13.

---

## 2. Phạm vi

### Trong phạm vi

| Nhóm | Nội dung |
|---|---|
| **Bình luận** | Tạo bình luận gốc và phản hồi (≤ 3 cấp); đọc bình luận gốc theo trang; đọc phản hồi của một bình luận theo trang; xóa mềm bình luận của mình |
| **Cảm xúc** | Thả / đổi / gỡ trên bài và trên bình luận; sáu loại đã có CHECK từ GĐ2 |
| **Bộ đếm** | `posts.comment_count`, `posts.reaction_counts` (có từ GĐ2) · `comments.reply_count`, `comments.reaction_counts` (mới) |
| **Mở rộng `PostResponse`** | Thêm `myReaction` — chỉ thêm, không đổi trường nào (Đ-3.10) |
| **Endpoint** | 8 endpoint mới trong nhóm `content-v1` (Mục 8) |
| **Lane frontend** | Cây bình luận 3 cấp tải lười + ô trả lời tại chỗ · thanh cảm xúc optimistic có rollback · bình luận đã xóa vẫn giữ nhánh |
| **Test** | Unit BR-08 + bộ đếm · 7 dòng AuthZ matrix · test đồng thời bộ đếm · cổng hợp đồng mở rộng · E2E hai tài khoản trên staging |

### Ngoài phạm vi — hoãn có địa chỉ

| Việc | Hoãn tới | Lý do |
|---|---|---|
| Thông báo "X đã bình luận / thả cảm xúc" | **GĐ6** | GĐ3 phát event trong tiến trình và chỉ log (Đ-3.12); GĐ6 nối notification vào đúng chỗ đó |
| Đẩy bình luận mới realtime cho người đang xem bài | **GĐ6** hoặc **ngoài MVP** | Cần hub; GĐ5 dựng hub cho chat trước. FE tự nạp lại khi người dùng bấm "Tải bình luận mới" |
| Ẩn bình luận vi phạm (kiểm duyệt) | **GĐ6** | Dùng chung luồng báo cáo của UC-19; cột `status` của `comments` sẽ cần giá trị `hidden` — ALTER CHECK ở GĐ6 |
| Chủ bài xóa bình luận của người khác trên bài mình | **Ngoài MVP** | Không có FR nào yêu cầu. Thêm vào là thêm một nhánh tầng 3 thứ hai cho cùng endpoint — làm khi có FR |
| Sửa bình luận | **Ngoài MVP** | FR-007 không yêu cầu; nhờ vậy không cần cột `edited_at` hay luồng "đã chỉnh sửa" thứ hai |
| Danh sách "ai đã thả cảm xúc" | **Ngoài MVP** | Cần phân trang trên `reactions` theo đối tượng; index `idx_reactions_target` đã có sẵn nếu sau này làm |
| Nhắc tên (`@tag`) trong bình luận | **GĐ6** | Đi cùng notification loại `tag` của FR-018 |
| Ảnh trong bình luận | **Ngoài MVP** | `media_attachments.owner_type` chỉ nhận `post`, `message` — thêm `comment` là đổi CHECK |
| Xóa cứng bình luận + cảm xúc khi xóa tài khoản | **GĐ8** | Đi cùng NĐ 13/2023. Chú ý: `reactions` đa hình nên **không** có FK — xóa cứng bài/bình luận để lại cảm xúc mồ côi, GĐ8 phải dọn theo `(target_type, target_id)` |
| Đối soát lại bộ đếm định kỳ (job) | **GĐ8** nếu cần | Đ-3.8 giữ bộ đếm đúng bằng giao dịch; job đối soát chỉ là lưới cho lỗi ta chưa biết. GĐ3 có câu SQL đối soát để chạy tay (Mục 12) |

---

## 3. Quyết định thiết kế

Mười bốn quyết định. Ba cái đầu (**Đ-3.1 → Đ-3.3**) quyết định *hình dạng* của cả giai đoạn và phải chốt ở **cổng mở**;
**Đ-3.8** là quyết định đắt nhất về kỹ thuật — sai ở đó thì không có cách vá sau mà không phải đối soát dữ liệu.

### Đ-3.1 Bình luận và cảm xúc ở lại module Content, schema `content`

PTTK xếp `comments` và `reactions` vào CMP-04 cùng `posts`; GĐ2 đã dựng hai bảng trong schema `content`. GĐ3 không tách
module mới.

Lý do không chỉ là "theo PTTK": bộ đếm nằm trên `posts` và phải cập nhật **cùng một transaction** với dòng bình luận /
cảm xúc (Đ-3.8). Tách module là tách `DbContext`, và một transaction trải qua hai `DbContext` là thứ Đ-2.1 cố tình loại
bỏ. Trong cùng schema thì FK vẫn giữ (`comments.post_id`, `comments.parent_id` — Đ-2.2).

**Hệ quả:** tám endpoint mới nằm trong nhóm Swagger `content-v1`, hợp đồng vẫn là `content-v1.yaml`. Không có file hợp
đồng mới, không có thư mục `lib/api/<nhóm>/` mới.

### Đ-3.2 Không thêm mã quyền nào — 17 mã của GĐ1 là đủ

Ma trận Mục 6.7.2 đã có `comment.create` (7) và `reaction.set` (8), seed cho `USER` và `MODERATOR` từ GĐ1. GĐ3 **không**
thêm `comment.delete` hay `reaction.remove`, cùng tinh thần Đ-2.6:

| Endpoint | Tầng 2 | Tầng 3 (ở service) |
|---|---|---|
| `GET /posts/{postId}/comments`, `GET /comments/{commentId}/replies` | `post.read.public` | BR-02 của bài (Đ-3.3) |
| `POST /posts/{postId}/comments` | `comment.create` | có hồ sơ (Đ-2.4) · BR-02 của bài · cha hợp lệ (Đ-3.4) |
| `DELETE /comments/{commentId}` | `[Authorize]` | `author_id == actorId` |
| `PUT` / `DELETE …/reactions/me` (bài và bình luận) | `reaction.set` | BR-02 của bài chứa đối tượng |

**Chỗ phải giải thích: vì sao xóa bình luận chỉ cần `[Authorize]`.** Xóa nội dung *của chính mình* là quyền của chủ dữ
liệu, không phải một tính năng được cấp (tinh thần NĐ 13/2023 — GOAL-05). Nếu gắn nó vào `comment.create` thì một
người bị Admin gỡ quyền bình luận (GĐ6) sẽ không xóa được bình luận cũ của mình — đúng chiều ngược với thứ ta muốn.
Tiền lệ trong repo: `DELETE /users/me/avatar` cũng chỉ `[Authorize]`.

Thêm hai hằng `CommentCreate`, `ReactionSet` vào `ContentPermissions` (và vào mảng `All` của nó) —
`ContentPermissionsTests` ở ArchitectureTests tự canh chúng nằm trong `PermissionCodes.All`.

### Đ-3.3 Bình luận và cảm xúc thừa kế BR-02 của bài — một hàm, đánh giá lúc đọc, không tồn tại = không được thấy = 404

Bình luận không có cột `privacy`. Nó hiện được khi và chỉ khi **bài chứa nó** hiện được với người gọi, đánh giá lại ở
**mọi** request — đúng nghĩa "tại thời điểm đọc" của GĐ2 Mục 7.4. Tác giả đổi bài từ `public` sang `private` thì toàn bộ
bình luận biến mất với người khác ngay request kế tiếp, kể cả với chính người đã viết bình luận.

Hiện thực bằng **một** hàm trong `Content.Application` — `PostAccess.ResolveVisibleAsync(postId, actorId)` — gọi lại
`PostVisibility.CanView` và `IFriendshipReader` y như `GET /posts/{postId}`. Mọi endpoint của GĐ3 đi qua nó; endpoint nhận
`commentId` thì tra `post_id` của bình luận trước rồi mới gọi. Có hai bản của cùng một luật là có chỗ lệch.

**Mã lỗi — lệch kế hoạch gốc, có chủ đích.** Kế hoạch gốc ghi *"bình luận trên bài không có quyền xem → 403"*. GĐ3 trả
**404** — cho cả đọc, viết bình luận và thả cảm xúc — vì:

- Quy ước 3b của GĐ1: "không tồn tại" và "không được phép thấy" trả **cùng** một thứ. Bài không tồn tại chắc chắn là 404
  (không có gì để cấm). Nếu bài tồn tại-nhưng-bị-giấu trả 403 thì status code tự khai bài đó có thật.
- GĐ2 đã chốt `GET /posts/{id}` bài không được xem → 404. Bình luận vào bài đó mà trả 403 là hai endpoint cùng module
  nói hai điều khác nhau về cùng một bài.

Phân biệt với tầng 3: **403** chỉ dành cho thao tác cần *sở hữu* (`DELETE /comments/{id}` của người khác). Bảng đầy đủ ở
Mục 6.1.

**Hai trường hợp biên phải chốt rõ:**

| Trường hợp | Kết quả | Vì sao |
|---|---|---|
| Tác giả bình luận xóa bình luận của mình, trong khi bài giờ đã `private` với họ | **Được** (204) | Tầng 3 của xóa là sở hữu bình luận, không phải quyền xem bài. Quyền xóa dữ liệu của mình không phụ thuộc người khác đổi cài đặt |
| Bài đã xóa mềm / `hidden` | Mọi endpoint đọc/viết/cảm xúc → **404**; xóa bình luận của mình vẫn được | Global query filter của GĐ2 đã loại bài `deleted`; `hidden` (GĐ6) đi qua cùng nhánh "không xem được" |

### Đ-3.4 Độ sâu do server tính từ cha; client không bao giờ gửi `depth`

`CreateCommentRequest` chỉ có `body` và `parentId?`. Server:

1. `parentId` vắng → `depth = 1`.
2. `parentId` có → tra cha. Cha phải **cùng bài** với `postId` trên route, `status = visible`, và `depth < 3`.
   Khi đó `depth = cha.depth + 1`.

Ba lưới cho BR-08, cùng nếp BR-01 của GĐ2:

1. **Hàm thuần** `CommentDepthPolicy` trong `Domain/` — unit test không cần DB.
2. **Service** — kiểm ở bước 2 trên, trả 400.
3. **DB** — `ck_comments_depth` (có từ GĐ2) + **`ck_comments_root_depth`** mới: `(parent_id IS NULL) = (depth = 1)`.
   CHECK không so được với dòng khác nên không bắt được "con sâu hơn cha đúng một", nhưng bắt được bình luận gốc
   mang `depth = 2` hoặc phản hồi mang `depth = 1` — hai lỗi rẻ nhất để gây ra bằng một dòng code sai.

Mọi lỗi của `parentId` trả **400 với `errors.parentId`**, hai câu:

| Tình huống | Thông điệp |
|---|---|
| Cha không tồn tại · thuộc bài khác · đã xóa | "Bình luận cần trả lời không còn tồn tại." |
| Cha đã ở cấp 3 (BR-08) | "Chỉ được trả lời tối đa 3 cấp." |

Gộp ba tình huống đầu vào một câu là cố ý: "thuộc bài khác" mà có câu riêng là một kênh dò `commentId` của bài người
khác. **Không** tự đẩy phản hồi cấp 4 lên làm con của cấp 3 như vài mạng xã hội — kế hoạch gốc ghi "cấp 4 bị chặn", và
tự sửa âm thầm là server quyết định thay người dùng. FE ẩn nút "Trả lời" ở cấp 3 nên người dùng thường không bao giờ
chạm tới lỗi này.

### Đ-3.5 Xóa bình luận là xóa mềm, giữ nhánh; nội dung đã xóa không bao giờ ra khỏi server

`DELETE /comments/{id}` đặt `status = 'deleted'` + `deleted_at`. Không `DELETE` dòng — `parent_id` của các phản hồi
đang trỏ vào nó và FK là `ON DELETE CASCADE`: xóa cứng là **xóa cả nhánh của người khác**.

Bình luận đã xóa **vẫn nằm trong danh sách**, ở đúng vị trí, dạng:

```
{ commentId, postId, parentId, depth, status: "deleted", author: null, body: null,
  replyCount, reactionCounts: {}, myReaction: null, createdAt, canDelete: false }
```

- `body` và `author` là `null`: người xóa muốn nội dung biến mất, và "biến mất" gồm cả việc ai đã viết. Cột `body`
  **giữ nguyên trong DB** (bằng chứng cho báo cáo vi phạm ở GĐ6); GĐ8 quyết định xóa hẳn theo NĐ 13/2023.
- `replyCount` giữ nguyên để FE vẫn hiện "Xem N phản hồi" dưới dòng "Bình luận đã bị xóa".
- Cảm xúc trên bình luận đã xóa: không trả số, không nhận thêm (`PUT …/reactions/me` → 404).
- Xóa lần hai, hoặc xóa bình luận của người khác → **403**, cùng một phản hồi — đúng khuôn D8 của GĐ2.

*Chốt 2026-09-24 (lệch, theo Đ-6.14 của GĐ6):* `CommentStatus` có thêm giá trị **`hidden`** ngay trong migration
`Gd3Interactions` — CHECK `ck_comments_status` nhận `('visible','deleted','hidden')` — để GĐ6 ẩn bình luận vi phạm mà
không phải ALTER CHECK trên bảng của Content. GĐ3 không có đường nào ghi `hidden`; mapper coi nó như `deleted`
(`body`/`author` = `null`, không nhận cảm xúc, không làm cha được). Câu "Bình luận đã bị ẩn…" và bộ đếm khi ẩn là việc của GĐ6.

**Ngữ nghĩa hai bộ đếm** — chốt để không ai phải đoán:

| Bộ đếm | Đếm gì | Đổi khi |
|---|---|---|
| `posts.comment_count` | Bình luận **đang hiển thị** (`visible`) ở **mọi** cấp của bài | +1 khi tạo · −1 khi xóa mềm |
| `comments.reply_count` | Phản hồi **trực tiếp**, **mọi** trạng thái | +1 khi tạo phản hồi · **không** đổi khi xóa |

`reply_count` đếm cả phản hồi đã xóa vì chúng vẫn hiện (dạng "đã bị xóa") khi mở nhánh — con số phải khớp với số dòng
FE sẽ thấy. `comment_count` thì là con số người dùng đọc trên card ("12 bình luận") nên chỉ đếm cái còn nội dung.

Phương án đã cân nhắc rồi loại: ẩn luôn bình luận đã xóa không có con. Muốn làm đúng phải biết "không có con **còn
hiển thị được**", tức là đệ quy qua nhánh ở mỗi lần xóa — một luồng bộ đếm thứ ba cho một cải thiện thẩm mỹ.

### Đ-3.6 Tải lười theo cấp: bình luận gốc một trang, phản hồi một trang riêng cho từng cha

Hai endpoint đọc, cùng dạng cursor Đ-2.11:

```
GET /posts/{postId}/comments?cursor=&limit=     → bình luận gốc của bài (depth = 1)
GET /comments/{commentId}/replies?cursor=&limit= → phản hồi trực tiếp của một bình luận
sort   = (created_at ASC, comment_id ASC)        // cũ trước, mới sau — đọc như một cuộc hội thoại
limit  : mặc định 20, tối đa 50                  // FE dùng 10 cho phản hồi
trả    : { items: [CommentResponse], nextCursor: string | null }
```

**Không** trả cả cây trong một response. Một bài có 300 bình luận, mỗi cái 20 phản hồi, là 6.000 dòng cho một lần mở bài
— và kích thước response khi đó tùy vào người khác đã gõ bao nhiêu, không phải vào `limit`. Tải lười cho kích thước
response bị chặn trên bằng `limit`, và chính là thứ FE cần cho "thu gọn nhánh dài" của kế hoạch gốc.

**Chiều sắp ASC, khác danh sách bài (DESC).** Bình luận mới chèn vào giữa hai lần gọi rơi vào **cuối** danh sách, nên
keyset ASC không nhân đôi và không nhảy cóc — cùng bản chất `PAGE-03` của GĐ2. Cursor dùng **chung bộ mã hóa** với
`PostCursor` (tách thành `KeysetCursor` dùng lại được, `created_at|id`); nó vẫn mờ với client.

Cursor rác → 400 `errors.cursor`, y như GĐ2. `commentId` của `replies` không tồn tại hoặc nằm trong bài không được xem
→ 404.

### Đ-3.7 Cảm xúc là tài nguyên "của tôi trên đối tượng này": `PUT` và `DELETE` trên `…/reactions/me`

```
PUT    /posts/{postId}/reactions/me          { type }   → 200 ReactionSummary
DELETE /posts/{postId}/reactions/me                     → 200 ReactionSummary
PUT    /comments/{commentId}/reactions/me    { type }   → 200 ReactionSummary
DELETE /comments/{commentId}/reactions/me               → 200 ReactionSummary

ReactionSummary { reactionCounts: { like?: int, love?: int, … }, myReaction: ReactionType | null }
```

Kế hoạch gốc ghi `PUT /reactions` (một endpoint, đối tượng trong body). Đổi sang đường dẫn theo đối tượng vì:

- **Tầng 3 đọc được từ route.** Đối tượng nằm trên đường dẫn thì log, rate limit và matrix đều thấy nó; nằm trong body thì
  `targetType` là thêm một trường client có thể gõ bậy.
- **`me` thay cho id người dùng**, cùng tiền lệ `/users/me/profile`: không có đường nào để gửi `userId` của người khác.
- **Idempotent thật sự.** `PUT` cùng `type` hai lần = một dòng, bộ đếm không đổi lần hai. `DELETE` khi chưa thả gì vẫn
  **200** với tóm tắt hiện tại — không 404: "đảm bảo tôi không còn cảm xúc ở đây" đã đúng, và 404 ở đây trộn lẫn với 404
  "không thấy bài" của Đ-3.3.

Trả `ReactionSummary` (không phải 204) để FE đối chiếu optimistic update với **con số thật của server** mà không phải
nạp lại cả bài (Đ-3.13).

### Đ-3.8 Bộ đếm cập nhật trong cùng transaction, bằng phép toán nguyên tử, sau khi khóa dòng đối tượng

Đây là quyết định đắt nhất của GĐ3. Cách "tự nhiên" với EF — nạp `Post`, sửa `ReactionCounts["like"]++`, `SaveChanges`
— **mất cập nhật** khi hai người bấm cùng lúc: cả hai đọc `like = 5`, cả hai ghi `6`. Không test đơn luồng nào thấy.

**Khuôn chốt cho một lần thả / đổi / gỡ cảm xúc trên bài:**

```
BEGIN
  1. SELECT 1 FROM content.posts WHERE post_id = @id AND status = 'published' FOR UPDATE   -- khóa đối tượng
     (0 dòng → 404; BR-02 đã kiểm TRƯỚC transaction, Đ-3.3)
  2. SELECT type FROM content.reactions WHERE (user_id, target_type, target_id) = (@me, 'post', @id)
  3. So cũ/mới → một trong bốn nhánh:   không→có: INSERT   có→khác: UPDATE type
                                          có→không: DELETE   giống nhau: không làm gì (idempotent)
  4. UPDATE content.posts SET reaction_counts = <−1 cho loại cũ, +1 cho loại mới>   -- bằng SQL, trên jsonb
  5. SELECT reaction_counts … → dựng ReactionSummary
COMMIT
```

Ba điểm không thương lượng:

- **Khóa dòng đối tượng ở bước 1**, không chỉ dựa vào PK của `reactions`. Hai tab của *cùng một người* cùng bấm lần đầu:
  không có khóa thì cả hai thấy "chưa có" ở bước 2, cả hai INSERT, một cái nổ `23505` và rơi thành 500. Có khóa thì cái
  sau chờ, rồi thấy dòng của cái trước và đi nhánh "giống nhau".
- **Bước 4 là một câu `UPDATE` với biểu thức trên `jsonb`**, không nạp dictionary lên rồi ghi xuống. Khóa ở bước 1 đã
  tuần tự hóa, nhưng viết nguyên tử thì bộ đếm vẫn đúng kể cả khi ai đó sau này gỡ khóa "cho nhanh".
  Loại nào về **0** thì **xóa khóa** khỏi object — `{}` vẫn là `{}` (hợp đồng GĐ2: không bao giờ `null`, không có khóa
  giá trị 0 để FE phải lọc).
- **Không đi qua `SaveChanges` cho `posts`.** Override `SaveChanges` của `ContentDbContext` đóng dấu `updated_at`; tệ hơn,
  nếu ai đó dùng chung đường với `PATCH` thì `edited_at` bị đóng dấu và bài hiện nhãn "đã chỉnh sửa" mỗi lần có người
  thả tim. Bộ đếm là thống kê, không phải nội dung: **không** chạm `updated_at`, **không** chạm `edited_at`.

**Thứ tự khóa cố định toàn module: bài trước, bình luận sau.** Tạo phản hồi khóa bài (tăng `comment_count`) rồi khóa
cha (tăng `reply_count`); xóa bình luận cũng phải khóa **bài trước** rồi mới đổi trạng thái bình luận — viết ngược lại
(đổi bình luận xong mới trừ bài) là hai request đan nhau thành deadlock. Cảm xúc trên bình luận chỉ khóa đúng dòng
bình luận đó. Cùng loại cạm bẫy với "khóa theo family, cùng thứ tự với `RotateAsync`" của D6 GĐ1.

**Cái giá đã biết:** một bài "nóng" có hàng trăm người bấm cùng giây thì các transaction xếp hàng trên một dòng. Ở quy
mô đồ án (mục tiêu 1.000 CCU là cho *feed*, không phải cho một bài) đây là cái giá đúng. Phương án cho quy mô lớn —
bảng đếm tách rời, cộng dồn bất đồng bộ — ghi ở Mục 14 như một rủi ro có đường lui, không làm bây giờ.

### Đ-3.9 `comments` thêm hai cột bộ đếm — migration chỉ-mở-rộng

`CommentResponse` cần `replyCount` và `reactionCounts`. Đếm lúc đọc bằng `GROUP BY` trên mỗi trang thì được, nhưng là
hai truy vấn gộp cho mỗi trang 20 bình luận, và không nhất quán với cách `posts` đã làm. Chốt: thêm cột, cùng khuôn
`posts` (Mục 4).

Migration GĐ3 **chỉ thêm** (cột có `DEFAULT`, CHECK mới, index mới) — tương thích ngược một phiên bản, đúng luật
expand–contract mà GĐ7 sẽ đòi cho rolling deploy. Không đổi tên, không xóa cột nào của GĐ2. GĐ2 đã báo trước cái giá
này: *"GĐ3 được phép ALTER chúng — một migration nữa là cái giá đã biết trước"* (Đ-2.12).

### Đ-3.10 `content-v1` mở lại theo kiểu **chỉ-thêm**; `PostResponse` thêm đúng một trường `myReaction`

GĐ2 đóng băng `content-v1.yaml` ngày 2026-09-21 (`F5`) với lời hẹn: *"cần thêm field nào vào `PostResponse` thì đổi ở cổng
mở, không sửa lặng"*. Cổng mở GĐ3 là chỗ đó. Luật cho lần mở này:

- **Được:** thêm path mới, thêm schema mới, thêm trường mới vào response.
- **Không được:** đổi tên, đổi kiểu, xóa, hay đổi một trường từ bắt buộc sang tùy chọn — dù chỉ "cho gọn".
- `info.version` → `1.0.0-gd3`.

`PostResponse` thêm `myReaction: ReactionType | null` — **bắt buộc có mặt, được phép null**, cùng cách `nextCursor` của
GĐ2. Không có nó thì thanh cảm xúc không biết nút nào đang sáng, và FE phải gọi thêm một endpoint cho mỗi bài.

**Hệ quả đã biết trước:** fixture MSW của GĐ2 gắn kiểu bằng `satisfies PostResponse` (luật frontend Mục 8) nên đỏ
compile ngay khi `pnpm gen:api` chạy lại. Đó là **hành vi mong muốn** — sửa fixture là việc của `E1`, không phải dấu hiệu
hợp đồng sai.

### Đ-3.11 `myReaction` tính theo lô, mỗi trang đúng một truy vấn

Luật batch-first của Đ-2.3 áp nguyên: trang 20 bài (hay 20 bình luận) → **một** truy vấn
`SELECT target_id, type FROM content.reactions WHERE user_id = @me AND target_type = @t AND target_id = ANY(@ids)`.
Không gọi theo từng dòng.

`myReaction` và `canEdit`/`canDelete` là **trường theo người xem**: cùng một bài, hai người xem thấy hai giá trị. Đây là
thứ **không bao giờ** được nằm trong cache dùng chung — GĐ4 đã dựng cache feed chỉ lưu `post_id` (Đ-4.9) đúng vì lý do
này; GĐ3 không được phá luật đó.

### Đ-3.12 Không realtime ở GĐ3; phát event trong tiến trình và chỉ log

Tạo bình luận và thả cảm xúc phát event in-process (`CommentCreated`, `ReactionSet`) đúng chỗ `PostCreated` của GĐ2 —
**chỉ log** ở GĐ3. GĐ6 nối notification vào đúng chỗ đó, không phải lần tìm lại luồng.

Event phát **sau** `COMMIT`. Phát trong transaction thì notification của GĐ6 có thể chạy trước khi dữ liệu nhìn thấy
được, hoặc chạy cho một bình luận đã bị rollback.

Người đang mở bài **không** thấy bình luận mới của người khác cho tới khi nạp lại. Chấp nhận: kế hoạch gốc không yêu cầu
realtime cho bình luận, và GĐ5 là giai đoạn dựng hub.

*Chốt 2026-09-24 (lệch, vì GĐ6 merge trước — Đ-6.4):* không "chỉ log". Event bus của GĐ6 (`IEventPublisher`, record
`CommentCreated`/`ReactionSet` ở `SharedKernel/Events/ContentEvents.cs`) đã có trên `develop`, nên GĐ3 **phát thật** qua lớp bọc
`ContentInteractionEvents` (cùng khuôn `SocialGraphEvents`), vẫn **sau `COMMIT`**. `ReactionSet` phát khi thả mới (`isNew = true`)
và khi đổi loại (`isNew = false`); gỡ và "giống nhau" không phát. `MentionedUserIds` rỗng (tag là việc của GĐ6). Handler thông
báo `comment`/`reply`/`reaction` và provider kiểm duyệt bình luận vẫn là việc của GĐ6 (Mục 9.3 bước 9 của `giai-doan-6.md`).

### Đ-3.13 Frontend: hai feature mới, ghép vào card qua slot; optimistic update tuần tự theo đối tượng

**Đặt file** (luật frontend Mục 2, Đ-E13): `features/comment/` và `features/reaction/` — tên theo **màn**, không theo
module backend. `features/post/` **không** import hai feature này và ngược lại (Đ-E13 cấm import chéo).

Ghép bằng **slot**, theo đúng tiền lệ prop `actions` mà `PostCard` đã có từ E6 của GĐ2:

```
app/(app)/(with-profile)/posts/[postId]/page.tsx     ← chỉ ráp
  <PostDetail postId
     footer={(post) => <>
        <ReactionBar target={{ kind: "post", id: post.postId }} initial={…} />   // features/reaction
        <CommentThread postId={post.postId} />                                  // features/comment
     </>} />
```

`CommentThread` cần thanh cảm xúc cho từng bình luận → nó cũng nhận slot `renderReactions`, do `app/` truyền vào. Không
feature nào biết feature kia tồn tại; chỉ `app/` biết cả hai.

**Optimistic update — luật tuần tự theo đối tượng.** Mỗi đối tượng (một bài, một bình luận) giữ ba giá trị: *đã xác
nhận* (lần cuối server trả), *mong muốn* (lần bấm cuối của người dùng), và *đang bay* (tối đa **một** request).

1. Bấm → giao diện đổi theo *mong muốn* ngay lập tức (số đếm tự tính lại từ *đã xác nhận*).
2. Không có request nào đang bay → gửi request cho *mong muốn*.
3. Đang có request bay → **không** gửi thêm; chờ nó về.
4. Request về thành công → *đã xác nhận* = `ReactionSummary` của server. Nếu *mong muốn* khác *đã xác nhận* → gửi tiếp
   một request (quay lại bước 2).
5. Request lỗi → *mong muốn* = *đã xác nhận* (rollback), toast theo `errorMessage(…)`.

Vì sao phải phức tạp vậy: người dùng bấm tim–bỏ–tim–bỏ trong một giây sinh bốn request. Gửi hết thì (a) các response về
không theo thứ tự và giao diện dừng ở trạng thái sai, (b) mười lần như vậy là đụng hạn mức 100 req/phút và nhận 429 lúc
đang "chỉ bấm tim". Luật trên cho **tối đa hai** request cho một chuỗi bấm bất kỳ, và trạng thái cuối luôn là lần bấm
cuối.

**Không thêm thư viện** (TanStack Query hay tương tự). Một reducer nhỏ trong `features/reaction/` là đủ và test được bằng
Vitest không cần render. React 19 `useOptimistic` đã cân nhắc: nó gắn với một transition và không diễn đạt được bước 3–4
(gộp các lần bấm trong khi đang bay), nên không dùng.

### Đ-3.14 Validation bình luận: đếm như GĐ2, không trim, thông điệp chép đúng câu server

- Độ dài đo bằng `.length` UTF-16 ở client, khớp `string.Length` của .NET — **chép nguyên** cách của `lib/validation/post.ts`,
  không đếm byte, không đếm code point. Cột `varchar(1000)` đếm ký tự nên luôn rộng hơn hoặc bằng: client = server ≤ DB.
- **Không trim trước khi đo** độ dài; nhưng chuỗi chỉ gồm khoảng trắng là **rỗng** (400 `errors.body`). Lưu nguyên văn,
  không chuẩn hóa — xuống dòng người dùng gõ là nội dung.
- Hai câu: "Bình luận không được để trống." · "Bình luận không được vượt quá 1000 ký tự." Hằng ở `Domain/CommentPolicy`,
  client chép đúng câu (Đ-E5: một lỗi không hiện hai cách nói).

---

## 4. Mô hình dữ liệu

Một migration mới của module Content (tên gợi ý `Gd3Interactions`). DDL dưới đây là **phần thay đổi** so với GĐ2 Mục 4;
hiện thực qua EF Core migration, không viết SQL tay vào repo.

```sql
-- ================= schema "content" — thay đổi của GĐ3 =================

-- ENT-03 · comments: hai bộ đếm + một CHECK nhất quán gốc/cấp (Đ-3.4, Đ-3.9)
ALTER TABLE content.comments
    ADD COLUMN reply_count     integer NOT NULL DEFAULT 0,
    ADD COLUMN reaction_counts jsonb   NOT NULL DEFAULT '{}'::jsonb,
    ADD CONSTRAINT ck_comments_reply_count CHECK (reply_count >= 0),
    ADD CONSTRAINT ck_comments_root_depth  CHECK ((parent_id IS NULL) = (depth = 1));

-- Đ-6.14 (chốt 2026-09-24): nới CHECK trạng thái để GĐ6 ẩn được bình luận — chỉ-thêm một giá trị
ALTER TABLE content.comments DROP CONSTRAINT ck_comments_status,
    ADD CONSTRAINT ck_comments_status CHECK (status IN ('visible','deleted','hidden'));

-- ENT-02 · posts: bộ đếm có sẵn từ GĐ2, thêm lưới không âm
ALTER TABLE content.posts
    ADD CONSTRAINT ck_posts_comment_count CHECK (comment_count >= 0);

-- Trang bình luận gốc: keyset ASC đọc thẳng từ index (Đ-3.6)
CREATE INDEX idx_comments_post_roots ON content.comments (post_id, created_at, comment_id)
    WHERE parent_id IS NULL;

-- Trang phản hồi: thay index quy ước của FK parent_id bằng index có đủ khóa sắp
DROP INDEX content."IX_comments_parent_id";
CREATE INDEX idx_comments_parent ON content.comments (parent_id, created_at, comment_id);

-- GIỮ NGUYÊN "IX_comments_post_id" (post_id, created_at): nó là index của FK post_id → posts ON DELETE CASCADE.
-- Index một phần ở trên (WHERE parent_id IS NULL) KHÔNG thay được nó — xóa cứng một bài (GĐ8) sẽ phải quét
-- tuần tự cả bảng để tìm phản hồi.
```

**Bốn chỗ dễ sai trong migration này:**

1. **EF tự bỏ index quy ước của FK** khi đã có một index khác bắt đầu bằng cột FK. Khai `HasIndex(parent_id, created_at,
   comment_id)` thì EF sinh `DROP IX_comments_parent_id` — đúng ý. Nhưng khai index một phần cho `post_id` mà EF coi
   nó là "đã có index cho FK" thì **mất luôn** `IX_comments_post_id`. Đọc file migration sinh ra, đừng tin.
2. **`ck_comments_root_depth` áp lên dữ liệu có sẵn.** Staging có thể đã có dòng `comments` nếu ai đó thử bằng SQL tay ở
   GĐ2. Trước khi deploy: `SELECT count(*) FROM content.comments` trên staging — khác 0 thì kiểm dữ liệu trước, vì
   `ADD CONSTRAINT` đỏ sẽ làm service `migrate` thoát khác 0 và CD dừng.
3. **`'{}'::jsonb` phải có cast** — cùng cạm bẫy 3 của GĐ2 Mục 4.
4. **`reaction_counts` của `Comment`** cần `ValueComparer` như `Post.ReactionCounts` — thiếu nó EF không phát hiện thay
   đổi bên trong dictionary. Dù GĐ3 ghi cột này bằng SQL (Đ-3.8), entity vẫn phải map đúng để đọc.

**Bảng `reactions` không đổi.** PK ba cột đã là BR-05, `idx_reactions_target` đã có. `myReaction` theo lô (Đ-3.11) đi
thẳng vào PK vì `user_id` là cột đầu.

---

## 5. Dữ liệu nền

**GĐ3 không seed gì.** `comment.create` và `reaction.set` đã gán cho `USER` và `MODERATOR` từ GĐ1 (`giai-doan-1.md`
Mục 5.3); Admin qua bằng short-circuit tầng 2.

| Việc | Vì sao vẫn phải làm |
|---|---|
| Migration mới chạy qua hook `--migrate` đã có | Không thêm dòng nào vào `Program.cs` — `MigrateContentModuleAsync` áp mọi migration chưa chạy của context. Kiểm log CD có tên migration mới |
| Kiểm trên staging rằng USER thật sự có quyền 7 và 8 | Người test tay bằng Admin sẽ thấy "chạy tốt" dù USER bị chặn (Admin short-circuit). `SELECT` một lần trên `identity.role_permissions`, ghi vào `F4` |

---

## 6. Ba tầng kiểm soát truy cập áp vào GĐ3

GĐ3 là **người tiêu thụ thứ hai** của khuôn tầng 3 — và là người đầu tiên có tài nguyên *con*. Không endpoint nào tự
chế cách kiểm tra riêng (`giai-doan-1.md` Mục 6, `AGENTS.md` Mục 10).

### 6.1 Bảng đầy đủ: endpoint × tầng 2 × tầng 3 × mã lỗi

| Endpoint | Tầng 2 | Tầng 3 (ở service) | Không đạt tầng 3 |
|---|---|---|---|
| `GET /posts/{postId}/comments` | `post.read.public` | BR-02 của bài | **404** |
| `GET /comments/{commentId}/replies` | `post.read.public` | tra bài của bình luận → BR-02 | **404** |
| `POST /posts/{postId}/comments` | `comment.create` | có hồ sơ → BR-02 của bài → cha hợp lệ | **403** (chưa có hồ sơ) · **404** (bài) · **400** (cha) |
| `DELETE /comments/{commentId}` | `[Authorize]` | `author_id == actorId` và đang `visible` | **403** |
| `PUT`/`DELETE /posts/{postId}/reactions/me` | `reaction.set` | BR-02 của bài | **404** |
| `PUT`/`DELETE /comments/{commentId}/reactions/me` | `reaction.set` | bình luận `visible` → BR-02 của bài | **404** |

**Thứ tự kiểm của `POST /posts/{postId}/comments`: hồ sơ trước, bài sau.** Người chưa có hồ sơ nhận 403 bất kể bài có
tồn tại hay không — không lộ gì. Làm ngược thì vẫn không lộ, nhưng chốt một thứ tự để thông điệp lỗi của FE (ngữ cảnh
`comment-create`) chỉ có một cách hiểu. Cùng thứ tự với `POST /posts` của GĐ2 ("hồ sơ → tiền tố → BR-01 → HEAD").

**Cảm xúc không đòi hồ sơ.** Cảm xúc không hiển thị tác giả ở đâu cả (không có danh sách "ai đã thả"), nên bất biến
Đ-2.4 không có lý do áp vào. Trên thực tế người chưa có hồ sơ không tới được màn nào có nút cảm xúc (layout
`(with-profile)`).

### 6.2 Khuôn tầng 3 cho tài nguyên con — lần ngược về cha, không nhân bản luật

```csharp
// Modules/Content/Application/Comments/CommentService.cs
public async Task<Result<CommentPage>> ListRepliesAsync(Guid commentId, Guid actorId, PageQuery q, CancellationToken ct)
{
    var parent = await _comments.FindAsync(commentId, ct);          // chỉ cần post_id của nó
    if (parent is null)
        return ContentErrors.CommentNotFound;                       // 404

    // MỘT hàm cho mọi endpoint của GĐ3 (Đ-3.3). Không viết lại ba mệnh đề BR-02 ở đây.
    var post = await _access.ResolveVisibleAsync(parent.PostId, actorId, ct);
    if (post is null)
        return ContentErrors.CommentNotFound;                       // CÙNG phản hồi với "không tồn tại"
    ...
}
```

Hai luật kế thừa nguyên xi từ GĐ2 Mục 6.2: `actorId` **luôn** từ `User.GetUserId()` do controller truyền; **không** có
nhánh `if role == ADMIN` ở tầng 3. Luật mới của GĐ3: **404 của bình luận không phân biệt "bình luận không có" với "bài
không được xem"** — cùng một `Error`, cùng một câu.

### 6.3 Bảy dòng AuthZ matrix mới — chỉ thêm dòng vào `AuthZMatrix.cs`

Khung đã có từ GĐ1, được sửa **một lần** ở GĐ2 (`Q-B2`: `AuthZArrange.CallerUserId`). GĐ3 **không** sửa khung. Nếu
một dòng dưới đây không thêm được mà không sửa khung thì dừng lại và ghi lý do như `Q-B2` đã làm.

| Id | Kịch bản | Người gọi | Gọi gì | Kỳ vọng |
|---|---|---|---|---|
| `TC-A03-comment` | A xóa bình luận của B | `Caller.User` | `DELETE /api/v1/comments/{id của B}` | **403** |
| `READ-CMT-01` | A đọc bình luận của bài `private` của B | `Caller.User` | `GET /api/v1/posts/{id}/comments` | **404** |
| `READ-CMT-02` | A bình luận vào bài `private` của B | `Caller.User` | `POST /api/v1/posts/{id}/comments` | **404** |
| `READ-CMT-03` | A đọc phản hồi của một bình luận nằm trong bài `private` của B | `Caller.User` | `GET /api/v1/comments/{id}/replies` | **404** |
| `READ-REACT-01` | A thả cảm xúc vào bài `private` của B | `Caller.User` | `PUT /api/v1/posts/{id}/reactions/me` | **404** |
| `READ-REACT-02` | A thả cảm xúc vào bình luận nằm trong bài `private` của B | `Caller.User` | `PUT /api/v1/comments/{id}/reactions/me` | **404** |
| `TC-A01-comment` | Bình luận không kèm JWT | `Caller.Anonymous` | `POST /api/v1/posts/{id}/comments` | **401** |

`READ-CMT-03` và `READ-REACT-02` là hai dòng quan trọng nhất: chúng canh đúng lỗ "lần theo `commentId` để vòng qua BR-02"
ở Mục 1. `ArrangePath` phải dựng được cảnh: B tạo bài `public`, B bình luận, B đổi bài sang `private` — rồi trả path chứa
`commentId`. Dựng bằng API thật, không INSERT thẳng DB (luật `B2` của GĐ2).

---

## 7. Luồng nghiệp vụ

### 7.1 Mở một bài và đọc bình luận (FR-007)

```
FE : GET /posts/{postId}                       → PostResponse (có commentCount, reactionCounts, myReaction)
FE : GET /posts/{postId}/comments?limit=20     → 20 bình luận gốc, cũ trước
       mỗi bình luận có replyCount > 0 → FE hiện nút "Xem N phản hồi" (CHƯA tải)
người dùng bấm "Xem N phản hồi" của C
FE : GET /comments/{C}/replies?limit=10        → 10 phản hồi cấp 2 của C
       lặp lại cho cấp 3 — cấp 3 không có nút "Trả lời" (BR-08)
API (mỗi lần đọc) : BR-02 của bài → 1 truy vấn trang → 1 lô IUserDirectory → 1 lô myReaction   = 3 truy vấn/trang
```

### 7.2 Viết bình luận / phản hồi

```
 1. FE  : kiểm body (Đ-3.14) — cùng ngưỡng, cùng câu với server
 2. FE  → BFF → API : POST /posts/{postId}/comments { body, parentId? }
 3. API : tầng 2 comment.create → có hồ sơ? (403) → BR-02 của bài (404)
          parentId có → cha cùng bài, visible, depth < 3 (400 errors.parentId)
 4. API : MỘT transaction, khóa theo thứ tự BÀI → CHA (Đ-3.8):
            UPDATE posts    SET comment_count = comment_count + 1 WHERE post_id = @p
            UPDATE comments SET reply_count   = reply_count   + 1 WHERE comment_id = @parent   -- nếu có cha
            INSERT comments (depth tính ở bước 3)
 5. API : COMMIT → phát CommentCreated (chỉ log, Đ-3.12)
       <- 201 CommentResponse
 6. FE  : chèn bình luận vào CUỐI danh sách đang mở (cũ trước, mới sau); tăng commentCount của bài
          và replyCount của cha TẠI CHỖ — không nạp lại cả bài
```

Bước 4 là hai `UPDATE` nguyên tử (`x = x + 1`) chứ không đọc lên rồi ghi: chính câu `UPDATE` giữ khóa dòng tới cuối
transaction nên hai bình luận đồng thời không mất lượt đếm.

### 7.3 Xóa bình luận

```
DELETE /comments/{id}
  tầng 3: author_id == actorId VÀ status = visible     → không đạt: 403 (cùng phản hồi cho mọi lý do)
  MỘT transaction, khóa BÀI trước (Đ-3.8):
     SELECT 1 FROM posts WHERE post_id = @p FOR UPDATE
     UPDATE comments SET status='deleted', deleted_at=now() WHERE comment_id=@id AND status='visible'
     nếu 1 dòng bị đổi → UPDATE posts SET comment_count = comment_count - 1
  <- 204
```

Điều kiện `AND status = 'visible'` trong câu `UPDATE` cộng với "chỉ trừ khi đổi được đúng 1 dòng" là thứ chặn trừ hai lần
khi hai tab cùng bấm Xóa — cùng bản chất với bước 1 của Đ-3.8.

### 7.4 Thả / đổi / gỡ cảm xúc (FR-008, BR-05)

Theo khuôn Đ-3.8. Bảng bốn nhánh và tác động lên bộ đếm:

| Trước | Sau (request) | Dòng `reactions` | `reaction_counts` |
|---|---|---|---|
| không có | `PUT like` | INSERT | `like +1` |
| `like` | `PUT love` | UPDATE type | `like −1` (về 0 thì xóa khóa) · `love +1` |
| `like` | `PUT like` | không đổi | không đổi |
| `like` | `DELETE` | DELETE | `like −1` (về 0 thì xóa khóa) |
| không có | `DELETE` | không đổi | không đổi — vẫn 200 |

Cảm xúc trên **bình luận**: y hệt, đối tượng khóa là dòng `comments`, bộ đếm là `comments.reaction_counts`, và điều kiện
thêm: bình luận phải `visible` (đã xóa → 404, Đ-3.5).

---

## 8. Hợp đồng API

Base `/api/v1`. Mọi lỗi RFC 7807 kèm `traceId`. Rate limit chung 100 req/phút/user. **Không có file hợp đồng mới**: mọi
thứ dưới đây thêm vào `src/backend/Modules/Content/Presentation/content-v1.yaml` theo luật chỉ-thêm của Đ-3.10.

### 8.1 Bình luận

| Method | Path | Auth | Thành công | Lỗi |
|---|---|---|---|---|
| GET | `/posts/{postId}/comments` | Bearer + `post.read.public` | 200 `CommentPage` | 400 id/cursor sai · 401 · 404 |
| GET | `/comments/{commentId}/replies` | Bearer + `post.read.public` | 200 `CommentPage` | 400 id/cursor sai · 401 · 404 |
| POST | `/posts/{postId}/comments` | Bearer + `comment.create` | 201 `CommentResponse` | 400 body/parentId · 401 · 403 chưa có hồ sơ · 404 |
| DELETE | `/comments/{commentId}` | Bearer | 204 | 400 id sai · 401 · 403 |

```
CreateCommentRequest { body (bắt buộc, 1–1000, không toàn khoảng trắng), parentId?: uuid }
CommentResponse      { commentId, postId, parentId: uuid | null, depth: 1 | 2 | 3,
                       status: "visible" | "deleted",
                       author: { userId, displayName, avatarUrl? } | null,   // null khi deleted
                       body: string | null,                                  // null khi deleted
                       replyCount, reactionCounts, myReaction: ReactionType | null,
                       createdAt, canDelete }
CommentPage          { items: [CommentResponse], nextCursor: string | null }
```

- `author` dùng lại **đúng** schema `PostAuthor` của GĐ2 (tham chiếu `$ref`, không định nghĩa bản thứ hai).
- `canDelete` do server tính (`author_id == actorId` và `visible`), cùng lý do `canEdit` của GĐ2 Mục 8.2.
- Không có `postId` nào của bài khác lọt qua `parentId`: cha luôn cùng bài (Đ-3.4).
- Bình luận trả về cho **mọi** người gọi đều đi qua cùng một mapper; nhánh `deleted` xóa `author`/`body` ở **mapper**,
  không ở truy vấn — để không có đường đọc nào quên.

### 8.2 Cảm xúc

| Method | Path | Auth | Thành công | Lỗi |
|---|---|---|---|---|
| PUT | `/posts/{postId}/reactions/me` | Bearer + `reaction.set` | 200 `ReactionSummary` | 400 type sai · 401 · 404 |
| DELETE | `/posts/{postId}/reactions/me` | Bearer + `reaction.set` | 200 `ReactionSummary` | 400 id sai · 401 · 404 |
| PUT | `/comments/{commentId}/reactions/me` | Bearer + `reaction.set` | 200 `ReactionSummary` | 400 type sai · 401 · 404 |
| DELETE | `/comments/{commentId}/reactions/me` | Bearer + `reaction.set` | 200 `ReactionSummary` | 400 id sai · 401 · 404 |

```
SetReactionRequest { type: "like" | "love" | "haha" | "wow" | "sad" | "angry" }
ReactionSummary    { reactionCounts: { [type]: int > 0 }, myReaction: ReactionType | null }
```

Enum `ReactionType` định nghĩa **một lần** trong `components.schemas` và được `$ref` từ `SetReactionRequest`,
`ReactionSummary`, `CommentResponse`, `PostResponse` — chữ thường, cùng converter `LowercaseEnum` của D0 GĐ2.

### 8.3 Thay đổi trên schema đã có

| Schema | Thay đổi | Loại |
|---|---|---|
| `PostResponse` | thêm `myReaction: ReactionType \| null`, **required** | chỉ-thêm |
| `ReactionType` | schema mới, tách từ chỗ ngầm định trong mô tả `reactionCounts` | chỉ-thêm |
| `info.version` | `1.0.0-gd2` → `1.0.0-gd3` | — |

### 8.4 Codegen frontend

Không thêm script (luật frontend Mục 7). `pnpm gen:api` sinh lại `lib/api/content/schema.d.ts`; alias mới
(`CommentResponse`, `CommentPage`, `ReactionSummary`, `ReactionType`, `CreateCommentRequest`) thêm vào `lib/api/types.ts`
— **không** khai tay. `schema.test-d.ts` của content thêm khẳng định cho `myReaction` nullable và `author` nullable.

---

## 9. Kế hoạch thi công (một người, tuần tự, sau GĐ4)

### Cổng mở — nửa ngày đầu

Làm một mình thì không có buổi họp, nhưng **sản phẩm của cổng mở vẫn bắt buộc** (Mục 0C của kế hoạch gốc): hợp đồng
commit trước, code sau.

1. Tự rà **Đ-3.1 → Đ-3.14**; sửa cái nào thì ghi ngày và lý do dưới quyết định đó. Riêng Đ-3.3 (404 thay 403 của kế
   hoạch gốc) và Đ-3.8 (khuôn bộ đếm) — đọc lại hai lần, chúng đắt nhất.
2. Sửa **`content-v1.yaml`** theo Mục 8 (chỉ-thêm, `info.version` `1.0.0-gd4` → `1.0.0-gd3`) → chạy `pnpm gen:api` →
   **commit cả yaml lẫn `schema.d.ts`**. Cổng `API contract` sẽ **đỏ có chủ đích** tới khi `D0` có controller trả đúng —
   ghi rõ trong commit, giống cách GĐ2 để bốn dòng matrix đỏ chờ `D5`–`D8`.
3. Kiểm trên staging: `SELECT count(*) FROM content.comments` (Mục 4, cạm bẫy 2).

### Thứ tự thi công và ước lượng

Kế hoạch gốc cho GĐ3 **2 ngày với 3 người**. Một người làm cả hai lane: ước lượng **khoảng 6 ngày làm việc**. Ghi thẳng
ra để lịch tổng dời theo (Mục 13).

| Bước | Việc | Ước lượng | Xong khi |
|---|---|---|---|
| 1 | Cổng mở | 0,5 ngày | Hợp đồng + `schema.d.ts` đã commit |
| 2 | **A1–A3** policy, entity, migration · **B1–B2** harness + matrix đỏ có chủ đích | 1 ngày | Migration áp được trên DB có dữ liệu GĐ2 + GĐ4 |
| 3 | **C1–C4** khuôn giao dịch cảm xúc, bộ đếm, `myReaction` theo lô · **D5–D6** · **B4** `COUNT-*` | 1,5 ngày | `COUNT-01..04` xanh **và** đã thấy `COUNT-01` đỏ với bản đọc-rồi-ghi |
| 4 | **D0–D4** bình luận · **B3** bảng đột biến | 1 ngày | `CMT-*`, bảy dòng matrix xanh |
| 5 | **D7** `myReaction` trên `PostResponse` + mở rộng `FEED-13` · **B5** · **E1–E6** | 1,5 ngày | Vitest + Playwright xanh ở local |
| 6 | **F1–F5** cổng đóng + chạy lại k6 lượt lạnh | 0,5 ngày | Mục 11, 12 đã tick hoặc ghi "chờ server" |

**Bộ đếm (bước 3) đứng trước bình luận (bước 4)**: `C2` là đầu việc rủi ro nhất và là khuôn mà C3, D3, D4 chép lại. Biết
muộn rằng khuôn sai là sửa bốn chỗ. `D0` (nền chung: `PostAccess`, `KeysetCursor`) làm ở đầu bước 3 vì cả hai nửa cần nó.

**Khối E không phụ thuộc backend** — chỉ cần hợp đồng ở bước 1, dựng trên `msw/node`. Backend kẹt thì chuyển sang khối E.

**Nhánh và PR:** làm trên `loveart1210`, commit theo mã việc (scope `gd3-<khối>`), một PR vào `develop` khi cả giai đoạn xong.
Không có người review chéo: danh sách "tự rà" ở B.9 thay chỗ đó — chạy nó **trước khi mở PR**.

Việc chưa xong kịp thì cắt theo bảng ưu tiên của `ke-hoach-trien-khai.md` — bình luận và cảm xúc nằm ở dòng "Mở rộng: cắt
xuống mức tối thiểu được". **Thứ tự cắt đã chốt trước** (Mục B.9).

### Cổng đóng

Frontend trỏ staging HTTPS thật; chạy E2E lát cắt với **hai tài khoản** (hai profile Chrome); **đóng băng lại `content-v1`**
(bản `-gd3`); tick Mục 11 và Mục 12.

### Thư viện cần thêm

**Không gói nào**, cả backend lẫn frontend. Không thêm TanStack Query (Đ-3.13), không thêm thư viện cây / virtual list —
tải lười theo cấp (Đ-3.6) giữ mỗi danh sách ≤ 50 dòng.

Kit shadcn có thể cần `collapsible` hoặc `toggle-group` cho thanh cảm xúc: thêm bằng `pnpm exec shadcn add <tên>`
(luật frontend Mục 1 #4), kiểm `--diff` trước khi commit.

---

## 10. Chiến lược test

### 10.1 Nghiệm thu chức năng (integration, Postgres thật qua Testcontainers)

| Mã | Kịch bản | Kỳ vọng |
|---|---|---|
| `CMT-01` | Bình luận gốc vào bài `public` của người khác | 201, `depth=1`, `posts.comment_count` +1 |
| `CMT-02` | Phản hồi cấp 2 rồi cấp 3 | 201 cả hai, `depth` 2 và 3, `reply_count` của từng cha +1 |
| `CMT-03` | Phản hồi vào bình luận cấp 3 (cấp 4) | 400 `errors.parentId`, câu BR-08, **không** có dòng mới |
| `CMT-04` | `parentId` là bình luận của **bài khác** | 400 `errors.parentId`, câu "không còn tồn tại" |
| `CMT-05` | Body rỗng · toàn khoảng trắng · 1001 ký tự | 400 `errors.body` |
| `CMT-06` | Xóa bình luận có 2 phản hồi, rồi đọc lại nhánh | 204; bình luận hiện `status=deleted`, `body=null`, `author=null`, `replyCount=2`; hai phản hồi vẫn đọc được |
| `CMT-07` | Xóa rồi xóa lại | lần hai **403** |
| `CMT-08` | Phản hồi vào bình luận đã xóa | 400 `errors.parentId` |
| `CMT-09` | 25 bình luận gốc, `limit=20` | 20 + `nextCursor`; trang 2 đủ 5, `nextCursor=null`; thứ tự cũ trước |
| `CMT-10` | Chưa có hồ sơ mà bình luận | 403 |
| `REACT-01` | `PUT like` lần đầu | 200, `{like:1}`, `myReaction=like` |
| `REACT-02` | `PUT like` rồi `PUT love` (BR-05) | một dòng `reactions`; `{love:1}` — **không** còn khóa `like` |
| `REACT-03` | `PUT like` hai lần | một dòng; `{like:1}` |
| `REACT-04` | `DELETE` khi chưa thả | 200, tóm tắt hiện tại |
| `REACT-05` | `PUT` rồi `DELETE` | `{}` — **không** phải `{like:0}` |
| `REACT-06` | Cảm xúc trên bình luận đã xóa | 404 |
| `REACT-07` | Thả cảm xúc **không** đổi `posts.updated_at` và `edited_at` | cả hai cột y nguyên (Đ-3.8) |
| `REACT-08` | `GET /posts/{id}` sau khi thả | `myReaction` đúng cho người thả, `null` cho người khác |

### 10.2 Test đồng thời bộ đếm — thứ duy nhất chứng minh Đ-3.8

| Mã | Kịch bản | Kỳ vọng |
|---|---|---|
| `COUNT-01` | 50 người dùng khác nhau `PUT like` song song vào một bài | `reaction_counts.like = 50` **và** `= SELECT count(*)` trên `reactions` |
| `COUNT-02` | **Một** người dùng, 10 request `PUT` song song với loại ngẫu nhiên | đúng 1 dòng `reactions`; tổng `reaction_counts` = 1; **không** có 500 |
| `COUNT-03` | 30 bình luận song song vào một bài + 10 xóa song song | `comment_count` = số dòng `visible` |
| `COUNT-04` | Tạo phản hồi và xóa cha song song, lặp 20 lần | không deadlock (không `40P01`), bộ đếm khớp |

Chạy bằng `Task.WhenAll` qua `HttpClient` thật của `WebApplicationFactory`, Postgres thật. **Bắt buộc** thử cho đỏ: thay
bước 4 của Đ-3.8 bằng nạp-dictionary-rồi-`SaveChanges` → `COUNT-01` phải đỏ. Thử không đỏ nghĩa là test chưa đủ song
song (tăng số request, bỏ `ConfigureAwait` lạ, kiểm pool kết nối), **không** phải khuôn đúng.

`COUNT-04` là test chứng minh thứ tự khóa. Đảo thứ tự trong `DeleteAsync` (bình luận trước, bài sau) phải làm nó đỏ ít
nhất một lần trong 20 vòng — nếu không đỏ thì ghi lại là "không tái hiện được" chứ không tick.

### 10.3 Unit test

`CommentDepthPolicy` (gốc → 1; cha 1 → 2; cha 2 → 3; cha 3 → lỗi BR-08) · `CommentPolicy` (rỗng, toàn khoảng trắng, 1000 vs
1001, emoji đếm UTF-16) · phép tính chuyển trạng thái cảm xúc (bảng bốn nhánh Mục 7.4 dạng hàm thuần: cũ, mới → delta) ·
`KeysetCursor` round-trip và rác vào thì ném · mapper nhánh `deleted` xóa `author` và `body`.

### 10.4 Cổng CI phải mở rộng

1. `AuthZMatrix.cs`: bảy dòng mới (Mục 6.3). Không sửa khung.
2. `ContentContractTests` **không** phải sửa: nó đọc `content-v1.yaml` đã có, tám operation mới tự được so hai chiều.
   Thử cho đỏ một lần: thêm `[ProducesResponseType(409)]` vào một action mà không sửa yaml → `API contract` phải đỏ.
3. `ContentPermissionsTests`: tự canh hai hằng mới **nếu** chúng được thêm vào `ContentPermissions.All` (Đ-3.2). Thêm hằng
   mà quên mảng là test xanh trong chân không — tự rà trước PR (B.9).
4. Cổng codegen FE: không sửa — nó chạy `pnpm gen:api` rồi đòi worktree sạch.
5. `ModuleBoundaryTests`: không sửa. GĐ3 không chạm module nào ngoài Content.

### 10.5 Frontend

| Công cụ | Kiểm |
|---|---|
| Vitest (thuần) | Reducer cảm xúc: chuỗi bấm nhanh sinh ≤ 2 request · lỗi thì rollback về *đã xác nhận* · response về không theo thứ tự không làm sai trạng thái cuối · validation bình luận (Đ-3.14) |
| Vitest + `msw/node` | Cây bình luận: tải trang gốc, mở nhánh, "Xem thêm phản hồi" · cấp 3 không có nút Trả lời · 400 `errors.parentId` hiện dưới ô trả lời · bình luận đã xóa giữ nhánh · 404 không lộ bài |
| Vitest `<StrictMode>` | **Đúng một ca** cho `comment-thread` (nó sở hữu request hủy được — luật frontend Mục 9). Khẳng định trạng thái cuối, **không** đếm request |
| Playwright (local, `workers: 1`) | Hai tài khoản: A đăng bài, B bình luận + A trả lời + B trả lời cấp 3 · B thả tim, đổi sang haha, gỡ · A xóa bình luận giữa nhánh · bấm tim 10 lần nhanh không ra 429 |

`waitFor` chờ handler có `delay` thì ghi `timeout` viết tay (luật frontend Mục 9 — bài học `user-posts` của GĐ2).

---

## 11. Definition of Done

Theo Mục 3.5 của PTTK, áp cho **từng** UC (UC-06 bình luận, UC-07 cảm xúc). Tick ở `F4`, kèm bằng chứng.

- [x] Đủ AC (FR-007, FR-008, BR-05, BR-08) — Mục 10.1 xanh *(2026-09-25: `CommentTests` CMT-01..10 + `ReactionTests` REACT-01..08,
  Postgres thật; `a7c35b3`)*
- [x] Có kiểm RBAC (tầng 2) **và** ownership / BR-02 (tầng 3), có dòng trong `AuthZMatrix.cs` — bảy dòng Mục 6.3 xanh
  trên CI, bảng đột biến `B3` *(local: matrix 40/40; B3 12 đột biến đều bị bắt — `0403ee1`. "Xanh trên CI" tick lại khi PR chạy)*
- [x] Lỗi theo RFC 7807, `errors` đúng key hợp đồng (`body`, `parentId`, `type`, `cursor`), 404 không phân biệt "không có"
  với "không được xem" *(CMT-03/04/05/09b, "Bai_da_xoa_va_khong_duoc_xem_cung_mot_404", type sai → 400)*
- [ ] Bộ đếm khớp bản ghi thật — `COUNT-01..04` xanh **và** câu SQL đối soát Mục 12 trả 0 dòng trên staging
  *(COUNT-01..04 xanh ×5; đối soát trên DB dev sau E2E: 0 dòng. **Chờ server:** chạy lại trên staging sau F3)*
- [ ] Đã chạy thử trên **staging** bằng hai tài khoản thật, qua domain HTTPS (`F3`) *(**chờ server** — cần merge `develop` +
  CD; `e2e/comment-reaction.spec.ts` đã xanh trên stack local, chạy staging bằng `.env.e2e.local`)*
- [x] Hợp đồng `.yaml` khớp Swagger runtime, `pnpm gen:api` chạy lại thì worktree sạch *(ContentContractTests xanh; B5 thử
  đỏ bằng 409 lạ rồi khôi phục)*
- [x] Không lộ secret/PII: bình luận đã xóa không trả `body`/`author` ở **bất kỳ** endpoint nào; log không chứa nội dung
  bình luận *(một mapper duy nhất; CMT-06 + mapper unit + E2E soi response `/replies`; B.9 mục 5 rà log: chỉ id + cấp)*

## 12. Checklist nghiệm thu cuối GĐ3

**Dữ liệu**

- [ ] Migration GĐ3 áp trên staging, log CD có tên migration; `--migrate` chạy lần hai không đổi gì *(dev 2026-09-25: DB có
  7 bài GĐ2/GĐ4 → `20260924163546_Gd3Interactions` áp, thoát 0, lần hai thoát 0. **Chờ server:** staging — trước merge chạy
  `SELECT count(*) FROM content.comments` (cạm bẫy 2))*
- [x] `\d content.comments` có `reply_count`, `reaction_counts`, `ck_comments_root_depth`; **còn** `IX_comments_post_id`
  *(dev + `ContentDbContextSchemaTests`; thêm `ck_comments_status` có `hidden` — Đ-6.14. Staging kiểm lại lúc F1)*
- [ ] Câu đối soát trả **0 dòng** trên staging sau `F3` *(dev sau E2E: 0 dòng cho cả bốn câu. **Chờ server**)*:

  ```sql
  -- bài: comment_count lệch số bình luận visible
  SELECT p.post_id FROM content.posts p
  LEFT JOIN content.comments c ON c.post_id = p.post_id AND c.status = 'visible'
  GROUP BY p.post_id, p.comment_count HAVING p.comment_count <> count(c.comment_id);

  -- bài: reaction_counts lệch GROUP BY (so dạng jsonb, loại 0 không có khóa)
  SELECT p.post_id FROM content.posts p
  WHERE p.reaction_counts <> coalesce((
      SELECT jsonb_object_agg(type, n) FROM (
          SELECT type, count(*) AS n FROM content.reactions
          WHERE target_type = 'post' AND target_id = p.post_id GROUP BY type) t), '{}'::jsonb);
  -- lặp lại hai câu tương tự cho comments (reply_count, reaction_counts)
  ```

**Bảo mật**

- [x] Bảy dòng matrix mới xanh; thử cho đỏ: bỏ kiểm BR-02 ở `ListRepliesAsync` → `READ-CMT-03` đỏ; bỏ kiểm tác giả ở
  `DeleteAsync` → `TC-A03-comment` đỏ *(bỏ ở service thôi thì XANH — câu UPDATE của store còn `author_id`; bỏ cả hai lớp mới đỏ)*
- [ ] Network tab trên staging: response của bình luận đã xóa không có `body` hay `author` *(local: E2E soi response
  `/bff/api/comments/{id}/replies` — `body`/`author` null. **Chờ server**)*
- [ ] Đã `SELECT` trên staging: vai trò `USER` có `comment.create` và `reaction.set` (Mục 5) *(dev: USER + MODERATOR có cả hai.
  **Chờ server**)*

**Lát cắt dọc**

- [ ] E2E trên staging, hai tài khoản: bình luận → trả lời cấp 2 → cấp 3 → nút Trả lời biến mất ở cấp 3 → xóa bình
  luận giữa nhánh → nhánh còn → thả / đổi / gỡ cảm xúc trên bài và bình luận (`F3`) *(xanh local, Chrome 153. **Chờ server**)*
- [ ] Bấm tim liên tục 10 lần trên staging: không 429, trạng thái cuối đúng lần bấm cuối, reload thấy đúng như vậy *(local
  xanh: 10 request 200, không 429 — mỗi request về trước cú bấm kế. **Chờ server**: staging trễ mạng thật mới gộp được chuỗi)*

## 13. Sai khác so với kế hoạch gốc và báo cáo v5.0

| # | Kế hoạch gốc / v5.0 | GĐ3 thực hiện | Lý do |
|---|---|---|---|
| 1 | "Bình luận trên bài không có quyền xem → **403**" | **404** cho đọc, viết và cảm xúc (Đ-3.3) | Quy ước 3b của GĐ1 và `GET /posts/{id}` của GĐ2: 403 tự khai bài có tồn tại |
| 2 | `PUT /reactions` một endpoint, đối tượng trong body | `PUT`/`DELETE …/{đối tượng}/reactions/me` (Đ-3.7) | Tầng 3 đọc từ route; `me` không cho gửi id người khác; `DELETE` idempotent tách bạch |
| 3 | Chỉ nói cập nhật `posts.reaction_counts` + `comment_count` | Thêm `comments.reply_count` + `comments.reaction_counts` (Đ-3.9) | Cảm xúc trên bình luận là một phần FR-008; cây tải lười cần biết có bao nhiêu phản hồi |
| 4 | `content-v1` đã đóng băng ở GĐ2 | Mở lại **chỉ-thêm**: 8 path + `myReaction` (Đ-3.10) | Đúng lời hẹn của `F5` GĐ2: đổi ở cổng mở, không sửa lặng |
| 5 | — (không nói ai được xóa bình luận) | Chỉ tác giả; tầng 2 chỉ `[Authorize]`, không thêm mã quyền (Đ-3.2) | Xóa dữ liệu của mình là quyền của chủ dữ liệu; giữ ma trận 17 mã |
| 6 | "Trả lời tối đa 3 cấp" | Server tính `depth` từ cha, thêm `ck_comments_root_depth` (Đ-3.4) | Client không được quyết định cấp; CHECK bắt lỗi rẻ nhất ở DB |
| 7 | GĐ3 do 3 người làm trong 2 ngày (Ngày 13–15) | **Một người** làm cả hai lane, tuần tự, sau GĐ4, ước lượng ~6 ngày làm việc (Mục 9) | Nhân lực thực tế. Thứ tự GĐ4 → GĐ3 giữ nguyên lịch gốc; lịch tổng dời theo |
| 8 | `comments.status` chỉ `visible`/`deleted` | Thêm `hidden` vào CHECK ngay migration `Gd3Interactions` (Đ-3.5, chốt 2026-09-24) | Thỏa thuận Đ-6.14 với GĐ6 — GĐ6 merge trước GĐ3 |
| 9 | Đ-3.12: event "chỉ log", GĐ6 thay thân hàm sau | Phát thật qua `IEventPublisher` sau `COMMIT` (chốt 2026-09-24) | GĐ6 đã dựng đường ray (Đ-6.4) trước khi GĐ3 bắt đầu code |

Mỗi dòng trong bảng phải được nhắc lại trong commit tương ứng, mở bằng "Lệch …" theo luật commit Mục 5.3.

## 14. Rủi ro cần theo dõi

| Mã | Rủi ro | Dấu hiệu sớm | Ứng phó |
|---|---|---|---|
| **COUNT-01** | Bộ đếm lệch bản ghi thật | Câu đối soát Mục 12 ra dòng; `COUNT-*` đỏ ngẫu nhiên | Đ-3.8 là phòng tuyến chính. Lệch trên staging → sửa code trước, rồi chạy một câu `UPDATE … FROM (GROUP BY)` đối soát một lần, ghi vào commit. Không vá bằng cách "nạp lại số lúc đọc" |
| **HOT-01** | Bài "nóng" làm các transaction cảm xúc xếp hàng trên một dòng | p95 của `PUT …/reactions/me` tăng khi nhiều người bấm cùng bài | Quy mô đồ án chấp nhận được. Đường lui có sẵn: bảng `reaction_count_deltas` chỉ-INSERT + job cộng dồn — hợp đồng API không đổi |
| **LEAK-01** | Một endpoint nhận `commentId` quên lần về BR-02 của bài | `READ-CMT-03` / `READ-REACT-02` đỏ, hoặc tự rà thấy truy vấn bình luận không đi qua `PostAccess` | Một hàm duy nhất (Đ-3.3); tự rà từng endpoint mới (B.9); matrix có dòng cho **cả hai** endpoint nhận `commentId` |
| **DEAD-01** | Deadlock giữa tạo phản hồi và xóa bình luận | Log Postgres `40P01`; `COUNT-04` đỏ | Thứ tự khóa bài → bình luận (Đ-3.8) — ghi thành comment ngay trên hai hàm |
| **FE-02** | Optimistic update sai khi mạng chậm, hoặc tự sinh 429 | Con số nhảy khi bấm nhanh; 429 trên tab Network | Reducer tuần tự theo đối tượng (Đ-3.13) + test Vitest cho chuỗi bấm |
| **GĐ4-01** | `myReaction` được thêm ở chỗ khác ngoài hàm hydrate chung của GĐ4, rồi lọt vào cache feed | Người B thấy nút tim sáng theo cảm xúc của người A | GĐ4 đã dựng cache chỉ lưu `post_id` (Đ-4.9). GĐ3 thêm `myReaction` **chỉ** trong hàm hydrate (Đ-3.11) và mở rộng `FEED-13` |
| **GĐ4-02** | Số cảm xúc trên feed "nhảy lùi" sau khi bấm tim | Feed nạp lại hiện số cũ hơn trạng thái *đã xác nhận* của reducer | GĐ4 hydrate bộ đếm từ DB mỗi lần (Đ-4.9) nên cache không giữ số cũ; nếu vẫn thấy thì FE ưu tiên *đã xác nhận* của reducer hơn số trong trang vừa nạp |
| **SCOPE-02** | Bình luận "tiện tay" thêm sửa, @tag, ảnh | PR khối D có `PATCH /comments` | Mục 2 đã nói rõ ngoài phạm vi |
| **REV-01** | Không ai review chéo — lỗi chỉ con mắt thứ hai bắt được sẽ lọt | Danh sách tự rà B.9 chưa chạy trước PR | Chạy B.9 trước PR; bảng đột biến `B3` và `COUNT-*` đã từng đỏ thay một phần việc review |

---

# Phần B — Kế hoạch triển khai

## B.0 Cách đọc phần này

Phần A nói *cái gì* và *vì sao*. Phần B chia việc thành **sáu khối A–F**, mỗi khối một chuỗi đầu việc có mã
(`A1`, `C2`, …). Mã việc là thứ đi vào **tiêu đề commit** (luật commit Mục 4) với scope `gd3-<khối>` — ví dụ
`feat(gd3-c): C2 — cảm xúc khóa dòng bài, bộ đếm jsonb nguyên tử, không chạm edited_at`.

Một đầu việc được coi là xong khi: code chạy · có test tương ứng · tài liệu/hợp đồng liên quan đã sửa **trong cùng
commit** · `detect-changes` sạch. Không có "xong 90%".

Thứ tự trong mỗi khối là **thứ tự phụ thuộc**, không phải thứ tự ưu tiên — B.9 nói rõ cái nào nằm trên đường găng và
cái nào cắt trước khi trễ.

## B.1 Điểm xuất phát — cái gì đã có sẵn

Kiểm ngày 2026-09-21 trên nhánh `loveart1210` (sau PR #20). GĐ3 **không** dựng lại thứ nào trong bảng dưới:

| Đã có | Ở đâu | GĐ3 dùng để làm gì |
|---|---|---|
| Bảng `comments` (CHECK depth 1..3, status, hai FK cùng schema) + `reactions` (PK ba cột = BR-05) | `Content/Infrastructure/Configurations/`, migration `InitialContent` | Nền của cả giai đoạn — chỉ ALTER thêm (Mục 4) |
| Entity khung `Comment`, `Reaction`, enum `CommentStatus`, `ReactionTargetType`, `ReactionType` | `Content/Domain/` | Thêm hành vi và hai thuộc tính bộ đếm |
| `Post.CommentCount`, `Post.ReactionCounts` (+ `ValueComparer`) | `Content/Domain/Post.cs` | Bộ đếm của bài — GĐ2 chỉ đọc, GĐ3 ghi |
| `PostResponse.commentCount`, `reactionCounts` (luôn `{}`) | `Content/Application/Posts/PostResponse.cs` | Hình dạng không đổi; thêm `myReaction` |
| `PostVisibility.CanView` + `IFriendshipReader` (`AlwaysStrangers`) | `Content/Application/Posts/`, SharedKernel | BR-02 thừa kế cho bình luận (Đ-3.3) |
| `PostCursor` keyset | `Content/Application/Posts/PostCursor.cs` | Tách thành `KeysetCursor` dùng chung (Đ-3.6) |
| `IUserDirectory` batch | SharedKernel + Profile | Tác giả bình luận, một lô mỗi trang |
| `ContentErrors`, `LowercaseEnum`, khuôn tầng 3 | `Content/Application/`, `Content/Infrastructure/` | Lỗi mới thêm vào cùng chỗ; enum chữ thường |
| Mã quyền `comment.create` (7), `reaction.set` (8) đã seed cho USER + MODERATOR | Identity seeder (GĐ1) | Tầng 2 — không seed thêm (Mục 5) |
| Khung AuthZ matrix (đã có `CallerUserId` từ `Q-B2`) | `tests/…/AuthZ/` | Bảy dòng mới, không sửa khung |
| `ContentContractTests`, `ContentPermissionsTests` | `tests/` | Tự phủ endpoint và hằng mới |
| `PostCard` có slot `actions` | `features/post/post-card.tsx` | Tiền lệ cho slot `footer` (Đ-3.13) |
| `request()` đủ `GET/POST/PUT/PATCH/DELETE`, `errorMessage` theo ngữ cảnh | `lib/api/http.ts`, `lib/api/messages.ts` | Thêm ngữ cảnh mới, không sửa cơ chế |

**Ba chỗ có sẵn nhưng phải sửa:**

| Chỗ | Sửa gì | Vì sao |
|---|---|---|
| `features/post/post-card.tsx` | Bỏ comment "GĐ3 mới có endpoint"; thay khối đếm tĩnh bằng slot `footer` | Hai con số tĩnh của GĐ2 chuyển thành thanh cảm xúc thật do `app/` ghép vào |
| Fixture MSW của content (`mocks/`) | Thêm `myReaction: null` | `satisfies PostResponse` đỏ compile sau `gen:api` — mong muốn (Đ-3.10) |
| `ContentPermissions` | Thêm `CommentCreate`, `ReactionSet` vào hằng **và** mảng `All` | Đ-3.2 |

## B.2 Bản đồ công việc

| Khối | Nội dung | Số việc | Cần trước | Chặn | Bước (Mục 9) |
|---|---|---|---|---|---|
| **A. Nền dữ liệu** | Policy thuần, hành vi entity, migration chỉ-thêm | 3 | cổng mở | D1–D4 | 2 |
| **B. Test + cổng CI** | Matrix, test đồng thời bộ đếm, đột biến, rà hợp đồng | 5 | A, C (một phần) | F | 2–5 |
| **C. Cảm xúc và bộ đếm** | Khuôn giao dịch Đ-3.8, SQL jsonb, `myReaction` theo lô | 4 | A2 | D5–D6 | 3 |
| **D. Endpoint** | 4 của bình luận + 4 của cảm xúc + `myReaction` trên `PostResponse` | 8 | A, C | E (ráp thật), F | 3–5 |
| **E. Lane frontend** | Cây bình luận, thanh cảm xúc, ghép slot | 6 | chỉ cần hợp đồng | F | 5 |
| **F. Cổng đóng** | Staging + E2E hai tài khoản + đối soát + đóng băng + chạy lại k6 | 5 | D, E | GĐ tiếp theo | 6 |

Một người làm hết; **thứ tự làm** nằm ở Mục 9, không phải thứ tự chữ cái của khối. Khối E chỉ cần hợp đồng đã commit, dựng
trên `msw/node` — là việc để chuyển sang khi backend kẹt.

---

## B.3 Khối A — Nền dữ liệu

> **Mục tiêu khối:** BR-08 và luật xóa giữ nhánh thành hàm thuần test được; schema có đủ cột bộ đếm; migration chỉ-thêm.

### A1 — `CommentPolicy` + `CommentDepthPolicy` trong `Domain/`

Hai hàm thuần, cùng nếp `PostContentPolicy`: body (rỗng, toàn khoảng trắng, > 1000 UTF-16) và độ sâu (cha → cấp con hoặc
lỗi BR-08). Hằng thông điệp nằm ở đây; `ContentErrors` và validator lấy từ đây, không gõ lại câu (Đ-3.14).

### A2 — Hành vi entity + configuration

`Comment` bỏ chữ "KHUNG" trong doc comment, thêm `ReplyCount`, `ReactionCounts` (+ `ValueComparer`, Mục 4 cạm bẫy 4),
và hai phương thức: `Comment.CreateRoot(...)`, `Comment.CreateReply(parent, ...)` — độ sâu tính trong entity từ
`CommentDepthPolicy`, không có setter công khai cho `Depth`. `Reaction` giữ nguyên (không có hành vi nào đáng đặt ở entity:
bốn nhánh là việc của service trong transaction).

### A3 — Migration `Gd3Interactions`

Theo DDL Mục 4. Nghiệm thu: đọc file migration sinh ra và đối chiếu từng dòng (cạm bẫy 1 — `IX_comments_post_id` phải
còn); `--migrate` hai lần trên DB sạch; `\d content.comments` khớp Mục 4.

**Kết quả khối A:** `dotnet test --filter Category!=Integration` xanh với unit mới; migration áp được lên DB có dữ liệu
GĐ2 thật (dựng bằng seed của B1 GĐ2).

---

## B.4 Khối B — Test và cổng CI

> **Mục tiêu khối:** biến Đ-3.3 và Đ-3.8 thành thứ **chặn merge**. Khung không sửa, chỉ thêm dòng.

### B1 — Harness cho cảnh "bài đổi sang private sau khi có bình luận"

Một helper trong harness integration dựng cảnh bằng API thật: B đăng bài `public` → B bình luận → B `PATCH` bài sang
`private` → trả về `(postId, commentId)`. Dùng chung cho `READ-CMT-03`, `READ-REACT-02` và vài ca `CMT-*`.

### B2 — Bảy dòng AuthZ matrix (Mục 6.3), viết cho đỏ trước

Đúng nếp `B2`/`B3` của GĐ1 và GĐ2: thêm dòng **trước** khi có endpoint → đỏ có chủ đích → `D*` làm xanh.

### B3 — Bảng đột biến

| Đột biến | Test phải đỏ |
|---|---|
| Bỏ `author_id != actorId` trong `DeleteAsync` | `TC-A03-comment` |
| `ListRepliesAsync` không gọi `PostAccess` | `READ-CMT-03` |
| Cảm xúc trên bình luận không lần về bài | `READ-REACT-02` |
| Bỏ `FOR UPDATE` ở bước 1 của Đ-3.8 | `COUNT-02` (500 do `23505`) |
| Bước 4 đổi sang nạp dictionary + `SaveChanges` | `COUNT-01` · `REACT-07` |
| Đổi thứ tự khóa trong `DeleteAsync` | `COUNT-04` (có thể không tái hiện — ghi rõ nếu vậy) |
| Không xóa khóa khi về 0 | `REACT-05` |

### B4 — Test đồng thời `COUNT-01..04` (Mục 10.2)

Tách collection riêng nếu làm chậm bộ integration quá ~3 phút (ngưỡng GĐ1 đã ghi). Mỗi test tạo database riêng
(`CreateDatabaseAsync`) — test sửa dữ liệu không dùng bản seed chung.

### B5 — Rà hợp đồng + thử cho đỏ cổng `API contract`

Thêm một mã trả về vào controller mà không sửa yaml → cổng đỏ → khôi phục; `git status` sạch trước và sau.

---

## B.5 Khối C — Cảm xúc và bộ đếm

> **Mục tiêu khối:** một khuôn giao dịch duy nhất cho mọi thay đổi bộ đếm, đúng dưới tải đồng thời, không chạm
> `updated_at`/`edited_at`.

### C1 — Hàm thuần chuyển trạng thái cảm xúc

`ReactionTransition.Apply(ReactionType? old, ReactionType? @new)` → `(Action, Delta)` với `Action ∈ {Insert, Update,
Delete, None}` và `Delta` là tối đa hai cặp `(type, ±1)`. Bảng bốn nhánh Mục 7.4 thành unit test.

### C2 — `ReactionStore` theo khuôn Đ-3.8 cho **bài**

Transaction tường minh trên `ContentDbContext`; khóa bằng `FOR UPDATE`; bước 4 là một câu SQL tham số hóa trên `jsonb`
(cộng/trừ theo khóa, xóa khóa khi về 0). Viết câu SQL này **một lần** thành hàm dùng chung cho cả `posts` và `comments`
(chỉ khác tên bảng và cột khóa — tên bảng lấy từ hằng, không từ input).

**Không log** nội dung request hay tên người dùng ở đây — chỉ `targetType`, số dòng bị đổi.

### C3 — Mở rộng cho **bình luận** + hai bộ đếm của bình luận

Cảm xúc trên bình luận (khóa dòng `comments`, điều kiện `visible`); `reply_count` +1 khi tạo phản hồi và
`comment_count` ±1 cho D3/D4 — cùng một chỗ, cùng thứ tự khóa bài → bình luận. Ghi thứ tự khóa thành doc comment ngay
trên từng hàm.

### C4 — `myReaction` theo lô

`IReactionReader.GetMineAsync(actorId, targetType, ids)` → `IReadOnlyDictionary<Guid, ReactionType>`. Một truy vấn.
Gắn vào `PostReadService` (một bài và danh sách bài của GĐ2) và vào service đọc bình luận (D1, D2).

---

## B.6 Khối D — Endpoint

> **Mục tiêu khối:** hợp đồng Mục 8 thành hệ thống chạy thật, khớp từng mã lỗi; mọi endpoint nhận id đối tượng đều đi
> qua `PostAccess` (Đ-3.3).

### D0 — Nền chung

`PostAccess.ResolveVisibleAsync` (Đ-3.3), tách `KeysetCursor` từ `PostCursor` (giữ `PostCursor` là lớp mỏng dùng lại —
**chạy impact analysis trước**: `PostCursor` đang được `ListUserPostsQuery` và `PostStore` dùng), `ContentErrors` thêm
`CommentNotFound` (404), `ParentNotFound`/`TooDeep` (400 `parentId`), hai hằng mới ở `ContentPermissions`. Controller
mới `CommentsController`, `ReactionsController` khai `[ApiExplorerSettings(GroupName = ContentApiGroup.Name)]` từ file
đầu tiên.

### D1 — `GET /posts/{postId}/comments`

BR-02 → trang gốc theo keyset ASC trên `idx_comments_post_roots` → một lô `IUserDirectory` → một lô `myReaction`. Mapper
xóa `author`/`body` cho nhánh `deleted`.

### D2 — `GET /comments/{commentId}/replies`

Tra `post_id` của cha → `PostAccess` → trang phản hồi trên `idx_comments_parent`. Cùng mapper với D1.

### D3 — `POST /posts/{postId}/comments`

Thứ tự Mục 6.1: tầng 2 → hồ sơ (403) → BR-02 (404) → cha (400) → transaction Mục 7.2 bước 4 → event sau `COMMIT`.

### D4 — `DELETE /comments/{commentId}`

Tầng 3 sở hữu; transaction Mục 7.3; 204. Lần hai → 403.

### D5 — `PUT` + `DELETE /posts/{postId}/reactions/me`

Controller mỏng gọi `ReactionStore` của C2. `type` sai → 400 tự động từ converter enum.

### D6 — `PUT` + `DELETE /comments/{commentId}/reactions/me`

Như D5, đối tượng là bình luận (C3). Bình luận `deleted` → 404.

### D7 — `myReaction` trên `PostResponse` + rà RFC 7807

Nối C4 vào `GET /posts/{id}`, `GET /users/{id}/posts`, `POST /posts` (luôn `null`), `PATCH /posts/{id}`. Rồi rà giống `D9`
của GĐ2: từng mã lỗi trong hợp đồng đối chiếu với thứ code trả, `title`/`type` theo `ProblemTitles`, không thông điệp
nào chứa id, nội dung bình luận hay tên kiểu.

---

## B.7 Khối E — Lane frontend

> **Mục tiêu khối:** người dùng thật thấy và dùng được cây bình luận và cảm xúc; optimistic update không bao giờ để lại
> trạng thái sai.

Luật đặt file theo `frontend-rules.md` Mục 2: `features/comment/`, `features/reaction/`. Không `features/` nào import chéo;
ghép ở `app/`.

### E1 — Codegen, kiểu, fixture, ngữ cảnh lỗi

`pnpm gen:api` → alias mới ở `lib/api/types.ts` → sửa fixture `satisfies` của GĐ2 (`myReaction: null`) → hàm mới ở
`lib/api/content-api.ts` → `errorMessage` thêm ngữ cảnh `comment-read`, `comment-create`, `comment-delete`, `reaction`.
Bốn ngữ cảnh chứ không một, cùng lý do `Q-E4` của GĐ2: 403 của `comment-create` là "chưa có hồ sơ", 403 của
`comment-delete` là "không phải bình luận của bạn".

`lib/validation/comment.ts`: chép Đ-3.14, có test.

### E2 — Cây bình luận tải lười

`features/comment/comment-thread.tsx`: trang gốc + "Xem thêm bình luận"; mỗi bình luận có "Xem N phản hồi" tải trang phản
hồi; cấp 3 không có nút Trả lời; bình luận `deleted` hiện "Bình luận đã bị xóa" và giữ nhánh. Request hủy khi rời màn
(`AbortController` tạo **trong effect** — luật frontend Mục 1 #14). Đúng một ca `<StrictMode>`.

### E3 — Thanh cảm xúc + reducer

`features/reaction/reaction-reducer.ts` (thuần, test Vitest không render) theo luật Đ-3.13; `reaction-bar.tsx` dùng kit
(`Button` + popover/toggle của kit), icon `lucide-react`, màu theo token — **không** màu thô cho từng loại cảm xúc
(luật frontend Mục 1 #5); cần phân biệt sáu loại thì dùng icon + nhãn, không dùng sáu màu.

### E4 — Ô bình luận, ô trả lời tại chỗ, xóa

Dùng `components/form/` (không tự ráp `Field` + `Textarea`). 400 `errors.parentId` / `errors.body` hiện dưới ô. Xóa có
`AlertDialog` xác nhận; hiện nút theo `canDelete` của server.

### E5 — Slot và ráp ở `app/`

`PostCard` nhận `footer`; `PostDetail`, danh sách bài của GĐ2 và chỗ ráp `renderPost` của trang chủ (GĐ4) chuyền slot xuống;
`app/(app)/(with-profile)/posts/[postId]/page.tsx` ráp `ReactionBar` + `CommentThread`. Danh sách bài chỉ ghép
`ReactionBar` và số bình luận dẫn tới trang chi tiết — **không** mở cây bình luận trong danh sách.

### E6 — Vitest + Playwright

Theo Mục 10.5. Playwright local, `workers: 1`, kết quả dán vào PR kèm bản Chrome đã chạy (Đ-E8).

---

## B.8 Khối F — Cổng đóng

> **Mục tiêu khối:** chứng minh trên hệ thống thật — và chứng minh con số, không chỉ chứng minh nút bấm.

### F1 — Deploy staging qua CD

**Không có key `.env` mới** (GĐ3 không thêm cấu hình). Trước merge: kiểm Mục 4 cạm bẫy 2 trên staging. Sau deploy:
service `migrate` xanh, log có tên migration GĐ3, `/health/ready` = 200.

### F2 — Kiểm tab Network trên staging

Chỉ thấy `/bff/*`; response bình luận đã xóa không có `body`/`author`; không JWT nào.

### F3 — E2E lát cắt, hai tài khoản

Theo Mục 12 "Lát cắt dọc". Bằng chứng: ảnh cây 3 cấp có một nút đã xóa giữa nhánh; ảnh Network của chuỗi bấm tim
nhanh (≤ 2 request, không 429).

### F4 — Checklist Mục 12 + Definition of Done Mục 11

Tick từng dòng có bằng chứng; chạy câu đối soát bộ đếm trên Postgres staging và dán kết quả (0 dòng). Dòng nào chờ
thao tác trên server thì ghi rõ "chờ server", **không xóa dòng** — đúng nếp `F4` của GĐ2.

### F5 — Đóng băng lại `content-v1` (bản `-gd3`) + bàn giao

Thông báo đóng băng; liệt kê phần **hoãn có địa chỉ** (Mục 2); chạy lại k6 lượt lạnh của GĐ4 (Đ-4.13) với `myReaction` trên
đường hydrate và ghi con số cạnh mốc GĐ4 trong `docs/giai-doan-4/bao-cao-k6-so-bo.md`.

---

## B.9 Thứ tự thực thi, đường găng, và thứ tự cắt

**Đường găng:** `A2 → A3 → C3` · `C1 → C2 → D5` · `D0 → D1 → D3` · `F1 → F3 → F4`

`C2` phải xong **ngay trong bước 3** (Mục 9) — nó là đầu việc rủi ro nhất (đồng thời, SQL jsonb) và là khuôn mà C3, D4, D5, D6 chép lại.
Biết muộn một ngày rằng khuôn sai là sửa bốn chỗ.

**Thứ tự cắt khi trễ** — chốt trước để lúc trễ không phải họp. Cắt từ trên xuống, dừng khi kịp:

| Thứ tự | Cắt gì | Còn lại vẫn đạt FR |
|---|---|---|
| 1 | Cảm xúc trên **bình luận** (D6, phần bình luận của C3, `READ-REACT-02`) | FR-008 trên bài vẫn đủ. Xóa hai path khỏi yaml và ghi vào Mục 13 — **không** để path trả 501 (hợp đồng hứa một thứ không tồn tại) |
| 2 | Cấp 3 trên UI (giữ API) | API vẫn đủ BR-08; FE chỉ cho trả lời tới cấp 2 |
| 3 | "Xem thêm phản hồi" (trang 2 của phản hồi) | FE tải 50 phản hồi đầu, ghi giới hạn |
| **Không cắt** | Đ-3.3 (BR-02 thừa kế), Đ-3.8 (bộ đếm đúng), bảy dòng matrix | Đây là phần "Bắt buộc phi chức năng" của bảng ưu tiên — GOAL-03 |

**Năm thứ không test tự động nào bắt được — tự rà trước khi mở PR** (không có người review chéo):

1. `actorId` lấy từ `User.GetUserId()`, **không** từ route/body — kể cả trong đường `…/reactions/me`.
2. **Thứ tự khóa bài → bình luận** ở mọi hàm có transaction (`COUNT-04` có thể không tái hiện được).
3. Mọi truy vấn đọc bình luận đi qua `PostAccess` — rà bằng cách tìm mọi chỗ truy vấn `comments` ngoài `CommentStore`.
4. Event phát **sau** `COMMIT`, không trong transaction (Đ-3.12).
5. Không log `body` của bình luận.

---

## B.10 Mục tiêu từng khối — chúng cộng lại thành cái gì

| Khối | Mục tiêu | Thiếu nó thì mất gì |
|---|---|---|
| **A** | Luật nghiệp vụ thành hàm thuần; schema đủ cột | BR-08 chỉ nằm ở một `if` trong controller, không ai test được |
| **B** | Bộ đếm và BR-02 thừa kế thành cổng chặn merge | Lệch bộ đếm và lộ bài `private` chỉ lộ ra ở GĐ8 — hoặc không bao giờ |
| **C** | Một khuôn giao dịch đúng dưới tải đồng thời | Con số trên màn hình sai dần theo thời gian, không tự lành |
| **D** | Hợp đồng thành hệ thống chạy thật | Không có sản phẩm |
| **E** | Người dùng thật dùng được, giao diện không nói dối lâu hơn một request | Backend đúng nhưng cảm giác "lag", hoặc tự sinh 429 |
| **F** | "Xong" thành sự kiện kiểm chứng được — kể cả con số | "Xong" thành cảm giác |

## B.11 Mục tiêu của GĐ3

### Ba điều kiện để tuyên bố GĐ3 xong

Thiếu bất kỳ điều nào thì **chưa xong**, dù code đã chạy:

1. **Trên staging, một bài có cây bình luận 3 cấp với một bình luận đã xóa giữa nhánh**, do hai tài khoản thật tạo qua
   giao diện, và câu đối soát bộ đếm (Mục 12) trả 0 dòng.
2. **Bảy dòng AuthZ matrix mới và bốn test `COUNT-*` xanh trên CI**, và đã từng thấy đỏ khi cố tình bỏ kiểm BR-02 và bỏ
   khóa dòng (bảng đột biến `B3`).
3. **CI xanh cả năm nhóm**: unit + integration · AuthZ matrix · cổng hợp đồng · codegen FE · bundle sạch.

### GĐ3 để lại gì cho GĐ5–GĐ8

| Di sản | Ai thừa hưởng |
|---|---|
| Khuôn giao dịch bộ đếm (khóa dòng → SQL nguyên tử → không chạm `updated_at`) | GĐ5 (`seq_counter` của hội thoại, badge chưa đọc), GĐ6 (gộp thông báo) |
| `PostAccess` — BR-02 cho tài nguyên con | GĐ6 (báo cáo một bình luận phải kiểm được người báo cáo có xem được nó) |
| Luật "trường theo người xem không vào cache chung" (`myReaction`, `canEdit`, `canDelete`) | GĐ5 (danh sách hội thoại), GĐ6 (thông báo) |
| `KeysetCursor` dùng chung, cả hai chiều sắp | GĐ5 (lịch sử tin nhắn — cuộn ngược) |
| Reducer optimistic tuần tự theo đối tượng | GĐ6 (đánh dấu đã đọc) |
| Event `CommentCreated`, `ReactionSet` sau `COMMIT` | GĐ6 (notification loại comment/reaction) |
| `comments.status` chỉ có `visible`/`deleted` | GĐ6 — thêm `hidden` cho kiểm duyệt (ALTER CHECK) |
| Nợ có địa chỉ: cảm xúc mồ côi khi xóa cứng bài/bình luận | GĐ8 (NĐ 13/2023) |

**Một câu để nhớ:** GĐ2 là lần đầu hệ thống có **thứ thuộc về ai đó**; GĐ3 là lần đầu **nhiều người cùng chạm vào một
thứ** — và đó là lúc "đúng với một người" không còn đủ, phải "đúng khi năm mươi người bấm cùng một giây".

---

## Thực tế thi công (2026-09-24 → 2026-09-25, nhánh `endgame`)

Một người làm cả hai lane, theo thứ tự Mục 9. Mỗi dòng là một commit (tiêu đề và thân bài có đủ chỗ lệch, `Test:`,
`detect-changes:`).

| Bước | Commit | Kết quả |
|---|---|---|
| 0 — dọn khối A | `abbea97` | `CommentStatus.Hidden` (Đ-6.14) · sinh lại `Gd3Interactions` (chưa lên staging) · gỡ EF Design khỏi `SocialApp.Api` (revert `c720f21`, giữ ADR-001) |
| 1 — cổng mở | `08baa07` | `content-v1` `1.0.2-gd4` → `1.1.0-gd3`, chỉ-thêm; `schema.d.ts` + fixture `myReaction: null` |
| 2 — B1–B2 | `f8e89ad` | Bảy dòng matrix; ba đỏ có chủ đích, bốn "xanh trong chân không" tới khi có route |
| 3–5 — C + D | `a7c35b3` | `ReactionStore` (Đ-3.8), `CommentStore` (khóa BÀI → CHA/BÌNH LUẬN), `PostAccess`, `KeysetCursor`, 8 endpoint, event thật, `myReaction` ở `PostHydrator` + đọc/sửa một bài |
| B3 + B5 | `0403ee1` | 12 đột biến đều bị bắt (bảng ở thân commit); COUNT-04 phải tăng lên 5 phản hồi/vòng mới tái hiện được deadlock khi đảo thứ tự khóa |
| D7 | `1d080c4` | FEED-13 thêm `myReaction` hai người xem, đổi cảm xúc thấy ngay dù trúng cache |
| E1–E6 | `c00cf94`, `505d539` | Cây bình luận, thanh cảm xúc + reducer, slot ở `app/(app)/(with-profile)/_interactions/`, Playwright hai tài khoản |

**Con số cuối (local):** Unit 356 → 383 · Integration 627 → 671 · Architecture 26 · Vitest 590 → 625 · matrix 40/40 ·
`FEED-Q1` 5 → 6 câu SQL (thêm lô `myReaction`, Đ-3.11). Migration áp trên DB dev có dữ liệu GĐ2/GĐ4, `--migrate` lần hai
thoát 0; câu đối soát Mục 12 trên DB dev sau E2E: 0 dòng.

**Ba điều học được, ghi lại để GĐ sau khỏi vấp:**

1. **Hai lưới của Đ-3.8 che cho nhau.** Bỏ riêng `FOR UPDATE` thì `COUNT-01` vẫn xanh (SQL jsonb nguyên tử giữ đúng số) và chỉ
   `COUNT-02` đỏ (500 do `23505`); đổi riêng bước 4 sang đọc-rồi-`SaveChanges` thì `COUNT-01` vẫn xanh (khóa tuần tự hóa) và
   chỉ `REACT-07` đỏ (`updated_at` bị đóng dấu). Muốn `COUNT-01` đỏ phải bỏ CẢ HAI. Bảng đột biến gốc (B3) giả định một đột biến
   một test — thực tế là một đột biến một LƯỚI.
2. **Test deadlock cần đủ tranh chấp.** `COUNT-04` một-phản-hồi-mỗi-vòng không tái hiện được deadlock khi đảo thứ tự khóa (2/2 lần
   xanh); năm phản hồi xếp hàng trên khóa bài cho lần xóa chen giữa thì đỏ 2/2.
3. **Test "bấm nhanh" không được dựa vào độ trễ giả.** Cả bộ Vitest chạy nặng thì một cú bấm của `userEvent` chậm hơn 150 ms độ
   trễ giả; request đầu về giữa hai cú bấm và mỗi cú bấm thành một chuỗi riêng (4 request — hợp lệ). Test giữ response đầu bằng
   một chốt thì tất định. Cùng lý do, E2E local thấy 10 request cho 10 cú bấm (API trả trong vài ms).

**Chờ server (khối F) — không xóa dòng nào ở Mục 11–12:**

- F1: trước merge chạy `SELECT count(*) FROM content.comments` trên staging (Mục 4 cạm bẫy 2); merge `endgame` → `develop`
  (người trong đội bấm), CD chạy `--migrate`, kiểm log có `Gd3Interactions`, `/health/ready` = 200.
- F2–F3: `E2E_BANG_CHUNG=../../docs/giai-doan-3/bang-chung PLAYWRIGHT_BASE_URL=https://mxh.banhgao.net
  PLAYWRIGHT_API_URL=https://mxh.banhgao.net/api/v1 pnpm exec playwright test e2e/comment-reaction.spec.ts` (tài khoản từ
  `.env.e2e.local`).
- F4: câu đối soát Mục 12 + `SELECT` quyền của `USER` trên Postgres staging, dán kết quả.
- F5: chạy lại k6 lượt lạnh của GĐ4 (Đ-4.13) — `myReaction` thêm một câu vào đường hydrate; ghi con số cạnh mốc GĐ4 trong
  `docs/giai-doan-4/bao-cao-k6-so-bo.md`. Đóng băng `content-v1` bản `1.1.0-gd3`.
- Báo người làm GĐ6: `CommentCreated`/`ReactionSet` đã phát thật; provider kiểm duyệt bình luận và nhãn "đã bị ẩn" có sẵn chỗ
  cắm (`CommentStatus.Hidden`, mapper coi như `deleted`).

**Môi trường máy dev khi thi công:** Node 24 ở `/tmp/node24` (máy là 20); stack dev `docker compose -p socialapp-gd5dev`;
`post-create.spec.ts` đỏ ở bước `PUT` ảnh lên R2 dev (0 request tới R2 — khóa R2 dev chờ xoay), không liên quan GĐ3.
