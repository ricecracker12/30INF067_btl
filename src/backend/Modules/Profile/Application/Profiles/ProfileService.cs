using SocialApp.Modules.Profile.Domain;
using SocialApp.SharedKernel.Results;
using SocialApp.SharedKernel.Storage;

namespace SocialApp.Modules.Profile.Application.Profiles;

/// <summary>
/// Nghiệp vụ hồ sơ. Nhận <see cref="IProfileStore"/> chứ không nhận <c>ProfileDbContext</c>, và
/// <see cref="IObjectStorage"/> của SharedKernel để ký <c>avatarUrl</c> (Đ-2.9) — cả hai đều là bề mặt trừu tượng nên lớp
/// này test được mà không cần Postgres lẫn R2.
///
/// <c>TimeProvider</c> (D2) là đồng hồ ghi <c>created_at</c>/<c>updated_at</c> — cùng nguồn với UUID v7 và với hai module
/// còn lại, và là thứ test lùi mốc thời gian được mà không phải sửa DB. Đăng ký bằng <c>TryAddSingleton</c> ở
/// <c>AddProfileModule</c>.
///
/// Bốn thao tác, đủ cho bốn endpoint của module.
/// </summary>
public sealed class ProfileService(IProfileStore profiles, IObjectStorage storage, TimeProvider clock)
{
    /// <summary>
    /// <c>GET /users/{userId}/profile</c>. Hồ sơ là dữ liệu CÔNG KHAI trong MVP (Mục 6.1) nên KHÔNG có tầng 3 ở đây:
    /// <paramref name="userId"/> đến từ route, không từ token, và đó là đúng hợp đồng — ai đăng nhập cũng xem được hồ sơ
    /// của bất kỳ ai. Đây là ngoại lệ có chủ đích với luật "actorId luôn từ token" (Mục 1.3 luật 5): endpoint này không có
    /// khái niệm chủ sở hữu, nó không đọc gì của người gọi.
    ///
    /// Chưa có hồ sơ → <see cref="ProfileErrors.NotFound"/>, và đó là TÍN HIỆU ONBOARDING của FE (Đ-2.4, Mục 7.1): FE gọi
    /// với chính <c>userId</c> của mình, 404 thì chuyển sang <c>/onboarding</c>. "Chưa onboarding" và "người dùng không
    /// tồn tại" cố ý cùng một phản hồi — module này không có bảng <c>users</c> để phân biệt (Đ-2.2), và kể cả có thì phân
    /// biệt cũng là biến endpoint công khai thành máy dò email đã đăng ký.
    /// </summary>
    public async Task<Result<ProfileResponse>> GetAsync(Guid userId, CancellationToken ct)
    {
        var profile = await profiles.FindAsync(userId, ct);
        if (profile is null)
            return ProfileErrors.NotFound;

        return ToResponse(profile);
    }

    /// <summary>
    /// <c>PUT /users/me/profile</c> — bước ONBOARDING của Đ-2.4. Một mã 200 cho cả tạo lẫn sửa: FE không cần biết đây là
    /// lần đầu hay không, và không có nhánh nào để đoán sai.
    ///
    /// <paramref name="actorId"/> đến từ <c>User.GetUserId()</c> ở controller (Mục 1.3 luật 5) — route là <c>me</c> nên
    /// KHÔNG có tầng 3: người gọi chỉ có thể sửa chính mình, không có id nào khác để truyền vào.
    ///
    /// Hai bước chuẩn hóa trước khi xuống store, cả hai đều thuộc về đây chứ không thuộc store:
    /// <list type="number">
    /// <item><c>Trim()</c> tên hiển thị — hợp đồng đo "2–50 ký tự sau khi trim", nên lưu bản chưa trim là DB giữ khoảng
    /// trắng mà <c>GET</c> trả lại <c>"  An  "</c>. Đây là lần <c>Trim()</c> thứ hai có chủ đích; lần thứ nhất ở
    /// validator lúc ĐO. Xem <see cref="UpsertProfileRequestValidator"/> — hai lần là đúng, đừng gộp.</item>
    /// <item><c>bio</c> rỗng hoặc toàn khoảng trắng → <c>null</c> (Q-D3): vắng mặt, <c>null</c>, <c>""</c> và <c>"   "</c>
    /// đều là "không có bio", và để chúng thành bốn giá trị khác nhau trong DB thì <c>GET</c> trả bốn thứ khác nhau cho
    /// cùng một ý.</item>
    /// </list>
    /// </summary>
    public async Task<Result<ProfileResponse>> UpsertAsync(Guid actorId, UpsertProfileRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var bio = string.IsNullOrWhiteSpace(request.Bio) ? null : request.Bio;
        var profile = await profiles.UpsertAsync(actorId, request.DisplayName.Trim(), bio, clock.GetUtcNow(), ct);

        return ToResponse(profile);
    }

