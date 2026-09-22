using SocialApp.Modules.Content.Domain;
using SocialApp.SharedKernel.Contracts;

namespace SocialApp.Modules.Content.Application.Feed;

/// <summary>
/// Bảng Đ-4.5 và điều kiện feed gợi ý Đ-4.6 dưới dạng HÀM THUẦN — cùng nếp <c>PostVisibility</c> của GĐ2.
///
/// Đây là bản thứ hai của luật đã nằm trong <c>WHERE</c> của <see cref="IFeedStore"/>. Hai bản tồn tại vì hai lý do khác nhau:
/// SQL lọc TRƯỚC khi cắt trang (không thì trang ngắn ngẫu nhiên), còn hàm này kiểm lại SAU khi trúng cache trang đầu, với
/// nguồn HIỆN TẠI (Đ-4.9 — cache không bao giờ là nguồn sự thật cuối). Hai bản của một luật là chỗ lệch được, nên
/// <c>FeedStoreTests</c> đối chiếu chúng trên cùng bộ dữ liệu.
/// </summary>
public static class FeedVisibility
{
    /// <summary>
    /// Feed mạng lưới. Chính mình → mọi mức; bạn bè → <c>public</c>, <c>friends</c>; chỉ theo dõi → <c>public</c>; còn lại
    /// → không. Chỉ bài <c>published</c> (BR-07: <c>hidden</c> của GĐ6 không lên feed, kể cả của chính mình).
    /// </summary>
    public static bool CanSee(PostPrivacy privacy, PostStatus status, Guid authorId, Guid me, FeedSources sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        if (status != PostStatus.Published)
            return false;

        if (authorId == me)
            return privacy is PostPrivacy.Public or PostPrivacy.Friends or PostPrivacy.Private;

        if (sources.Friends.Contains(authorId))
            return privacy is PostPrivacy.Public or PostPrivacy.Friends;

        // Hai tập rời nhau theo hợp đồng IFeedSourceReader (FollowingOnly = theo dõi TRỪ bạn bè) — thứ tự hai nhánh không
        // đổi kết quả.
        if (sources.FollowingOnly.Contains(authorId))
            return privacy == PostPrivacy.Public;

        // Giá trị enum lạ rơi xuống đây với người lạ, và các nhánh trên chỉ liệt kê mức ĐƯỢC thấy: mặc định đóng.
        return false;
    }

    /// <summary>
    /// Feed gợi ý (Đ-4.6): <c>public</c> + <c>published</c>, không phải bài của chính mình. Không nhận nguồn — ở chế độ
    /// này nguồn rỗng theo định nghĩa, và gọi <see cref="CanSee"/> với nguồn rỗng là loại sạch mọi bài của người lạ.
    /// </summary>
    public static bool CanSeeSuggested(PostPrivacy privacy, PostStatus status, Guid authorId, Guid me) =>
        status == PostStatus.Published && privacy == PostPrivacy.Public && authorId != me;
}
