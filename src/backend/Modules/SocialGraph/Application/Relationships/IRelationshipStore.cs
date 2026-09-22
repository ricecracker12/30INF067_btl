namespace SocialApp.Modules.SocialGraph.Application.Relationships;

/// <summary>
/// Bảng <c>socialgraph.friendships</c> + <c>socialgraph.follows</c> cho các luồng của khối D. Hiện thực EF nằm ở
/// <c>Infrastructure/Persistence</c> — <c>Application</c> không chạm EF (<c>PersistenceBoundaryTests</c> canh bằng máy).
///
/// D0 chưa có phương thức. Lớn dần theo từng <c>D*</c>, không khai trước thứ chưa có người gọi — cùng nếp
/// <c>IPostStore</c>.
/// </summary>
public interface IRelationshipStore
{
}
