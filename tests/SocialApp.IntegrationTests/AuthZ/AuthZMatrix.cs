using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SocialApp.IntegrationTests.Harness;

namespace SocialApp.IntegrationTests.AuthZ;

/// <summary>
/// BẢNG của AuthZ matrix (Mục 10.2). Mỗi giai đoạn CHỈ thêm dòng vào đây — không sửa
/// <see cref="AuthZMatrixTests"/>, <see cref="AuthZCase"/> hay <see cref="AuthZApiFactory"/>.
///
/// Kỳ vọng viết tay theo Mục 10.2 và hợp đồng, CỐ Ý không đọc lại PermissionCodes/RoleCodes: test dùng chung
/// nguồn với code thì code sai kiểu gì test cũng sai theo và vẫn xanh. Mã kỳ vọng không lấy từ output.
/// </summary>
public static class AuthZMatrix
{
    /// <summary>
    /// B2 (GĐ5): người lạ D của TC-A07 — id cố định vì <c>Body</c> của dòng là hằng; hồ sơ dựng lại ở ArrangePath (PUT upsert).
    /// Khai TRƯỚC <see cref="Cases"/>: trường static khởi tạo theo thứ tự khai báo — khai sau thì Body của TC-A07 mang
    /// <c>Guid.Empty</c> và dòng nhận 400 thay vì 403 (đã gặp khi thi công B2).
    /// </summary>
    private static readonly Guid NguoiLaD = Guid.Parse("0192f3c1-0000-7000-8000-0000000000dd");

