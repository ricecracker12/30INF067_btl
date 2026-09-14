using System.Net;

namespace SocialApp.IntegrationTests.AuthZ;

/// <summary>Người gọi của một dòng matrix. Vai trò ghi bằng chuỗi hợp đồng, không đọc RoleCodes.</summary>
public enum Caller { Anonymous, ExpiredToken, WrongSignature, User, Moderator, Admin }

/// <summary>
/// Một dòng của AuthZ matrix (Mục 10.2).
/// <paramref name="ArrangePath"/>: với dòng cần dựng dữ liệu trước (TC-A03: bài của user B), hàm này tạo
/// dữ liệu và trả về path cụ thể; <paramref name="Path"/> khi đó chỉ để đọc cho người.
/// </summary>
public sealed record AuthZCase(
    string Id,
    string Scenario,
    string AddedIn,
    Caller Caller,
    HttpMethod Method,
    string Path,
    HttpStatusCode Expected,
    Func<AuthZArrange, Task<string>>? ArrangePath = null)
{
    public override string ToString() => Id;
}

/// <summary>Thứ một dòng được dùng khi dựng dữ liệu: HttpClient của app + chuỗi kết nối DB của matrix.</summary>
public sealed record AuthZArrange(HttpClient Client, string PostgresConnectionString);
