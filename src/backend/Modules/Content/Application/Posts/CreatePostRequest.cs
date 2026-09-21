using System.ComponentModel.DataAnnotations;
using SocialApp.Modules.Content.Domain;

namespace SocialApp.Modules.Content.Application.Posts;

/// <summary>
/// Body của <c>POST /posts</c>, khớp schema <c>CreatePostRequest</c> của <c>content-v1.yaml</c>.
/// </summary>
public sealed class CreatePostRequest
{
    /// <summary>
    /// Nội dung chữ. <c>null</c> hoặc rỗng CHỈ hợp lệ khi có ít nhất một ảnh — mệnh đề 3 của BR-01
    /// (<see cref="PostContentPolicy.Validate"/>), và DB canh lại bằng <c>ck_posts_not_empty</c>.
    /// </summary>
    public string? Body { get; init; }

    /// <summary>
    /// <b>Nullable có chủ đích</b> (Q-D2) — đây là trường mà quyết định đó sinh ra để bảo vệ. Bỏ dấu <c>?</c> thì body
    /// thiếu <c>privacy</c> rơi vào <c>default(PostPrivacy)</c> = <see cref="PostPrivacy.Public"/>, tức là một bài
    /// <b>công khai ngoài ý muốn</b>: người dùng định đăng riêng tư, quên/mất trường, và cả thế giới đọc được.
    ///
    /// <c>[Required]</c> chỉ để Swagger ghi <c>required: [privacy]</c>; người bắt lỗi thật là validator
    /// (DataAnnotations đã tắt validate ở <c>Program.cs</c>).
    /// </summary>
    [Required]
    public PostPrivacy? Privacy { get; init; }

    /// <summary>
    /// Ảnh đính kèm, thứ tự trong mảng chính là <c>position</c> hiển thị (0..9). <c>null</c> và <c>[]</c> đều là "bài chỉ
    /// có chữ" — hợp đồng để <c>mediaKeys</c> ngoài <c>required</c> nên cả hai phải nhận được.
    ///
    /// Để <c>null</c> được (khác <c>CreateUploadsRequest.Files</c> của D4, luôn là danh sách rỗng): ở đó <c>files</c> là
    /// trường BẮT BUỘC nên "vắng mặt" là lỗi cần một câu; ở đây vắng mặt là một lựa chọn hợp lệ, và service đọc
    /// <c>MediaKeys ?? []</c> đúng một chỗ.
    /// </summary>
    public List<MediaKeyDeclaration>? MediaKeys { get; init; }
}