    public static readonly AuthZCase[] Cases =
    [
        // --- GĐ1 (B3). GĐ2 trở đi CHỈ thêm dòng, không sửa file nào khác. ---

        new("TC-A01", "Gọi endpoint bảo vệ, không kèm JWT", "GĐ1",
            Caller.Anonymous, HttpMethod.Get, "/__test/authz/authenticated", HttpStatusCode.Unauthorized),

        // TC-A02 tách đôi: "hết hạn" và "sai chữ ký" là hai nhánh validate khác nhau.
        new("TC-A02-expired", "Token đã hết hạn", "GĐ1",
            Caller.ExpiredToken, HttpMethod.Get, "/__test/authz/authenticated", HttpStatusCode.Unauthorized),

        new("TC-A02-signature", "Token sai chữ ký", "GĐ1",
            Caller.WrongSignature, HttpMethod.Get, "/__test/authz/authenticated", HttpStatusCode.Unauthorized),

        new("RBAC-01", "ADMIN gọi endpoint đòi quyền bất kỳ — qua dù không có dòng role_permissions", "GĐ1",
            Caller.Admin, HttpMethod.Get, "/__test/authz/post-hide", HttpStatusCode.OK),

        new("RBAC-02", "USER gọi endpoint đòi post.hide", "GĐ1",
            Caller.User, HttpMethod.Get, "/__test/authz/post-hide", HttpStatusCode.Forbidden),

        // Bốn mã gốc xanh được với handler "từ chối mọi vai trò trừ Admin" — dòng này bắt loại hỏng đó.
        new("RBAC-02b", "Đối chứng: MODERATOR gọi endpoint đòi post.hide — vai trò thường CÓ quyền thì phải qua", "GĐ1",
            Caller.Moderator, HttpMethod.Get, "/__test/authz/post-hide", HttpStatusCode.OK),

        // RBAC-02b vẫn để lọt handler "vai trò khác USER thì cho qua" — dòng này bắt nó.
        new("RBAC-02c", "Đối chứng: MODERATOR gọi endpoint đòi user.lock — handler phải xét MÃ QUYỀN, không chỉ vai trò", "GĐ1",
            Caller.Moderator, HttpMethod.Get, "/__test/authz/user-lock", HttpStatusCode.Forbidden),

        // C4: fallback policy. Đỏ nếu xóa FallbackPolicy hoặc provider trả null ở GetFallbackPolicyAsync.
        new("DEFAULT-DENY", "Endpoint KHÔNG khai [Authorize] hay [RequirePermission], không kèm JWT", "GĐ1",
            Caller.Anonymous, HttpMethod.Get, "/__test/authz/no-attribute", HttpStatusCode.Unauthorized),

        // C6: khuôn tầng 3. GĐ2 viết TC-A03 CÙNG hình dạng: ArrangePath tạo bài của user B rồi trả
        // /api/v1/posts/{id}, Caller.User, HttpMethod.Patch, Forbidden. Không sửa khung.
        new("OWN-00", "Khuôn tầng 3: user A đọc tài nguyên (probe) của user B", "GĐ1",
            Caller.User, HttpMethod.Get, "/__test/authz/owned/{id của người khác}", HttpStatusCode.Forbidden,
            ArrangePath: _ => Task.FromResult($"/__test/authz/owned/{Guid.NewGuid()}")),

        // D7: endpoint THẬT đầu tiên của matrix — các dòng trên chạy trên probe của assembly test.
        new("TC-A01-me", "GET /me không kèm JWT — endpoint thật đầu tiên của matrix", "GĐ1",
            Caller.Anonymous, HttpMethod.Get, "/api/v1/me", HttpStatusCode.Unauthorized),

        // D6: logout chạm refresh token có chủ. Tầng 3 kiểm ở Auth/LogoutTests.Cookie_cua_nguoi_khac_khong_bi_thu_hoi (quan sát
        // hệ quả — khung matrix không mang cookie). Dòng dưới chỉ canh tầng 1.
        new("TC-A01-logout", "POST /auth/logout không kèm JWT", "GĐ1",
            Caller.Anonymous, HttpMethod.Post, "/api/v1/auth/logout", HttpStatusCode.Unauthorized),

        // --- GĐ2 (B2). Mục 6.3. Kỳ vọng viết tay theo Mục 6.1 + hợp đồng, không lấy từ output. ---
        //
        // TC-A03 trả 403 còn READ-01 trả 404 là CỐ Ý, không phải mâu thuẫn (quy ước 3b, Mục 6.1): thao tác GHI cần
        // ownership trả 403; ĐỌC nội dung có mức hiển thị trả 404 — "không tồn tại" và "không được thấy" phải là cùng
        // một phản hồi, vì 403 ở đây tự nó tố cáo bài có tồn tại. Thấy "lệch" và muốn thống nhất về một mã thì đọc lại
        // Mục 6.1 trước, đừng sửa bảng.

        new("TC-A03", "A sửa bài của B — PATCH tài nguyên của người khác", "GĐ2",
            Caller.User, HttpMethod.Patch, "/api/v1/posts/{id của B}", HttpStatusCode.Forbidden,
            ArrangePath: async a => $"/api/v1/posts/{await TaoBaiCuaNguoiKhacAsync(a, PrivacyCongKhai)}",
            Body: new { body = "Sửa trộm bài của người khác." }),

        new("TC-A03-delete", "A xóa bài của B — DELETE tài nguyên của người khác", "GĐ2",
            Caller.User, HttpMethod.Delete, "/api/v1/posts/{id của B}", HttpStatusCode.Forbidden,
            ArrangePath: async a => $"/api/v1/posts/{await TaoBaiCuaNguoiKhacAsync(a, PrivacyCongKhai)}"),

        // Đ-2.7. Q-B2 là cả lý do dòng này có ArrangePath dù path không phụ thuộc dữ liệu nào: hàm arrange tạo HỒ SƠ cho
        // chính người gọi, để 403 nhận được không thể là "chưa onboarding" (Đ-2.4). Không có bước đó thì bỏ hẳn kiểm
        // tiền tố khóa trong D5 mà dòng này VẪN xanh — đúng định nghĩa của lưới giả.
        new("TC-A03-media", "A đăng bài gắn ảnh nằm dưới tiền tố khóa của B", "GĐ2",
            Caller.User, HttpMethod.Post, "/api/v1/posts", HttpStatusCode.Forbidden,
            ArrangePath: async a =>
            {
                await TaoHoSoAsync(a.Client, a.CallerUserId);
                return "/api/v1/posts";
            },
            Body: new
            {
                body = "Ảnh này không phải của tôi.",
                privacy = PrivacyCongKhai,
                mediaKeys = new[]
                {
                    // Khóa của NGƯỜI KHÁC: Guid cố định, viết thường, đúng pattern `MediaKeyDeclaration.mediaKey` của
                    // content-v1.yaml. Cố định được vì người gọi luôn là một Guid ngẫu nhiên mới — hai id không thể
                    // trùng nhau, và một hằng số đọc được ở đây hơn một giá trị phải lần ngược mới biết là của ai.
                    new
                    {
                        mediaKey = $"posts/{KhoaCuaNguoiKhac:D}/0123456789abcdef0123456789abcdef.jpg",
                        contentType = "image/jpeg",
                        sizeBytes = 1024,
                    },
                },
            }),

        new("TC-A01-posts", "Đăng bài không kèm JWT", "GĐ2",
            Caller.Anonymous, HttpMethod.Post, "/api/v1/posts", HttpStatusCode.Unauthorized,
            Body: new { body = "Không có token.", privacy = PrivacyCongKhai, mediaKeys = Array.Empty<object>() }),

        new("TC-A01-profile", "Sửa hồ sơ không kèm JWT", "GĐ2",
            Caller.Anonymous, HttpMethod.Put, "/api/v1/users/me/profile", HttpStatusCode.Unauthorized,
            Body: new { displayName = "Không có token" }),

        // BR-02 lúc đọc (Đ-2.9, Mục 7.4): bài `private` của người khác → 404, KHÔNG 403. Xem ghi chú quy ước 3b ở trên.
        new("READ-01", "A đọc bài private của B", "GĐ2",
            Caller.User, HttpMethod.Get, "/api/v1/posts/{id của B}", HttpStatusCode.NotFound,
            ArrangePath: async a => $"/api/v1/posts/{await TaoBaiCuaNguoiKhacAsync(a, PrivacyRiengTu)}"),

        // --- GĐ4 (B2). Mục 6.3. Kỳ vọng viết tay theo Mục 6.3 + hợp đồng, không lấy từ output. ---
        // Năm dòng đỏ có chủ đích chờ D2/D3/D7; READ-06 xanh ngay vì A5 đã bật FriendshipReader thật (L8).

        // US-010 AC-04. C có hồ sơ (dù accept không đòi) để 403 chỉ còn MỘT lý do — nếp TC-A03-media / Q-B2.
        new("TC-A03-friend-accept", "C chấp nhận lời mời mà A gửi cho B", "GĐ4",
            Caller.User, HttpMethod.Post, "/api/v1/friends/requests/{A}/accept", HttpStatusCode.Forbidden,
            ArrangePath: async a =>
            {
                var (nguoiGui, nguoiNhan) = (Guid.NewGuid(), Guid.NewGuid());
                await TaoHoSoAsync(a.Client, nguoiGui);
                await TaoHoSoAsync(a.Client, nguoiNhan);
                await TaoHoSoAsync(a.Client, a.CallerUserId);
                await GuiLoiMoiAsync(a.Client, nguoiGui, nguoiNhan);
                return $"/api/v1/friends/requests/{nguoiGui:D}/accept";
            }),

        // Dòng hay bị quên nhất (Mục 6.3): UPDATE thiếu vế requester_id = @other vẫn qua mọi happy path.
        // Lời mời do CHÍNH người gọi gửi — chỉ làm được nhờ CallerUserId (Q-B2).
        new("TC-A03-friend-self-accept", "A tự chấp nhận lời mời chính A gửi cho B", "GĐ4",
            Caller.User, HttpMethod.Post, "/api/v1/friends/requests/{B}/accept", HttpStatusCode.Forbidden,
            ArrangePath: async a =>
            {
                var b = Guid.NewGuid();
                await TaoHoSoAsync(a.Client, a.CallerUserId);
                await TaoHoSoAsync(a.Client, b);
                await GuiLoiMoiAsync(a.Client, a.CallerUserId, b);
                return $"/api/v1/friends/requests/{b:D}/accept";
            }),

        // BR-02 thật (A5): người lạ đọc bài friends → 404. Xanh ngay sau B2 (L8).
        new("READ-06", "Người lạ đọc bài friends của B", "GĐ4",
            Caller.User, HttpMethod.Get, "/api/v1/posts/{id bài friends của B}", HttpStatusCode.NotFound,
            ArrangePath: async a => $"/api/v1/posts/{await TaoBaiCuaNguoiKhacAsync(a, PrivacyBanBe)}"),

        // Đối chứng của READ-06 (nếp RBAC-02b): bạn của B phải thấy. Đỏ ở ArrangePath tới D3 — dựng bạn qua API thật.
        new("READ-06b", "Bạn của B đọc bài friends của B", "GĐ4",
            Caller.User, HttpMethod.Get, "/api/v1/posts/{id bài friends của B}", HttpStatusCode.OK,
            ArrangePath: async a => $"/api/v1/posts/{await TaoBaiBanBeCuaBanAsync(a)}"),

        new("TC-A01-feed", "GET /feed không kèm JWT", "GĐ4",
            Caller.Anonymous, HttpMethod.Get, "/api/v1/feed", HttpStatusCode.Unauthorized),

        new("TC-A01-friends", "Gửi lời mời không kèm JWT", "GĐ4",
            Caller.Anonymous, HttpMethod.Post, "/api/v1/friends/requests", HttpStatusCode.Unauthorized,
            Body: new { userId = Guid.NewGuid() }),

        // --- GĐ5 (B2). giai-doan-5.md Mục 6.3. Kỳ vọng viết tay theo Mục 6.1 + hợp đồng messaging-v1. Bạn bè và hội thoại
        // dựng qua API thật (GĐ4 D2/D3 đã có — bàn giao GĐ4), không INSERT thẳng. Người gọi C có hồ sơ để 403 chỉ còn MỘT lý do.

        // BR-06: người thứ ba đọc/ghi hội thoại của A và B — 403 (không 404, TC-A04 PTTK), cùng phản hồi với "không tồn tại".
        new("TC-A04", "C đọc hội thoại của A và B", "GĐ5",
            Caller.User, HttpMethod.Get, "/api/v1/conversations/{id A-B}", HttpStatusCode.Forbidden,
            ArrangePath: async a => $"/api/v1/conversations/{await HoiThoaiCuaNguoiKhacAsync(a)}"),

        new("TC-A04-messages", "C đọc lịch sử hội thoại A-B", "GĐ5",
            Caller.User, HttpMethod.Get, "/api/v1/conversations/{id A-B}/messages", HttpStatusCode.Forbidden,
            ArrangePath: async a => $"/api/v1/conversations/{await HoiThoaiCuaNguoiKhacAsync(a)}/messages"),

        new("TC-A04-send", "C gửi tin vào hội thoại A-B", "GĐ5",
            Caller.User, HttpMethod.Post, "/api/v1/conversations/{id A-B}/messages", HttpStatusCode.Forbidden,
            ArrangePath: async a => $"/api/v1/conversations/{await HoiThoaiCuaNguoiKhacAsync(a)}/messages",
            Body: new { content = "Chen ngang.", clientMsgId = Guid.Parse("0192f3c1-0000-7000-8000-00000000a04c") }),

        new("TC-A04-receipt", "C đánh dấu đã xem hội thoại A-B", "GĐ5",
            Caller.User, HttpMethod.Post, "/api/v1/conversations/{id A-B}/receipts", HttpStatusCode.Forbidden,
            ArrangePath: async a => $"/api/v1/conversations/{await HoiThoaiCuaNguoiKhacAsync(a)}/receipts",
            Body: new { kind = "seen", upToSeq = 1 }),

        // BR-09: mở hội thoại với người KHÔNG phải bạn (có hồ sơ — để 403 không lẫn với 404).
        new("TC-A07", "A mở hội thoại với người lạ D", "GĐ5",
            Caller.User, HttpMethod.Post, "/api/v1/conversations", HttpStatusCode.Forbidden,
            ArrangePath: async a =>
            {
                await TaoHoSoAsync(a.Client, a.CallerUserId);
                await TaoHoSoAsync(a.Client, NguoiLaD);
                return "/api/v1/conversations";
            },
            Body: new { userId = NguoiLaD }),

        // AC-04: A là THÀNH VIÊN nhưng đã bị hủy kết bạn với B → gửi bị chặn (kiểm lúc gửi, mỗi lần — Đ-5.3).
        new("TC-A07-send", "A gửi tin trong hội thoại với B sau khi hủy kết bạn", "GĐ5",
            Caller.User, HttpMethod.Post, "/api/v1/conversations/{id A-B}/messages", HttpStatusCode.Forbidden,
            ArrangePath: async a => $"/api/v1/conversations/{await HoiThoaiCuaNguoiGoiAsync(a, huyKetBan: true)}/messages",
            Body: new { content = "Sau khi hủy kết bạn.", clientMsgId = Guid.Parse("0192f3c1-0000-7000-8000-00000000a07c") }),

        // Đối chứng BẮT BUỘC (nếp RBAC-02b, READ-06b): matrix chỉ có dòng "bị chặn" thì xanh cả khi AreFriendsAsync luôn false
        // hoặc ConversationAccess từ chối mọi người.
        new("TC-A07b", "Đối chứng: A gửi tin cho B đang là bạn", "GĐ5",
            Caller.User, HttpMethod.Post, "/api/v1/conversations/{id A-B}/messages", HttpStatusCode.Created,
            ArrangePath: async a => $"/api/v1/conversations/{await HoiThoaiCuaNguoiGoiAsync(a, huyKetBan: false)}/messages",
            Body: new { content = "Chào bạn.", clientMsgId = Guid.Parse("0192f3c1-0000-7000-8000-00000000a07b") }),

        new("TC-A01-conversations", "Danh sách hội thoại không kèm JWT", "GĐ5",
            Caller.Anonymous, HttpMethod.Get, "/api/v1/conversations", HttpStatusCode.Unauthorized),

        new("TC-A01-ticket", "Xin vé realtime không kèm JWT", "GĐ5",
            Caller.Anonymous, HttpMethod.Post, "/api/v1/realtime/tickets", HttpStatusCode.Unauthorized),

        // --- GĐ6 (B2, đi cùng commit D làm dòng xanh — L-D7). Mục 6.3. Kỳ vọng viết tay theo Mục 6.1 + admin-v1.yaml. ---
        //
        // Endpoint [PrivilegedEndpoint] fail-closed khi Redis chết → matrix chạy với Redis thật từ D2 (L-D17). Thiếu Redis thì
        // cả dòng "bị chặn" lẫn dòng đối chứng ra 503 — đỏ đúng chỗ, không xanh giả.

        new("TC-A05", "User thường gọi /admin/* — danh sách tài khoản", "GĐ6",
            Caller.User, HttpMethod.Get, "/api/v1/admin/users", HttpStatusCode.Forbidden),

        // Đối chứng bắt buộc: TC-A05 xanh cả khi handler "chặn mọi người", hay khi policy any-of đòi đủ cả ba mã.
        new("TC-A05b", "Đối chứng: Admin đọc danh sách tài khoản", "GĐ6",
            Caller.Admin, HttpMethod.Get, "/api/v1/admin/users", HttpStatusCode.OK),

        // D3: MODERATOR có report.resolve, post.hide — không có user.lock. Id đích không cần tồn tại: tầng 2 chặn trước mọi I/O.
        new("TC-A05-mod-lock", "Moderator khóa tài khoản", "GĐ6",
            Caller.Moderator, HttpMethod.Post, "/api/v1/admin/users/{id}/lock", HttpStatusCode.Forbidden,
            ArrangePath: _ => Task.FromResult($"/api/v1/admin/users/{Guid.NewGuid()}/lock"),
            Body: new { reason = "Thử khóa khi không có user.lock." }),

        // D4: MODERATOR không có role.assign. Dòng thêm ngoài bảng Mục 6.3 — cùng lý do TC-A05-mod-lock, cho action thứ ba.
        new("TC-A05-mod-role", "Moderator đổi vai trò tài khoản", "GĐ6",
            Caller.Moderator, HttpMethod.Put, "/api/v1/admin/users/{id}/role", HttpStatusCode.Forbidden,
            ArrangePath: _ => Task.FromResult($"/api/v1/admin/users/{Guid.NewGuid()}/role"),
            Body: new { roleCode = "USER" }),

        // D5: USER không có role.manage. {USER} = role_id 1 (Mục 5.1 GĐ1) — viết tay, không đọc hằng.
        new("TC-A05-roles", "User sửa quyền một vai trò", "GĐ6",
            Caller.User, HttpMethod.Put, "/api/v1/admin/roles/1/permissions", HttpStatusCode.Forbidden,
            Body: new { permissions = new[] { "post.create" }, confirm = true }),

        // D6 (Đ-6.12): IDOR theo chiều ĐỌC — "thấy được mới báo được". 404, KHÔNG 403 (quy ước 3b, như READ-01): 403 tự nó tố
        // cáo bài riêng tư có tồn tại. POST /reports không đặc quyền (B.10 #8) nên không phụ thuộc Redis của matrix.
        RepIdor(),

        // D7b (Mục 8.1): hàng đợi là cửa vào đường DUY NHẤT Moderator đọc nội dung không công khai. USER không có report.resolve.
        // Audit của lần từ chối này (AUD-03) ở ReportQueueTests — matrix chỉ so status.
        new("TC-A06-queue", "User thường đọc hàng đợi kiểm duyệt", "GĐ6",
            Caller.User, HttpMethod.Get, "/api/v1/reports", HttpStatusCode.Forbidden),

        // D7c (Đ-6.13, US-019 AC-03): tầng 2 report.resolve chặn TRƯỚC mọi I/O — id báo cáo không cần tồn tại.
        new("TC-A06", "User thường xử lý báo cáo", "GĐ6",
            Caller.User, HttpMethod.Patch, "/api/v1/reports/{id}", HttpStatusCode.Forbidden,
            ArrangePath: _ => Task.FromResult($"/api/v1/reports/{Guid.NewGuid()}"),
            Body: new { decision = "dismiss" }),

        // Đối chứng bắt buộc: TC-A06 xanh cả khi handler "chặn mọi người". Báo cáo MỞ thật (bài công khai của B, người báo C qua
        // POST /reports) — 200 chứng minh Moderator đi hết đường, không dừng ở 404/409.
        new("TC-A06b", "Đối chứng: Moderator bỏ qua báo cáo", "GĐ6",
            Caller.Moderator, HttpMethod.Patch, "/api/v1/reports/{id}", HttpStatusCode.OK,
            ArrangePath: async a => $"/api/v1/reports/{await BaoCaoMoAsync(a)}",
            Body: new { decision = "dismiss" }),

        // --- GĐ3 (B2). giai-doan-3.md Mục 6.3. Kỳ vọng viết tay theo Mục 6.1 + hợp đồng content-v1, không lấy từ output. ---
        //
        // Bình luận và cảm xúc THỪA KẾ BR-02 của bài (Đ-3.3): không xem được bài → 404 ở MỌI đường, kể cả ghi — cùng quy ước
        // 3b với READ-01. 403 chỉ dành cho thao tác cần SỞ HỮU (TC-A03-comment). READ-CMT-03 và READ-REACT-02 là hai dòng
        // quan trọng nhất: chúng đi vòng qua `commentId` của một bình luận nằm trong bài đã thành private — đúng lỗ LEAK-01.

        new("TC-A03-comment", "A xóa bình luận của B", "GĐ3",
            Caller.User, HttpMethod.Delete, "/api/v1/comments/{id của B}", HttpStatusCode.Forbidden,
            ArrangePath: async a => $"/api/v1/comments/{(await BinhLuanCuaNguoiKhacAsync(a, doiSangRiengTu: false)).CommentId}"),

        new("READ-CMT-01", "A đọc bình luận của bài private của B", "GĐ3",
            Caller.User, HttpMethod.Get, "/api/v1/posts/{id của B}/comments", HttpStatusCode.NotFound,
            ArrangePath: async a => $"/api/v1/posts/{await TaoBaiCuaNguoiKhacAsync(a, PrivacyRiengTu)}/comments"),

        // Người gọi CÓ hồ sơ: không có bước đó thì 403 "chưa onboarding" (Mục 6.1, kiểm trước BR-02) che mất đúng thứ dòng
        // này canh — bỏ kiểm BR-02 ở CreateAsync mà dòng vẫn "đỏ đúng mã" là lưới giả (cùng lý do TC-A03-media).
        new("READ-CMT-02", "A bình luận vào bài private của B", "GĐ3",
            Caller.User, HttpMethod.Post, "/api/v1/posts/{id của B}/comments", HttpStatusCode.NotFound,
            ArrangePath: async a =>
            {
                await TaoHoSoAsync(a.Client, a.CallerUserId);
                return $"/api/v1/posts/{await TaoBaiCuaNguoiKhacAsync(a, PrivacyRiengTu)}/comments";
            },
            Body: new { body = "Bình luận lén." }),

        new("READ-CMT-03", "A đọc phản hồi của một bình luận nằm trong bài private của B", "GĐ3",
            Caller.User, HttpMethod.Get, "/api/v1/comments/{id}/replies", HttpStatusCode.NotFound,
            ArrangePath: async a => $"/api/v1/comments/{(await BinhLuanCuaNguoiKhacAsync(a, doiSangRiengTu: true)).CommentId}/replies"),

        new("READ-REACT-01", "A thả cảm xúc vào bài private của B", "GĐ3",
            Caller.User, HttpMethod.Put, "/api/v1/posts/{id của B}/reactions/me", HttpStatusCode.NotFound,
            ArrangePath: async a => $"/api/v1/posts/{await TaoBaiCuaNguoiKhacAsync(a, PrivacyRiengTu)}/reactions/me",
            Body: new { type = "like" }),

        new("READ-REACT-02", "A thả cảm xúc vào bình luận nằm trong bài private của B", "GĐ3",
            Caller.User, HttpMethod.Put, "/api/v1/comments/{id}/reactions/me", HttpStatusCode.NotFound,
            ArrangePath: async a => $"/api/v1/comments/{(await BinhLuanCuaNguoiKhacAsync(a, doiSangRiengTu: true)).CommentId}/reactions/me",
            Body: new { type = "like" }),

        new("TC-A01-comment", "Bình luận không kèm JWT", "GĐ3",
            Caller.Anonymous, HttpMethod.Post, $"/api/v1/posts/{NguoiLaD:D}/comments", HttpStatusCode.Unauthorized,
            Body: new { body = "Không có token." }),
    ];

