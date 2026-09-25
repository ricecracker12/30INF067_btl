# Hợp đồng hub `notification-hub-v1` — thông báo realtime (GĐ6 · FR-018, C6)

> Hợp đồng của `docs/giai-doan-6/giai-doan-6.md` Mục 8.4, Đ-6.18. Hub **không** có trong Swagger nên file này là nguồn sự thật cho
> lane frontend. Ví dụ payload ở `notification-hub-v1.examples.json` cùng thư mục — **nguồn chung** cho test backend
> (`NotificationHubContractTests`, category `Contract`) và fixture frontend. Đổi tên trường ở đây mà không đổi ví dụ → cả hai phía đỏ.
>
> Tên file cố ý không khớp glob `*-v1.yaml` của codegen FE và `ContractGateCoverageTests` (khuôn `chat-hub-v1`).
>
> **Luật sửa:** đổi hợp đồng hub → sửa file này + `examples.json` + code hub **trong cùng commit**.
>
> **Hub là một nguồn đẩy thêm, không thay REST.** Không nối được hub (hay chưa nối) thì FE hỏi `GET /notifications/unread-count` mỗi
> 30 giây + khi tab lấy lại focus (Đ-6.18 — đường lùi vĩnh viễn, không phải tạm). Hợp đồng dữ liệu giống hệt hai chế độ.

## 1. Kết nối

| Mục | Giá trị |
|---|---|
| Đường | `/hubs/notifications` (cùng origin; dev: `http://localhost:5259/hubs/notifications`) |
| Transport | **Chỉ WebSockets**, client bắt buộc `skipNegotiation: true` (như `chat-hub-v1`, Đ-5.16) |
| Giao thức | JSON (`JsonHubProtocol`), tên trường camelCase |
| Xác thực | `?access_token=<vé>` — vé xin bằng `POST /api/v1/realtime/tickets` (hợp đồng `messaging-v1`), sống 30 giây, **dùng một lần**. Mỗi kết nối một vé: tab mở cả hub chat lẫn hub thông báo xin **hai** vé |
| Tuổi thọ | Server cắt kết nối sau **15 phút**; client tự kết nối lại với vé **mới** (Đ-5.10) |
| Thu hồi | Tài khoản bị khóa / đổi vai trò → kết nối bị cắt như hub chat (`RevocationHubFilter` toàn cục) |

Kết nối hub thông báo cũng tính là **online** trong presence của GĐ5 (Đ-5.11): người đang mở app không nhận thông báo `message` (Đ-6.17).

## 2. Client → server

**Không có phương thức nào.** Mọi thao tác (đọc danh sách, đánh dấu đã đọc) đi qua REST `notification-v1`.

## 3. Server → client (sự kiện)

| Sự kiện | Payload | Gửi tới | Khi nào |
|---|---|---|---|
| `NotificationUpserted` | `{ notification: NotificationResponse, unreadTotal: integer }` | Mọi kết nối của **người nhận** thông báo | Sau `COMMIT` của mỗi lần một nhóm được tạo hoặc có sự kiện mới (kể cả đợt mới). Không phát khi tự báo mình (bị bỏ qua), khi đổi loại cảm xúc, khi đánh dấu đã đọc |

- `NotificationResponse` cùng hình dạng với schema cùng tên trong `notification-v1.yaml` — đúng phần tử của `GET /notifications`.
- `unreadTotal` là số nhóm chưa đọc **tuyệt đối** tại thời điểm đẩy — nhận hai lần hay sai thứ tự đều vô hại, FE ghi đè badge.
- `notificationId` là khóa khử trùng: nhóm đã có trong danh sách đang mở → thay phần tử cũ và đưa lên đầu; chưa có → chèn đầu.

## 4. Quy tắc

- **Không bảo đảm tới.** Mất kết nối là mất sự kiện; không có hàng đợi phát lại.
- **Nối lại → nạp lại:** `onreconnected` gọi lại `GET /notifications/unread-count`, và nạp lại trang đầu nếu chuông đang mở.
- Đẩy hỏng ở server (hub lỗi, Redis backplane lỗi) **không** làm mất thông báo: dòng đã lưu, lượt hỏi lại 30 giây sẽ thấy.
