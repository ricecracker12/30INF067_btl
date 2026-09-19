using SocialApp.Modules.Profile.Domain;
using SocialApp.SharedKernel.Results;
using SocialApp.SharedKernel.Storage;

namespace SocialApp.Modules.Profile.Application.Profiles;

/// <summary>
/// Nghiệp vụ hồ sơ. Nhận <see cref="IProfileStore"/> chứ không nhận <c>ProfileDbContext</c>, và
/// <see cref="IObjectStorage"/> của SharedKernel để ký <c>avatarUrl</c> (Đ-2.9) — cả hai đều là bề mặt trừu tượng nên lớp
/// này test được mà không cần Postgres lẫn R2.
///
/// Lớn dần theo từng đầu việc: <c>UpsertAsync</c> vào ở D2, <c>SetAvatarAsync</c>/<c>RemoveAvatarAsync</c> ở D3.
/// </summary>
public sealed class ProfileService(IProfileStore profiles, IObjectStorage storage)
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
