using System.Net;
using Microsoft.EntityFrameworkCore;
using SocialApp.Modules.Identity.Application;
using SocialApp.Modules.Identity.Domain;

namespace SocialApp.Modules.Identity.Infrastructure.Persistence;

internal sealed class RefreshTokenStore(IdentityDbContext db) : IRefreshTokenStore
{
    // Không gian khóa tư vấn (pg_advisory_xact_lock hai tham số) cho family refresh token của module Identity. Module khác cần
    // advisory lock thì chọn namespace khác để không vô tình xếp hàng với nhau.
    private const int FamilyLockNamespace = 0x5246;   // "RF"

    public async Task CreateAsync(
        Guid userId, Guid familyId, string tokenHash, DateTimeOffset expiresAt, IPAddress? createdIp, DateTimeOffset now,
        CancellationToken ct)
    {
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = userId,
            FamilyId = familyId,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt,
            CreatedIp = createdIp,
            CreatedAt = now,   // cùng nguồn thời gian với expires_at (Đ-D10), không để mặc định DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<RotateOutcome> RotateAsync(
        string tokenHash, DateTimeOffset now, string newTokenHash, DateTimeOffset newExpiresAt, IPAddress? createdIp,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // 0. Khóa theo FAMILY trước khi khóa dòng. FOR UPDATE chỉ khóa MỘT dòng: reuse detection của token cũ T1 và lượt xoay
        //    của token kế nhiệm T2 khóa hai dòng khác nhau nên không xếp hàng nhau — T3 do lượt xoay sinh ra, commit trong lúc câu
        //    UPDATE thu hồi family đang chờ, nằm ngoài snapshot của câu đó và sống sót (RT-06 tái hiện được). Khóa tư vấn theo
        //    family_id làm mọi lượt xoay/thu hồi của một family chạy tuần tự. family_id của một token không bao giờ đổi nên đọc
        //    nó không cần khóa. Mọi nơi đụng một family (logout D6) lấy khóa family TRƯỚC khóa dòng — cùng thứ tự thì không deadlock.
        var familyIds = await db.Database
            .SqlQuery<Guid>($"SELECT family_id AS \"Value\" FROM identity.refresh_tokens WHERE token_hash = {tokenHash}")
            .ToListAsync(ct);
        if (familyIds.Count == 0)
        {
            await transaction.CommitAsync(ct);
            return RotateOutcome.Invalid.Instance;
        }
        await LockFamilyAsync(familyIds[0], ct);

        // 1. Khóa dòng. Sau khóa family nên đây là bản đã commit mới nhất: tab thứ hai cùng token chờ ở bước 0 tới khi tab thứ
        //    nhất COMMIT, rồi đọc thấy dòng đã bị xoay và rơi vào ân hạn 3a. FOR UPDATE giữ lại làm lưới thứ hai. ToListAsync,
        //    KHÔNG compose (SingleOrDefault…): EF bọc câu SQL thành subquery và không chắc còn khóa đúng như câu gốc.
        var rows = await db.RefreshTokens
            .FromSql($"SELECT * FROM identity.refresh_tokens WHERE token_hash = {tokenHash} FOR UPDATE")
            .ToListAsync(ct);
        var row = rows.SingleOrDefault();

        // 2. Không tồn tại.
        if (row is null)
        {
            await transaction.CommitAsync(ct);
            return RotateOutcome.Invalid.Instance;
        }

        // 3. Đã bị xoay hoặc thu hồi — kiểm TRƯỚC hạn dùng: token hết hạn mà bị dùng lại vẫn là reuse.
        if (row.ReplacedById is not null || row.RevokedAt is not null)
        {
            // 3a. Ân hạn (Đ-D3): bị XOAY (có replaced_by_id, không phải bị thu hồi) trong 10 giây VÀ family còn lá sống. "Còn
            //     sống" định nghĩa bằng dữ liệu, không suy từ revoked_at của dòng đang cầm — token bị xoay cũng có revoked_at.
            if (row.ReplacedById is not null
                && row.RevokedAt > now - RefreshTokenPolicy.ReuseGracePeriod
                && await db.RefreshTokens.AnyAsync(t => t.FamilyId == row.FamilyId && t.RevokedAt == null, ct))
            {
                // Token ANH EM cùng family, không đụng token kế nhiệm: DB chỉ giữ băm của nó, không có bản rõ để trả lại.
                db.RefreshTokens.Add(Successor(row, newTokenHash, newExpiresAt, createdIp, now));
                await db.SaveChangesAsync(ct);
                var graceRole = await RoleCodeAsync(row.UserId, ct);
                await transaction.CommitAsync(ct);
                return new RotateOutcome.Grace(row.UserId, graceRole);
            }

            // 3b. Reuse → thu hồi CẢ family bằng một câu rồi COMMIT. Không nhận ct: client ngắt kết nối giữa chừng cũng không
            //     được làm mất việc thu hồi. Rollback / ném exception ở đây là family sống sót mà endpoint vẫn 401.
            await db.RefreshTokens
                .Where(t => t.FamilyId == row.FamilyId && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), CancellationToken.None);
            await transaction.CommitAsync(CancellationToken.None);
            return new RotateOutcome.ReuseDetected(row.UserId);
        }

        // 4. Hết hạn — không phải reuse, family giữ nguyên.
        if (row.ExpiresAt <= now)
        {
            await transaction.CommitAsync(ct);
            return RotateOutcome.Invalid.Instance;
        }

        // 5. Xoay: INSERT token mới TRƯỚC — replaced_by_id là FK tới dòng mới — rồi mới đánh dấu dòng cũ.
        var successor = Successor(row, newTokenHash, newExpiresAt, createdIp, now);
        db.RefreshTokens.Add(successor);
        await db.SaveChangesAsync(ct);
        row.RevokedAt = now;
        row.ReplacedById = successor.Id;
        await db.SaveChangesAsync(ct);

        var role = await RoleCodeAsync(row.UserId, ct);
        await transaction.CommitAsync(ct);
        return new RotateOutcome.Rotated(row.UserId, role);
    }

    /// <summary>
    /// Khóa tư vấn theo family, tự nhả khi transaction kết thúc (commit hoặc rollback). Phải gọi TRONG transaction, và TRƯỚC mọi
    /// khóa dòng của family đó.
    /// </summary>
    private Task LockFamilyAsync(Guid familyId, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock({FamilyLockNamespace}, hashtext({familyId}::text))", ct);

    private static RefreshToken Successor(
        RefreshToken current, string tokenHash, DateTimeOffset expiresAt, IPAddress? createdIp, DateTimeOffset now) => new()
    {
        UserId = current.UserId,
        FamilyId = current.FamilyId,
        TokenHash = tokenHash,
        ExpiresAt = expiresAt,
        CreatedIp = createdIp,
        CreatedAt = now,
    };

    // Vai trò cho access token mới đọc từ DB (join users → roles), KHÔNG chép từ token cũ: hạ/nâng quyền ở GĐ6 có hiệu lực
    // sau một lần refresh (Mục 7.5).
    private Task<string> RoleCodeAsync(Guid userId, CancellationToken ct) =>
        (from u in db.Users
         join r in db.Roles on u.RoleId equals r.RoleId
         where u.UserId == userId
         select r.Code)
        .SingleAsync(ct);
}