    /// <summary>
    /// B1 (GĐ3): B có hồ sơ, đăng bài <c>public</c>, tự bình luận vào bài — rồi (<paramref name="doiSangRiengTu"/>) <c>PATCH</c>
    /// bài sang <c>private</c>. Trả id bài và id bình luận. Bình luận tạo KHI bài còn public (lúc private thì không ai ngoài B
    /// tạo được) — đúng cảnh LEAK-01: người gọi cầm một <c>commentId</c> có thật của bài giờ đã không được xem. Toàn bộ qua
    /// API thật (luật B2 của GĐ2).
    /// </summary>
    private static async Task<(Guid PostId, Guid CommentId)> BinhLuanCuaNguoiKhacAsync(AuthZArrange a, bool doiSangRiengTu)
    {
        var b = Guid.NewGuid();
        await TaoHoSoAsync(a.Client, b);

        using var createPost = new HttpRequestMessage(HttpMethod.Post, "/api/v1/posts")
        {
            Content = JsonContent.Create(new { body = "Bài có bình luận.", privacy = PrivacyCongKhai, mediaKeys = Array.Empty<object>() }),
        };
        createPost.Headers.Authorization = Bearer(b);
        using var created = await a.Client.SendAsync(createPost);
        await NemNeuKhongPhaiAsync(created, HttpStatusCode.Created, "POST /api/v1/posts public cho B");
        using var post = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var postId = post.RootElement.GetProperty("postId").GetGuid();

        using var comment = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/posts/{postId:D}/comments")
        {
            Content = JsonContent.Create(new { body = "Bình luận của B." }),
        };
        comment.Headers.Authorization = Bearer(b);
        using var commented = await a.Client.SendAsync(comment);
        await NemNeuKhongPhaiAsync(commented, HttpStatusCode.Created, $"POST /posts/{postId:D}/comments bởi B");
        using var body = JsonDocument.Parse(await commented.Content.ReadAsStringAsync());
        var commentId = body.RootElement.GetProperty("commentId").GetGuid();

        if (doiSangRiengTu)
        {
            using var patch = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/posts/{postId:D}")
            {
                Content = JsonContent.Create(new { privacy = PrivacyRiengTu }),
            };
            patch.Headers.Authorization = Bearer(b);
            using var patched = await a.Client.SendAsync(patch);
            await NemNeuKhongPhaiAsync(patched, HttpStatusCode.OK, $"PATCH /posts/{postId:D} → private");
        }

        return (postId, commentId);
    }

