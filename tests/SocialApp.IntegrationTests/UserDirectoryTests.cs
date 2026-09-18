using Microsoft.Extensions.DependencyInjection;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.Profile.DependencyInjection;
using SocialApp.Modules.Profile.Domain;
using SocialApp.Modules.Profile.Infrastructure;
using SocialApp.SharedKernel.Contracts;
using Xunit;

namespace SocialApp.IntegrationTests;

/// <summary>
/// Contract chéo module <see cref="IUserDirectory"/> (A6) trên Postgres thật. Đi qua CHÍNH DI của
/// <c>AddProfileModule</c> (hiện thực là internal): kiểm cả dòng đăng ký, không chỉ câu truy vấn — cùng nếp
/// <see cref="RolePermissionSourceTests"/> của GĐ1.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class UserDirectoryTests(PostgresFixture postgres)
{
    private static async Task<ServiceProvider> MigratedAsync(PostgresFixture postgres)
    {
        var services = new ServiceCollection()
            .AddProfileModule(await postgres.CreateDatabaseAsync())
            .BuildServiceProvider();

        await services.MigrateProfileModuleAsync();
        return services;
    }

    [Fact]
    public async Task Doc_duoc_nhieu_ho_so_trong_mot_luot()
    {
        var services = await MigratedAsync(postgres);
        await using var scope = services.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<ProfileDbContext>();
        var an = Guid.NewGuid();
        var binh = Guid.NewGuid();
        db.Profiles.AddRange(
            new UserProfile { UserId = an, DisplayName = "An Nguyễn", AvatarKey = "avatars/an/1.jpg" },
            new UserProfile { UserId = binh, DisplayName = "Bình Trần" });
        await db.SaveChangesAsync();

        var cards = await scope.ServiceProvider.GetRequiredService<IUserDirectory>()
            .GetManyAsync([an, binh]);

        Assert.Equal(2, cards.Count);
        Assert.Equal(new UserCard(an, "An Nguyễn", "avatars/an/1.jpg"), cards[an]);
        // avatar_key null = dùng ảnh mặc định; chiếu giữ nguyên null, không tự bịa URL (ký presigned GET là
        // việc của khối D).
        Assert.Equal(new UserCard(binh, "Bình Trần", null), cards[binh]);
    }

    /// <summary>
    /// Id không có hồ sơ thì VẮNG MẶT trong dictionary, không phải giá trị null. D6 phải xử lý vắng mặt —
    /// khẳng định này là thứ giữ hợp đồng đó khỏi trôi.
    /// </summary>
    [Fact]
    public async Task Id_khong_co_ho_so_thi_vang_mat_chu_khong_phai_null()
    {
        var services = await MigratedAsync(postgres);
        await using var scope = services.CreateAsyncScope();

        var khongCoHoSo = Guid.NewGuid();

        var cards = await scope.ServiceProvider.GetRequiredService<IUserDirectory>()
            .GetManyAsync([khongCoHoSo]);

        Assert.Empty(cards);
        Assert.False(cards.ContainsKey(khongCoHoSo));
    }

    /// <summary>Danh sách rỗng: trả rỗng và KHÔNG chạm DB — trang không có bài nào là chuyện thường ở D6.</summary>
    [Fact]
    public async Task Danh_sach_rong_tra_ve_rong()
    {
        var services = await MigratedAsync(postgres);
        await using var scope = services.CreateAsyncScope();

        var cards = await scope.ServiceProvider.GetRequiredService<IUserDirectory>()
            .GetManyAsync([]);

        Assert.Empty(cards);
    }
}
