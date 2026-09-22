using SocialApp.Modules.SocialGraph.Application.Relationships;

namespace SocialApp.Modules.SocialGraph.Infrastructure.Persistence;

/// <summary>
/// Hiện thực EF của <see cref="IRelationshipStore"/>. Chỗ DUY NHẤT của module chạm
/// <c>SocialGraphDbContext</c> cho luồng ghi quan hệ — <c>Application</c> chỉ thấy interface
/// (<c>PersistenceBoundaryTests</c> canh).
///
/// D0 chưa có phương thức. D1 thêm constructor nhận DbContext cùng phương thức đọc đầu tiên.
/// </summary>
public sealed class RelationshipStore : IRelationshipStore
{
}
