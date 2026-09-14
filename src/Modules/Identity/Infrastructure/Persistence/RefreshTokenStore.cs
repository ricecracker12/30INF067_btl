using System.Net;
using SocialApp.Modules.Identity.Application;
using SocialApp.Modules.Identity.Domain;

namespace SocialApp.Modules.Identity.Infrastructure.Persistence;

internal sealed class RefreshTokenStore(IdentityDbContext db) : IRefreshTokenStore
{
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
}
