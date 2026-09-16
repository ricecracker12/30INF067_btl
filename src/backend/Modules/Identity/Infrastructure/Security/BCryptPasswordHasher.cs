using SocialApp.Modules.Identity.Application.Security;

namespace SocialApp.Modules.Identity.Infrastructure.Security;

internal sealed class BCryptPasswordHasher : IPasswordHasher
{
    public const int WorkFactor = 12;   // NFR-SEC-01. Đổi số này là đổi thời gian của CẢ HAI nhánh login.

    // Hash giả sinh lúc dùng lần đầu bằng CÙNG WorkFactor. Hash giả cost 10 ghi cứng thì nhánh email không tồn tại
    // nhanh hơn 4 lần — đúng thứ AC-02 cấm.
    private static readonly Lazy<string> DummyHash =
        new(() => BCrypt.Net.BCrypt.HashPassword(SecureToken.Generate(), WorkFactor));

    public string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);

    public bool Verify(string password, string hash) => BCrypt.Net.BCrypt.Verify(password, hash);

    public void VerifyAgainstDummy(string password) => BCrypt.Net.BCrypt.Verify(password, DummyHash.Value);
}
