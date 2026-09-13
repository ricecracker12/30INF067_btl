# Identity · Presentation

Tầng HTTP của module Identity: controller + hợp đồng API. Module sở hữu bề mặt HTTP của chính nó —
`SocialApp.Api` chỉ là host nhận assembly này qua `AddApplicationPart` và dựng một nhóm Swagger
riêng cho nó.

| File | Là gì |
|---|---|
| `identity-v1.yaml` | **Hợp đồng API** chốt ở cổng mở GĐ1. Nguồn sự thật, không phải Swagger runtime |
| `*Controller.cs` | Hiện thực (khối D GĐ1) — `[ApiExplorerSettings(GroupName = IdentityModuleExtensions.ApiGroup)]` |

## Ranh giới

Chỉ tầng này được chạm `Microsoft.AspNetCore.Mvc`. `Domain`, `Application`, `Infrastructure` phải
sạch HTTP — `PresentationBoundaryTests` giữ luật này, song song với `PersistenceBoundaryTests` giữ
luật "chỉ Infrastructure chạm EF".

Lý do cần lưới: ASP.NET Core đã có sẵn trong module (kéo qua `SharedKernel` và
`FluentValidation.AspNetCore`), nên `IActionResult`, `[HttpGet]`, `ControllerBase` compile được ở
**mọi** file trong module. Trả `NotFound()` từ một domain service thì vẫn chạy đúng, và không ai bắt
được trong code review. Giá phải trả đến muộn: logic nghiệp vụ dính chặt vào HTTP, không unit test
được nếu không dựng cả pipeline.

## Hợp đồng API

**Đổi hợp đồng → sửa `identity-v1.yaml` trong cùng commit với code.** Không phải quy ước lịch sự:
`IdentityContractTests` so file này với `/swagger/identity-v1/swagger.json` sinh từ code và fail CI
nếu lệch (tập `path × method`, tập status code mỗi operation, required field). Không so
description/example — sửa một chữ mô tả không làm đỏ test.

Đó là thứ thay cho việc "đối chiếu Swagger với stub bằng mắt" ở cổng đóng: stub = điều đã thỏa
thuận, Swagger = điều code thật sự làm, CI = trọng tài.

Kiểm tra tại chỗ trước khi commit:

```bash
npx @redocly/cli lint src/Modules/Identity/Presentation/identity-v1.yaml
dotnet test --filter "Category=Contract"
```

Hai warning của redocly đã biết và chấp nhận: `info-license` (đồ án nội bộ) và
`no-server-example.com` (server dev là `localhost`, cố ý). **Không có error** — đó mới là thứ phải
giữ xanh.

## Lane frontend dùng file này thế nào

```bash
# chạy từ frontend/ — đã kiểm chứng với openapi-typescript 7.13.0
npx openapi-typescript ../src/Modules/Identity/Presentation/identity-v1.yaml \
    -o src/lib/api/schema.d.ts
```

`RoleCode` ra union `'USER' | 'MODERATOR' | 'ADMIN'`, body là interface có `required` đúng — nên đổi
hợp đồng mà quên sửa FE là **compile lỗi**, không phải lỗi runtime phát hiện muộn ở staging. Mock MSW
dựng từ chính các `example` trong file.

Hai ràng buộc bắt buộc phía client, đã ghi trong `info.description` của hợp đồng:

- **`credentials: 'include'` ở mọi lời gọi.** Thiếu là trình duyệt im lặng không gửi cookie refresh
  và `/auth/refresh` luôn 401 mà không có manh mối nào chỉ ra nguyên nhân.
- **Interceptor 401→refresh phải single-flight.** Nhiều 401 đồng thời chỉ được gọi `/auth/refresh`
  một lần, số còn lại xếp hàng chờ. Không làm vậy thì chính FE kích hoạt reuse detection của server
  và người dùng bị đăng xuất oan — triệu chứng trông hệt lỗi backend.

## Bảy quyết định đứng sau hợp đồng này

Chốt ở cổng mở GĐ1. Nguồn đầy đủ: `docs/ke-hoach-trien-khai.md` GĐ1 và `docs/giai-doan-1.md` Mục 3.
Bảng dưới chỉ ghi **phần hiện ra trong hợp đồng**.

| # | Quyết định | Hiện ra ở đâu |
|---|---|---|
| 1 | JWT claim `role` mang `code` **chuỗi** cho mọi vai trò; `role_id` số không ra khỏi DB. Thu hồi bằng `revoked:user` + `iat` | `RoleCode` là union 3 giá trị; `401` bao gồm cả token bị thu hồi |
| 2 | Admin short-circuit ở **tầng 2**, tuyệt đối không ở tầng 3 | Không lộ ra API. Là lý do `ADMIN` không có dòng nào trong `role_permissions` |
| 3 | `roles` tách `code` (bất biến) + `display_name` (sửa được) | `GET /me` trả **cả hai**: `role` để so logic, `roleDisplayName` để hiển thị |
| 4 | Bổ sung `refresh_tokens.family_id` | Cơ chế đằng sau `401` của reuse detection và `/auth/logout` (thu hồi cả family) |
| 5 | Không dùng cột `roles.is_system` | `UserStatus` giữ đủ 4 giá trị của `ck_users_status` để GĐ6/GĐ8 không mở lại hợp đồng |
| 6 | Refresh token trong cookie `HttpOnly`; access token giữ **trong memory** | `/auth/login` + `/auth/refresh` trả `{accessToken, expiresIn}` + `Set-Cookie`; **`/auth/refresh` không nhận body**. Thuộc tính cookie là **một phần hợp đồng** |
| 7 | CORS bật ngay ở GĐ1 | FE phải `credentials: 'include'`; server `AllowCredentials()` với origin liệt kê tường minh — chuẩn CORS cấm dùng kèm `AllowAnyOrigin()` |

Quyết định 6 là **quyết định hợp đồng API, không phải quyết định frontend**: nó đổi chữ ký của ba
endpoint. Chốt ở cổng mở chính vì thế — để muộn là phải mở lại hợp đồng vừa đóng băng.
