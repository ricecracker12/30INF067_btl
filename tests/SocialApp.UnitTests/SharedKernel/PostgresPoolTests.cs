using Npgsql;
using SocialApp.SharedKernel.Configuration;

namespace SocialApp.UnitTests.SharedKernel;

/// <summary>
/// PERF-03 — trần pool mặc định. Đọc kết quả bằng <see cref="NpgsqlConnectionStringBuilder"/> (thứ app thật dùng) chứ không
/// so chuỗi: cách <c>DbConnectionStringBuilder</c> trình bày lại chuỗi không phải điều cần canh, con số Npgsql đọc ra mới là.
/// </summary>
public sealed class PostgresPoolTests
{
    private const string Base = "Host=postgres;Port=5432;Database=socialapp;Username=socialapp;Password=secret";

    [Fact]
    public void Chua_ghi_thi_them_80() =>
        Assert.Equal(80, new NpgsqlConnectionStringBuilder(PostgresPool.WithDefaultMaxPoolSize(Base)).MaxPoolSize);

    /// <summary>Người vận hành đã đặt con số thì giữ nguyên — với MỌI cách viết khóa Npgsql chấp nhận, hoa thường tùy ý.</summary>
    [Theory]
    [InlineData("Maximum Pool Size=40")]
    [InlineData("MaxPoolSize=40")]
    [InlineData("maximum pool size=40")]
    public void Da_ghi_thi_giu_nguyen(string setting)
    {
        var cs = $"{Base};{setting}";

        Assert.Equal(cs, PostgresPool.WithDefaultMaxPoolSize(cs));
        Assert.Equal(40, new NpgsqlConnectionStringBuilder(cs).MaxPoolSize);
    }

    /// <summary>Các khóa khác đi qua nguyên vẹn — kể cả mật khẩu base64 có <c>+</c>, <c>/</c>, <c>=</c> và dấu <c>;</c> trong ngoặc.</summary>
    [Fact]
    public void Khong_dung_khoa_khac_ke_ca_mat_khau_ky_tu_dac_biet()
    {
        const string cs = "Host=db;Database=x;Username=u;Password='a+b/c=d;e'";

        var result = new NpgsqlConnectionStringBuilder(PostgresPool.WithDefaultMaxPoolSize(cs));

        Assert.Equal("db", result.Host);
        Assert.Equal("x", result.Database);
        Assert.Equal("u", result.Username);
        Assert.Equal("a+b/c=d;e", result.Password);
        Assert.Equal(80, result.MaxPoolSize);
    }
}