    /// <summary>
    /// <c>PUT /users/me/avatar</c> — ba lớp của Đ-2.8, chạy theo ĐÚNG thứ tự này:
    /// <list type="number">
    /// <item><b>Sai dạng → 400.</b> Đã xong trước khi vào đây (<see cref="SetAvatarRequestValidator"/>, auto-validation).</item>
    /// <item><b>Tiền tố của người khác → 403</b> (Đ-2.7). TRƯỚC khi HEAD: kẻ dò key của người khác không được tiêu một
    /// lời gọi R2 nào — R2 tính tiền theo lời gọi, và đảo thứ tự biến endpoint này thành máy bơm hóa đơn.</item>
    /// <item><b>Object chưa có, hoặc sai loại → 400</b> (HEAD, Đ-2.8 lớp 2). Đây là thứ chỉ biết được SAU I/O nên không
    /// phải việc của validator — xem <c>Error.Validation</c> (Q-D4); 400 sinh ra đi qua cùng
    /// <c>ValidationProblemDetails</c> với 400 của FluentValidation nên FE không thấy hai hình dạng.</item>
    /// </list>
    ///
    /// <b>Dung lượng KHÔNG kiểm ở đây.</b> <c>Content-Length</c> nằm trong chữ ký của presigned PUT (Đ-2.8 lớp 1), nên
    /// object to hơn khai báo không vào nổi bucket qua URL ta ký. Kiểm lại là làm chậm mọi request vì một trường hợp
    /// không tồn tại.
    ///
    /// <b>Không xóa object avatar cũ</b> (Đ-2.10): đổi avatar chỉ đổi con trỏ. Xóa trong request thì một lần
    /// <c>UPDATE</c> rollback là hồ sơ trỏ vào object đã bốc hơi. Avatar mồ côi là nợ CÓ ĐỊA CHỈ của Profile (Mục 7.5),
    /// worker dọn rác (C4) lo.
    ///
    /// Chưa có hồ sơ → <b>403</b> (Q-D9): nhất quán với Đ-2.4 ("chưa onboarding thì chưa đăng bài được" → cũng chưa gắn
    /// avatar được), không thêm mã mới vào hợp đồng, và dùng chung <c>Error.Forbidden</c> nên không lộ được gì qua
    /// chênh lệch câu chữ.
    /// </summary>
    public async Task<Result<ProfileResponse>> SetAvatarAsync(Guid actorId, string mediaKey, CancellationToken ct)
    {
        if (!StorageKeys.BelongsTo(mediaKey, StorageKeys.AvatarsPrefix, actorId))
            return Result<ProfileResponse>.Forbidden();

        var head = await storage.HeadAsync(mediaKey, ct);
        if (head is null)
            return ProfileErrors.AvatarNotUploaded;
        if (!StorageKeys.IsAllowedContentType(head.ContentType))
            return ProfileErrors.AvatarTypeNotAllowed;

        var profile = await profiles.SetAvatarKeyAsync(actorId, mediaKey, clock.GetUtcNow(), ct);

        return profile is null ? Result<ProfileResponse>.Forbidden() : ToResponse(profile);
    }

    /// <summary>
    /// <c>DELETE /users/me/avatar</c> — chỉ gỡ liên kết, <b>luôn 204</b>.
    ///
    /// Idempotent theo đúng hợp đồng: đang không có avatar vẫn 204, và CHƯA CÓ HỒ SƠ cũng 204. Khác <c>PUT</c> ở điểm
    /// cuối đó là cố ý — <c>PUT</c> phải trả một hồ sơ nên không có hồ sơ là không trả được gì (Q-D9 → 403), còn
    /// <c>DELETE</c> không trả gì cả, nên "không có gì để gỡ" và "đã gỡ xong" là cùng một trạng thái kết thúc. Phân biệt
    /// hai cái đó chỉ tổ nói cho người gọi biết hồ sơ có tồn tại hay không.
    ///
    /// Object trên R2 không bị xóa (Đ-2.10), kể cả khi người dùng chủ động gỡ.
    /// </summary>
    public async Task<Result> RemoveAvatarAsync(Guid actorId, CancellationToken ct)
    {
        await profiles.SetAvatarKeyAsync(actorId, null, clock.GetUtcNow(), ct);
        return Result.Success();
    }

    /// <summary>
    /// Entity → DTO, kèm bước ký <c>avatarUrl</c>. Ở SERVICE chứ không ở store (store không biết R2) và không ở controller
    /// (tầng Presentation không giữ logic nào). D2 và D3 cũng trả <see cref="ProfileResponse"/> nên dùng lại đúng hàm này —
    /// hai chỗ ánh xạ là hai chỗ lệch nhau, và cái lệch ở đây là "một endpoint trả key thô".
    ///
    /// <c>CreatePresignedGet</c> là HMAC cục bộ, không gọi mạng — ký trong luồng request không tốn một lượt I/O nào.
    /// </summary>
    private ProfileResponse ToResponse(UserProfile profile) => new(
        profile.UserId,
        profile.DisplayName,
        profile.Bio,
        profile.AvatarKey is null ? null : storage.CreatePresignedGet(profile.AvatarKey),
        profile.CreatedAt,
        profile.UpdatedAt);
}
