namespace SocialApp.Modules.Content.Application.Posts;

/// <summary>
/// Body 200 của <c>GET /users/{userId}/posts</c>, khớp schema <c>PostPage</c> của <c>content-v1.yaml</c>. Hình dạng này
/// là thứ GĐ4 dùng lại nguyên cho feed — đó là lý do nó tồn tại từ GĐ2 thay vì trả thẳng một mảng.
/// </summary>
/// <param name="NextCursor">
/// <c>null</c> khi hết dữ liệu — <b>không phải chuỗi rỗng</b> (Đ-2.11). FE kiểm <c>if (nextCursor)</c> thì <c>""</c> cũng
/// falsy nên tình cờ đúng, nhưng codegen ghi <c>string | null</c> và <c>""</c> là lệch hợp đồng — và ca tình cờ đúng đó
/// hỏng ngay khi ai đó viết <c>if (nextCursor !== null)</c>.
/// </param>
public sealed record PostPage(IReadOnlyList<PostResponse> Items, string? NextCursor);
