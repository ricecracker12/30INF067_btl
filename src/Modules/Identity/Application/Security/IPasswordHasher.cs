namespace SocialApp.Modules.Identity.Application.Security;

/// <summary>Băm và kiểm mật khẩu (NFR-SEC-01). Hiện thực BCrypt ở Infrastructure/Security.</summary>
public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string password, string hash);

    /// <summary>
    /// Chạy một phép Verify tốn đúng chi phí như thật, trên hash giả — cho nhánh "email không tồn tại" của
    /// login (Mục 7.2 bước 2). Là method riêng để unit test khẳng định được là nó ĐÃ bị gọi.
    /// </summary>
    void VerifyAgainstDummy(string password);
}