    /// <summary>
    /// B2 (GĐ5): A và B là bạn, có hội thoại với một tin; người gọi C có hồ sơ nhưng KHÔNG liên quan. Trả id hội thoại A-B.
    /// </summary>
    private static async Task<Guid> HoiThoaiCuaNguoiKhacAsync(AuthZArrange a)
    {
        await TaoHoSoAsync(a.Client, a.CallerUserId);
        return await HoiThoaiCoTinAsync(a.Client, Guid.NewGuid(), Guid.NewGuid());
    }

    /// <summary>
    /// B2 (GĐ5): NGƯỜI GỌI (A) và B là bạn, có hội thoại. <paramref name="huyKetBan"/>: B hủy kết bạn SAU khi mở hội thoại —
    /// A vẫn là thành viên, chỉ còn BR-09 chặn (TC-A07-send). Không hủy thì là đối chứng TC-A07b.
    /// </summary>
    private static async Task<Guid> HoiThoaiCuaNguoiGoiAsync(AuthZArrange a, bool huyKetBan)
    {
        var b = Guid.NewGuid();
        var id = await HoiThoaiCoTinAsync(a.Client, a.CallerUserId, b);
        if (huyKetBan)
        {
            using var unfriend = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/friends/{a.CallerUserId:D}");
            unfriend.Headers.Authorization = Bearer(b);
            using var response = await a.Client.SendAsync(unfriend);
            await NemNeuKhongPhaiAsync(response, HttpStatusCode.NoContent, $"DELETE /friends/{a.CallerUserId:D} bởi {b:D}");
        }

        return id;
    }

