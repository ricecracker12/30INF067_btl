using System.Net;

namespace SocialApp.IntegrationTests.AuthZ;

/// <summary>Người gọi của một dòng matrix. Vai trò ghi bằng chuỗi hợp đồng, không đọc RoleCodes.</summary>
public enum Caller { Anonymous, ExpiredToken, WrongSignature, User, Moderator, Admin }

/// <summary>
/// Một dòng của AuthZ matrix (Mục 10.2).
/// <paramref name="ArrangePath"/>: với dòng cần dựng dữ liệu trước (TC-A03: bài của user B), hàm này tạo
/// dữ liệu và trả về path cụ thể; <paramref name="Path"/> khi đó chỉ để đọc cho người.
/// <paramref name="Body"/>: body JSON cho dòng gọi endpoint có <c>requestBody: required</c> — xem ghi chú ở
/// chính tham số đó.
/// </summary>
public sealed record AuthZCase(
    string Id,
    string Scenario,
    string AddedIn,
    Caller Caller,
    HttpMethod Method,
    string Path,
    HttpStatusCode Expected,
    Func<AuthZArrange, Task<string>>? ArrangePath = null,
    object? Body = null)
{
    public override string ToString() => Id;
}

/// <summary>
/// Thứ một dòng được dùng khi dựng dữ liệu: HttpClient của app + chuỗi kết nối DB của matrix + <b>id của chính người
/// gọi dòng đó</b>.
///
/// <see cref="CallerUserId"/> vào ở GĐ2 theo chốt <b>Q-B2</b>. Trước đó token được ký SAU khi <c>ArrangePath</c> chạy
/// xong, nên hàm dựng dữ liệu không có cách nào biết A là ai — và <c>TC-A03-media</c> sẽ xanh vì <b>lý do sai</b>: A vừa
/// sinh ra nên chưa có hồ sơ, mà "chưa có hồ sơ" cũng trả 403 (Đ-2.4). Bỏ hẳn kiểm tiền tố khóa của Đ-2.7 thì dòng đó
/// <b>vẫn xanh</b>. Có id ở đây thì <c>ArrangePath</c> tạo hồ sơ cho A trước, và 403 nhận được chỉ còn một lý do.
/// </summary>
public sealed record AuthZArrange(HttpClient Client, string PostgresConnectionString, Guid CallerUserId);
