using Microsoft.EntityFrameworkCore;
using SocialApp.SharedKernel.Contracts;

namespace SocialApp.Modules.Profile.Infrastructure;

/// <summary>
/// Hiện thực <see cref="IUserDirectory"/> cho module Profile (A6). Module CHỦ đăng ký hiện thực của mình —
/// đúng tiền lệ <c>IRolePermissionSource</c> → <c>RolePermissionSource</c> trong <c>AddIdentityModule</c>
/// (C5 của GĐ1).
///
/// <c>internal</c>: bên ngoài chỉ thấy interface ở SharedKernel. Test đi qua chính DI của
/// <c>AddProfileModule</c> nên vẫn kiểm được cả dòng đăng ký, không chỉ câu truy vấn.
///
/// Đặt phẳng trong <c>Infrastructure/</c> chứ không trong thư mục con <c>Directory/</c> như bản phác của A6:
/// namespace <c>...Infrastructure.Directory</c> che mất <c>System.IO.Directory</c> của chính
/// <c>DesignTimeProfileDbContextFactory</c> cùng namespace. Chi tiết ở Mục 7 của hướng dẫn khối A.
/// </summary>
internal sealed class UserDirectory(ProfileDbContext db) : IUserDirectory
{
    public async Task<IReadOnlyDictionary<Guid, UserCard>> GetManyAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken ct = default)
    {
        // Không chạm DB khi rỗng: D6 gọi hàm này với danh sách tác giả của trang, và trang rỗng là chuyện
        // thường. `IN ()` rỗng cũng là một câu SQL hợp lệ nhưng vẫn tốn một lượt đi về.
        if (userIds.Count == 0) return new Dictionary<Guid, UserCard>();

        // Select TRƯỚC ToDictionary là cố ý: nó sinh `SELECT user_id, display_name, avatar_key` chứ không
        // kéo cả dòng về rồi bỏ đi phần thừa.
        return await db.Profiles
            .Where(p => userIds.Contains(p.UserId))
            .Select(p => new UserCard(p.UserId, p.DisplayName, p.AvatarKey))
            .ToDictionaryAsync(c => c.UserId, ct);
    }
}