    /// <summary>Hai người có hồ sơ, là bạn (API thật), mở hội thoại và <paramref name="nguoiA"/> gửi một tin. Trả id hội thoại.</summary>
    private static async Task<Guid> HoiThoaiCoTinAsync(HttpClient client, Guid nguoiA, Guid nguoiB)
    {
        await TaoHoSoAsync(client, nguoiA);
        await TaoHoSoAsync(client, nguoiB);
        await GuiLoiMoiAsync(client, nguoiA, nguoiB);
        await ChapNhanAsync(client, nguoiB, nguoiA);

        using var open = new HttpRequestMessage(HttpMethod.Post, "/api/v1/conversations")
        {
            Content = JsonContent.Create(new { userId = nguoiB }),
        };
        open.Headers.Authorization = Bearer(nguoiA);
        using var opened = await client.SendAsync(open);
        await NemNeuKhongPhaiAsync(opened, HttpStatusCode.Created, $"POST /conversations {nguoiA:D} → {nguoiB:D}");
        using var document = JsonDocument.Parse(await opened.Content.ReadAsStringAsync());
        var id = document.RootElement.GetProperty("conversationId").GetGuid();

        using var send = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/conversations/{id:D}/messages")
        {
            Content = JsonContent.Create(new { content = "Tin đầu tiên.", clientMsgId = Guid.NewGuid() }),
        };
        send.Headers.Authorization = Bearer(nguoiA);
        using var sent = await client.SendAsync(send);
        await NemNeuKhongPhaiAsync(sent, HttpStatusCode.Created, $"POST /conversations/{id:D}/messages");
        return id;
    }

