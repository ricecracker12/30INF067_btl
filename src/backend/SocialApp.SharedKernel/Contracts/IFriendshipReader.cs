namespace SocialApp.SharedKernel.Contracts;

/// <summary>
/// Quan hệ bạn bè, chỉ đọc — đầu vào của BR-02 khi bài để chế độ <c>friends</c> (Mục 7.4). Cùng lý do với
/// <see cref="IUserDirectory"/>, contract nằm ở SharedKernel: module Content không được import module
/// SocialGraph (Đ-2.3), và ở GĐ2 thì module đó còn chưa tồn tại.
/// </summary>
public interface IFriendshipReader
{
    Task<bool> AreFriendsAsync(Guid userId, Guid otherUserId, CancellationToken ct = default);
}

/// <summary>
/// Null-object CÓ CHỦ ĐÍCH (Đ-2.9, Mục 7.4), không phải chỗ trống chờ ai đó điền: GĐ2 chưa có module
/// SocialGraph, nên bài <c>friends</c> chỉ chính tác giả xem được.
///
/// GĐ4 bật kết bạn thật bằng <b>một dòng DI</b> trong <c>AddContentModule</c> và KHÔNG chạm một dòng nào
/// trong module Content. Có test pin giữ hành vi này — đổi nó mà không đổi test là đỏ.
///
/// Trả <c>false</c> kể cả khi hai id BẰNG NHAU: bản thân mình không phải "bạn" của mình. Đường tác giả xem
/// bài của chính mình đi qua nhánh <c>author_id == actorId</c> ở D6, không đi qua đây — trả <c>true</c> cho
/// ca đó là trộn hai khái niệm và làm BR-02 khó đọc.
/// </summary>
public sealed class AlwaysStrangers : IFriendshipReader
{
    public Task<bool> AreFriendsAsync(Guid userId, Guid otherUserId, CancellationToken ct = default) =>
        Task.FromResult(false);
}
