using Microsoft.EntityFrameworkCore;
using SocialApp.Modules.Profile.Application.Profiles;
using SocialApp.Modules.Profile.Domain;

namespace SocialApp.Modules.Profile.Infrastructure.Persistence;

/// <summary>
/// Hiện thực EF của <see cref="IProfileStore"/>. <c>internal</c> như <c>IdentityUserStore</c> của GĐ1: ngoài module không
/// ai gọi thẳng được, cửa duy nhất là interface ở <c>Application</c>.
/// </summary>
internal sealed class ProfileStore(ProfileDbContext db) : IProfileStore
{
    /// <summary>
    /// <c>AsNoTracking</c>: D1 chỉ đọc rồi ánh xạ sang DTO, không có gì để ghi lại. Theo dõi thay đổi ở đây chỉ tốn một bản
    /// sao snapshot cho mỗi lời gọi. D2/D3 ghi thì dùng đường riêng của chúng, không mượn phương thức này.
    /// </summary>
    public Task<UserProfile?> FindAsync(Guid userId, CancellationToken ct) =>
        db.Profiles.AsNoTracking().SingleOrDefaultAsync(p => p.UserId == userId, ct);

    /// <summary>
    /// MỘT câu <c>INSERT … ON CONFLICT (user_id) DO UPDATE … RETURNING *</c> — lý do không được đọc-rồi-ghi nằm ở
    /// <see cref="IProfileStore.UpsertAsync"/>.
    ///
    /// Bốn chi tiết của câu lệnh, mỗi cái ứng với một cách hỏng:
    /// <list type="bullet">
    /// <item><c>created_at</c> KHÔNG có trong <c>SET</c> → lần sửa thứ hai không dời mốc tạo (PROF-02).</item>
    /// <item><c>avatar_key</c> KHÔNG có trong <c>SET</c> → sửa tên không làm mất avatar. Cột đó chỉ D3 đụng; đưa nó vào
    /// đây là một dòng trông vô hại xóa ảnh của người dùng.</item>
    /// <item><c>updated_at</c> gán TAY trong <c>SET</c> → SQL thô đi vòng qua ChangeTracker nên
    /// <c>ProfileDbContext.StampUpdatedAt</c> không với tới, và <c>DEFAULT now()</c> chỉ chạy lúc INSERT.</item>
    /// <item><c>RETURNING *</c> → trả đúng dòng vừa ghi, không cần SELECT lần hai (thứ vẫn có thể đọc trúng bản ghi của
    /// một request song song).</item>
    /// </list>
    ///
    /// <b><c>ToListAsync</c> rồi <c>Single()</c> ở client, KHÔNG phải <c>SingleAsync()</c></b> — chỗ này khác đoạn mẫu
    /// trong hướng dẫn D2 và đã làm 500 lúc thi công. EF coi <c>INSERT … RETURNING</c> là SQL <i>non-composable</i>;
    /// <c>SingleAsync</c> thì thêm <c>LIMIT 2</c>, tức là COMPOSE lên trên nó, và EF ném
    /// <c>InvalidOperationException</c> trước khi chạm DB. <c>ToListAsync</c> không sửa một ký tự nào của SQL nên hợp lệ,
    /// vẫn đúng một round-trip, và <c>Single()</c> sau đó vẫn khẳng định "đúng một dòng" — chỉ là khẳng định ở client.
    /// Bọc lại bằng CTE (<c>WITH … AS (INSERT …) SELECT</c>) KHÔNG cứu được: Postgres đòi CTE ghi dữ liệu phải ở top
    /// level, mà EF sẽ nhét nó vào subquery.
    ///
    /// Tham số nội suy vào <c>FromSql</c> là <c>FormattableString</c> — EF gắn chúng làm tham số Npgsql, không ghép chuỗi.
    /// <c>AsNoTracking</c>: entity trả về chỉ để ánh xạ sang DTO, không sửa tiếp; nó chỉ đổi cách theo dõi, không đổi SQL,
    /// nên không phải composition.
    /// </summary>
    public async Task<UserProfile> UpsertAsync(
        Guid userId, string displayName, string? bio, DateTimeOffset now, CancellationToken ct)
    {
        var rows = await db.Profiles.FromSql($"""
            INSERT INTO profile.profiles (user_id, display_name, bio, avatar_key, created_at, updated_at)
            VALUES ({userId}, {displayName}, {bio}, NULL, {now}, {now})
            ON CONFLICT (user_id) DO UPDATE
               SET display_name = EXCLUDED.display_name,
                   bio          = EXCLUDED.bio,
                   updated_at   = EXCLUDED.updated_at
            RETURNING *
            """).AsNoTracking().ToListAsync(ct);

        return rows.Single();
    }

    /// <summary>
    /// MỘT câu <c>UPDATE … RETURNING *</c>. Không đọc trước để kiểm "hồ sơ có tồn tại không": số dòng trả về đã trả lời
    /// câu đó, và đọc-rồi-ghi thì giữa hai bước hồ sơ biến mất được.
    ///
    /// <c>updated_at</c> gán TAY, cùng lý do với <c>UpsertAsync</c>: SQL thô đi vòng qua ChangeTracker nên
    /// <c>ProfileDbContext.StampUpdatedAt</c> không với tới.
    ///
    /// <c>ToListAsync</c> rồi <c>SingleOrDefault()</c> ở client, KHÔNG <c>SingleOrDefaultAsync()</c> — cùng cái bẫy
    /// non-composable đã làm 500 ở <c>UpsertAsync</c>: mọi toán tử thêm <c>LIMIT</c> đều là compose lên trên
    /// <c>UPDATE … RETURNING</c>.
    /// </summary>
    public async Task<UserProfile?> SetAvatarKeyAsync(
        Guid userId, string? avatarKey, DateTimeOffset now, CancellationToken ct)
    {
        var rows = await db.Profiles.FromSql($"""
            UPDATE profile.profiles
               SET avatar_key = {avatarKey},
                   updated_at = {now}
             WHERE user_id = {userId}
            RETURNING *
            """).AsNoTracking().ToListAsync(ct);

        return rows.SingleOrDefault();
    }
}