    /// <summary>
    /// Dòng <c>REP-IDOR</c>: id bài của B nằm trong BODY, mà body của <see cref="AuthZCase"/> dựng trước khi <c>ArrangePath</c> chạy.
    /// Body là một đối tượng mà <c>ArrangePath</c> điền id vào; <c>JsonContent.Create</c> serialize lúc gửi, SAU arrange — không
    /// phải sửa <see cref="AuthZCase"/> hay <see cref="AuthZMatrixTests"/>.
    /// </summary>
    private static AuthZCase RepIdor()
    {
        var body = new BaoCaoBody();
        return new("REP-IDOR", "A báo cáo bài private của B", "GĐ6",
            Caller.User, HttpMethod.Post, "/api/v1/reports", HttpStatusCode.NotFound,
            ArrangePath: async a =>
            {
                body.TargetId = await TaoBaiCuaNguoiKhacAsync(a, PrivacyRiengTu);
                return "/api/v1/reports";
            },
            Body: body);
    }

    /// <summary>
    /// Dòng <c>TC-A06b</c>: một báo cáo MỞ trên bài công khai của B, người báo là một người lạ (không phải người gọi). Trả
    /// <c>reportId</c>. Qua <c>POST /reports</c> thật — không SQL tay.
    /// </summary>
    private static async Task<Guid> BaoCaoMoAsync(AuthZArrange a)
    {
        var bai = await TaoBaiCuaNguoiKhacAsync(a, PrivacyCongKhai);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/reports")
        {
            Content = JsonContent.Create(new { targetType = "post", targetId = bai, reasonCode = "spam" }),
        };
        request.Headers.Authorization = Bearer(Guid.NewGuid());
        using var response = await a.Client.SendAsync(request);
        await NemNeuKhongPhaiAsync(response, HttpStatusCode.Created, $"POST /reports cho bài {bai:D}");

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("reportId").GetGuid();
    }

