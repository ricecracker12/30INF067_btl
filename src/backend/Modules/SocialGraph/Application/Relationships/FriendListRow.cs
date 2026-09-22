namespace SocialApp.Modules.SocialGraph.Application.Relationships;

/// <summary>
/// Một dòng của trang danh sách, trước khi ghép hồ sơ. <see cref="Since"/> là <c>accepted_at</c> (bạn bè) hoặc
/// <c>created_at</c> (lời mời) — cùng trường <c>since</c> trên thẻ. <see cref="OtherUserId"/> là người kia, đã chiếu
/// từ cặp chuẩn hóa.
/// </summary>
public readonly record struct FriendListRow(Guid OtherUserId, DateTimeOffset Since);
