# Hợp đồng hub `chat-hub-v1` — nhắn tin 1-1 realtime (GĐ5 · UC-15)

> Artifact bắt buộc của cổng mở GĐ5 (`docs/giai-doan-5/giai-doan-5.md` Mục 8.2, Mục 9.2 bước 3). Hub **không** có trong
> Swagger nên file này là nguồn sự thật cho lane frontend. Ví dụ payload nằm ở `chat-hub-v1.examples.json` cùng thư mục —
> **nguồn chung** cho test backend (`ChatHubContractTests`, category `Contract`) và fixture frontend
> (`lib/realtime/chat-hub-contract.ts` gắn kiểu bằng `satisfies`). Đổi tên trường ở đây mà không đổi ví dụ → cả hai phía đỏ.
>
> Tên file cố ý không khớp glob `*-v1.yaml` của codegen FE và `ContractGateCoverageTests`.
>
> **Luật sửa:** đổi hợp đồng hub → sửa file này + `examples.json` + code hub **trong cùng commit**.
>
> **Đóng băng (2026-09-24, cổng đóng GĐ5 — F5):** không đổi hình dạng (tên phương thức, sự kiện, trường, mã lỗi) trong phạm vi
> GĐ5. Đổi sau mốc này theo luật **chỉ-thêm** ở cổng mở của giai đoạn cần đổi, hoặc hotfix blocking cả nhóm thống nhất. Hub
> thông báo GĐ6 (`/hubs/notifications`) là hợp đồng RIÊNG — dùng lại vé + scheme, không sửa file này.

## 1. Kết nối

| Mục | Giá trị |
|---|---|
| Đường | `/hubs/chat` (cùng origin; dev: `http://localhost:5259/hubs/chat` — Đ-E18) |
| Transport | **Chỉ WebSockets**, client bắt buộc `skipNegotiation: true` (Đ-5.16) |
| Giao thức | JSON (`JsonHubProtocol`), tên trường camelCase, enum là chuỗi thường |
| Xác thực | `?access_token=<vé>` — vé xin bằng `POST /api/v1/realtime/tickets` (qua `/bff/api/realtime/tickets`), sống 30 giây, **dùng một lần** (Đ-E16, Đ-5.9). JWT đặt vào query **bị từ chối** (401); vé đặt vào `Authorization` của REST **bị từ chối** (401) |
| Keep-alive | Mặc định SignalR: server ping 15 giây, client timeout 30 giây — thấp hơn timeout nhàn rỗi 100 giây của Cloudflare |
| Tuổi thọ | Server cắt kết nối sau **15 phút** (Đ-5.10). Client tự kết nối lại với vé **mới** |
| Thu hồi | Mỗi lời gọi phương thức kiểm `revoked:user`; bị thu hồi → server `Abort()` kết nối (Đ-5.10) |

`accessTokenFactory` của client chạy **đúng một lần** mỗi lần kết nối (vì không có negotiate). Hai lần xin vé cho một lần
kết nối là cấu hình client sai.

## 2. Client → server (invoke, có kết quả)

`HubException.Message` là **mã lỗi** (cột cuối), không phải câu cho người đọc — FE ánh xạ mã sang câu tiếng Việt.

**Dạng thực tế ở client (đo 2026-09-24):** server tắt `EnableDetailedErrors`, nên SignalR bọc thêm câu dẫn — client nhận
`"An unexpected error occurred invoking 'SendMessage' on the server. HubException: forbidden"`. FE lấy **mã sau chuỗi
`HubException: ` cuối cùng**; không có chuỗi đó (lỗi mạng, kết nối đóng giữa lời gọi) thì coi như `unavailable`.

| Phương thức | Tham số (một object) | Kết quả | Mã lỗi |
|---|---|---|---|
| `SendMessage` | `SendMessageArgs { conversationId: uuid, content: string (1–2000, không toàn khoảng trắng), clientMsgId: uuid }` | `SendMessageResult { message: MessageResponse, replayed: boolean }` — đây là **ACK "Đã gửi"** | `forbidden` · `not-friends` · `validation` · `conflict` · `rate-limited` · `unavailable` |
| `SendReceipt` | `SendReceiptArgs { conversationId: uuid, kind: "delivered" \| "seen", upToSeq: integer ≥ 1 }` | `null` | `forbidden` · `validation` |

- `replayed: true` — `clientMsgId` này đã được lưu trước đó với **cùng** nội dung; `message` là **đúng** tin cũ (cùng
  `messageId`, cùng `seq`). Không có `MessageReceived` lần hai (Đ-5.5).
- `conflict` — `clientMsgId` đã dùng cho một nội dung **khác**: bug của client.
- `not-friends` — người gọi là thành viên nhưng hai người không còn là bạn (BR-09): hội thoại chỉ đọc.
- `forbidden` — thiếu quyền `message.send`, hoặc không phải thành viên, hoặc hội thoại không tồn tại (cùng một mã — không lộ
  hội thoại có thật hay không).
- `upToSeq` lớn hơn `seq` cuối của hội thoại → server kẹp, **không** lỗi. Nhỏ hơn mốc hiện tại → không đổi gì.

## 3. Server → client (sự kiện)

| Sự kiện | Payload | Gửi tới | Khi nào |
|---|---|---|---|
| `MessageReceived` | `MessageResponse` | Mọi kết nối của **người nhận và người gửi** | Sau `COMMIT` của mỗi tin **mới** (không phát khi `replayed`) |
| `ReceiptUpdated` | `ReceiptUpdated { conversationId, userId, deliveredSeq, seenSeq }` | Mọi kết nối của **cả hai** thành viên | Sau khi mốc của `userId` thật sự **tăng** |

`MessageResponse` cùng hình dạng với schema cùng tên trong `messaging-v1.yaml`:
`{ messageId, conversationId, senderId, seq, content, clientMsgId, createdAt }`.

## 4. Thứ tự và độ tin cậy — FE lập trình theo đúng năm câu này

1. Server **không** bảo đảm sự kiện tới theo thứ tự `seq`, và **không** bảo đảm tới (mất kết nối là mất sự kiện).
2. `seq` là thứ tự duy nhất đáng tin. Thấy chỗ hở → lấp bằng REST `GET /conversations/{id}/messages?afterSeq=n`.
3. `messageId` là khóa khử trùng. Một tin có thể tới qua ACK, `MessageReceived` và REST — hiện **một** lần.
4. `ReceiptUpdated` mang mốc **tuyệt đối** — nhận hai lần hay sai thứ tự đều vô hại (lấy max).
5. Lỗi `unavailable` hoặc mất kết nối giữa lời gọi `SendMessage` → **không biết** tin đã lưu chưa → gửi lại **cùng**
   `clientMsgId` (qua hub hoặc REST `POST /conversations/{id}/messages`).

## 5. Không có trong hợp đồng này (Mục 2 "Ngoài phạm vi")

Ảnh/tệp trong tin · sửa/thu hồi tin · "đang gõ…" · chấm online · group theo hội thoại (Đ-5.8 — server tự chọn người nhận).