    /// <summary>Body của <c>POST /reports</c>, chuỗi hợp đồng viết tay. Xem <see cref="RepIdor"/>.</summary>
    private sealed class BaoCaoBody
    {
        public string TargetType { get; } = "post";

        public Guid TargetId { get; set; }

        public string ReasonCode { get; } = "spam";
    }

    /// <summary>
    /// Giá trị <c>privacy</c> viết bằng chuỗi hợp đồng, KHÔNG đọc <c>PostPrivacy</c> của module Content — cùng nếp với
    /// vai trò và tên claim ở đầu file: test dùng chung nguồn với code thì code sai kiểu gì test cũng sai theo.
    ///
    /// <c>TC-A03</c>/<c>TC-A03-delete</c> dùng <c>public</c> còn <c>READ-01</c> dùng <c>private</c>, và đó là cả điểm
    /// của hai hằng số này: để <c>private</c> cho <c>TC-A03</c> thì vẫn ra 403 nhưng ta không còn biết vì ownership hay
    /// vì BR-02.
    /// </summary>
    private const string PrivacyCongKhai = "public";

    /// <summary>Xem <see cref="PrivacyCongKhai"/>.</summary>
    private const string PrivacyRiengTu = "private";

    /// <summary>Mức <c>friends</c> của hợp đồng — dùng cho READ-06 / READ-06b (GĐ4).</summary>
    private const string PrivacyBanBe = "friends";

    /// <summary>Chủ sở hữu giả định của khóa ảnh trong <c>TC-A03-media</c> — xem ghi chú tại chỗ dùng.</summary>
    private static readonly Guid KhoaCuaNguoiKhac = new("0192f3c1-8a4e-7c31-9f2a-6b5d4e3c2a10");

    /// <summary>
    /// Dựng "bài của user B" QUA API THẬT (B.4: không INSERT thẳng DB — INSERT thẳng thì test không đi qua đúng đường
    /// mà người dùng đi, và bỏ lọt mọi lỗi nằm ở tầng controller/service). B là người dùng mới tinh mỗi lần gọi, nên
    /// không dòng nào thấy dữ liệu của dòng nào.
    ///
    /// Hồ sơ TRƯỚC, bài SAU: <c>POST /posts</c> kiểm "người gọi đã có hồ sơ" ở tầng 3 (Đ-2.4). Bỏ bước đó thì B nhận
    /// 403 ngay lúc dựng dữ liệu, hàm này ném, và dòng matrix đỏ với thông báo không liên quan gì tới ownership.
    /// </summary>
    private static async Task<Guid> TaoBaiCuaNguoiKhacAsync(AuthZArrange arrange, string privacy)
    {
        var b = Guid.NewGuid();
        await TaoHoSoAsync(arrange.Client, b);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/posts")
        {
            Content = JsonContent.Create(new { body = "Bài của người khác.", privacy, mediaKeys = Array.Empty<object>() }),
        };
        request.Headers.Authorization = Bearer(b);
        using var response = await arrange.Client.SendAsync(request);
        await NemNeuKhongPhaiAsync(response, HttpStatusCode.Created, $"POST /api/v1/posts cho B ({privacy})");

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("postId").GetGuid();
    }

