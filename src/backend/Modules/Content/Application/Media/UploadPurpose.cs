namespace SocialApp.Modules.Content.Application.Media;

/// <summary>
/// Mục đích của một lô tải lên — khớp enum <c>UploadPurpose</c> của <c>content-v1.yaml</c>. Quyết định HAI thứ, và đó là
/// cả lý do endpoint chỉ có một tham số này thay vì hai endpoint riêng:
/// <list type="bullet">
/// <item><b>Tiền tố key</b> (Đ-2.7): <c>posts/{userId}/…</c> hay <c>avatars/{userId}/…</c>.</item>
/// <item><b>Mức quyền</b> (Đ-2.6, Q-D5): <see cref="Post"/> đòi thêm <c>post.create</c>; <see cref="Avatar"/> chỉ cần
/// đăng nhập — người chưa có quyền đăng bài vẫn phải đổi được ảnh đại diện.</item>
/// </list>
///
/// Ra JSON là chữ thường (<c>post</c>, <c>avatar</c>) nhờ <c>JsonStringEnumConverter(JsonNamingPolicy.CamelCase)</c> ở
/// <c>Program.cs</c> (Q-D2) — KHÔNG phải nhờ thuộc tính nào ở đây. Đọc vào không phân biệt hoa thường, nới hơn hợp đồng.
/// </summary>
public enum UploadPurpose
{
    /// <summary>Ảnh của bài đăng. Tiền tố <c>posts/</c>, đòi quyền <c>post.create</c>.</summary>
    Post,

    /// <summary>Ảnh đại diện. Tiền tố <c>avatars/</c>, chỉ cần <c>[Authorize]</c>.</summary>
    Avatar,
}
