namespace SocialApp.Modules.Moderation.Application.Reports;

/// <summary>
/// <c>GET /reports/{reportId}</c> — mọi thứ Moderator cần để quyết một đối tượng (Đ-6.13, Mục 8.1).
///
/// <b>Không có <c>reporterId</c> ở bất kỳ độ sâu nào</b> — Moderator quyết theo nội dung, không theo người báo, và tránh trả thù
/// (<c>QUE-04</c> canh). Admin cần thì đọc DB/audit.
/// </summary>
/// <param name="ReportId">Báo cáo được mở — có thể đã đóng; chi tiết vẫn xem được (lịch sử).</param>
/// <param name="OpenReports">Mọi báo cáo ĐANG MỞ của cùng đối tượng, cũ nhất trước — quyết định ở D7c đóng tất cả cùng lúc.</param>
/// <param name="History">Các quyết định đã có trên đối tượng này, cũ nhất trước — mỗi quyết định một dòng.</param>
public sealed record ReportDetail(
    Guid ReportId,
    ReportTargetSnapshot Target,
    IReadOnlyList<OpenReport> OpenReports,
    IReadOnlyList<ReportHistoryEntry> History);

/// <summary>
/// Ảnh chụp đối tượng tại lúc đọc — schema <c>TargetSnapshot</c> của hợp đồng. Đọc qua <c>IModerationTargets</c> (C2), KHÔNG qua
/// <c>GET /posts/{id}</c>: BR-02 và BR-07 chặn Moderator ở đó, còn đây là đường DUY NHẤT Moderator thấy nội dung không công khai.
/// </summary>
/// <param name="Status">
/// <c>published</c> · <c>hidden</c> · <c>deleted</c> (bài) · <c>active</c> · <c>disabled</c> (người dùng). Đối tượng biến mất khỏi mọi
/// bảng → <c>deleted</c>, mọi trường nội dung <c>null</c>.
/// </param>
/// <param name="Author">Tác giả (với người dùng: chính người đó). <c>null</c> khi không có hồ sơ hoặc đối tượng đã biến mất.</param>
/// <param name="Body">Nội dung bài / tiểu sử — có mặt cả khi bài riêng tư hay đã xóa mềm.</param>
/// <param name="Media">Ảnh bài / ảnh đại diện, URL ký 15 phút — ký SAU khi tầng 2 đã cho qua (cạm bẫy 3).</param>
/// <param name="PostId">Bài chứa đối tượng: bài → chính nó, người dùng → <c>null</c>.</param>
/// <param name="CreatedAt"><c>null</c> chỉ khi đối tượng đã biến mất khỏi mọi bảng.</param>
public sealed record ReportTargetSnapshot(
    string Type,
    Guid Id,
    string Status,
    ModerationUserCard? Author,
    string? Body,
    IReadOnlyList<ReportTargetMedia> Media,
    Guid? PostId,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? EditedAt);

/// <summary>Thẻ người dùng — schema <c>UserCard</c> của hợp đồng. Hydrate MỘT lô qua <c>IUserDirectory</c>.</summary>
/// <param name="AvatarUrl">Presigned GET 15 phút, <c>null</c> khi chưa có ảnh đại diện.</param>
public sealed record ModerationUserCard(Guid UserId, string DisplayName, string? AvatarUrl);

/// <summary>Một ảnh của đối tượng. Không có key lưu trữ — chỉ URL đã ký.</summary>
public sealed record ReportTargetMedia(string Url);

/// <summary>Một báo cáo đang mở. KHÔNG có người báo.</summary>
/// <param name="Detail">Mô tả người báo tự gõ — chỉ Moderator đọc, không bao giờ vào log (B.10 #5).</param>
public sealed record OpenReport(Guid ReportId, string ReasonCode, string? Detail, DateTimeOffset CreatedAt);

/// <summary>
/// Một quyết định đã có (L-D15): các báo cáo đóng cùng một lần quyết chung <c>(status, resolver_id, resolved_at, resolution_note)</c>
/// và thành MỘT dòng.
/// </summary>
/// <param name="Outcome">
/// <c>resolved</c> | <c>dismissed</c> — chính <c>reports.status</c>. Bảng không phân biệt <c>hide</c> với <c>resolve</c>; "đã ẩn chưa"
/// đọc ở <c>target.status</c> và trong audit (L-D15: đổi tên trường thay vì bịa hay thêm cột).
/// </param>
public sealed record ReportHistoryEntry(string Outcome, Guid ResolverId, DateTimeOffset ResolvedAt, string? Note);
