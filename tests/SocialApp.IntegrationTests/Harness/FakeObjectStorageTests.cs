using SocialApp.IntegrationTests.Harness;

namespace SocialApp.IntegrationTests;

/// <summary>
/// Fake cũng phải giữ đúng hợp đồng của IObjectStorage — nếu không thì mọi test dựng trên nó xanh trên một hành vi
/// không tồn tại ngoài đời. Ba điểm hợp đồng dễ lệch: HEAD null khi không có, Delete idempotent, List phân trang thật.
/// Không cần container: đây là test của harness.
/// </summary>
public sealed class FakeObjectStorageTests
{
    [Fact]
    public async Task Head_tra_dung_thu_da_Put_va_null_khi_khong_co()
    {
        var fake = new FakeObjectStorage();
        fake.Put("posts/u/a.jpg", 12L * 1024 * 1024, "image/jpeg");

        var head = await fake.HeadAsync("posts/u/a.jpg");
        Assert.NotNull(head);
        Assert.Equal(12L * 1024 * 1024, head.ContentLength);
        Assert.Equal("image/jpeg", head.ContentType);

        Assert.Null(await fake.HeadAsync("posts/u/khong-co.jpg"));
        Assert.Equal(2, fake.HeadCalls);
    }

    [Fact]
    public async Task Delete_idempotent_va_ghi_lai_key_da_xoa()
    {
        var fake = new FakeObjectStorage();
        fake.Put("avatars/u/a.png", 10, "image/png");

        await fake.DeleteAsync("avatars/u/a.png");
        await fake.DeleteAsync("avatars/u/a.png");   // lần hai: không ném, không ghi thêm

        Assert.False(fake.Exists("avatars/u/a.png"));
        Assert.Equal(["avatars/u/a.png"], fake.Deleted);
    }

    [Fact]
    public async Task List_phan_trang_theo_prefix_bang_continuation_token()
    {
        var fake = new FakeObjectStorage();
        for (var i = 0; i < 5; i++)
            fake.Put($"posts/u/{i}.jpg", 1, "image/jpeg");
        fake.Put("avatars/u/x.png", 1, "image/png");   // prefix khác — không được lọt vào

        var page1 = await fake.ListAsync("posts/", continuationToken: null, maxKeys: 2);
        var page2 = await fake.ListAsync("posts/", page1.NextContinuationToken, maxKeys: 2);
        var page3 = await fake.ListAsync("posts/", page2.NextContinuationToken, maxKeys: 2);

        Assert.Equal(2, page1.Items.Count);
        Assert.Equal(2, page2.Items.Count);
        Assert.Single(page3.Items);
        Assert.Null(page3.NextContinuationToken);
        Assert.All(page1.Items.Concat(page2.Items).Concat(page3.Items), i => Assert.StartsWith("posts/", i.Key));
    }

    [Fact]
    public void URL_gia_khong_giong_R2_va_khong_mang_chu_ky()
    {
        var fake = new FakeObjectStorage();
        var url = fake.CreatePresignedPut("posts/u/a.jpg", "image/jpeg", 1);

        Assert.StartsWith("https://fake.invalid/", url);
        Assert.DoesNotContain("X-Amz-Signature", url);
        Assert.DoesNotContain("r2.cloudflarestorage.com", url);
    }
}
