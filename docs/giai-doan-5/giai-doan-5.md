# GĐ 5 — Nhắn tin 1-1 realtime (UC-15) · lịch gốc Ngày 15–19 ⚠️ trọng điểm realtime

> Nguồn: [`ke-hoach-trien-khai.md`](../ke-hoach-trien-khai.md) mục "GĐ 5 — Nhắn tin 1-1 realtime", nhịp cổng mở / cổng
> đóng ở Mục 0C, và báo cáo PTTK v5.0 (UC-15 ở Mục 2.3, US-015 ở Mục 3, FR-015/016 ở Mục 4, BR-06/09, ENT-06/07 ở Mục 5.5,
> Mục 5.6 index, SEQ-02 ở Mục 6.5, máy trạng thái tin nhắn, ADR-003, ISS-01, TC-A04/A07).
> Quyết định frontend đã chốt từ GĐ1 mà GĐ5 phải thi công: **Đ-E16** (vé realtime ngắn hạn dùng một lần) trong
> [`huong-dan-khoi-e-frontend.md`](../giai-doan-1/huong-dan-khoi-e-frontend.md).
> Nền móng: GĐ1 (xác thực, `revoked:user`, BFF), GĐ2 (khuôn module, `IUserDirectory`, `IObjectStorage`, cursor), GĐ4
> (`IFriendshipReader` thật). GĐ5 **tiêu thụ**, không dựng lại.
>
> **Người thi công: một người (B)**, làm cả backend lẫn frontend. A đang làm GĐ4 → GĐ3, chủ dự án đang làm GĐ7; ba người
> chạy song song. Chỗ đụng nhau đã liệt kê ở Mục 9.4.
>
> **Ba mốc không lùi được của giai đoạn này:**
> 1. **E2E hai trình duyệt đo p95 gửi→nhận ≤ 1s trên staging** (NFR-PERF-03, GOAL-02) — đo bằng **chính chat UI thật**,
>    không trang HTML tạm. Đây là chỗ duy nhất GOAL-02 được nghiệm thu.
> 2. **Không có đường nào đọc hay gửi vào hội thoại của người khác** (BR-06, TC-A04) và **không nhắn được cho người không
>    phải bạn** (BR-09, TC-A07) — kể cả qua hub, nơi AuthZ matrix HTTP không nhìn thấy.
> 3. **Không mất tin, không trùng tin** — retry cùng `clientMsgId` không tạo bản thứ hai; 50 tin gửi đồng thời vào một hội
>    thoại ra đúng `seq` 1..50, không lỗ, không trùng.

## Tài liệu này có hai phần

| Phần | Trả lời câu hỏi | Đọc khi |
|---|---|---|
| **A — Thiết kế và quyết định** | *Cái gì* và *vì sao* | Trước khi gõ dòng đầu tiên; lúc review PR |
| **B — Kế hoạch triển khai** | *Làm gì, theo thứ tự nào, xong thì trông ra sao* | Lúc lên lịch; lúc kiểm tiến độ |

Hướng dẫn thi công từng bước (lệnh nào, file nào, cạm bẫy nào) nằm ở các file `huong-dan-khoi-*.md` cùng thư mục,
**viết khi khối đó bắt đầu** — đúng nếp GĐ1, GĐ2, GĐ4.

> **Trạng thái các quyết định:** mười tám quyết định ở Mục 3 là **đề xuất**, viết ngày 2026-09-22 trên nền code nhánh
> `loveart1210` (commit `e120090`, GĐ4 xong khối A + cổng mở). Cổng mở chốt hoặc sửa từng cái; cái nào sửa thì ghi ngày
> và lý do ngay dưới quyết định đó. Làm một mình thì "cổng mở" là một buổi tự rà **có sản phẩm**: hợp đồng commit trước,
> code sau — không bỏ bước này (Mục 0C).

