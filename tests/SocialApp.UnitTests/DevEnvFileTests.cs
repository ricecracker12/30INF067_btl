using SocialApp.SharedKernel.Configuration;

namespace SocialApp.UnitTests;

/// <summary>
/// <see cref="DevEnvFile"/> là cổng "bắt buộc có <c>deploy/.env</c>" ở máy dev: có file thì phải đọc đúng
/// mật khẩu, thiếu thì phải từ chối với thông báo chỉ đúng chỗ sửa — không được dựng chuỗi thiếu mật khẩu
/// rồi để lỗi lộ ra muộn ở lần chạm DB đầu tiên.
/// </summary>
public sealed class DevEnvFileTests : IDisposable
{
    private readonly string _repo = Directory.CreateTempSubdirectory("devenvfile-").FullName;
    private readonly string _project;

    public DevEnvFileTests()
    {
        _project = Directory.CreateDirectory(Path.Combine(_repo, "src", "SocialApp.Api")).FullName;
        Directory.CreateDirectory(Path.Combine(_repo, "deploy"));
        File.WriteAllText(Path.Combine(_repo, "deploy", ".env.example"), "POSTGRES_PASSWORD=\n");
    }

    public void Dispose() => Directory.Delete(_repo, recursive: true);

    private void WriteEnv(string content) => File.WriteAllText(Path.Combine(_repo, "deploy", ".env"), content);

    [Fact]
    public void Doc_duoc_mat_khau_khi_dung_tu_thu_muc_con_cua_repo()
    {
        WriteEnv("# comment\r\nPOSTGRES_PASSWORD_OLD=cu\r\nPOSTGRES_PASSWORD=abc+/def=\r\nJwt__SigningKey=x\r\n");

        Assert.Equal("abc+/def=", DevEnvFile.FindValue("POSTGRES_PASSWORD", _project));
        Assert.Equal(
            "Host=localhost;Port=5432;Database=socialapp;Username=socialapp;Password=abc+/def=",
            DevEnvFile.LocalPostgresConnectionString(_project));
    }

    [Fact]
    public void Bo_dau_nhay_bao_quanh_gia_tri()
    {
        WriteEnv("POSTGRES_PASSWORD=\"co khoang trang\"\n");

        Assert.Equal("co khoang trang", DevEnvFile.FindValue("POSTGRES_PASSWORD", _project));
    }

    [Theory]
    [InlineData(null)]                    // chưa tạo deploy/.env
    [InlineData("POSTGRES_PASSWORD=\n")]  // chép .env.example mà chưa điền
    [InlineData("REDIS=x\n")]             // có file nhưng không có biến
    public void Thieu_mat_khau_thi_tu_choi_va_neu_dung_cho_sua(string? envContent)
    {
        if (envContent is not null)
            WriteEnv(envContent);

        Assert.Null(DevEnvFile.FindValue("POSTGRES_PASSWORD", _project));

        var ex = Assert.Throws<InvalidOperationException>(() => DevEnvFile.LocalPostgresConnectionString(_project));
        Assert.Contains("deploy/.env", ex.Message, StringComparison.Ordinal);
        Assert.Contains("POSTGRES_PASSWORD", ex.Message, StringComparison.Ordinal);
        Assert.Contains("ConnectionStrings__Postgres", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Khong_tim_lan_ra_ngoai_repo()
    {
        // Thư mục không nằm dưới repo nào (không có deploy/.env.example ở tổ tiên) — kể cả khi repo bên cạnh
        // có deploy/.env đầy đủ, không được đọc nhầm sang.
        WriteEnv("POSTGRES_PASSWORD=cua-repo-khac\n");
        var outside = Directory.CreateTempSubdirectory("devenvfile-outside-").FullName;
        try
        {
            Assert.Null(DevEnvFile.FindValue("POSTGRES_PASSWORD", outside));
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }
}