    /// <summary>
    /// Onboarding một người dùng bất kỳ (Đ-2.4). Dùng cho cả B (chủ bài) lẫn A (người gọi của <c>TC-A03-media</c>).
    /// <c>displayName</c> phải 2–50 ký tự sau <c>Trim</c>, nếu không thì 400 và dòng matrix đỏ ở chỗ không liên quan.
    /// </summary>
    private static async Task TaoHoSoAsync(HttpClient client, Guid userId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/users/me/profile")
        {
            Content = JsonContent.Create(new { displayName = "Người dùng matrix" }),
        };
        request.Headers.Authorization = Bearer(userId);
        using var response = await client.SendAsync(request);
        await NemNeuKhongPhaiAsync(response, HttpStatusCode.OK, $"PUT /api/v1/users/me/profile cho {userId:D}");
    }

    /// <summary>
    /// B2 (GĐ4): gửi lời mời kết bạn qua API thật. Endpoint chưa có → ném 404 ngay (đỏ có chủ đích tới D2).
    /// </summary>
    private static async Task GuiLoiMoiAsync(HttpClient client, Guid tu, Guid den)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/friends/requests")
        {
            Content = JsonContent.Create(new { userId = den }),
        };
        request.Headers.Authorization = Bearer(tu);
        using var response = await client.SendAsync(request);
        await NemNeuKhongPhaiAsync(response, HttpStatusCode.Created, $"POST /friends/requests {tu:D} → {den:D}");
    }

    /// <summary>
    /// B2 (GĐ4): chấp nhận lời mời. Endpoint chưa có → ném 404 ngay (đỏ có chủ đích tới D3).
    /// </summary>
    private static async Task ChapNhanAsync(HttpClient client, Guid nguoiNhan, Guid nguoiGui)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/friends/requests/{nguoiGui:D}/accept");
        request.Headers.Authorization = Bearer(nguoiNhan);
        using var response = await client.SendAsync(request);
        await NemNeuKhongPhaiAsync(response, HttpStatusCode.OK, $"POST /friends/requests/{nguoiGui:D}/accept bởi {nguoiNhan:D}");
    }

    /// <summary>
    /// B2 (GĐ4): B có hồ sơ + bài <c>friends</c>; người gọi có hồ sơ, gửi lời mời; B chấp nhận; trả id bài.
    /// Không INSERT thẳng friendships — bỏ lọt D3 ghi sai trạng thái (cạm bẫy B2).
    /// </summary>
    private static async Task<Guid> TaoBaiBanBeCuaBanAsync(AuthZArrange a)
    {
        var b = Guid.NewGuid();
        await TaoHoSoAsync(a.Client, a.CallerUserId);
        await TaoHoSoAsync(a.Client, b);

        using var createPost = new HttpRequestMessage(HttpMethod.Post, "/api/v1/posts")
        {
            Content = JsonContent.Create(new
            {
                body = "Bài friends của bạn.",
                privacy = PrivacyBanBe,
                mediaKeys = Array.Empty<object>(),
            }),
        };
        createPost.Headers.Authorization = Bearer(b);
        using var created = await a.Client.SendAsync(createPost);
        await NemNeuKhongPhaiAsync(created, HttpStatusCode.Created, $"POST /api/v1/posts friends cho B");

        await GuiLoiMoiAsync(a.Client, a.CallerUserId, b);
        await ChapNhanAsync(a.Client, b, a.CallerUserId);

        using var document = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("postId").GetGuid();
    }

    private static AuthenticationHeaderValue Bearer(Guid userId) =>
        new("Bearer", TestJwt.Create("USER", userId));

    /// <summary>
    /// Dựng dữ liệu hỏng thì ném NGAY với status + body, đừng trả <c>Guid.Empty</c> rồi để dòng matrix đỏ ở chỗ khác:
    /// "403 thay vì 404" và "arrange nhận 404 vì endpoint chưa có" là hai chuyện hoàn toàn khác nhau, mà đọc log thì
    /// giống hệt nhau nếu hàm này im lặng.
    /// </summary>
    private static async Task NemNeuKhongPhaiAsync(HttpResponseMessage response, HttpStatusCode expected, string what)
    {
        if (response.StatusCode == expected)
            return;

        throw new InvalidOperationException(
            $"ArrangePath hỏng — {what}: mong đợi {(int)expected}, nhận {(int)response.StatusCode}. "
          + $"Body: {await response.Content.ReadAsStringAsync()}");
    }

    /// <summary>
    /// Chỉ đưa Id vào MemberData, không đưa cả record: xUnit 2 không serialize được record chứa delegate
    /// nên sẽ gộp mọi dòng thành MỘT test trong Test Explorer. Đưa chuỗi thì mỗi dòng là một test riêng.
    /// </summary>
    public static IEnumerable<object[]> Ids => Cases.Select(c => new object[] { c.Id });
}