> **Bàn giao từ GĐ4 (2026-09-23, GĐ4 `F4` — PR #21 đã merge vào `develop` ở `0d0a093`).** Thêm vào, không sửa quyết định nào
> của tài liệu này:
>
> - `IFriendshipReader` **thật** (SocialGraph) — BR-09 / `TC-A07*` dựa vào nó. `SocialGraph` D2/D3 **đã có**: `ArrangePath`
>   dựng quan hệ bạn bè qua API (`POST /friends/requests` + `accept`), **không** cần nhánh `INSERT` thẳng + TODO.
> - Event sau `COMMIT`: `SocialGraphEvents.FriendRequestSent(a, b)`, `FriendRequestAccepted(a, b)` (chỉ log ở GĐ4, Đ-4.15).
> - Slot `actions` của `PublicProfile` giờ là **hàm** `(profile) => ReactNode`, chỉ gọi khi hồ sơ đã nạp (lệch Đ-4.16, GĐ4
>   E5): `StartChatButton` ráp **trong hàm đó** cạnh `RelationshipButtons`. `RelationshipButtons` không xuất trạng thái
>   quan hệ ra ngoài — nút "Nhắn tin" (chỉ khi `friendship = friends`) cần tự đọc `GET /relationships/{id}` hoặc nâng trạng
>   thái lên `app/`: quyết định của GĐ5.
> - `hooks/use-cursor-pages.ts` (GĐ4 Q-E8): hook phân trang cursor dùng chung, không biết nghiệp vụ — lịch sử hội thoại / danh
>   sách hội thoại dùng lại; `use-post-page.ts` của GĐ2 chuyển sang hook này là nợ có địa chỉ ghi cho GĐ5.
> - Lỗi cùng status khác nghĩa phân nhánh theo `type` của Problem Details (GĐ4 Q-E4, luật frontend Mục 4).
> - Báo cáo k6 sơ bộ GĐ4 (`docs/giai-doan-4/bao-cao-k6-so-bo.md`) là mốc so sánh.

---

# Phần A — Thiết kế và quyết định

## 0. Thuật ngữ

| Từ | Nghĩa trong tài liệu này |
|---|---|
| **hội thoại** | ENT-06 `conversations` — đúng một hội thoại cho mỗi cặp người dùng, hai thành viên cố định |
| **`seq`** | Số thứ tự của tin trong **một** hội thoại, tăng 1, 2, 3… không lỗ. Là thứ tự hiển thị và là cursor của lịch sử |
| **`clientMsgId`** | UUID do **client** sinh cho mỗi tin trước khi gửi. Gửi lại cùng giá trị = cùng một tin (idempotency) |
| **ACK** | Kết quả trả về của lời gọi hub `SendMessage` — tin đã lưu bền, trạng thái **Sent** |
| **mốc đã nhận / đã xem** | `delivered_seq` / `seen_seq` của mỗi thành viên: "tôi đã nhận/xem mọi tin có `seq` ≤ N" (Đ-5.6) |
| **hub** | `ChatHub` của SignalR ở đường `/hubs/chat`; kênh hai chiều qua WebSocket |
| **vé (ticket)** | Chuỗi ngẫu nhiên 32 byte, sống 30 giây, dùng **một lần** để bắt tay với hub (Đ-E16, Đ-5.9) |
| **backplane** | Redis pub/sub để hai bản sao API đẩy tin cho kết nối nằm ở bản sao kia (ADR-003) |
| **presence** | Redis biết người dùng nào đang có ít nhất một kết nối hub |
| **fallback** | Mất WebSocket → gửi bằng REST `POST …/messages`, nhận bằng hỏi lại mỗi 3 giây (UC-15 A2, ISS-01) |
| **p95 gửi→nhận** | 95% tin tới trình duyệt người nhận trong ≤ X ms kể từ lúc người gửi bấm gửi (NFR-PERF-03) |

## 1. Mục tiêu giai đoạn

### Phát biểu một câu

> **Hai người bạn trên staging nhắn tin cho nhau và thấy tin tới gần như tức thời, kèm trạng thái Đã gửi → Đã nhận →
> Đã xem; mất mạng rồi có lại không mất tin nào, không lặp tin nào; và không một đường nào — REST hay hub — cho người thứ
> ba đọc hay chen vào hội thoại của họ.**

### Mục tiêu chính thức và khối nào gánh

| Mã | Mục tiêu | Đạt bằng | Kiểm bằng |
|---|---|---|---|
| **FR-015** | Lưu bền tin + đẩy tới người nhận ≤ 1s khi online | A, C2, D5, D8, E4–E5 | AC US-015/AC-01, F3 |
| **FR-016** | Chỉ 2 thành viên đọc/gửi, cập nhật Sent/Delivered/Seen | D0 (tầng 3), D6, E6 | TC-A04*, HUB-07, RCP-* |
| **BR-06** | Chỉ 2 thành viên của hội thoại | CHECK + UQ ở DB (A3) + tầng 3 ở service (D0) | TC-A04, TC-A04-messages, TC-A04-send |
| **BR-09** | Chỉ bạn bè tạo hội thoại / nhắn tin; hủy kết bạn → chỉ đọc | D1, D5 (`IFriendshipReader` lúc gửi) | TC-A07, TC-A07-send, AC-04 |
| **US-015 AC-02** | B offline → tin lưu bền, B thấy + badge khi online | Mốc đã xem (Đ-5.6) + D7 + E3 | MSG-10, F2 |
| **US-015 AC-03** | Retry cùng `clientMsgId` không tạo tin trùng | UQ `(conversation_id, client_msg_id)` + Đ-5.5 | MSG-C2, MSG-06 |
| **NFR-PERF-03 / GOAL-02** | p95 gửi→nhận ≤ 1s | Cả giai đoạn | **F3 — báo cáo đo trên staging** |
| **GOAL-03** | 0 IDOR ở module nhắn tin | Tầng 3 + matrix + test hub | Mục 6.3, Mục 6.4 |
| **ISS-01** | SignalR trễ tiến độ → vẫn có đường gửi/nhận | Fallback REST + hỏi lại 3s (Đ-5.12) | E7, MSG-FB-* |

### Vì sao GĐ5 là giai đoạn rủi ro kỹ thuật nhất

Nhìn danh sách endpoint thì GĐ5 chỉ có "gửi tin, đọc tin". Bốn thứ làm nó khác mọi giai đoạn trước:

1. **Lần đầu hệ thống có kết nối sống lâu.** Mọi giai đoạn trước là request–response: xác thực một lần mỗi request,
   xong là quên. Kết nối hub sống hàng giờ — nên "đăng xuất", "bị khóa", "hạ quyền" phải **cắt được một kết nối đang
   mở**, thứ mà JWT bearer của GĐ1 không bao giờ phải làm (Đ-5.10).
2. **AuthZ matrix HTTP không nhìn thấy hub.** Khung matrix của GĐ1 bắn HTTP request. Một lời gọi hub `SendMessage` vào
   hội thoại của người khác đi qua **một cửa khác** — nếu chỉ test REST, IDOR qua hub lọt thẳng tới GĐ8 (Mục 6.4).
3. **Đồng thời là bình thường, không phải ngoại lệ.** Hai người gõ cùng lúc, một người mở ba tab, mạng chập chờn làm
   client gửi lại — `seq` phải không lỗ, không trùng dưới mọi thứ tự đến (Đ-5.4, Đ-5.5).
4. **Ba lớp hạ tầng chen giữa trình duyệt và API: Cloudflare → apache → Kestrel.** Mỗi lớp có thể cắt WebSocket theo
   cách riêng (thiếu `Upgrade`, timeout nhàn rỗi, log mất query). Integration test trên `TestServer` không chạm lớp nào
   trong số đó — nên **spike end-to-end trên staging là việc đầu tiên**, không phải việc cuối (C0, Mục 9.2).

---

## 2. Phạm vi

### Trong phạm vi

| Nhóm | Nội dung |
|---|---|
| **Module Messaging** | Schema `messaging`; bảng `conversations`, `messages`; `MessagingDbContext`; migration; nối `--migrate` |
| **Endpoint REST** | Tạo/lấy hội thoại, danh sách hội thoại, lịch sử tin (cuộn ngược + lấp khoảng hở), gửi tin (fallback), mốc đã nhận/đã xem, tổng chưa đọc, xin vé realtime — 8 endpoint (Mục 8.1) |
| **Hub** | `ChatHub` tại `/hubs/chat`: 2 phương thức client→server, 2 sự kiện server→client (Mục 8.2) |
| **Xác thực hub** | Scheme `RealtimeTicket` trong SharedKernel theo Đ-E16; kiểm `revoked:user` lúc bắt tay **và** mỗi lần gọi phương thức |
| **Presence tối thiểu** | Redis biết ai online — GĐ6 dùng để quyết định có gửi thông báo không |
| **Backplane Redis** | Bật theo cấu hình; mặc định tắt khi chạy một bản sao (Đ-5.13) |
| **Hạ tầng** | apache chuyển `/hubs` (WebSocket) về API; không log query string của `/hubs` (Đ-E16) |
| **Lane frontend** | Danh sách hội thoại · cửa sổ chat cuộn ngược · composer optimistic có Thất bại/Thử lại · trạng thái Sent/Delivered/Seen · badge chưa đọc · nút "Nhắn tin" trên hồ sơ bạn bè · fallback + tự kết nối lại · **công cụ đo p95** |
| **Test** | AuthZ matrix (dòng mới) · test hub (xác thực + tầng 3) · test đồng thời · hai cổng hợp đồng (REST + hub) · E2E hai trình duyệt |

### Ngoài phạm vi — hoãn có địa chỉ

| Việc | Hoãn tới | Lý do |
|---|---|---|
| Tạo **thông báo** khi B offline (UC-15 A1, FR-018) | **GĐ6** | Module Notification chưa có. GĐ5 phát event `MessageSent` sau `COMMIT`, **chỉ log** (Đ-5.15) — GĐ6 nối vào đúng chỗ đó; presence (Đ-5.11) là thứ GĐ6 hỏi "B có online không" |
| Cắt kết nối hub ngay khi Admin khóa/hạ quyền | **GĐ6** (bên ghi `revoked:user`) | Bên đọc làm ở GĐ5 (Đ-5.10); bên ghi của `user.lock` / `role.assign` là việc GĐ6 |
| Ảnh / tệp trong tin nhắn | **Ngoài MVP** | PTTK có `media_attachments.owner_type = 'message'` nhưng **không** FR, API hay luồng nào; `StorageKeys` không có tiền tố `messages/`. CK đã chừa sẵn từ GĐ2 nên làm sau không cần đổi CK |
| Sửa / thu hồi / xóa tin | **Ngoài MVP** | Không nguồn nào trong PTTK đặc tả |
| Chat nhóm | **Ngoài MVP** | PTTK chốt "1-1"; bảng `conversations` hai cột thành viên là cố ý (Đ-5.2) |
| "Đang gõ…", chấm online trên UI | **Ngoài MVP** | Không có FR; presence có sẵn nên làm sau là thêm một sự kiện hub |
| Xóa/ẩn danh tin nhắn khi xóa tài khoản | **GĐ8** (NĐ 13/2023) | Hệ quả của Đ-5.1 (không FK chéo schema) — đường xóa tài khoản phải fan-out sang Messaging. Ghi vào nợ có địa chỉ |
| Partition bảng `messages` theo tháng | **Roadmap** | PTTK Mục 5.6: chỉ cần khi > 50M dòng |
| Grafana cho chat (tin/phút, độ trễ đẩy) | **GĐ7** | GĐ5 để sẵn một counter + một histogram (D5, D8); GĐ7 vẽ |

---

## 3. Quyết định thiết kế

Mười tám quyết định. **Đ-5.1 → Đ-5.6** là mô hình dữ liệu và nghiệp vụ — đắt nhất nếu sai, chốt đầu tiên ở cổng mở.
**Đ-5.7 → Đ-5.13** là realtime và hạ tầng. **Đ-5.14 → Đ-5.18** là hợp đồng, quyền, frontend.

### Đ-5.1 Messaging là module thứ năm: schema `messaging`, một `DbContext`, không FK sang schema khác

Đúng khuôn Đ-2.1/Đ-2.2 và SocialGraph (Đ-4.1): `messaging.conversations`, `messaging.messages`; `MessagingDbContext` +
`MessagingDbContextOptions` + `DesignTimeMessagingDbContextFactory`; bảng lịch sử migration trong schema `messaging`.

`user_a_id`, `user_b_id`, `sender_id` là `uuid` **trần** — không `REFERENCES identity.users` (PTTK ghi "FK users"; lệch có
chủ đích, cùng lý do Đ-2.2). Trong **cùng** schema thì FK giữ nguyên: `messages.conversation_id → conversations.id`.

Riêng `conversations.last_message_id` **không** có FK tới `messages`, dù cùng schema: hai bảng trỏ vòng vào nhau nên
FK thường làm câu `UPDATE` ở Đ-5.4 đỏ (tin chưa được INSERT lúc cập nhật con trỏ); FK `DEFERRABLE` thì EF Core không
khai báo được, phải viết SQL tay trong migration. Con trỏ này được ghi **trong cùng transaction** với tin nên không lệch
được — ghi rõ bằng comment trong configuration.

Module Messaging **không** import SocialGraph, Profile hay Content (`ModuleBoundaryTests`). Mọi thứ nó cần từ module khác
đi qua contract ở SharedKernel: `IFriendshipReader` (BR-09), `IUserDirectory` (tên + avatar người kia), `IObjectStorage`
(ký URL avatar).

### Đ-5.2 Một hội thoại mỗi cặp: `user_a_id < user_b_id` theo **thứ tự uuid của Postgres**

`UNIQUE (user_a_id, user_b_id)` + `CHECK (user_a_id < user_b_id)` (PTTK ENT-06). Hội thoại tạo bằng `INSERT … ON CONFLICT
DO NOTHING` rồi `SELECT` — hai người cùng bấm "Nhắn tin" cho nhau cùng lúc vẫn ra **một** hội thoại, không 409, không 500.

**Bẫy đã có tiền lệ ở GĐ4:** `Guid.CompareTo` của .NET và toán tử `<` trên `uuid` của Postgres **không cùng thứ tự**
(Postgres so từng byte theo thứ tự hiển thị; .NET so theo bố cục nội bộ). Chuẩn hóa cặp bằng `CompareTo` là để lọt những
cặp mà CHECK của DB từ chối → 500 ngẫu nhiên, chỉ với một số cặp người dùng. SocialGraph đã giải bằng `FriendPair` "theo
thứ tự uuid Postgres" (commit `7e6e0d5`). Messaging **không import được** kiểu đó → chép quy tắc thành
`ConversationPair.Of(a, b)` trong `Messaging.Domain`, kèm unit test so với danh sách cặp mà test tích hợp đã kiểm bằng
Postgres thật. *(Nếu cổng mở muốn gom về một chỗ: chuyển hàm so sánh sang `SharedKernel/Ids/` — nhưng đó là sửa code của
A, phải báo A trước.)*

**Không** thêm bảng `ConversationMember` như bản ROADMAP cũ gợi ý: PTTK đã chốt hai cột, và chat nhóm nằm ngoài MVP.

### Đ-5.3 Tạo hội thoại cần là bạn; **đọc** hội thoại cũ thì không

BR-09: *"Chỉ bạn bè mới tạo hội thoại và nhắn tin; hủy kết bạn → hội thoại chỉ đọc."* Tách thành ba câu:

| Thao tác | Cần là thành viên (BR-06) | Cần đang là bạn (BR-09) |
|---|---|---|
| Tạo / lấy hội thoại với một người (`POST /conversations`) | — | **Có** |
| Đọc hội thoại, lịch sử, gửi mốc đã xem | **Có** | Không — hủy kết bạn vẫn đọc lại được lịch sử |
| Gửi tin (REST và hub) | **Có** | **Có** — kiểm **lúc gửi**, mỗi lần |

BR-09 kiểm **tại thời điểm gửi**, bằng `IFriendshipReader.AreFriendsAsync` — đọc thẳng DB, không cache (Đ-4.3). Không
có bản sao "được phép nhắn" nào đóng băng trong bảng `conversations`, nên hủy kết bạn có hiệu lực ngay ở tin kế tiếp,
**không cần** event từ SocialGraph sang Messaging (mà hạ tầng event cũng chưa tồn tại). Đây là lý do GĐ5 không phải chờ
bên ghi của SocialGraph.

`GET /conversations/{id}` trả thêm `canSend` (tính sống) để FE hiện thanh "Hai bạn không còn là bạn bè — hội thoại chỉ đọc"
**trước khi** người dùng gõ. Danh sách hội thoại **không** có `canSend`: `IFriendshipReader` chỉ có bản đơn, gọi nó cho 20
dòng là N+1 đúng ở màn mở nhiều nhất (luật "batch trước" của Đ-2.3). FE dùng `canSend` của màn chi tiết là đủ.

### Đ-5.4 Gửi tin = **một** transaction: khóa dòng hội thoại → kiểm trùng → cấp `seq` → INSERT

PTTK: *"INSERT message (transaction: message + seq + last_message)"*. Thứ tự cụ thể, không được đảo:

```
BEGIN
 1. SELECT … FROM messaging.conversations WHERE id = @cid FOR UPDATE      -- khóa dòng: tuần tự hóa mọi lượt gửi của hội thoại này
 2. tầng 3: người gọi là user_a hoặc user_b? (không → 403, rollback)
 3. SELECT … FROM messaging.messages WHERE conversation_id = @cid AND client_msg_id = @cmid
      có rồi → trả CHÍNH tin đó (Đ-5.5), rollback, không cấp seq mới
 4. UPDATE messaging.conversations
       SET seq_counter = seq_counter + 1, last_message_id = @mid, last_message_at = @now
     WHERE id = @cid RETURNING seq_counter                               -- @mid là UUID v7 app sinh sẵn
 5. INSERT messaging.messages (id=@mid, conversation_id, sender_id, seq = <giá trị vừa RETURNING>, content, client_msg_id, created_at)
COMMIT
 6. SAU COMMIT: ACK cho người gửi · đẩy MessageReceived · phát event MessageSent (Đ-5.15)
```

- **Kiểm BR-09 (`AreFriendsAsync`) đứng TRƯỚC `BEGIN`**, không nằm trong transaction: nó đọc DbContext của module khác, và
  giữ khóa dòng trong lúc chờ một truy vấn không liên quan là kéo dài hàng đợi của cả hội thoại.
- **Khóa dòng là cố ý:** chat 1-1 có tối đa hai người gửi; tuần tự hóa theo hội thoại rẻ hơn mọi cơ chế lạc quan, và nó
  làm bước 3 hết race (hai lần gửi lại cùng `clientMsgId` không thể cùng qua bước 3).
- **Lưới cuối vẫn là DB:** `UQ (conversation_id, seq)` và `UQ (conversation_id, client_msg_id)`. Nếu một ngày ai đó bỏ
  `FOR UPDATE`, lỗi `23505` phải được dịch thành "đọc lại tin theo `clientMsgId` và trả nó" — không để rơi thành 500.
- **Push sau `COMMIT`**, không trong transaction: push trước commit thì người nhận có thể thấy tin mà một giây sau bị
  rollback — tin "ma" không bao giờ có trong lịch sử.

Khuôn khóa dòng → câu SQL nguyên tử → không chạm `updated_at` là di sản GĐ3 để lại cho GĐ5 (`giai-doan-3.md`, "GĐ3 để lại
gì"). Nếu GĐ3 chưa xong lúc B bắt đầu thì dùng đoạn trên — hai khuôn cùng hình dạng.

### Đ-5.5 Gửi lại cùng `clientMsgId`: trả **lại đúng tin cũ** như thành công — lệch PTTK "409", có chủ đích

PTTK API-Message-Send ghi *"409 trùng clientMsgId"*. Nhưng tình huống thật sinh ra lần gửi trùng là: **lần đầu đã thành
công, ACK bị mất trên đường về** (mất mạng đúng lúc). Trả 409 thì client phải hiểu "lỗi xung đột" nghĩa là "thật ra đã
thành công" — tức là 409 thành một mã thành công trá hình, và mọi client phải viết một nhánh đặc biệt cho nó.

Chốt, theo cách idempotency key vẫn làm:

| Tình huống | REST | Hub |
|---|---|---|
| `clientMsgId` mới | **201** + `MessageResponse` | ACK `{ message, replayed: false }` |
| `clientMsgId` đã có, **cùng** nội dung | **200** + **đúng** `MessageResponse` cũ (cùng `messageId`, cùng `seq`) | ACK `{ message, replayed: true }` |
| `clientMsgId` đã có, **khác** nội dung | **409** `messaging.client-msg-id-reused` | lỗi `conflict` |

Dòng thứ ba giữ lại mã 409 của PTTK cho đúng trường hợp nó có nghĩa: client dùng lại một id cho một tin khác — đó là bug
của client, phải lộ ra. Ghi vào Mục 13.

Lần gửi lại **không** đẩy `MessageReceived` lần hai (bước 3 của Đ-5.4 rollback trước bước 6). FE vẫn phải khử trùng theo
`messageId` (Đ-5.17), vì người nhận có thể đã nhận tin qua hub **và** qua lần nạp REST.

### Đ-5.6 Trạng thái Sent/Delivered/Seen là **hai mốc mỗi thành viên**, không phải cột `status` trên từng tin — lệch PTTK

PTTK đặt `messages.status CK (sent, delivered, seen)`. Hệ quả: người nhận mở một hội thoại có 300 tin chưa đọc là **300
câu UPDATE** (hoặc một UPDATE chạm 300 dòng), lặp lại mỗi lần mở; và "số tin chưa đọc" (UC-15 A1, AC-02) lại cần thêm
một cột bộ đếm mà PTTK **không** có trong ENT-06/07.

Nhưng trong chat 1-1, biên nhận luôn **tích lũy**: đã xem tin 57 nghĩa là đã xem mọi tin trước đó. Nên thay bằng bốn cột
trên `conversations`:

```
user_a_delivered_seq, user_a_seen_seq, user_b_delivered_seq, user_b_seen_seq   bigint NOT NULL DEFAULT 0
```

| Câu hỏi | Tính thế nào |
|---|---|
| Tin `seq = n` do A gửi đang ở trạng thái nào? | `seen` nếu `n ≤ b_seen_seq` · `delivered` nếu `n ≤ b_delivered_seq` · còn lại `sent` |
| B có bao nhiêu tin chưa đọc trong hội thoại này? | số tin do **A** gửi có `seq > b_seen_seq` — xấp xỉ rẻ: `seq_counter − b_seen_seq` (Đ-5.14 chốt cách đếm) |
| B đọc tới đâu? | `UPDATE … SET user_b_seen_seq = GREATEST(user_b_seen_seq, @n), user_b_delivered_seq = GREATEST(user_b_delivered_seq, @n)` |

- **Một câu UPDATE cho mỗi lần xem**, không phụ thuộc số tin. `GREATEST` làm mốc **chỉ tăng** — biên nhận đến trễ, đến
  sai thứ tự, hay gửi lại đều vô hại.
- **Đã xem kéo theo đã nhận** (cùng câu UPDATE).
- `@n` bị kẹp ở `seq_counter` hiện tại: client không đánh dấu "đã xem" một tin chưa tồn tại.
- Trạng thái **Failed** của máy trạng thái PTTK là trạng thái **chỉ có ở client** (tin chưa từng tới DB) — đúng như CK của
  PTTK vốn chỉ cho ba giá trị. Không lưu.

Hai lệch (bỏ cột `status`, thêm bốn cột mốc) ghi vào Mục 13, kèm lý do: đúng máy trạng thái PTTK, rẻ hơn ở mọi đường đọc
lẫn ghi, và lấp luôn chỗ hở "bộ đếm chưa đọc" của ENT-06/07.

### Đ-5.7 Một service gửi tin, hai cửa vào (hub và REST) — cửa nào cũng qua đủ ba tầng

`MessageSendService.SendAsync(actorId, conversationId, content, clientMsgId)` là **chỗ duy nhất** chứa Đ-5.4/Đ-5.5.
`ChatHub.SendMessage` và `POST /conversations/{id}/messages` đều là vỏ mỏng gọi nó rồi dịch `Result` sang ngôn ngữ của
cửa đó (HubException / RFC 7807).

Tầng 2 phải có ở **cả hai cửa**: REST dùng `[RequirePermission("message.send")]`; hub không có attribute tương đương cho
từng phương thức theo cách REST dùng, nên kiểm bằng `IPermissionCache` trong service (cùng cách Đ-2.6 kiểm `post.create`
khi `purpose=post`). Admin short-circuit **chỉ** ở tầng 2, không ở tầng 3 — luật kế thừa GĐ1 Mục 3.2: Admin **không** đọc
được hội thoại của người khác.

`actorId` ở hub lấy từ `Context.UserIdentifier` (tức claim `sub` của principal dựng từ vé), **không bao giờ** từ tham số
của phương thức hub. Cùng luật "actorId từ token" của GĐ2 Mục 6.2, và cũng không test tự động nào phân biệt được nguồn.

### Đ-5.8 Đẩy tin bằng `Clients.User(userId)`, không bằng group theo hội thoại

Hub gửi `MessageReceived` tới `Clients.Users(recipientId, senderId)` — mọi kết nối (mọi tab, mọi thiết bị) của cả hai
người; tab gửi tự bỏ qua bản sao của chính nó nhờ khử trùng theo `messageId`.

Không dùng group `conversation:{id}` (bản ROADMAP cũ): group bắt client `Join` từng hội thoại, và **kiểm quyền lúc join
là một cửa tầng 3 thứ ba** phải canh; quên kiểm là người ngoài nghe lén được. `Clients.User` không có cửa đó — server tự
quyết người nhận từ dữ liệu trong DB. Nhận tin cho hội thoại đang không mở (để cập nhật danh sách và badge) cũng tự có.

`IUserIdProvider` mặc định của SignalR đọc `ClaimTypes.NameIdentifier`; dự án tắt `MapInboundClaims` và dùng claim `sub`
→ phải đăng ký `IUserIdProvider` riêng đọc `sub`. Quên dòng này thì `Clients.User` **im lặng không gửi cho ai**.

### Đ-5.9 Vé realtime theo Đ-E16: SharedKernel cấp và kiểm, Messaging chỉ là người tiêu thụ đầu tiên

Thi công đúng luồng Đ-E16 (đã chốt từ GĐ1, không mở lại):

```
POST /api/v1/realtime/tickets     [Authorize] (bearer của phiên, qua proxy BFF chung — không route BFF mới)
  → 32 byte ngẫu nhiên (RandomNumberGenerator), base64url
  → Redis: SET rt:ticket:{sha256(vé)} = {sub, role, iat của access token đang dùng}  EX 30
  ← 201 { ticket, expiresIn: 30 }

wss://<domain>/hubs/chat?access_token=<vé>
  → scheme "RealtimeTicket": CHỈ đọc query access_token khi Request.Path bắt đầu bằng /hubs
  → GETDEL rt:ticket:{sha256(vé)}      (dùng một lần — lần thứ hai không còn gì để lấy)
  → ITokenRevocationStore.IsRevokedAsync(sub, iat)
  → dựng principal { sub, role }  |  vé sai / hết hạn / đã dùng / bị thu hồi → 401
```

- **Đặt ở đâu:** `SharedKernel/Realtime/` — `IRealtimeTicketStore`, `RedisRealtimeTicketStore`,
  `RealtimeTicketAuthenticationHandler`, hằng tên scheme. Lý do: GĐ6 dựng hub thông báo dùng **đúng** vé này; để trong
  Messaging thì GĐ6 phải import Messaging (bị ArchUnitNET chặn). Controller `POST /realtime/tickets` nằm ở Messaging
  (nhóm `messaging-v1`) vì Đ-E16 yêu cầu endpoint vé nằm trong một file hợp đồng — ghi chú trong yaml rằng vé dùng chung
  cho mọi `/hubs/*`.
- **Scheme vé chỉ nhận ở `/hubs/*`; bearer không bao giờ nhận ở `/hubs/*`.** Hub khai
  `[Authorize(AuthenticationSchemes = RealtimeTicketDefaults.Scheme)]`. Hai test canh hai chiều: vé làm bearer ở REST →
  401 (HUB-06); access token thật đặt vào `?access_token=` của hub → 401 (HUB-05). Chiều thứ hai quan trọng: nếu hub nhận
  JWT qua query thì JWT 15 phút nằm trong log truy cập — đúng thứ Đ-E16 sinh ra để tránh.
- **Redis chết → không cấp được vé → 503**, không fail-open: không có chỗ lưu thì không có vé nào để kiểm. Client nhận 503
  thì chuyển fallback REST (Đ-5.12) — chat vẫn chạy, chỉ chậm hơn.
- **Rate limit riêng cho endpoint vé** (policy `realtime-ticket`, 20/phút/user): mạng chập chờn làm client tự kết nối lại
  liên tục; không giới hạn là một vòng lặp đốt Redis. Thêm policy vào `SharedKernelExtensions` cạnh policy `auth`.

### Đ-5.10 Kết nối đang mở không được sống lâu hơn quyền của người mở nó

Đ-E16 bắt buộc: *"kết nối đang mở phải bị cắt khi người dùng đăng xuất hoặc bị `revoked:user` — vé chỉ kiểm lúc bắt tay."*
Ba cơ chế, rẻ tới đắt:

1. **Kiểm `revoked:user` ở mỗi lời gọi phương thức hub** — một `IHubFilter` (`RevocationHubFilter`) gọi
   `IsRevokedAsync(sub, iat)` trước `SendMessage`/`SendReceipt`; bị thu hồi → `Context.Abort()`. Một lần `GET` Redis, cùng
   giá với bên đọc của REST. Fail-open khi Redis chết, **cùng** chính sách GĐ1 (có log cảnh báo).
2. **Tuổi thọ tối đa của kết nối = TTL access token (15 phút).** Một timer theo kết nối gọi `Abort()` khi hết hạn; client
   tự kết nối lại → xin vé mới → BFF phải còn phiên hợp lệ mới xin được. Nhờ vậy người đã đăng xuất ở thiết bị khác, hoặc
   bị khóa mà không gọi phương thức nào, mất kết nối trong **tối đa 15 phút** — cùng cửa sổ mà REST đang chấp nhận.
3. **Đăng xuất ở chính trình duyệt đó:** FE dừng kết nối ngay khi nhận tín hiệu đăng xuất (`BroadcastChannel
   'socialapp:auth'` đã có từ GĐ1) — không đợi server.

Bên **ghi** `revoked:user` cho khóa tài khoản / đổi vai trò là của GĐ6. GĐ5 chỉ bảo đảm khi GĐ6 ghi thì kết nối chết ở
lời gọi kế tiếp — có test HUB-04b.

### Đ-5.11 Presence tối thiểu, TTL tự dọn — không tin vào `OnDisconnectedAsync`

`OnDisconnectedAsync` **không** chạy khi process chết (deploy, OOM, `docker kill` ở GĐ7). Presence chỉ dựa vào nó thì
người dùng "online vĩnh viễn" sau mỗi lần deploy.

Chốt: `rt:presence:{userId}` là sorted set, member = `connectionId`, score = thời điểm hết hạn (now + 90s).
`OnConnectedAsync` thêm, `OnDisconnectedAsync` xóa, và một `BackgroundService` mỗi instance gia hạn score của **các kết nối
nó đang giữ** mỗi 30s. Online ⇔ có member với score > now. Kết nối của instance đã chết tự hết hạn sau ≤ 90s.

Expose `IPresenceReader.IsOnlineAsync(Guid userId)` ở SharedKernel — **người tiêu thụ là GĐ6** (gửi thông báo khi người
nhận offline). GĐ5 không dùng presence để quyết định có đẩy tin hay không: `Clients.User` đẩy tới ai đang kết nối, không
ai thì thôi — tin đã lưu bền, lần mở app kế tiếp sẽ nạp. Vì vậy presence là **phần cắt được** của GĐ5 nếu trễ (B.9),
và khi cắt thì ghi "hoãn tới GĐ6" chứ không xóa.

### Đ-5.12 Fallback (ISS-01, UC-15 A2): gửi bằng REST, nhận bằng hỏi lại 3 giây — cùng hợp đồng dữ liệu

Khi hub không kết nối được (vé 503, WebSocket bị chặn, tự kết nối lại thất bại quá 3 lần liền):

- **Gửi:** `POST /conversations/{id}/messages` với **cùng** `clientMsgId` — cùng service (Đ-5.7) nên cùng idempotency.
  Một tin đang gửi dở qua hub thì chuyển sang REST mà không sinh bản thứ hai.
- **Nhận:** hội thoại đang mở hỏi `GET /conversations/{id}/messages?afterSeq={seq cuối đã có}` mỗi 3 giây; danh sách hội
  thoại và badge làm mới mỗi 30 giây. Hub nối lại được thì dừng hỏi.
- UI hiện một dải nhỏ "Đang kết nối lại — tin vẫn gửi được" — không chặn gõ.

PTTK có hai câu hơi khác nhau: ISS-01 nói "polling 3s", UC-15 A2 nói "fallback REST POST". Đây là **cả hai**, cho hai chiều.
Không có WebSocket thì p95 ~1,5s (trung bình nửa chu kỳ hỏi) — không đạt GOAL-02, nhưng đó là chế độ suy giảm, không phải
chế độ đo.

**Không** bật transport Long Polling / SSE của SignalR làm fallback: cả hai cần bước negotiate, mà negotiate làm vỡ vé
dùng một lần (Đ-5.16).

### Đ-5.13 Backplane Redis bật theo cấu hình, mặc định **tắt** — bật khi có bản sao thứ hai

ADR-003: *"phụ thuộc Redis backplane khi scale"*. Hiện staging chạy **một** container `api`; GĐ7 khối E mới lên hai bản
sao (và khối đó cắt được — Đ-7.1).

Chốt: `Realtime:Backplane:Enabled` (mặc định `false`). Bật thì `AddSignalR().AddStackExchangeRedis(...)` với **kết nối
riêng** — cấu hình Redis dùng chung của SharedKernel có `SyncTimeout = AsyncTimeout = 250ms` (chính comment trong
`RedisExtensions.cs` đã dặn phải nghĩ lại cho backplane); backplane cần timeout dài hơn và kênh có tiền tố
(`ChannelPrefix = "socialapp-{env}"`) để staging và production chung một Redis không nghe lẫn nhau.

Lý do không bật sẵn: với backplane, **mọi** lần đẩy (kể cả tới kết nối cùng instance) đi qua Redis pub/sub — Redis chết là
chat realtime chết hẳn. Một instance thì backplane chỉ thêm một điểm hỏng mà không đổi lại gì.

Nhưng **phải chứng minh nó chạy trước khi GĐ7 cần**: C4 dựng hai instance ở local (compose `--scale api=2` + edge tạm)
và chạy E2E A ở instance 1, B ở instance 2. Không có bước này thì ngày GĐ7 bật khối E là ngày chat âm thầm hỏng một nửa.

### Đ-5.14 Chưa đọc = tin **của người kia** sau mốc đã xem; tổng badge tính bằng một câu SQL

`seq_counter − my_seen_seq` đếm cả tin **mình** gửi sau lần cuối mình xem → tự nhắn cho người kia 3 tin là tự thấy badge 3.
Sai theo cách người dùng nhận ra ngay.

Chốt: gửi tin **tự đẩy mốc đã xem của chính người gửi** lên `seq` vừa cấp (cùng câu UPDATE ở bước 4 của Đ-5.4). Khi đó
`seq_counter − my_seen_seq` đúng bằng số tin của người kia chưa xem, và đếm vẫn O(1) mỗi hội thoại, không `COUNT(*)` trên
`messages`.

Tổng badge: `GET /conversations/unread-count` → `SUM(GREATEST(seq_counter − my_seen_seq, 0))` trên các hội thoại có mình,
chạy trên hai index `(user_a_id, last_message_at DESC)` và `(user_b_id, last_message_at DESC)`. Không cache: một câu SQL
trên vài chục dòng rẻ hơn một vòng xóa cache đúng lúc; và đây là trường **theo người xem** — luật GĐ3/GĐ4 "không vào cache
chung" áp nguyên.

### Đ-5.15 Event trong tiến trình sau `COMMIT`, chỉ log — GĐ6 nối thông báo vào đúng chỗ này

Theo Đ-4.15 / Đ-3.12: `MessageSent { conversationId, messageId, senderId, recipientId, seq }` phát **sau** `COMMIT`,
GĐ5 chỉ ghi log (không ghi nội dung tin — Đ-5.18). Hiện **chưa có** hạ tầng event nào trong repo (không MediatR, không
dispatcher) → không dựng khung event chung ở GĐ5: một interface `IMessagingEvents` trong `Messaging.Application` với hiện
thực ghi log là đủ, GĐ6 thay hiện thực. Dựng khung chung là quyết định cấp dự án, không phải của một giai đoạn.

### Đ-5.16 Client SignalR bắt buộc `skipNegotiation` + chỉ WebSockets — vì vé dùng một lần

Bẫy chắc chắn gặp nếu không biết trước: với cấu hình mặc định, SignalR JS client **gọi `accessTokenFactory` hai lần** —
một lần cho request `POST /hubs/chat/negotiate`, một lần nữa cho kết nối WebSocket. Vé đã bị `GETDEL` ở lần negotiate →
lần thứ hai 401 → **mọi** kết nối thất bại, và triệu chứng trông như "vé sai".

Chốt: client dùng `{ transport: HttpTransportType.WebSockets, skipNegotiation: true, accessTokenFactory }` — không có
negotiate, `accessTokenFactory` chạy **đúng một lần** mỗi lần kết nối (và chạy lại mỗi lần tự kết nối lại → xin vé mới,
đúng bước 5 của Đ-E16). ADR-003 cũng ghi sẵn *"cần sticky session/skip-negotiation"*: bỏ negotiate còn gỡ luôn nhu cầu
sticky session khi GĐ7 có hai bản sao.

**Kiểm chứng ở C0 (spike), không tin tài liệu này:** chạy một kết nối thật, đếm số lần `POST /realtime/tickets` bằng tab
Network. Phải là **1** mỗi lần kết nối. Nếu phiên bản client đang ghim cư xử khác, sửa quyết định này trước khi code tiếp.

Test tích hợp .NET dùng `Microsoft.AspNetCore.SignalR.Client` với **cùng** cấu hình (`SkipNegotiation = true`,
`Transports = WebSockets`, `WebSocketFactory` trỏ `TestServer.CreateWebSocketClient()`) — để test đi đúng đường client thật đi.

### Đ-5.17 Frontend: `features/chat/` + `lib/realtime/`; một kết nối cho cả app; optimistic có khử trùng

- **Đặt file** (luật frontend Mục 2, bảng Đ-E13 đã định sẵn cho GĐ5): feature `features/chat/`, client
  `lib/api/messaging-api.ts` (kiểu từ `lib/api/messaging/schema.d.ts` sinh tự động), route
  `app/(app)/(with-profile)/messages/` và `messages/[conversationId]/`.
- **Kết nối hub ở `lib/realtime/`** (không ở `features/`): badge ở header và màn chat cùng cần nó, mà `features/` không
  được import chéo. `lib/realtime/chat-connection.ts` là **một** kết nối cho cả app, trạng thái
  (`connecting | connected | reconnecting | fallback | stopped`) phát ra bằng module store + `useSyncExternalStore` — đúng
  khuôn `lib/auth/token-store.ts` (luật frontend Mục 5). **Chỉ `lib/realtime/**` được import `@microsoft/signalr`** — thêm
  luật `no-restricted-imports` vào ESLint, cùng tinh thần luật cấm `fetch` ngoài `http.ts`.
- **Kiểu của hub không sinh được từ OpenAPI** → viết tay ở `lib/realtime/chat-hub-contract.ts`, và **ví dụ JSON của hợp
  đồng hub là nguồn chung** cho test backend lẫn fixture frontend (Mục 8.3). Đổi hợp đồng hub mà không đổi ví dụ → cả hai
  phía đỏ.
- **Gửi là optimistic:** tin hiện ngay với trạng thái "đang gửi" + `clientMsgId = crypto.randomUUID()` → ACK thì thành
  Sent và mang `seq` thật → biên nhận thì Delivered/Seen. Lỗi mạng → **Thất bại** + nút "Thử lại" gửi lại **cùng**
  `clientMsgId`. Lỗi 403 (`not-friends`) → không cho thử lại, hiện thanh "chỉ đọc".
- **Danh sách tin là tập khử trùng theo `messageId`, sắp theo `seq`** — không nối mảng. Ba nguồn đổ vào cùng một tập: ACK,
  `MessageReceived`, lần nạp REST. Nhận `seq` nhảy cóc (có `seq` 12 trong khi mới có tới 10) → gọi `afterSeq=10` lấp chỗ
  hở; **không** tin rằng hub không bao giờ rơi tin.
- **Tự kết nối lại → lấp khoảng hở:** `onreconnected` nạp lại `afterSeq` cho hội thoại đang mở + làm mới danh sách và
  badge. Tắt mạng 10 giây rồi bật lại **không mất tin nào** (bản ROADMAP cũ gọi đây là đầu ra bắt buộc).
- Nút "Nhắn tin" trên hồ sơ bạn bè: `features/chat/` xuất `StartChatButton`; `app/users/[userId]/page.tsx` ráp nó vào slot
  `actions` của `PublicProfile` **cạnh** `RelationshipButtons` của GĐ4 — chỉ `app/` biết cả hai (Đ-4.16). Đây là **chỗ đụng
  file với A** (Mục 9.4).
- Kết nối hub là tài nguyên có vòng đời → hook sở hữu nó cần **đúng một** ca `<StrictMode>` (luật frontend Mục 9).

### Đ-5.18 Nội dung tin là dữ liệu người dùng: không log, không nằm trong URL, không nằm trong metric

- Không log `content` ở bất kỳ mức log nào — kể cả log lỗi khi INSERT hỏng. Log `messageId`, `conversationId`, `seq`,
  độ dài.
- `/hubs/*` mang vé trên query string → **không log query string của `/hubs/*`** ở Serilog lẫn apache (ràng buộc của
  Đ-E16). Kiểm cả hai: Serilog request logging mặc định ghi `RequestPath` không kèm query, nhưng log của middleware khác
  (và của SignalR ở mức Debug) có thể ghi URL đầy đủ.
- Grep chặn log PII của GĐ7 (Đ-7.14) thêm `content` của Messaging vào danh sách trường bị che — báo chủ dự án khi họ làm C6
  của GĐ7.

---

## 4. Mô hình dữ liệu

DDL dưới đây là **đích đến**; hiện thực qua EF Core migration, không viết SQL tay vào repo (trừ chỗ EF không biểu diễn
được, ghi rõ bằng comment). Quy ước thời gian, UUID v7 và `updated_at` giữ **nguyên xi** GĐ1/GĐ2.

```sql
CREATE SCHEMA IF NOT EXISTS messaging;

-- ENT-06 · conversations
CREATE TABLE messaging.conversations (
    id                   uuid        PRIMARY KEY,                 -- UUID v7
    user_a_id            uuid        NOT NULL,                    -- = identity.users.user_id · KHÔNG FK (Đ-5.1)
    user_b_id            uuid        NOT NULL,                    -- = identity.users.user_id · KHÔNG FK
    seq_counter          bigint      NOT NULL DEFAULT 0,          -- seq đã cấp gần nhất (Đ-5.4)
    last_message_id      uuid,                                    -- con trỏ, KHÔNG FK (Đ-5.1) — ghi cùng transaction với tin
    last_message_at      timestamptz,                             -- khóa sắp danh sách; NULL = chưa có tin
    user_a_delivered_seq bigint      NOT NULL DEFAULT 0,          -- mốc biên nhận (Đ-5.6) — thay cột messages.status
    user_a_seen_seq      bigint      NOT NULL DEFAULT 0,
    user_b_delivered_seq bigint      NOT NULL DEFAULT 0,
    user_b_seen_seq      bigint      NOT NULL DEFAULT 0,
    created_at           timestamptz NOT NULL DEFAULT now(),
    updated_at           timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT uq_conversations_pair   UNIQUE (user_a_id, user_b_id),
    CONSTRAINT ck_conversations_order  CHECK (user_a_id < user_b_id),                -- thứ tự uuid của Postgres (Đ-5.2)
    CONSTRAINT ck_conversations_marks  CHECK (
        user_a_seen_seq <= user_a_delivered_seq AND user_a_delivered_seq <= seq_counter AND
        user_b_seen_seq <= user_b_delivered_seq AND user_b_delivered_seq <= seq_counter)
);
-- Danh sách hội thoại của một người: hai phía, keyset theo (last_message_at DESC, id DESC)
CREATE INDEX idx_conversations_a_recent ON messaging.conversations (user_a_id, last_message_at DESC, id DESC)
    WHERE last_message_at IS NOT NULL;
CREATE INDEX idx_conversations_b_recent ON messaging.conversations (user_b_id, last_message_at DESC, id DESC)
    WHERE last_message_at IS NOT NULL;

-- ENT-07 · messages
CREATE TABLE messaging.messages (
    id              uuid          PRIMARY KEY,                     -- UUID v7, app sinh TRƯỚC transaction (Đ-5.4)
    conversation_id uuid          NOT NULL REFERENCES messaging.conversations(id),
    sender_id       uuid          NOT NULL,                        -- KHÔNG FK (Đ-5.1)
    seq             bigint        NOT NULL,
    content         varchar(2000) NOT NULL,
    client_msg_id   uuid          NOT NULL,
    created_at      timestamptz   NOT NULL DEFAULT now(),
    CONSTRAINT uq_messages_conv_seq       UNIQUE (conversation_id, seq),            -- cũng là index của lịch sử (seq DESC đọc ngược được)
    CONSTRAINT uq_messages_conv_client_id UNIQUE (conversation_id, client_msg_id),  -- idempotency (AC-03)
    CONSTRAINT ck_messages_seq_positive   CHECK (seq > 0),
    CONSTRAINT ck_messages_content        CHECK (char_length(content) BETWEEN 1 AND 2000 AND btrim(content) <> '')
);
```

**Bốn chỗ dễ sai trong migration này:**

1. **Không có `updated_at` trên `messages`** — tin không sửa được trong MVP. Override `SaveChanges` đóng dấu `updated_at`
   phải bỏ qua entity này, nếu không EF đòi cột không tồn tại.
2. **`ck_conversations_marks`** là lưới cho Đ-5.6: một câu UPDATE mốc quên kẹp `@n` ở `seq_counter` sẽ nổ ở DB thay vì
   ghi một mốc "đã xem tin tương lai". Service phải kẹp **trước** (để trả 400 có nghĩa), CHECK chỉ là lưới.
3. **`uq_messages_conv_seq` đủ làm index lịch sử** — PTTK Mục 5.6 ghi `idx(conversation_id, seq DESC)`, nhưng B-tree đọc
   ngược được nên không tạo thêm index thứ hai trùng cột. Kiểm bằng `EXPLAIN` ở B3 (phải thấy `Index Scan Backward`).
4. **Thứ tự `user_a_id < user_b_id`** phải khớp Postgres (Đ-5.2) — test tích hợp `PAIR-01` chèn 200 cặp ngẫu nhiên qua
   service và không cặp nào được đỏ `ck_conversations_order`.

**Lệch PTTK ở mô hình (ghi vào Mục 13):** bỏ `messages.status`, thêm bốn cột mốc (Đ-5.6); thêm `created_at/updated_at`
cho `conversations` (quy ước chung của dự án); không FK tới `users` (Đ-2.2); `last_message_id` không FK (Đ-5.1).

## 5. Dữ liệu nền

**GĐ5 không seed gì.** Mã quyền `message.send` (mã thứ 11 trong 17) đã có từ GĐ1 và đã gán cho `USER`, `MODERATOR`.

| Việc | Vì sao vẫn phải làm |
|---|---|
| `MigrateMessagingModuleAsync` nối vào hook `--migrate`, **sau** SocialGraph | Không nối thì staging chạy code mới trên schema cũ. Thứ tự cố định để log deploy đọc được (comment trong `Program.cs` đã ghi "thứ tự CỐ ĐỊNH") |
| `MessagingPermissions` (hằng `message.send` cục bộ) + `MessagingPermissionsTests` | Module không import `Identity.Domain.PermissionCodes`; test ở ArchitectureTests khẳng định hằng cục bộ nằm trong `PermissionCodes.All` — chép khuôn `ContentPermissionsTests` |
| `PermissionCodeUsageTests` (có từ GĐ1) | Gõ sai `"message.sned"` bị bắt ở CI, không phải ở runtime 403 |

---

## 6. Ba tầng kiểm soát truy cập áp vào GĐ5

### 6.1 Bảng đầy đủ: cửa vào × tầng 2 × tầng 3 × mã lỗi

| Cửa vào | Tầng 2 | Tầng 3 (service) | Không đạt |
|---|---|---|---|
| `POST /realtime/tickets` | `[Authorize]` | — | 401 · **503** Redis chết |
| `POST /conversations` | `message.send` | người kia có hồ sơ · khác mình · **đang là bạn** (BR-09) | **404** không có người · **400** chính mình · **403** không phải bạn |
| `GET /conversations` | `[Authorize]` | — (luôn là của người gọi) | — |
| `GET /conversations/unread-count` | `[Authorize]` | — | — |
| `GET /conversations/{id}` | `[Authorize]` | là thành viên (BR-06) | **403** |
| `GET /conversations/{id}/messages` | `[Authorize]` | là thành viên | **403** |
| `POST /conversations/{id}/messages` | `message.send` | là thành viên · đang là bạn | **403** `auth.forbidden` (không phải thành viên) · **403** `messaging.not-friends` (thành viên, hết là bạn) |
| `POST /conversations/{id}/receipts` | `[Authorize]` | là thành viên | **403** |
| Bắt tay `/hubs/chat` | scheme `RealtimeTicket` | vé hợp lệ · chưa dùng · chưa bị thu hồi | **401** |
| Hub `SendMessage` | `message.send` (kiểm trong service, Đ-5.7) | là thành viên · đang là bạn | HubException `forbidden` / `not-friends` |
| Hub `SendReceipt` | — | là thành viên | HubException `forbidden` |

**403, không 404, cho hội thoại của người khác — theo PTTK.** TC-A04 ghi đích danh *"Đọc /conversations/{id} khi không phải
thành viên | 403 (IDOR)"*. "Không tồn tại" và "không phải thành viên" trả **cùng** một phản hồi 403 `auth.forbidden` — mã
trạng thái không được tiết lộ id hội thoại có thật hay không (quy ước 3b GĐ1). Hội thoại là tài nguyên **thuộc sở hữu**
của hai người (giống thao tác ghi cần sở hữu), không phải nội dung có mức hiển thị như bài viết — nên không áp quy ước 404
của `GET /posts/{id}`.

**Hai mã 403 khác `code` khi gửi tin là cố ý:** người được phân biệt là **thành viên** của hội thoại — họ đã biết hội thoại
tồn tại và biết người kia là ai. Cho họ biết "không còn là bạn" không lộ gì, và là thứ FE cần để hiện thanh "chỉ đọc"
(AC-04). Người ngoài luôn nhận `auth.forbidden`.

### 6.2 Khuôn tầng 3 — chép nguyên hình dạng

```csharp
// Modules/Messaging/Application/Conversations/ConversationAccess.cs — một chỗ duy nhất cho BR-06
public async Task<Result<ConversationRow>> ResolveMemberAsync(Guid conversationId, Guid actorId, CancellationToken ct)
{
    var c = await _store.FindAsync(conversationId, ct);
    // Không có nhánh "if role == ADMIN": Admin short-circuit CHỈ ở tầng 2 (GĐ1 Mục 3.2) — Admin không đọc trộm tin nhắn.
    // "Không tồn tại" và "không phải thành viên" trả CÙNG một Result.
    if (c is null || (c.UserAId != actorId && c.UserBId != actorId))
        return Result.Forbidden();
    return c;
}
```

Mọi cửa (REST và hub) đi qua `ConversationAccess` — không controller hay hub method nào tự so `UserAId`.

### 6.3 Dòng AuthZ matrix mới — chỉ thêm dòng vào `AuthZMatrix.cs`

Khung đã có (GĐ1 `B2`/`B3`; `CallerUserId` từ Q-B2 của GĐ4). Thêm dòng, **không** sửa `AuthZMatrixTests`, `AuthZCase`,
`AuthZApiFactory`.

| Id | Kịch bản | Người gọi | Gọi gì | Kỳ vọng |
|---|---|---|---|---|
| `TC-A04` | C đọc hội thoại của A và B | `Caller.User` (C) | `GET /api/v1/conversations/{id A–B}` | **403** |
| `TC-A04-messages` | C đọc lịch sử hội thoại A–B | `Caller.User` (C) | `GET /api/v1/conversations/{id A–B}/messages` | **403** |
| `TC-A04-send` | C gửi tin vào hội thoại A–B | `Caller.User` (C) | `POST /api/v1/conversations/{id A–B}/messages` | **403** |
| `TC-A04-receipt` | C đánh dấu đã xem hội thoại A–B | `Caller.User` (C) | `POST /api/v1/conversations/{id A–B}/receipts` | **403** |
| `TC-A07` | A mở hội thoại với người lạ D | `Caller.User` (A) | `POST /api/v1/conversations` `{userId: D}` | **403** |
| `TC-A07-send` | A gửi tin trong hội thoại với B **sau khi** hủy kết bạn (AC-04) | `Caller.User` (A) | `POST /api/v1/conversations/{id A–B}/messages` | **403** |
| `TC-A07b` | *(đối chứng)* A gửi tin cho B **đang là bạn** | `Caller.User` (A) | cùng path | **201** |
| `TC-A01-conversations` | Danh sách hội thoại không kèm JWT | `Caller.Anonymous` | `GET /api/v1/conversations` | **401** |
| `TC-A01-ticket` | Xin vé không kèm JWT | `Caller.Anonymous` | `POST /api/v1/realtime/tickets` | **401** |

- `TC-A07b` là dòng **đối chứng** bắt buộc (nếp `RBAC-02b` GĐ1, `READ-06b` GĐ4): matrix chỉ có dòng "bị chặn" thì xanh cả
  khi `AreFriendsAsync` luôn trả `false` — tức là khi DI vẫn trỏ `AlwaysStrangers`.
- `ArrangePath` dựng hội thoại A–B và quan hệ bạn bè. **Quan hệ bạn bè:** dùng API của SocialGraph (`POST /friends/requests`
  + `accept`) nếu D2/D3 GĐ4 đã có trên nhánh; nếu chưa, `INSERT` thẳng `socialgraph.friendships` qua
  `PostgresConnectionString` **kèm comment TODO trỏ tới GĐ4 D2/D3** và đổi sang API trước cổng đóng.

### 6.4 Test hub — cửa thứ hai mà matrix HTTP không thấy

Khung matrix bắn HTTP, không bắn được lời gọi hub. Nhóm test riêng `HubAuthZTests` (category `AuthZ` để nằm trong **cùng
cổng CI** "AuthZ matrix") dùng `HubConnection` thật trên `TestServer` (Đ-5.16):

| Id | Kịch bản | Kỳ vọng |
|---|---|---|
| `HUB-01` | Kết nối không vé | bắt tay **401** |
| `HUB-02` | Dùng lại vé đã dùng | **401** |
| `HUB-03` | Vé quá 30 giây (`TimeProvider` giả) | **401** |
| `HUB-04` | Vé của người đã bị `revoked:user` sau lúc cấp vé | **401** |
| `HUB-04b` | Đang kết nối, bị `revoked:user`, rồi gọi `SendMessage` | kết nối bị cắt (`Closed`) |
| `HUB-05` | Access token JWT thật đặt vào `?access_token=` | **401** (hub không nhận bearer) |
| `HUB-06` | Vé đặt vào `Authorization: Bearer` gọi `GET /conversations` | **401** (REST không nhận vé) |
| `HUB-07` | C (có vé hợp lệ) gọi `SendMessage` vào hội thoại A–B | HubException `forbidden`, **không** có dòng mới trong `messages` |
| `HUB-08` | A gọi `SendMessage` sau khi hủy kết bạn với B | HubException `not-friends` |
| `HUB-09` | A gửi tin → **chỉ** kết nối của A và B nhận `MessageReceived`; kết nối của C không nhận gì trong 2 giây | đúng |
| `HUB-10` | Kết nối quá tuổi thọ tối đa (`TimeProvider` giả) | server cắt; client kết nối lại bằng vé mới thì được |

`HUB-09` là test IDOR chiều **nghe**: không ai gọi sai gì cả, nhưng nếu `IUserIdProvider` sai (Đ-5.8) hoặc ai đó đổi sang
`Clients.All` thì người thứ ba nhận được tin.

---

## 7. Luồng nghiệp vụ

### 7.1 Mở hội thoại từ hồ sơ bạn bè

```
Hồ sơ B (A đang xem) → nút "Nhắn tin" (chỉ hiện khi GET /relationships/{B} = friends — GĐ4)
  → POST /conversations { userId: B }
       200 (đã có) | 201 (vừa tạo) → { conversationId, … }
  → chuyển /messages/{conversationId}
```

`POST /conversations` là get-or-create **idempotent**: bấm hai lần, hai tab, hai người cùng bấm — luôn một hội thoại.

### 7.2 SEQ-02 — gửi tin, người nhận online (FR-015, AC-01)

```
 1. FE-A : sinh clientMsgId; hiện tin "đang gửi" (optimistic)
 2. FE-A → Hub : SendMessage({ conversationId, content, clientMsgId })
 3. API  : RevocationHubFilter · tầng 2 message.send · BR-09 AreFriendsAsync (ngoài transaction)
 4. API  : transaction Đ-5.4 — khóa dòng · tầng 3 thành viên · kiểm trùng clientMsgId · cấp seq · INSERT
 5. API  → FE-A : ACK { message{ messageId, seq, createdAt, … }, replayed:false }      ← trạng thái Sent
 6. API  → Clients.Users(B, A) : MessageReceived(message)                              ← sau COMMIT
 7. FE-B : thêm tin (khử trùng theo messageId) → SendReceipt({ conversationId, kind:"delivered", upToSeq })
 8. API  : UPDATE mốc delivered của B (GREATEST) → ReceiptUpdated tới A                 ← Delivered
 9. FE-B : hội thoại đang mở VÀ tab đang hiển thị (document.visibilityState) → SendReceipt(kind:"seen")
10. API  : UPDATE mốc seen của B → ReceiptUpdated tới A                                 ← Seen
11. API  : phát MessageSent (log) — GĐ6 nối thông báo vào đây
```

p95 gửi→nhận là khoảng **bước 2 → bước 7** (người nhận vẽ được tin). Bước 7–10 không nằm trong con số GOAL-02.

**"Đã xem" chỉ khi thật sự nhìn thấy:** tab ẩn hoặc cửa sổ không focus thì chỉ gửi `delivered`; quay lại tab mới gửi
`seen`. Không làm vậy thì người để quên tab chat mở sẽ "xem" mọi tin — sai theo cách người gửi nhận ra.

### 7.3 Người nhận offline (AC-02)

```
bước 1–5 như trên; bước 6: Clients.User(B) không có kết nối nào → không ai nhận, không lỗi
B mở app sau đó:
  → header gọi GET /conversations/unread-count → badge = N
  → kết nối hub; mở /messages → GET /conversations (mỗi dòng có unreadCount)
  → mở hội thoại → GET …/messages (trang mới nhất) → SendReceipt delivered + seen tới seq cuối
```

Không có "hàng đợi tin chờ gửi" nào ở server: nguồn sự thật là bảng `messages` + mốc đã xem. B offline một tuần hay một
phút đều đi cùng đường.

### 7.4 Mất mạng, gửi lại, và fallback (AC-03, UC-15 A2)

```
FE-A gửi (hub) ── mạng rớt ── ACK không về trong 10 giây
  → tin chuyển "Thất bại" + nút "Thử lại"
  → Thử lại: hub đã nối lại? → SendMessage cùng clientMsgId
            hub chưa nối?    → POST /conversations/{id}/messages cùng clientMsgId
  → server: lần đầu đã lưu? → trả lại đúng tin cũ (200 / replayed:true) · chưa lưu? → lưu mới (201)
  → FE thay tin optimistic bằng tin server trả, khớp theo clientMsgId
```

Hub không nối lại được sau 3 lần liền (hoặc xin vé 503) → chế độ fallback (Đ-5.12): hỏi `afterSeq` mỗi 3 giây cho hội
thoại đang mở, dải "Đang kết nối lại" hiện trên màn chat.

### 7.5 Hủy kết bạn giữa chừng (AC-04, BR-09)

```
A và B đang chat → A hủy kết bạn (GĐ4, DELETE /friends/{B})
  → tin kế tiếp của A hoặc B: AreFriendsAsync = false → 403 messaging.not-friends / HubException not-friends
  → FE: thanh "Hai bạn không còn là bạn bè. Hội thoại chỉ đọc." + khóa ô soạn
  → lịch sử vẫn đọc được; mốc đã xem vẫn gửi được
```

Không có độ trễ: `AreFriendsAsync` không cache (Đ-4.3). Chặt hơn "chấp nhận trễ 60s" của PTTK Mục 5.6.

### 7.6 Tải lịch sử — cuộn ngược và lấp chỗ hở

```
Mở hội thoại  : GET …/messages?limit=30                 → 30 tin mới nhất, sắp seq DESC, nextCursor
Cuộn lên      : GET …/messages?cursor={nextCursor}       → 30 tin cũ hơn; nextCursor = null khi tới tin đầu tiên
Lấp chỗ hở    : GET …/messages?afterSeq={n}&limit=50     → tin có seq > n, sắp seq ASC (dùng khi nối lại / fallback / nhảy cóc)
```

Cursor của lịch sử là `base64url(seq)` — mờ với client (luật Đ-2.11). `cursor` và `afterSeq` **không** đi cùng nhau (→ 400).

---

## 8. Hợp đồng

Base `/api/v1`. Mọi lỗi REST là RFC 7807 kèm `traceId`. Rate limit chung 100 req/phút/user; endpoint vé có policy riêng
20/phút/user (Đ-5.9).

**Ba file hợp đồng**, mỗi file là một nguồn sự thật **và** một cổng CI:

- `src/backend/Modules/Messaging/Presentation/messaging-v1.yaml` — nhóm Swagger `messaging-v1`
- `src/backend/Modules/Messaging/Presentation/chat-hub-v1.md` — hợp đồng hub bằng văn bản (không nằm trong Swagger)
- `src/backend/Modules/Messaging/Presentation/chat-hub-v1.examples.json` — ví dụ payload của từng phương thức / sự kiện hub,
  dùng chung cho test backend và fixture frontend

Tên nhóm `messaging-v1` phải khớp ở ba chỗ như mọi module: `[ApiExplorerSettings(GroupName=…)]`, `apiGroups` trong
`Program.cs`, tên file yaml.

### 8.1 REST — `messaging-v1.yaml`

| Method | Path | Auth | Thành công | Lỗi |
|---|---|---|---|---|
| POST | `/realtime/tickets` | Bearer | 201 `RealtimeTicket` | 401 · 429 · 503 |
| POST | `/conversations` | Bearer + `message.send` | 200 / 201 `ConversationResponse` | 400 chính mình / id sai · 401 · 403 không phải bạn / thiếu quyền · 404 |
| GET | `/conversations?cursor=&limit=` | Bearer | 200 `ConversationPage` | 400 cursor sai · 401 |
| GET | `/conversations/unread-count` | Bearer | 200 `UnreadCount` | 401 |
| GET | `/conversations/{conversationId}` | Bearer | 200 `ConversationResponse` | 400 · 401 · 403 |
| GET | `/conversations/{conversationId}/messages?cursor=&afterSeq=&limit=` | Bearer | 200 `MessagePage` | 400 · 401 · 403 |
| POST | `/conversations/{conversationId}/messages` | Bearer + `message.send` | 201 mới / 200 gửi lại `MessageResponse` | 400 · 401 · 403 · 409 |
| POST | `/conversations/{conversationId}/receipts` | Bearer | 204 | 400 · 401 · 403 |

```
RealtimeTicket       { ticket: string, expiresIn: 30 }
CreateConversation   { userId }
ConversationResponse { conversationId,
                       peer: { userId, displayName, avatarUrl? },          // UserCard, định nghĩa lại trong file này (nếp GĐ4 8.1)
                       lastMessage?: MessageResponse,
                       unreadCount: integer,
                       peerDeliveredSeq: integer, peerSeenSeq: integer,     // để FE tự suy Sent/Delivered/Seen cho tin của mình
                       canSend: boolean }                                    // CHỈ có ở GET /{id} và POST (Đ-5.3)
ConversationPage     { items: [ConversationResponse không có canSend], nextCursor: string | null }   // last_message_at DESC
UnreadCount          { total: integer }
SendMessageRequest   { content: string (1–2000), clientMsgId: uuid }
MessageResponse      { messageId, conversationId, senderId, seq, content, clientMsgId, createdAt }
MessagePage          { items: [MessageResponse], nextCursor: string | null }
                       // có cursor hoặc không tham số: seq DESC · có afterSeq: seq ASC, nextCursor luôn null
ReceiptRequest       { kind: "delivered" | "seen", upToSeq: integer ≥ 1 }
```

- `MessageResponse` **không** có trường `status`: trạng thái suy từ `peerDeliveredSeq`/`peerSeenSeq` (Đ-5.6) và cập nhật
  bằng sự kiện `ReceiptUpdated`. Một trường `status` trên từng tin sẽ cũ ngay khi được trả về.
- `limit`: mặc định 20 (lịch sử: 30), tối đa 50 (AGENTS.md Mục 9).
- `upToSeq` lớn hơn `seq_counter` → kẹp về `seq_counter`, **không** 400: client nhận tin qua hub trước khi transaction
  của một tin khác xong là chuyện bình thường, không phải lỗi. Nhỏ hơn mốc hiện tại → 204, không đổi gì.
- `POST /conversations` với chính mình → 400 `errors.userId` *"Không thể nhắn tin cho chính mình."*
- `409` chỉ khi `clientMsgId` đã dùng cho một nội dung **khác** (Đ-5.5).
- `info.version`: `1.0.0-gd5`.

### 8.2 Hub — `chat-hub-v1.md`

```
Đường       : /hubs/chat        Transport : chỉ WebSockets, skipNegotiation (Đ-5.16)     Giao thức : JSON
Xác thực    : ?access_token=<vé> — vé xin qua POST /realtime/tickets (qua /bff/api/realtime/tickets), dùng một lần
Keep-alive  : mặc định SignalR (server 15s, client timeout 30s) — thấp hơn timeout nhàn rỗi 100s của Cloudflare
Tuổi thọ    : server cắt sau 15 phút (Đ-5.10); client tự kết nối lại với vé mới
```

**Client → server** (invoke, có kết quả trả về):

| Phương thức | Tham số | Kết quả | Lỗi (`HubException.Message` là **mã**, không phải câu cho người đọc) |
|---|---|---|---|
| `SendMessage` | `{ conversationId, content, clientMsgId }` | `{ message: MessageResponse, replayed: boolean }` — đây là ACK "Sent" | `forbidden` · `not-friends` · `validation` · `conflict` · `rate-limited` · `unavailable` |
| `SendReceipt` | `{ conversationId, kind: "delivered" \| "seen", upToSeq }` | `null` | `forbidden` · `validation` |

**Server → client** (sự kiện):

| Sự kiện | Payload | Gửi tới | Khi nào |
|---|---|---|---|
| `MessageReceived` | `MessageResponse` | mọi kết nối của **người nhận và người gửi** | sau `COMMIT` của mỗi tin mới (không phát khi `replayed`) |
| `ReceiptUpdated` | `{ conversationId, userId, deliveredSeq, seenSeq }` | mọi kết nối của **người kia** (và các tab khác của chính người gửi biên nhận) | sau khi mốc thật sự **tăng** (UPDATE không đổi gì thì không phát) |

**Quy tắc thứ tự và độ tin cậy — FE phải lập trình theo đúng các câu này:**

1. Server **không** bảo đảm sự kiện tới theo thứ tự `seq`, và **không** bảo đảm tới (mất kết nối là mất sự kiện).
2. `seq` là thứ tự duy nhất đáng tin. Thấy chỗ hở → lấp bằng REST `afterSeq`.
3. `messageId` là khóa khử trùng. Cùng một tin có thể tới qua ACK, `MessageReceived` và REST — hiện **một** lần.
4. `ReceiptUpdated` mang mốc **tuyệt đối**, không phải chênh lệch — nhận hai lần hay sai thứ tự đều vô hại (lấy max).
5. Lỗi `unavailable` hoặc mất kết nối giữa lời gọi `SendMessage` → **không biết** tin đã lưu chưa → gửi lại cùng
   `clientMsgId` (Đ-5.5 lo phần còn lại).

### 8.3 Cổng hợp đồng hub

Hub không có Swagger nên không có `ContractTestsBase`. Thay bằng hai test đọc **cùng** `chat-hub-v1.examples.json`:

- **Backend** (`ChatHubContractTests`, category `Contract`): mỗi ví dụ deserialize được thành đúng DTO của hub và serialize
  lại ra đúng JSON đó (cùng tên trường, cùng kiểu — bắt lỗi đổi tên trường, đổi camelCase); tập tên phương thức public
  của `ChatHub` và tập hằng tên sự kiện **bằng** tập tên trong file ví dụ.
- **Frontend** (Vitest): fixture của hub giả lấy từ file ví dụ, gắn kiểu bằng `satisfies` với
  `lib/realtime/chat-hub-contract.ts` → đổi ví dụ mà không đổi kiểu là **typecheck đỏ**.

Thử cho đỏ một lần mỗi phía (luật frontend Mục 9, bài học B4/B5 GĐ2): đổi tên một trường trong ví dụ → cả hai đỏ.

### 8.4 Codegen frontend

Không thêm script: `pnpm gen:api` tự sinh `lib/api/messaging/schema.d.ts` khi `messaging-v1.yaml` được commit (glob
`Modules/*/Presentation/*-v1.yaml`). Alias vào `lib/api/types.ts`; client `lib/api/messaging-api.ts` theo khuôn
`content-api.ts`. Cổng CI "API types khop hop dong" không phải sửa. File `.md`/`.json` của hub **không** khớp glob
`*-v1.yaml` nên không bị codegen nhặt nhầm — kiểm lại khi đặt tên file.

---

## 9. Kế hoạch thi công (một người — B — làm cả hai lane)

### 9.1 Điều kiện trước khi bắt đầu

| Cần | Trạng thái 2026-09-22 | Nếu chưa có |
|---|---|---|
| `IFriendshipReader` thật (GĐ4 A5) | **Có** trên `loveart1210` (`c26d308`), **chưa** vào `develop` | Tách nhánh từ `loveart1210` (Mục 9.4) |
| API kết bạn (GĐ4 D2/D3) để tạo bạn bè khi test tay / E2E | Chưa — A đang làm bước 4 | Dev: `INSERT` thẳng `socialgraph.friendships` bằng SQL; test: `ArrangePath` như Mục 6.3 |
| Màn hồ sơ người khác có slot `actions` + nút quan hệ (GĐ4 E2/E5) | Chưa | Nút "Nhắn tin" (E6) làm sau cùng; trước đó mở hội thoại từ màn `/messages` bằng id trong dev |
| apache chuyển `/hubs` về API trên VM | Chưa (dòng mẫu còn comment) | Nhờ chủ dự án làm cùng lúc sửa apache cho GĐ7 (Mục 9.4) — **cần ngay cho spike C0** |

### 9.2 Cổng mở — nửa ngày đầu

Làm một mình thì không có buổi họp, nhưng **sản phẩm của cổng mở vẫn bắt buộc**:

1. Tự rà **Đ-5.1 → Đ-5.18**; sửa cái nào ghi ngày + lý do ngay dưới nó. Ưu tiên rà kỹ **Đ-5.5, Đ-5.6** (hai lệch PTTK) —
   báo chủ dự án một câu trước khi chốt, vì chúng sẽ bị hỏi lúc bảo vệ.
2. Viết `messaging-v1.yaml` → `pnpm gen:api` → **commit cả yaml lẫn `schema.d.ts`**. Cổng `API contract` sẽ đỏ tới khi có
   controller — ghi rõ trong commit, **hoặc** (nếu cổng hợp đồng đã canh module từ đầu) hoãn phần yaml nào chưa có
   controller theo đúng cách GĐ4 hoãn `/feed` (lệch Mục 9.2 của `giai-doan-4.md`). Chọn một, ghi lại.
3. Viết `chat-hub-v1.md` + `chat-hub-v1.examples.json` → commit. **Quên bước này là FE không mock được hub** và rơi về nhịp
   "chờ backend" — rủi ro đã đăng ký ở Mục 0C kế hoạch gốc.
4. Chốt với chủ dự án ngày có `/hubs` trên staging (C0 cần nó).

### 9.3 Thứ tự thi công và ước lượng

Kế hoạch gốc giao GĐ5 cho **2 backend + 1 frontend trong 4 ngày**. Một người làm cả hai lane: khoảng **8–9 ngày làm việc**.
Ghi thẳng con số để lịch tổng sửa theo (nếp GĐ4 Mục 9.3).

| Bước | Việc | Ước lượng | Xong khi |
|---|---|---|---|
| 1 | Cổng mở (Mục 9.2) | 0,5 ngày | Ba file hợp đồng đã commit |
| 2 | **C0 — spike end-to-end trên staging** ⭐ | 0,5 ngày | Trình duyệt thật trên `https://mxh.banhgao.net` mở WebSocket `/hubs/chat` bằng vé, gọi một phương thức echo, nhận lại — **qua Cloudflare + apache**. Tab Network: 1 lần xin vé / 1 lần kết nối (Đ-5.16) |
| 3 | **A1–A5** nền dữ liệu · **B1** harness | 1 ngày | `--migrate` hai lần không đổi gì; `\dn` thấy schema `messaging`; ArchUnitNET xanh với type thật |
| 4 | **C1–C3** vé + scheme + hub + filter · **B5** test hub `HUB-01..06`, `HUB-10` | 1,5 ngày | Test hub xác thực xanh; đã thử cho đỏ (bỏ `GETDEL` → `HUB-02` đỏ) |
| 5 | **D0–D7** endpoint + service gửi · **B2** matrix (viết đỏ trước) · **B3** test đồng thời · **B4** hai cổng hợp đồng · `HUB-07..09` | 2 ngày | Matrix đủ dòng Mục 6.3 xanh; `MSG-C1/C2` xanh 20 lần liền; cổng hợp đồng REST + hub xanh và đã từng đỏ |
| 6 | **E1–E9** toàn bộ lane frontend | 2,5 ngày | Vitest + Playwright local xanh; tắt mạng 10s bật lại không mất tin |
| 7 | **C4** backplane + thử 2 instance · **C5** presence *(cắt được)* | 0,5 ngày | E2E A ở instance 1, B ở instance 2 nhận tin |
| 8 | **F1–F5** cổng đóng + **đo p95** | 1 ngày | Báo cáo p95 có số (kể cả khi không đạt) · checklist Mục 12 |

**Vì sao spike C0 đứng thứ hai, trước cả dữ liệu:** ba lớp Cloudflare → apache → Kestrel là thứ duy nhất trong GĐ5 mà
integration test không chạm tới, và là thứ có thể buộc đổi phương án (ISS-01). Biết ở ngày 1 còn đường lùi; biết ở cổng đóng
thì GOAL-02 trượt. Spike **không cần** bảng, không cần nghiệp vụ: một hub `EchoHub` tạm + endpoint vé là đủ, xong thì xóa
`EchoHub` (hoặc giữ thành `HUB-00` smoke test).

**Vì sao backend trước frontend, dù hợp đồng đã chốt:** làm một mình thì không có song song thật; và rủi ro lớn nhất
(đồng thời, xác thực hub) nằm ở backend. FE dựng trên hợp đồng đã chốt ở bước 1, nên không bị backend kéo lùi. Kẹt
backend (chờ `/hubs` trên VM, chờ API kết bạn) → chuyển sang một việc của khối E thay vì ngồi chờ.

### 9.4 Làm song song với A và chủ dự án — chỗ đụng nhau

| Chỗ | Ai đụng | Cách xử |
|---|---|---|
| **Nhánh** | B | Tách `gd5` từ `origin/loveart1210` (có `IFriendshipReader` thật + schema `socialgraph`). Khi GĐ4 vào `develop`: `rebase` lên `develop`. **PR GĐ5 mở sau khi PR GĐ4 đã merge** — tránh PR mang theo commit của A |
| `Program.cs` | B (Messaging: `AddApplicationPart`, `apiGroups`, `AddMessagingModule`, dòng migrate, `AddSignalR`, `MapHub`, scheme vé) · A (GĐ4 D0: nhóm `socialgraph-v1`) · chủ dự án (GĐ7 C1: `UseHttpMetrics`, `MapMetrics`) | Mỗi người **chỉ thêm dòng của mình**, không sắp lại khối có sẵn; ai merge sau thì rebase. Thứ tự middleware: `MapHub` đặt cạnh `MapControllers`, sau `UseAuthentication/UseAuthorization` |
| `SharedKernel` (`Realtime/`, policy rate limit mới, `IPresenceReader`) | B | Thư mục mới + một policy — không sửa hành vi có sẵn. Báo A khi thêm policy vào `SharedKernelExtensions` |
| `app/users/[userId]/page.tsx` (nút "Nhắn tin" cạnh nút quan hệ) | B (E6) · A (GĐ4 E5) | B làm **sau** khi GĐ4 E5 đã có; chỉ thêm `StartChatButton` vào slot `actions` |
| `components/shell/app-header.tsx` / `(app)/layout.tsx` (badge chưa đọc, link "Tin nhắn") | B | Truyền badge qua prop `actions` có sẵn; không đổi chữ ký component |
| `lib/security/csp.ts` (dev nối thẳng API — Đ-E18, Mục 9.5) | B | Chỉ thêm nhánh `dev`; test khẳng định CSP production **không đổi một ký tự** |
| **apache `/hubs`** trên VM + `apache-socialapp.conf.example` | **chủ dự án** (đang sửa apache ở GĐ7 D1/D4) | Làm một lần cho cả staging lẫn production (Mục 9.6). B cung cấp đoạn cấu hình, chủ dự án áp lên VM |
| `.github/workflows/ci.yml` | B (nếu cần) · A (GĐ4 B5) · chủ dự án (GĐ7 C6) | GĐ5 **không** cần job mới: test hub dùng category `AuthZ`/`Contract` có sẵn. Nếu vẫn phải sửa → báo nhau trước khi push |
| Grep log PII (GĐ7 C6) | chủ dự án | B báo thêm trường `content` của Messaging vào danh sách che (Đ-5.18) |

### 9.5 Dev: trình duyệt nối hub thế nào khi FE ở `:3000`, API ở `:5259`

Chỗ hở chưa tài liệu nào trả lời: ở dev, `/hubs/chat` trên `:3000` rơi vào Next (và `proxy.ts`) → 404; luật frontend Mục 4
**cấm** `rewrites`; CSP `connect-src 'self'` chặn nối thẳng `ws://localhost:5259`.

Đề xuất chốt ở cổng mở thành **Đ-E18** (ghi vào `huong-dan-khoi-e-frontend.md` trong cùng commit — luật frontend Mục 11 #4):

- `lib/realtime/hub-url.ts`: production/staging → `/hubs/chat` (tương đối, cùng origin, đúng Đ-E16); dev
  (`process.env.NODE_ENV === "development"`, được Next nhúng sẵn vào bundle trình duyệt) → `http://localhost:5259/hubs/chat`.
- `buildCsp`: **chỉ khi `dev`**, `connect-src` thêm `http://localhost:5259 ws://localhost:5259` — đúng tiền lệ `'unsafe-eval'`
  chỉ bật ở dev. Test Vitest khẳng định CSP production không có hai nguồn này.
- CORS dev đã cho `http://localhost:3000` kèm credentials (`Program.cs`, `Cors:AllowedOrigins`); WebSocket không qua CORS
  nhưng request xin vé thì vẫn đi qua BFF như thường.

Phương án đã cân nhắc rồi loại: dựng Caddy local đứng trước cả FE lẫn API (giống staging hơn, nhưng thêm một container và
đổi `APP_ORIGIN`, cookie `__Host-` và `PLAYWRIGHT_BASE_URL` của mọi người dev — đắt hơn cái nó bảo vệ).

### 9.6 Hạ tầng staging — đoạn cấu hình giao cho chủ dự án

apache trên VM (Ubuntu 22.04+ có apache ≥ 2.4.47 — kiểm `apache2 -v`) chuyển WebSocket bằng `mod_proxy_http`, không cần
`mod_proxy_wstunnel`:

```apache
# Realtime SignalR (GĐ5) — đặt TRƯỚC ProxyPass /api và TRƯỚC catch-all "/"
ProxyPass        /hubs/ http://127.0.0.1:18080/hubs/ upgrade=websocket timeout=3600
ProxyPassReverse /hubs/ http://127.0.0.1:18080/hubs/

# Không ghi query string (mang vé) của /hubs vào access log — ràng buộc Đ-E16
SetEnvIf Request_URI "^/hubs/" hubs_request
CustomLog ${APACHE_LOG_DIR}/access.log combined env=!hubs_request
CustomLog ${APACHE_LOG_DIR}/access.log "%h %l %u %t \"%m %U %H\" %>s %b" env=hubs_request
```

Nếu apache cũ hơn 2.4.47: `a2enmod proxy_wstunnel` + `RewriteCond %{HTTP:Upgrade} =websocket [NC]` /
`RewriteRule ^/hubs/(.*) ws://127.0.0.1:18080/hubs/$1 [P,L]`.

Ba điều kiểm trên staging (C0):
1. Cloudflare: **Network → WebSockets = On** (mặc định bật, nhưng kiểm).
2. Kết nối không bị cắt định kỳ: SignalR ping mỗi 15 giây nên bình thường không chạm timeout nhàn rỗi của apache
   (`Timeout`/`ProxyTimeout`) hay của Cloudflare (100s); `timeout=3600` là lưới an toàn. Kiểm bằng cách để yên tab 10 phút và
   đếm số lần xin vé trong tab Network — ngoài lần cắt chủ động sau 15 phút (Đ-5.10), phải là 0. Kết nối lại định kỳ trông
   như "chạy được" nhưng đốt vé và làm lệch số đo p95.
3. `grep hubs /var/log/apache2/access.log` — **không** thấy `access_token=`.

`deploy/apache-socialapp.conf.example` sửa theo đúng đoạn trên (dòng comment GĐ0B hiện đang nằm **sau** catch-all `/` —
vị trí đó sai, `/hubs` phải đứng trước). Compose staging **không** cần sửa: hub chạy trong cùng container `api`, cùng cổng
`18080`.

### 9.7 Thư viện cần thêm

| Gói | Ở đâu | Ghi chú |
|---|---|---|
| — (SignalR server nằm sẵn trong ASP.NET Core 8) | `SocialApp.Api` / module | Không thêm gói cho hub |
| `Microsoft.AspNetCore.SignalR.StackExchangeRedis` | `SocialApp.Api` | **Ghim 8.0.x** (lineage net8). Chỉ dùng khi bật backplane (Đ-5.13) |
| `Microsoft.AspNetCore.SignalR.Client` | `tests/SocialApp.IntegrationTests` | Ghim 8.0.x; test hub đi đúng đường client thật (Đ-5.16) |
| EF Core + Npgsql 8.0.10 | `SocialApp.Modules.Messaging.csproj` | Chép đúng ba dòng của `SocialGraph.csproj` |
| `@microsoft/signalr` | frontend | Ghim chính xác (`save-exact`). Chỉ `lib/realtime/**` được import (Đ-5.17) |

Luật vàng số 8: mọi thứ phải chạy trên ARM64 — cả ba gói đều là managed code / JS thuần, không có native binary.

---

## 10. Chiến lược test

### 10.1 Nghiệm thu chức năng — REST (integration, Postgres thật qua Testcontainers)

| Id | Kịch bản | Kỳ vọng |
|---|---|---|
| `CONV-01` | A mở hội thoại với bạn B | 201; gọi lại → 200 cùng `conversationId` |
| `CONV-02` | A và B cùng mở hội thoại với nhau đồng thời (20 lần) | luôn đúng **một** dòng `conversations` |
| `CONV-03` | A mở hội thoại với chính mình | 400 `errors.userId` |
| `CONV-04` | A mở hội thoại với id không có hồ sơ | 404 |
| `PAIR-01` | 200 cặp uuid ngẫu nhiên qua service | không cặp nào đỏ `ck_conversations_order` (Đ-5.2) |
| `MSG-01` | A gửi tin | 201; `seq = 1`; `last_message_*` trỏ đúng tin; mốc đã xem của A = 1 (Đ-5.14) |
| `MSG-02` | Nội dung rỗng / chỉ khoảng trắng / 2001 ký tự | 400 `errors.content` |
| `MSG-03` | Lịch sử 75 tin, `limit=30` | ba trang 30/30/15, `seq` giảm dần, trang cuối `nextCursor = null` |
| `MSG-04` | `afterSeq=70` | tin 71..75 tăng dần |
| `MSG-05` | Cursor rác · `cursor` cùng `afterSeq` | 400, không 500, không âm thầm trả trang đầu |
| `MSG-06` | Gửi lại cùng `clientMsgId` + cùng nội dung | 200, **cùng** `messageId` và `seq`; bảng vẫn 1 dòng |
| `MSG-07` | Cùng `clientMsgId`, khác nội dung | 409 |
| `MSG-10` | B offline nhận 3 tin | `unread-count` của B = 3; của A = 0; B gửi `seen` tới 3 → B = 0 |
| `RCP-01` | `seen` 5 rồi `seen` 3 | mốc vẫn 5 |
| `RCP-02` | `upToSeq` lớn hơn `seq_counter` | 204, mốc = `seq_counter` |
| `RCP-03` | `seen` kéo theo `delivered` | `delivered_seq ≥ seen_seq` luôn đúng |
| `LIST-01` | A có 25 hội thoại có tin + 3 hội thoại rỗng | trang 20 + 5, sắp `last_message_at` giảm dần, không có hội thoại rỗng |
| `LIST-02` | Danh sách không N+1 | đếm truy vấn: số câu SQL không đổi khi số hội thoại trong trang tăng từ 1 lên 20 (nếp `FEED-Q1` GĐ4) |
| `FRIEND-01` | Hủy kết bạn rồi gửi | 403 `messaging.not-friends`; `GET /{id}` trả `canSend = false`; lịch sử vẫn đọc được |
| `LOG-01` | Gửi tin có nội dung đánh dấu (`"SECRET-xyz"`) | `CapturingLogSink` không có chuỗi đó ở bất kỳ mức log nào (Đ-5.18) |

### 10.2 Nghiệm thu đồng thời — chạy 20 lần liền trên CI local trước khi tin

| Id | Kịch bản | Kỳ vọng |
|---|---|---|
| `MSG-C1` | 50 lượt gửi song song vào một hội thoại (A và B xen nhau) | `seq` đúng tập 1..50, không lỗ, không trùng; `seq_counter = 50` |
| `MSG-C2` | 10 lượt gửi song song **cùng** `clientMsgId` | đúng 1 dòng; cả 10 phản hồi mang cùng `messageId`; 1 lượt 201, 9 lượt 200; không 500 |
| `MSG-C3` | Gửi song song ở 5 hội thoại khác nhau | không deadlock (`40P01`), không hội thoại nào chờ hội thoại khác (khóa theo **dòng**) |
| `RCP-C1` | 20 biên nhận `seen` với `upToSeq` ngẫu nhiên song song | mốc cuối = max của các giá trị |

### 10.3 Nghiệm thu hub

Mục 6.4 (`HUB-01..10`) + hai test chức năng:

| Id | Kịch bản | Kỳ vọng |
|---|---|---|
| `HUB-20` | A gửi qua hub; B có hai kết nối (hai tab) | ACK mang `seq`; **cả hai** kết nối của B và kết nối khác của A nhận `MessageReceived` |
| `HUB-21` | B gửi `SendReceipt(seen)` | A nhận `ReceiptUpdated` với mốc mới; gửi lại cùng mốc → **không** phát lần hai |

### 10.4 Unit test

`ConversationPair.Of` (thứ tự Postgres) · `MessageContentPolicy` (1–2000, không toàn khoảng trắng) · suy trạng thái từ mốc
(Đ-5.6) · `MessageCursor` round-trip và rác vào thì trả `false`, không ném · kẹp `upToSeq` · băm vé + so sánh thời gian hằng
(`CryptographicOperations.FixedTimeEquals` nếu có so sánh) · `RevocationHubFilter` với store giả.

### 10.5 Cổng CI phải mở rộng — quên chỗ nào là cổng xanh giả

1. `MessagingContractTests : ContractTestsBase` (`"messaging-v1.yaml"`, `MessagingApiGroup.Name`) + dòng
   `<Content Include=…messaging-v1.yaml … />` trong `IntegrationTests.csproj`. **Thiếu dòng csproj thì test không tìm thấy file.**
2. `ChatHubContractTests` + dòng `<Content Include=…chat-hub-v1.examples.json … />`.
3. `HubAuthZTests` mang `[Trait("Category","AuthZ")]` — nếu quên trait, nó chạy trong nhóm test thường và cổng "AuthZ matrix"
   không canh hub.
4. `Messaging_Domain_namespace_must_not_be_empty` ở `PersistenceBoundaryTests` (nếp GĐ2 A7, GĐ4) — không có nó,
   `ModuleBoundaryTests` xanh trong chân không.
5. `MessagingPermissionsTests` ở ArchitectureTests.
6. ESLint `no-restricted-imports` cho `@microsoft/signalr` ngoài `lib/realtime/**` — thử cho đỏ một lần.

Mọi cổng mới: **thử cho đỏ một lần rồi khôi phục**, `git status` sạch trước và sau (luật frontend Mục 9).

### 10.6 Frontend

- **Vitest:** `chat-connection` với `HubConnection` giả (inject qua interface, không mock module `@microsoft/signalr` toàn
  cục) — chuyển trạng thái `connecting → connected → reconnecting → fallback → connected`; xin vé mới mỗi lần nối lại;
  dừng khi nhận tín hiệu đăng xuất. Tập tin nhắn: khử trùng, sắp theo `seq`, phát hiện chỗ hở gọi `afterSeq`. Composer:
  Thất bại → Thử lại dùng **cùng** `clientMsgId`; 403 `not-friends` → khóa ô soạn. Đúng **một** ca `<StrictMode>` cho hook
  sở hữu kết nối. `errorMessage` cho các ngữ cảnh mới.
- **Playwright** (local, `workers: 1`, dán kết quả vào PR kèm bản Chrome — Đ-E8): hai `browser.newContext()` cho A và B
  trong **một** test → A gửi, B thấy; B mở hội thoại, A thấy "Đã xem"; `context.setOffline(true)` 10 giây rồi bật lại → không
  mất tin, không lặp tin; hủy kết bạn → thanh "chỉ đọc".

### 10.7 Đo p95 gửi→nhận (GOAL-02) — cách đo phải trung thực

Đồng hồ của hai máy khác nhau lệch nhau tới hàng trăm ms — lớn cỡ chính con số cần đo. Nên:

- **Hai trình duyệt trên cùng một máy** (hai `context` của Playwright trong một tiến trình, hoặc hai profile Chrome), trỏ
  **staging HTTPS thật** — cùng một đồng hồ, nên `Date.now()` của hai bên so được với nhau.
- **Công cụ gắn vào màn chat thật** (E8): bật bằng `?latency=1`; ở phía gửi ghi `t0` lúc bấm gửi theo `clientMsgId`; ở phía
  nhận ghi `t1` lúc tin được **vẽ** (sau `requestAnimationFrame`); xuất mẫu thành JSON. Không thêm trường nào vào hợp đồng.
- **Kịch bản:** 200 tin A→B, nhịp 1 tin/giây (không phải bắn dồn — đo độ trễ, không đo thông lượng), lặp lại một lượt
  B→A. Báo cáo p50/p95/p99, lượt nào kết nối lại giữa chừng thì ghi riêng.
- **Server** ghi thêm histogram `socialapp_message_push_seconds` (từ `COMMIT` tới lúc gọi `SendAsync`) — để khi p95 không
  đạt thì biết chậm ở DB, ở server hay ở đường truyền. GĐ7 vẽ nó lên Grafana.
- Kết quả lưu `docs/giai-doan-5/bao-cao-p95-chat.md`: ngày giờ, trình duyệt, mạng, số mẫu, p50/p95/p99, **kể cả khi không
  đạt** (nếp báo cáo k6 của GĐ4).

---

## 11. Definition of Done

Theo Mục 3.5 báo cáo:

- [ ] Đủ AC US-015 (AC-01..04), mỗi AC có test hoặc bằng chứng E2E trỏ tới
- [ ] Mọi cửa vào (REST **và** hub) có tầng 2 + tầng 3; mọi endpoint chạm hội thoại có dòng matrix; hub có `HUB-*`
- [ ] Lỗi REST là RFC 7807; lỗi hub là mã trong bảng Mục 8.2 — không câu nào chứa id, nội dung tin hay tên kiểu
- [ ] Chạy thử trên staging bằng **hai tài khoản thật là bạn của nhau**
- [ ] Swagger nhóm `messaging-v1` cập nhật; `chat-hub-v1.md` khớp code (cổng Mục 8.3 xanh)
- [ ] Không lộ secret/PII: vé không nằm trong log (Serilog + apache), nội dung tin không nằm trong log
- [ ] `README.md` Mục 1 cập nhật trạng thái GĐ5 (luật vàng 7); lệch quyết định đã ghi ngược vào tài liệu này

## 12. Checklist nghiệm thu cuối GĐ5

**Dữ liệu (A)**
- [ ] `--migrate` chạy hai lần liên tiếp trên DB sạch: lần hai không đổi gì, exit 0
- [ ] Schema `messaging` có hai bảng với đủ UQ + CHECK của Mục 4
- [ ] `EXPLAIN` lịch sử tin: `Index Scan Backward` trên `uq_messages_conv_seq`, không `Sort`

**Realtime (C)**
- [ ] Kết nối hub trên staging qua Cloudflare + apache, 1 lần xin vé mỗi lần kết nối
- [ ] Access log apache và log API **không** chứa `access_token=`
- [ ] Để yên tab chat 10 phút: 0 lần kết nối lại (keep-alive qua được timeout của apache và Cloudflare)
- [ ] Kết nối tự cắt sau 15 phút và tự nối lại không ai nhận ra
- [ ] Backplane: E2E hai instance ở local xanh *(ghi "chưa bật trên staging — chờ GĐ7 khối E")*
- [ ] Presence: `IPresenceReader` có test *(hoặc ghi "hoãn tới GĐ6" nếu đã cắt)*

**Bảo mật (B, D)**
- [ ] Matrix Mục 6.3 xanh trên CI; đã từng đỏ khi bỏ kiểm thành viên (bảng đột biến: bỏ `ConversationAccess` → `TC-A04*` đỏ;
      bỏ `AreFriendsAsync` → `TC-A07*` đỏ; đổi 403 thành 404 → đỏ)
- [ ] `HUB-01..10` xanh; đã từng đỏ (bỏ `GETDEL` → `HUB-02`; đổi `Clients.Users` thành `Clients.All` → `HUB-09`)
- [ ] `MSG-C1`, `MSG-C2` xanh 20 lần liền

**Lát cắt dọc (E, F) — trên staging, hai trình duyệt**
- [ ] A gửi → B thấy; trạng thái Đã gửi → Đã nhận → Đã xem hiện đúng ở A (AC-01)
- [ ] B đăng xuất, A gửi 3 tin, B đăng nhập lại → badge 3, mở ra thấy đủ, badge về 0 (AC-02)
- [ ] Tắt mạng A giữa lúc gửi, bật lại, bấm Thử lại → B thấy **một** tin (AC-03)
- [ ] Hủy kết bạn → cả hai thấy thanh "chỉ đọc", gửi → bị chặn, lịch sử còn nguyên (AC-04)
- [ ] Chặn WebSocket (DevTools → chặn `/hubs/*`) → vẫn gửi và nhận được qua fallback, trễ ≤ ~3s
- [ ] Tab Network: chỉ thấy `/bff/*` và **một** WebSocket `/hubs/chat` — không JWT nào, không id hội thoại của người khác
- [ ] **Báo cáo p95 gửi→nhận** trên staging đã lưu, có số (Mục 10.7)

## 13. Sai khác so với kế hoạch gốc và báo cáo v5.0

| # | Báo cáo v5.0 / kế hoạch gốc | Thực hiện | Lý do |
|---|---|---|---|
| 1 | `messages.status CK (sent, delivered, seen)` | Bỏ cột; bốn cột mốc `*_delivered_seq`, `*_seen_seq` trên `conversations` | Biên nhận 1-1 là tích lũy → một UPDATE mỗi lần xem thay vì N; lấp chỗ hở "bộ đếm chưa đọc" mà ENT-06/07 không có cột nào (Đ-5.6) |
| 2 | "409 trùng clientMsgId" | Gửi lại cùng nội dung → 200 + đúng tin cũ; 409 chỉ khi cùng id **khác** nội dung | Lần gửi trùng thật sự là "đã thành công, mất ACK" — 409 biến thành mã thành công trá hình (Đ-5.5) |
| 3 | `user_a_id`, `sender_id` "FK users" | `uuid` trần, không FK chéo schema | Đ-2.2 áp cho mọi module; hệ quả xóa tài khoản ghi nợ GĐ8 |
| 4 | `last_message_id` "FK messages" | Không FK, ghi cùng transaction | Hai bảng trỏ vòng; FK thường làm câu UPDATE Đ-5.4 đỏ, FK deferrable EF không khai báo được (Đ-5.1) |
| 5 | Máy trạng thái có `Failed` | `Failed` chỉ tồn tại ở client | Tin thất bại chưa từng tới DB; CK của PTTK vốn cũng chỉ có ba giá trị |
| 6 | UC-15 A1: B offline → "tạo notification" | GĐ5 phát event `MessageSent` (chỉ log) + badge; thông báo ở GĐ6 | Module Notification chưa có (Đ-5.15) |
| 7 | SEQ-02: "tra presence B" trước khi đẩy | Đẩy bằng `Clients.User` bất kể presence; presence chỉ để GĐ6 quyết định thông báo | Không có presence thì không ai nhận, không lỗi — tin đã lưu bền (Đ-5.11) |
| 8 | ROADMAP cũ: `?access_token=` là JWT; group theo hội thoại | Vé 30s dùng một lần (Đ-E16); `Clients.User` | JWT trên URL nằm trong log; group thêm một cửa tầng 3 phải canh (Đ-5.8, Đ-5.9) |
| 9 | ADR-003: backplane Redis | Có, bật theo cấu hình, mặc định tắt | Một instance thì backplane chỉ thêm điểm hỏng; bật khi GĐ7 có bản sao thứ hai (Đ-5.13) |
| 10 | PTTK Mục 5.6: kiểm quan hệ qua cache 60s | Kiểm thẳng DB mỗi lần gửi | `AreFriendsAsync` không cache (Đ-4.3) → hủy kết bạn có hiệu lực ngay |
| 11 | Kế hoạch gốc: 2 backend + 1 frontend, 4 ngày | Một người, ~8–9 ngày | Nhóm chia GĐ4/GĐ5/GĐ7 cho ba người song song |

## 14. Rủi ro cần theo dõi

| Mã | Rủi ro | Dấu hiệu sớm | Ứng phó |
|---|---|---|---|
| **R5-01** (ISS-01) | WebSocket không qua được Cloudflare/apache | Spike C0 đỏ | Sửa cấu hình theo Mục 9.6; nếu kẹt quá 1 ngày → chạy fallback REST + hỏi lại 3s làm đường chính, giữ nguyên hợp đồng, báo chủ dự án ngay |
| **R5-02** | Vé bị tiêu ở negotiate → mọi kết nối 401 | 2 lần xin vé mỗi lần kết nối | `skipNegotiation` (Đ-5.16) |
| **R5-03** | `seq` lỗ hoặc trùng dưới tải | `MSG-C1` đỏ lúc có lúc không | Khóa dòng Đ-5.4; không bao giờ cấp `seq` bằng `MAX(seq)+1` |
| **R5-04** | Kết nối sống sau khi người dùng mất quyền | `HUB-04b`/`HUB-10` đỏ | Đ-5.10 — filter mỗi lời gọi + tuổi thọ 15 phút |
| **R5-05** | Người thứ ba nhận được tin | `HUB-09` đỏ | `IUserIdProvider` đọc `sub`; cấm `Clients.All`/group tự đặt tên |
| **R5-06** | Presence "online vĩnh viễn" sau deploy | Người đã tắt máy vẫn online | TTL + gia hạn (Đ-5.11) |
| **R5-07** | Bật backplane ở GĐ7 thì chat hỏng một nửa | — (chỉ lộ khi có 2 instance) | C4 thử hai instance ngay ở GĐ5 |
| **R5-08** | Đụng file với A (Program.cs, trang hồ sơ) | Conflict khi rebase | Mục 9.4 — chỉ thêm dòng, rebase thường xuyên |
| **R5-09** | Chờ GĐ4 (API kết bạn, slot hồ sơ) làm đứng lane | Không tạo được bạn bè để test tay | SQL `INSERT` ở dev; E6 (nút "Nhắn tin") làm sau cùng |
| **R5-10** | p95 đo sai vì đồng hồ lệch | Số âm hoặc dao động vô lý | Cùng một máy, cùng một đồng hồ (Mục 10.7) |

---

# Phần B — Kế hoạch triển khai

## B.0 Cách đọc phần này

Phần A nói *cái gì* và *vì sao*. Phần B chia việc thành **sáu khối A–F**, mỗi khối một chuỗi đầu việc có mã (`A1`, `D5`…).
Mã việc đi vào **tiêu đề commit** — `feat(gd5-d): D5 — …` (`.claude/rules/commit-rules.md`: scope `gd5-<khối>`, tiếng Việt
có dấu, ≤ 95 ký tự, **không** dòng co-author/attribution) — và vào bảng theo dõi tiến độ.

Mỗi đầu việc ghi ba thứ: **Làm gì** · **Làm như nào** · **Xong khi (kết quả mong đợi)**. Một đầu việc xong khi: code chạy ·
có test tương ứng · tài liệu/hợp đồng liên quan sửa **trong cùng commit** · `detect-changes` sạch. Không có "xong 90%".

Thứ tự trong mỗi khối là thứ tự phụ thuộc; thứ tự **giữa** các khối theo Mục 9.3.

## B.1 Điểm xuất phát — cái gì đã có sẵn

Kiểm ngày 2026-09-22 trên `loveart1210` (`e120090`). GĐ5 **không** dựng lại thứ nào dưới đây:

| Đã có | Ở đâu | GĐ5 dùng để làm gì |
|---|---|---|
| Project `SocialApp.Modules.Messaging` rỗng (chỉ `.gitkeep`), đã tham chiếu SharedKernel, đã được `SocialApp.Api.csproj` tham chiếu | `src/backend/Modules/Messaging/` | Vỏ module — thêm `Presentation/`, `DependencyInjection/` |
| Mã quyền `message.send` (mã 11/17), đã gán USER + MODERATOR | `Modules/Identity/Domain/PermissionCodes.cs` | Tầng 2 của gửi tin và mở hội thoại |
| `[RequirePermission]`, `IPermissionCache`, Admin short-circuit tầng 2 | `SharedKernel/Authorization/` | Tầng 2 ở REST và trong service (hub) |
| `Result`/`Error`/`ToActionResult`, `Error.Forbidden`, `Error.Validation` | `SharedKernel/Results/`, `Http/ResultHttpExtensions.cs` | Tầng 3 + ánh xạ lỗi REST; dịch sang mã lỗi hub |
| `ITokenRevocationStore` (`revoked:user:{id}`, fail-open) | `SharedKernel/Authentication/` | Kiểm lúc bắt tay + mỗi lời gọi hub (Đ-5.10) |
| `JwtOptions` (access 900s, ClockSkew 30s), `GetUserId()` đọc `sub` | SharedKernel | Tuổi thọ kết nối; `IUserIdProvider` đọc `sub` |
| `RedisConnection` dùng chung (`ConnectedOrNull()`, `GetAsync()`) — comment đã chừa chỗ "backplane, presence" | `SharedKernel/Redis/` | Vé, presence. **Backplane dùng kết nối riêng** (Đ-5.13) |
| `IFriendshipReader` thật (không cache) | SharedKernel + `SocialGraph/Infrastructure/FriendshipReader.cs` | BR-09 |
| `IUserDirectory.GetManyAsync` (batch) | SharedKernel + Profile | Tên + avatar người kia trong danh sách hội thoại |
| `IObjectStorage.CreatePresignedGet` | `SharedKernel/Storage/` | Ký URL avatar |
| `Uuid7.New()` | `SharedKernel/Ids/` | PK hội thoại, tin |
| Khuôn cursor base64url (`PostCursor`) | `Content/Application/Posts/PostCursor.cs` | **Chép khuôn** thành `MessageCursor`/`ConversationCursor` trong Messaging (không import Content) |
| Rate limiter (fixed window 100/phút, policy `auth`) | `SharedKernel/DependencyInjection/SharedKernelExtensions.cs` | Thêm policy `realtime-ticket` |
| Khuôn module: `Add<X>Module`, `<X>DbContextOptions`, design-time factory, `Migrate<X>ModuleAsync` | Identity, Profile, Content, SocialGraph | Khuôn cho Messaging |
| Khung AuthZ matrix, harness Testcontainers (`PostgresFixture`, `RedisFixture`, `ModulesApiFactory`, `TestJwt`, `CapturingLogSink`) | `tests/SocialApp.IntegrationTests/` | Dòng mới + test hub |
| `ContractTestsBase`, `ContentPermissionsTests`, guard namespace | `tests/` | Chép cho Messaging |
| BFF proxy chung `/bff/api/[...path]` (gắn bearer, tự refresh) | `src/frontend/app/bff/`, `lib/bff/` | `POST /bff/api/realtime/tickets` chạy ngay, **không** route BFF mới |
| `BroadcastChannel('socialapp:auth')` báo đăng xuất | `lib/auth/` | Dừng kết nối hub khi đăng xuất |
| `use-post-page.ts` (cursor mờ, `nextCursor === null`, khử trùng theo id, chống phản hồi cũ) | `features/post/` | Khuôn logic cho lịch sử tin — **chép**, không import |
| `AppHeader` nhận `actions?: ReactNode` | `components/shell/app-header.tsx` | Badge chưa đọc + link "Tin nhắn" |

**Chỗ có sẵn nhưng phải sửa:**

| Chỗ | Sửa gì | Vì sao |
|---|---|---|
| `Program.cs` | `AddApplicationPart` · mục `apiGroups` · `AddMessagingModule` · dòng migrate · `AddSignalR` + `IUserIdProvider` · scheme `RealtimeTicket` · `MapHub<ChatHub>("/hubs/chat")` | Host liệt kê tường minh từng module |
| `SharedKernelExtensions.cs` | Policy rate limit `realtime-ticket` | Đ-5.9 |
| `PostgresFixture` / `ModulesApiFactory` | Thêm Messaging vào thứ tự migrate cố định | Test tích hợp cần schema mới |
| `IntegrationTests.csproj` | Hai dòng `Content Include` (yaml + ví dụ hub) | Cổng hợp đồng tìm thấy file |
| `deploy/apache-socialapp.conf.example` | Đoạn `/hubs` ở Mục 9.6, **trước** `/api` | Dòng comment hiện tại nằm sai chỗ |
| `lib/security/csp.ts` | Nhánh dev của Đ-E18 | Mục 9.5 |
| `eslint.config.mjs` | `no-restricted-imports` cho `@microsoft/signalr` | Đ-5.17 |

## B.2 Bản đồ công việc

| Khối | Nội dung | Số việc | Cần trước | Chặn | Bước (Mục 9.3) |
|---|---|---|---|---|---|
| **A. Nền dữ liệu** | Module, schema, migration, domain rule | 5 | cổng mở | D | 3 |
| **B. Test + cổng CI** | Harness, matrix, đồng thời, hợp đồng, test hub | 5 | A (một phần) | F | 3, 4, 5 |
| **C. Realtime** | Spike, vé, hub, filter, backplane, presence | 6 | cổng mở; `/hubs` trên VM (C0) | D8, E2, F | 2, 4, 7 |
| **D. Endpoint + nghiệp vụ** | 8 endpoint REST + 2 phương thức hub | 9 | A, C1–C3 | E (ráp thật), F | 5 |
| **E. Lane frontend** | Kết nối, danh sách, cửa sổ chat, composer, fallback, đo p95 | 9 | chỉ cần hợp đồng | F | 6 |
| **F. Cổng đóng** | Staging, E2E, báo cáo p95, đóng băng | 5 | D, E | GĐ6 | 8 |

**Khối E không phụ thuộc backend** — dựng trên hợp đồng REST (msw) và hợp đồng hub (hub giả từ `examples.json`). Kẹt backend
thì chuyển sang E.

---

## B.3 Khối C0 — Spike end-to-end *(làm ngay sau cổng mở)*

> **Mục tiêu:** biết trong ngày đầu rằng WebSocket có đi xuyên được Cloudflare → apache → Kestrel với vé dùng một lần hay
> không — thứ duy nhất có thể buộc cả giai đoạn đổi phương án.

### C0 — Hub echo + vé tối giản trên staging

**Làm gì:** một `EchoHub` tạm ở `/hubs/chat` có phương thức `Echo(string) → string`, xác thực bằng vé; một trang dev tạm
(hoặc DevTools console trên staging) kết nối bằng `@microsoft/signalr` với `skipNegotiation`.

**Làm như nào:** làm trước phần vé tối giản của C1 (store Redis + endpoint + handler) — đó là phần sẽ giữ lại; `EchoHub` là
phần bỏ đi. Nhờ chủ dự án áp đoạn apache Mục 9.6 lên VM. Deploy qua CD như thường (push nhánh → PR nhỏ vào `develop`
**chỉ khi** chủ dự án đồng ý; nếu không, build image tay trên VM theo cách GĐ0B từng làm).

**Xong khi:**
- Trên `https://mxh.banhgao.net`, kết nối ở trạng thái `Connected`, `Echo("ping")` trả `"ping"`.
- Tab Network: **1** `POST /bff/api/realtime/tickets` + **1** WebSocket `101 Switching Protocols` mỗi lần kết nối.
- Dùng lại vé cũ bằng tay → 401.
- Để yên 5 phút → vẫn `Connected` (keep-alive qua được timeout apache/Cloudflare).
- `access.log` không có `access_token=`.

**Cạm bẫy:** kết nối được ở local không chứng minh gì — `TestServer` và `localhost` không có Cloudflare lẫn apache.

---

## B.4 Khối A — Nền dữ liệu

> **Mục tiêu khối:** module thứ năm có schema riêng, migrate được, và **không có một tham chiếu nào** sang module khác
> ngoài contract ở SharedKernel.

### A1 — Entity và luật nghiệp vụ trong `Domain/`

**Làm gì:** `Conversation` (id, cặp thành viên, `SeqCounter`, con trỏ tin cuối, bốn mốc), `Message` (id, conversationId,
senderId, seq, content, clientMsgId, createdAt), và các hàm thuần: `ConversationPair.Of(a, b)` (Đ-5.2),
`MessageContentPolicy.Validate(content)`, `ReceiptMarks` (kẹp `upToSeq`, suy trạng thái Sent/Delivered/Seen — Đ-5.6).

**Làm như nào:** không navigation property sang người dùng (không có kiểu nào để trỏ). Luật đặt dạng hàm thuần như
`PostContentPolicy` GĐ2 để unit test không cần DB. Tránh đặt tên kiểu trùng tên namespace (bài học `UserProfile` của GĐ2 A1:
đừng đặt kiểu tên `Messaging`).

**Xong khi:** unit test `ConversationPair` (có ca so với thứ tự Postgres), `MessageContentPolicy`, `ReceiptMarks` xanh.

### A2 — `MessagingDbContext` + configuration + options + design-time factory

**Làm gì / như nào:** chép đúng hình dạng `SocialGraphDbContext`: `HasDefaultSchema("messaging")`, bảng lịch sử migration
trong schema `messaging`, `UseMessagingNpgsql(cs)` là **một** chỗ cấu hình dùng cho cả DI lẫn design-time (cạm bẫy Đ-2.1).
`Message` không có `updated_at` → override `SaveChanges` bỏ qua nó. Comment trên `last_message_id`: "không FK — Đ-5.1".

**Xong khi:** `dotnet ef migrations add InitialMessaging` sinh đúng DDL Mục 4 (đọc lại file migration, đối chiếu từng CHECK).

### A3 — Migration đầu tiên + `AddMessagingModule` + `MigrateMessagingModuleAsync` + nối `Program.cs`

**Làm gì:** một dòng DI, một dòng trong nhánh `--migrate` (sau SocialGraph), `AddApplicationPart` + `apiGroups` khi đã có
controller đầu tiên (D0).

**Xong khi:** `--migrate` chạy hai lần liên tiếp trên DB sạch, lần hai không đổi gì, exit 0; `\dn` thấy năm schema.

### A4 — Guard namespace + `MessagingPermissions`

**Làm gì:** `Messaging_Domain_namespace_must_not_be_empty` ở `PersistenceBoundaryTests`; `MessagingPermissions.MessageSend
= "message.send"` + `MessagingPermissionsTests` (chép `ContentPermissionsTests`).

**Xong khi:** ArchUnitNET xanh **với type thật**; đổi hằng thành `"message.sned"` → test đỏ.

### A5 — Store + truy vấn

**Làm gì:** `IConversationStore` (Application) + hiện thực EF (Infrastructure): tìm theo id, get-or-create theo cặp
(`INSERT … ON CONFLICT DO NOTHING` rồi `SELECT`), danh sách keyset hai phía, tổng chưa đọc (Đ-5.14), lịch sử (cursor
`seq` giảm dần / `afterSeq` tăng dần), cập nhật mốc (`GREATEST`, kẹp `seq_counter`). Câu gửi tin Đ-5.4 viết ở D5.

**Làm như nào:** danh sách hai phía = `UNION ALL` hai nhánh (mỗi nhánh dùng index của nó) rồi `ORDER BY last_message_at
DESC, id DESC LIMIT n+1` — **không** `WHERE user_a_id = @me OR user_b_id = @me` (OR giữa hai cột thường ra Seq Scan).

**Xong khi:** `EXPLAIN` của danh sách và lịch sử dùng index (ghi kết quả vào hướng dẫn khối A).

**Kết quả khối A:** schema `messaging` migrate được; domain rule có unit test; ranh giới module xanh với type thật.

---

## B.5 Khối C — Realtime

> **Mục tiêu khối:** một kênh hai chiều **đã xác thực**, tự cắt khi quyền hết hạn, chỉ đẩy tới đúng người — và chạy được
> cả khi có hai bản sao API.

### C1 — Vé realtime trong SharedKernel (Đ-5.9)

**Làm gì:** `SharedKernel/Realtime/`: `IRealtimeTicketStore` (`IssueAsync(sub, role, iat)` → vé; `RedeemAsync(vé)` →
principal data hoặc null), `RedisRealtimeTicketStore` (`SET … EX 30`, `GETDEL`, khóa `rt:ticket:{sha256}`), hằng
`RealtimeTicketDefaults.Scheme`, policy rate limit `realtime-ticket`. Controller `RealtimeTicketsController` ở
`Messaging/Presentation` (`POST /api/v1/realtime/tickets`, `[Authorize]`, `[EnableRateLimiting("realtime-ticket")]`).

**Làm như nào:** vé = 32 byte `RandomNumberGenerator.GetBytes` → base64url. Lưu **băm**, không lưu vé. `iat` lấy từ access
token của request xin vé (để `revoked:user` sau này so được). Redis không có → 503 (không fail-open).

**Xong khi:** unit/integration: xin vé → redeem lần 1 ra đúng `sub`, lần 2 null; sau 30s (TimeProvider giả hoặc TTL Redis
thật) null; Redis chết → 503.

### C2 — Scheme xác thực + `ChatHub` + `IUserIdProvider` (Đ-5.8, Đ-5.9)

**Làm gì:** `RealtimeTicketAuthenticationHandler` (chỉ xử lý khi `Path` bắt đầu `/hubs`, đọc `access_token` từ query, gọi
`RedeemAsync`, kiểm `IsRevokedAsync`, dựng principal `sub` + `role`); `AddSignalR()`; `SubClaimUserIdProvider :
IUserIdProvider`; `ChatHub` (khung rỗng + `[Authorize(AuthenticationSchemes = …)]`) trong `Messaging/Presentation`;
`MapHub<ChatHub>("/hubs/chat")`.

**Làm như nào:** đăng ký scheme bằng `AddAuthentication().AddScheme<…>(RealtimeTicketDefaults.Scheme, …)` **không** đổi
default scheme (bearer vẫn là mặc định cho REST). Kiểm chiều ngược lại: `JwtBearer` không được đọc query `access_token`
(không thêm `OnMessageReceived`).

**Xong khi:** `HUB-01..06` xanh.

**Cạm bẫy:** SignalR mặc định dò `ClaimTypes.NameIdentifier` — không đăng ký `IUserIdProvider` riêng thì `Clients.User`
im lặng không gửi cho ai (Đ-5.8). Test `HUB-09`/`HUB-20` bắt lỗi này.

### C3 — Hub filter: thu hồi, tuổi thọ, rate limit (Đ-5.10)

**Làm gì:** `RevocationHubFilter : IHubFilter` (mỗi lời gọi: `IsRevokedAsync` → `Abort` nếu bị thu hồi); tuổi thọ tối đa
15 phút (timer theo kết nối trong `OnConnectedAsync`, hủy trong `OnDisconnectedAsync`); giới hạn tần suất `SendMessage`
theo user (limiter in-memory theo instance, 60 lượt/phút — đủ cho người thật, chặn script).

**Xong khi:** `HUB-04b`, `HUB-10` xanh; vượt tần suất → HubException `rate-limited`.

### C4 — Backplane Redis theo cấu hình (Đ-5.13) *(bước 7)*

**Làm gì:** `Realtime:Backplane:Enabled` + `AddStackExchangeRedis` với kết nối riêng, `ChannelPrefix` theo môi trường;
thêm khóa vào `deploy/.env.example` với giá trị mặc định `false`.

**Làm như nào:** thử ở local: hai container `api` sau một Caddy tạm (khuôn `deploy/Caddyfile`), A nối instance 1, B nối
instance 2 (ép bằng cổng riêng hoặc tắt lần lượt).

**Xong khi:** tin A gửi tới B ở instance kia; tắt backplane → không tới (chứng minh test có nghĩa). Ghi kết quả vào hướng
dẫn khối C để GĐ7 khối E dùng.

### C5 — Presence tối thiểu (Đ-5.11) *(cắt được)*

**Làm gì:** `IPresenceReader` ở SharedKernel; hiện thực sorted set + `BackgroundService` gia hạn 30s; `OnConnected`/
`OnDisconnected` của `ChatHub` ghi/xóa.

**Xong khi:** test: kết nối → online; ngắt → offline; giả lập instance chết (không gọi `OnDisconnected`) → offline sau
≤ 90s. Nếu cắt: ghi "hoãn tới GĐ6" vào Mục 2 và checklist, **không** xóa dòng.

### C6 — Không log vé, không log nội dung (Đ-5.18)

**Làm gì:** kiểm Serilog request logging không ghi query của `/hubs/*`; hạ mức log `Microsoft.AspNetCore.SignalR` và
`Microsoft.AspNetCore.Http.Connections` xuống `Warning` ở Staging/Production; test `LOG-01` + một test bắt tay hub với
`CapturingLogSink` khẳng định vé không xuất hiện trong log.

**Xong khi:** hai test log xanh; đã thử cho đỏ (log thẳng `Context.Request.QueryString` một lần).

---

## B.6 Khối D — Endpoint và nghiệp vụ

> **Mục tiêu khối:** hợp đồng Mục 8 thành hệ thống chạy thật, khớp từng mã lỗi, và **cùng một** nghiệp vụ cho hai cửa vào.

### D0 — Nền chung

`MessagingApiGroup` (`Name = "messaging-v1"`); mọi controller khai `[ApiExplorerSettings(GroupName = …)]` ngay từ file đầu;
`[ProducesResponseType]` đủ mã; validator FluentValidation đăng ký trong `AddMessagingModule`; `ConversationAccess`
(Mục 6.2) — **chỗ duy nhất** kiểm thành viên; `MessagingHubErrors` dịch `Result` → mã lỗi hub (Mục 8.2).

**Xong khi:** `Every_controller_must_declare_a_swagger_group` xanh; Swagger thấy nhóm `messaging-v1`.

### D1 — `POST /conversations`

Tầng 2 `message.send`; tự mình → 400; `IUserDirectory` không có → 404; `AreFriendsAsync` false → 403; get-or-create
(Đ-5.2) → 201 (vừa tạo) / 200 (đã có). **Xong khi:** `CONV-01..04`, `TC-A07` xanh.

### D2 — `GET /conversations`

Keyset hai phía (A5); hydrate một lô: `IUserDirectory.GetManyAsync(peerIds)` **một lần**, tin cuối lấy theo lô
`last_message_id`, ký URL avatar; `unreadCount` theo Đ-5.14. **Xong khi:** `LIST-01`, `LIST-02` xanh.

### D3 — `GET /conversations/{id}` + `GET /conversations/unread-count`

Chi tiết qua `ConversationAccess` + `canSend` sống (Đ-5.3); tổng chưa đọc một câu SQL. **Xong khi:** `TC-A04`, `MSG-10`,
`FRIEND-01` (phần `canSend`) xanh.

### D4 — `GET /conversations/{id}/messages`

`cursor` (seq giảm dần) hoặc `afterSeq` (tăng dần), không cả hai; `MessageCursor` chép khuôn `PostCursor`. **Xong khi:**
`MSG-03..05`, `TC-A04-messages` xanh; `EXPLAIN` có `Index Scan Backward`.

### D5 — `MessageSendService` + `POST /conversations/{id}/messages` ⭐

**Làm gì:** Đ-5.4 + Đ-5.5 + Đ-5.14 trong **một** service; controller là vỏ mỏng (201/200/409). Counter
`socialapp_messages_sent_total` (nếu GĐ7 C1 đã có `prometheus-net`; chưa có thì để TODO trỏ GĐ7 C2).

**Làm như nào:** viết `TC-A04-send`, `TC-A07-send`, `TC-A07b` **trước** (đỏ), rồi mới viết kiểm quyền (nếp B3 GĐ2). Câu
SQL gửi tin dùng `FromSql`/`ExecuteSql` có tham số — không nối chuỗi. Bắt `23505` trên hai UQ → đọc lại theo `clientMsgId`.

**Xong khi:** `MSG-01/02/06/07`, `MSG-C1..C3`, matrix phần gửi tin xanh; bảng đột biến ghi trong commit.

**Tự rà trước commit:** kiểm BR-09 **ngoài** transaction · push/event **sau** `COMMIT` · không log `content`.

### D6 — `POST /conversations/{id}/receipts`

`GREATEST` + kẹp; trả 204; nếu mốc thật sự tăng thì đẩy `ReceiptUpdated` (sau `COMMIT`). **Xong khi:** `RCP-01..03`,
`RCP-C1`, `TC-A04-receipt` xanh.

### D7 — Event `MessageSent` (chỉ log) + histogram đẩy

`IMessagingEvents` + hiện thực log (Đ-5.15); đo `COMMIT → SendAsync`. **Xong khi:** log có dòng `MessageSent` với id, **không**
có nội dung.

### D8 — Hai phương thức hub

`ChatHub.SendMessage` và `ChatHub.SendReceipt` gọi đúng service của D5/D6; `actorId = Context.UserIdentifier`; tầng 2
`message.send` qua `IPermissionCache`; đẩy `MessageReceived` tới `Clients.Users(recipient, sender)`. **Xong khi:**
`HUB-07..09`, `HUB-20..21` xanh.

### D9 — Rà RFC 7807 + mã lỗi hub

Đối chiếu từng mã trong `messaging-v1.yaml` với thứ code trả; từng mã lỗi hub với bảng Mục 8.2; không thông điệp nào chứa
id, nội dung hay tên kiểu. **Xong khi:** `MessagingContractTests` và `ChatHubContractTests` xanh hai chiều.

---

## B.7 Khối B — Test và cổng CI

> **Mục tiêu khối:** biến mọi luật của Phần A thành thứ **chặn merge**, giữ tinh thần GĐ1: khung không sửa, chỉ thêm dòng.

### B1 — Harness

Thêm Messaging vào thứ tự migrate cố định của `PostgresFixture.SeededContentDatabaseAsync` và `ModulesApiFactory`; helper
dựng "A và B là bạn" (API SocialGraph nếu đã có, SQL nếu chưa — Mục 6.3); `RealtimeTestClient` dựng `HubConnection` trên
`TestServer` với cấu hình Đ-5.16. Đo lại thời gian nhóm test Postgres; vượt ~3 phút thì tách collection (ngưỡng GĐ1).

### B2 — Dòng AuthZ matrix (Mục 6.3), viết cho đỏ trước

Chỉ sửa `AuthZMatrix.cs`. Bảng đột biến trong commit: bỏ `ConversationAccess` → `TC-A04*` đỏ; bỏ `AreFriendsAsync` →
`TC-A07`, `TC-A07-send` đỏ; `FriendshipReader` luôn `false` → `TC-A07b` đỏ; đổi 403 thành 404 → đỏ.

### B3 — Test đồng thời (Mục 10.2)

`MSG-C1..C3`, `RCP-C1`, `CONV-02`. Chạy **20 lần liền** trước khi tin (`for i in $(seq 20); do dotnet test --filter …; done`)
— test đồng thời xanh một lần không chứng minh gì.

### B4 — Hai cổng hợp đồng (Mục 8.3, 10.5)

`MessagingContractTests` + `ChatHubContractTests` + hai dòng csproj. Thử cho đỏ: thêm một status code vào controller không
sửa yaml → đỏ; đổi tên trường trong `examples.json` → đỏ cả backend lẫn frontend.

### B5 — Test hub `HubAuthZTests` (Mục 6.4)

Category `AuthZ`. Thử cho đỏ: bỏ `GETDEL` (dùng `GET`) → `HUB-02` đỏ; bỏ filter → `HUB-04b` đỏ; `Clients.All` → `HUB-09` đỏ.

---

## B.8 Khối E — Lane frontend

> **Mục tiêu khối:** chat thật cho người dùng thật — tin tới gần như tức thời, không mất, không lặp — và là công cụ đo GOAL-02.

Luật đặt file (luật frontend Mục 2, Đ-E13): `features/chat/`, `lib/realtime/`, `lib/api/messaging-api.ts`. Không
`features/` nào import chéo; ghép ở `app/`. Viết `docs/giai-doan-5/huong-dan-khoi-e-*.md` khi bắt đầu khối, chốt các câu
hỏi Q-E\* ở đầu (nếp GĐ2/GĐ4).

### E1 — Codegen, client, ngữ cảnh lỗi

**Làm gì:** `pnpm gen:api` → `lib/api/messaging/schema.d.ts`; alias ở `lib/api/types.ts`; `lib/api/messaging-api.ts`;
`errorMessage` thêm ngữ cảnh `conversation-open`, `conversation-read`, `message-send`, `receipt`, `realtime-ticket`
(403 mang nghĩa khác nhau trên từng endpoint — nếp Q-E4). Fixture msw chép `example` của yaml, gắn kiểu `satisfies`.

**Xong khi:** typecheck xanh; test `errorMessage` cho từng ngữ cảnh mới.

### E2 — `lib/realtime/`: kết nối, vé, trạng thái (Đ-5.16, Đ-5.17, Đ-E18)

**Làm gì:** `hub-url.ts` · `chat-hub-contract.ts` (kiểu viết tay theo `chat-hub-v1.md`) · `chat-connection.ts` (một kết nối
cho cả app; `accessTokenFactory` gọi `POST ${BFF_ROUTES.api}/realtime/tickets` qua `request()`; `withAutomaticReconnect`
với lịch lùi dần; hết 3 lần liền → trạng thái `fallback`; module store + `useSyncExternalStore`; dừng khi nhận
`BroadcastChannel` đăng xuất) · hook `useChatConnection()`. Nhánh dev của CSP (Đ-E18) + ghi Đ-E18. Luật ESLint cấm import
`@microsoft/signalr` ngoài `lib/realtime/**`.

**Xong khi:** Vitest với `HubConnection` giả: chuyển trạng thái đúng; mỗi lần nối lại gọi xin vé đúng một lần; đăng xuất →
`stop()`; một ca `<StrictMode>` không mở hai kết nối. Ở dev thật: nối được `localhost:5259/hubs/chat`.

### E3 — Danh sách hội thoại + badge chưa đọc

**Làm gì:** `features/chat/conversation-list.tsx` (cursor, "Xem thêm", trạng thái rỗng "Chưa có cuộc trò chuyện nào — nhắn
cho một người bạn từ trang cá nhân của họ"); `features/chat/unread-badge.tsx` (gọi `unread-count`, cập nhật khi có
`MessageReceived`/`ReceiptUpdated`); route `app/(app)/(with-profile)/messages/page.tsx`; ráp badge + link "Tin nhắn" vào
`AppHeader` qua `actions` ở `(app)/layout.tsx`.

**Xong khi:** tin mới tới → dòng hội thoại nhảy lên đầu + badge tăng, không nạp lại trang.

### E4 — Cửa sổ chat + lịch sử cuộn ngược

**Làm gì:** `features/chat/chat-window.tsx` + hook `use-message-history.ts` (chép khuôn `use-post-page.ts`: cursor mờ,
`nextCursor === null`, khử trùng theo `messageId`, chống phản hồi cũ); cuộn lên nạp trang cũ **giữ nguyên vị trí nhìn** (bù
`scrollTop` bằng chênh lệch `scrollHeight`); tin mới tới khi đang ở đáy → cuộn xuống, đang đọc tin cũ → hiện nút "Tin mới ↓";
route `messages/[conversationId]/page.tsx`.

**Xong khi:** 200 tin cuộn lên hết không nhảy vị trí; thấy chỗ hở `seq` → gọi `afterSeq` (test Vitest).

### E5 — Composer: optimistic, `clientMsgId`, Thất bại / Thử lại

**Làm gì:** `features/chat/message-composer.tsx`: 1–2000 ký tự (cùng ngưỡng server — Đ-E5), Enter gửi / Shift+Enter xuống
dòng; tin optimistic "đang gửi" → ACK thay bằng tin server (khớp theo `clientMsgId`); không ACK sau 10 giây hoặc lỗi
`unavailable` → **Thất bại** + "Thử lại" (cùng `clientMsgId`, qua hub nếu đang nối, qua REST nếu không); `not-friends` →
khóa ô soạn + thanh chỉ đọc.

**Xong khi:** Vitest: Thử lại dùng **cùng** `clientMsgId`; gửi lại nhận `replayed:true` → **một** tin trên màn.

### E6 — Trạng thái Sent/Delivered/Seen, "Đã xem", nút "Nhắn tin"

**Làm gì:** suy trạng thái tin của mình từ `peerDeliveredSeq`/`peerSeenSeq` + `ReceiptUpdated` (Đ-5.6) — hiện dưới tin cuối
của mình ("Đã gửi" / "Đã nhận" / "Đã xem"); gửi `SendReceipt(delivered)` khi nhận tin, `SendReceipt(seen)` khi hội thoại
đang mở **và** `document.visibilityState === "visible"`; `StartChatButton` (`POST /conversations` → chuyển trang) ráp vào slot
`actions` của `PublicProfile` ở `app/users/[userId]/page.tsx`, chỉ hiện khi đang là bạn.

**Xong khi:** hai trình duyệt thấy trạng thái đổi đúng thứ tự; tab ẩn thì chỉ "Đã nhận".

**Phụ thuộc:** nút "Nhắn tin" cần GĐ4 E5 (slot + trạng thái quan hệ) — làm sau cùng (Mục 9.4).

### E7 — Fallback và kết nối lại (Đ-5.12)

**Làm gì:** trạng thái `fallback` → gửi bằng REST, hỏi `afterSeq` mỗi 3 giây cho hội thoại đang mở, danh sách + badge mỗi
30 giây; dải "Đang kết nối lại — tin vẫn gửi được"; `onreconnected` → lấp chỗ hở + làm mới danh sách/badge; hub nối lại →
dừng hỏi.

**Xong khi:** Playwright `setOffline(true)` 10 giây rồi bật → không mất, không lặp; chặn `/hubs/*` → vẫn chat được.

### E8 — Công cụ đo p95 (Mục 10.7)

**Làm gì:** `features/chat/latency-probe.ts`, bật bằng `?latency=1` (không hiện với người dùng thường); ghi `t0` theo
`clientMsgId` ở phía gửi, `t1` sau `requestAnimationFrame` ở phía nhận; nút "Xuất JSON"; kịch bản Playwright
`e2e/chat-latency.spec.ts` chạy trỏ staging (`PLAYWRIGHT_BASE_URL`), 200 tin nhịp 1 giây, in p50/p95/p99.

**Xong khi:** chạy ở local ra số; code probe **không** vào bundle khi không bật (import động) — cổng "bundle sạch" vẫn xanh.

### E9 — Vitest + Playwright cho lát cắt

Theo Mục 10.6. Playwright local, `workers: 1`, kết quả dán vào PR kèm bản Chrome (Đ-E8).

---

## B.9 Khối F — Cổng đóng

> **Mục tiêu khối:** chứng minh trên hệ thống thật, hai người thật, không phải trên máy local và không phải trên mock.

### F1 — Deploy staging

Trước merge: apache có `/hubs` (Mục 9.6), `.env` staging có `Realtime__Backplane__Enabled=false`. Sau deploy: service
`migrate` xanh cho **năm** module; `/health/ready` = 200; kết nối hub từ trình duyệt thành công.

### F2 — E2E lát cắt trên staging, hai tài khoản là bạn

Chạy hết mục "Lát cắt dọc" của Mục 12. Bằng chứng lưu `docs/giai-doan-5/bang-chung/`: ảnh hai cửa sổ (tin + "Đã xem"); ảnh
badge sau khi offline; ảnh thanh "chỉ đọc"; ảnh tab Network (một WebSocket, không JWT); `grep` access log không có vé.

### F3 — Đo p95 gửi→nhận và viết báo cáo ⭐

Theo Mục 10.7, lưu `docs/giai-doan-5/bao-cao-p95-chat.md`. **Không đạt vẫn phải có báo cáo**, kèm phân rã: phần server
(histogram D7) và phần đường truyền. Không đạt thì báo chủ dự án trong ngày — đừng để sang GĐ8.

### F4 — Checklist Mục 12 + Definition of Done Mục 11

Tick từng dòng có bằng chứng. Dòng chờ thao tác trên server thì ghi "chờ server", **không xóa dòng** (nếp F4 GĐ2).

### F5 — Đóng băng + bàn giao

Đóng băng `messaging-v1.yaml` và `chat-hub-v1.md` cho phạm vi GĐ5; liệt kê phần hoãn có địa chỉ (Mục 2); bàn giao cho GĐ6
bốn thứ nó cần: **vé + scheme realtime dùng chung** (hub thông báo dùng lại), **`IPresenceReader`** (quyết định gửi thông báo),
**event `MessageSent`** (điểm nối thông báo "tin nhắn mới"), **filter `revoked:user` ở hub** (bên ghi của GĐ6 có tác dụng
ngay). Cập nhật `README.md` Mục 1.

---

## B.10 Thứ tự thực thi, đường găng, và thứ tự cắt

**Đường găng:** `C0` → `A1 → A2 → A3` → `C1 → C2` → `D5 → D8` → `E2 → E4 → E5` → `F1 → F2 → F3`

`C0` phải xong **trong ngày thứ nhất sau cổng mở**. Đây là đầu việc rủi ro nhất và là đầu việc duy nhất có thể buộc đổi
phương án (ISS-01).

**Thứ tự cắt khi trễ** — từ trên xuống, dừng khi kịp:

| Thứ tự | Cắt gì | Còn lại vẫn đạt |
|---|---|---|
| 1 | Presence (C5) | GĐ5 không dùng presence để đẩy tin; GĐ6 nhận lại việc này (ghi "hoãn tới GĐ6") |
| 2 | Nút "Tin mới ↓" và giữ vị trí khi cuộn (phần trau chuốt của E4) | Lịch sử vẫn đọc được đủ |
| 3 | Thử backplane hai instance (C4) — **chỉ khi** chủ dự án xác nhận GĐ7 khối E đã bị cắt | Một instance không cần backplane |
| 4 | Nút "Nhắn tin" trên hồ sơ (phần E6) — mở hội thoại từ màn `/messages` bằng danh sách bạn bè | FR-015/016 vẫn đủ |
| **Không cắt** | Xác thực hub bằng vé + filter thu hồi · BR-06/BR-09 ở **cả hai** cửa · idempotency `clientMsgId` · `seq` không lỗ · fallback REST · **báo cáo p95** (kể cả khi không đạt) | "Quan trọng" + "Bắt buộc phi chức năng" của bảng ưu tiên kế hoạch gốc |

**Sáu thứ không test tự động nào bắt được — tự rà trước khi mở PR** (không có người review chéo cố định; đưa danh sách này
cho người duyệt PR):

1. `actorId` từ token (`GetUserId()` ở REST, `Context.UserIdentifier` ở hub) — **không** từ route, body hay tham số hub.
2. Kiểm BR-09 **ngoài** transaction; push, event và `ReceiptUpdated` **sau** `COMMIT`.
3. Không log `content`, không log vé, không log query string của `/hubs/*`.
4. Câu SQL gửi tin tham số hóa, không nối chuỗi.
5. Không có nhánh `if role == ADMIN` ở tầng 3 — Admin không đọc được tin nhắn của người khác.
6. Không có `Clients.All`, không group nào tên do client gửi lên.

## B.11 Mục tiêu từng khối — chúng cộng lại thành cái gì

| Khối | Mục tiêu | Thiếu nó thì mất gì |
|---|---|---|
| **C0** | Biết sớm đường WebSocket thật có thông không | GOAL-02 lộ ra trượt ở cổng đóng, không còn ngày để ứng phó |
| **A** | Module đứng độc lập, `seq` và idempotency do DB giữ | Mất tin / trùng tin — lỗi người dùng thấy ngay và không tự lành |
| **B** | Luật Phần A thành cổng chặn merge, cả REST lẫn hub | IDOR qua hub chỉ lộ ra ở GĐ8, lúc sửa đã đắt |
| **C** | Kênh realtime đã xác thực, tự cắt khi hết quyền, chạy được khi scale | Chat "chạy" nhưng người đã đăng xuất vẫn nghe được tin |
| **D** | Hợp đồng thành hệ thống chạy thật, một nghiệp vụ cho hai cửa | Không có sản phẩm |
| **E** | Chat UI thật + công cụ đo | GOAL-02 không có cách nghiệm thu (kế hoạch gốc: "không đảo được") |
| **F** | "Xong" thành sự kiện kiểm chứng được trên staging | "Xong" thành cảm giác |

## B.12 Mục tiêu của GĐ5

### Ba điều kiện để tuyên bố GĐ5 xong

Thiếu bất kỳ điều nào thì **chưa xong**, dù code đã chạy:

1. **Một báo cáo p95 gửi→nhận đo trên staging bằng chat UI thật**, hai trình duyệt, ≥ 200 mẫu — có số, kể cả khi không đạt.
2. **Matrix Mục 6.3 và test hub Mục 6.4 xanh trên CI**, và đã từng đỏ khi cố tình bỏ kiểm thành viên, bỏ kiểm bạn bè, bỏ
   `GETDEL`.
3. **Bốn AC của US-015 đã chạy trên staging** với hai tài khoản thật, có bằng chứng lưu trong `bang-chung/`.

### GĐ5 để lại gì cho GĐ6–GĐ8

| Di sản | Ai thừa hưởng |
|---|---|
| Vé realtime + scheme `RealtimeTicket` trong SharedKernel | **GĐ6** — hub thông báo dùng lại nguyên, không viết lại xác thực |
| `IPresenceReader` | **GĐ6** — gửi thông báo "tin nhắn mới" chỉ khi người nhận offline (UC-15 A1) |
| Event `MessageSent` sau `COMMIT` | **GĐ6** — điểm nối thông báo, không phải lần tìm lại luồng |
| `RevocationHubFilter` + tuổi thọ kết nối | **GĐ6** — khóa tài khoản / hạ quyền cắt được cả kết nối hub đang mở |
| Backplane đã thử hai instance | **GĐ7** khối E — bật một cờ cấu hình, không phải dò lỗi lúc lên production |
| Histogram `socialapp_message_push_seconds` + báo cáo p95 | **GĐ7** (Grafana) · **GĐ8** (chạy lại đo chính thức, so với mốc GĐ5) |
| Đoạn apache `/hubs` + luật không log vé | **GĐ7** — production kế thừa nguyên |
| Nợ có địa chỉ: xóa/ẩn danh tin nhắn khi xóa tài khoản | **GĐ8** (NĐ 13/2023) |

**Một câu để nhớ:** các giai đoạn trước hỏi *"người này có được làm việc này không"* một lần cho mỗi request; GĐ5 là lần
đầu hệ thống phải tiếp tục hỏi câu đó **trong suốt một kết nối sống hàng giờ** — và phải trả lời đúng cả khi hai tin tới cùng
một mili-giây.
