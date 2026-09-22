using SocialApp.Modules.SocialGraph.Application;
using SocialApp.SharedKernel.Contracts;

namespace SocialApp.Modules.SocialGraph.Application.Relationships;

/// <summary>
/// Nghiệp vụ quan hệ (kết bạn, theo dõi, đọc trạng thái). Nhận <see cref="IRelationshipStore"/> chứ không nhận
/// <c>SocialGraphDbContext</c> — lớp này test được mà không cần Postgres.
///
/// D0 khóa tập phụ thuộc; D1 thêm phương thức đầu tiên. <c>IFeedSourceCache</c> (C1 đã đăng ký) cắm vào
/// constructor ở đây — C1 lên trước D0 nên không chờ commit C1. Không đăng ký bản rỗng (cùng loại bẫy
/// <c>AlwaysStrangers</c>). D2–D6 gọi <c>InvalidateAsync</c> sau <c>COMMIT</c>.
/// </summary>
public sealed class RelationshipService(
    IRelationshipStore store,
    IUserDirectory directory,
    IFeedSourceCache feedSources,
    SocialGraphEvents events,
    TimeProvider clock)
{
#pragma warning disable IDE0052 // D0: khóa tập phụ thuộc; D1 đọc các trường này lần đầu.
    private readonly IRelationshipStore _store = store;
    private readonly IUserDirectory _directory = directory;
    private readonly IFeedSourceCache _feedSources = feedSources;
    private readonly SocialGraphEvents _events = events;
    private readonly TimeProvider _clock = clock;
#pragma warning restore IDE0052
}
