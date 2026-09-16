using SocialApp.SharedKernel.Results;

namespace SocialApp.Modules.Identity.Application.Me;

/// <summary>Thông tin phiên hiện tại cho <c>GET /me</c>.</summary>
public sealed class MeQuery(IIdentityUserStore users)
{
    /// <summary>
    /// <paramref name="userId"/> lấy từ token (C6), không từ route/query. User không còn trong DB (đã bị xóa) → 401: token
    /// đúng chữ ký nhưng phiên không còn chủ — hợp đồng của <c>/me</c> chỉ có 200/401.
    /// </summary>
    public async Task<Result<MeResponse>> GetAsync(Guid userId, CancellationToken ct)
    {
        var me = await users.FindMeAsync(userId, ct);
        if (me is null)
            return IdentityErrors.SessionInvalid;

        return me;
    }
}
