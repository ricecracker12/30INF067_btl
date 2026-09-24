using SocialApp.Modules.Profile.Application.Search;
using SocialApp.SharedKernel.Contracts;
using SocialApp.SharedKernel.Storage;

namespace SocialApp.UnitTests.Profile;

/// <summary>
/// GĐ6 D12 — phần thuần của <c>GET /search</c>: biên của <see cref="SearchUsersQueryValidator"/> (đo SAU trim, key <c>q</c>), và việc
/// <see cref="SearchService"/> làm với kết quả của DB (escape trước khi gửi, lấy dư, lọc tài khoản không hoạt động RỒI mới cắt, ký ảnh).
/// Truy vấn thật (không dấu, tiền tố từng từ, index) ở <c>SearchTests</c> của IntegrationTests.
/// </summary>
public sealed class SearchServiceTests
{
    private static readonly SearchUsersQueryValidator Validator = new();

    /// <summary>Tên trường viết thường: bộ đổi tên camelCase toàn cục của FluentValidation chỉ đặt ở Program.cs.</summary>
    private static IReadOnlyList<string> Loi(string? q, string? type = null, int? limit = null) =>
        [.. Validator.Validate(new SearchUsersQuery { Q = q, Type = type, Limit = limit })
            .Errors.Select(e => e.PropertyName.ToLowerInvariant())];

    [Theory]
    [InlineData("an", true)]
    [InlineData("  an  ", true)]    // 2 ký tự sau trim
    [InlineData("a", false)]
    [InlineData("  a  ", false)]
    [InlineData("   ", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Tu_khoa_do_sau_khi_trim_loi_duoi_key_q(string? q, bool hopLe) =>
        Assert.Equal(hopLe ? [] : ["q"], Loi(q));

    [Fact]
    public void Tu_khoa_toi_da_50_ky_tu()
    {
        Assert.Empty(Loi(new string('x', 50)));
        Assert.Empty(Loi("  " + new string('x', 50) + "  "));
        Assert.Equal(["q"], Loi(new string('x', 51)));
    }

    [Fact]
    public void Type_chi_user_limit_1_den_20()
    {
        Assert.Empty(Loi("an", "user", 1));
        Assert.Empty(Loi("an", null, 20));
        Assert.Equal(["type"], Loi("an", "post"));
        Assert.Equal(["type"], Loi("an", "USER"));
        Assert.Equal(["limit"], Loi("an", limit: 0));
        Assert.Equal(["limit"], Loi("an", limit: 21));
    }

    /// <summary>
    /// Từ khóa gửi xuống DB đã trim VÀ escape (<c>%</c>, <c>_</c>, <c>\</c> theo nghĩa đen); bản thô (đã trim) chỉ để xếp hạng. Lấy dư
    /// <see cref="SearchService.Overfetch"/> dòng.
    /// </summary>
    [Fact]
    public async Task Gui_tu_khoa_da_escape_va_lay_du()
    {
        var search = new FakeSearch([]);
        await new SearchService(search, new FakeAccounts([]), new FakeStorage())
            .SearchAsync(new SearchUsersQuery { Q = "  a_%\\b  ", Limit = 3 }, default);

        Assert.Equal(("a\\_\\%\\\\b", "a_%\\b", 3 + SearchService.Overfetch), search.Call);
    }

    /// <summary>Lọc người không hoạt động TRƯỚC khi cắt <c>limit</c>: người bị khóa đứng đầu không làm trang thiếu. Thứ tự của DB giữ nguyên.</summary>
    [Fact]
    public async Task Loc_tai_khoan_khong_hoat_dong_roi_moi_cat_limit()
    {
        var hits = Enumerable.Range(0, 5).Select(i => new SearchHit(Guid.NewGuid(), $"Người {i}", null)).ToList();
        var page = await new SearchService(new FakeSearch(hits), new FakeAccounts([hits[0].UserId, hits[2].UserId]), new FakeStorage())
            .SearchAsync(new SearchUsersQuery { Q = "ng", Limit = 2 }, default);

        Assert.Equal([hits[1].UserId, hits[3].UserId], page.Items.Select(i => i.UserId));
    }

    [Fact]
    public async Task Anh_dai_dien_ky_san_khong_anh_thi_null_khong_khop_khong_goi_trang_thai()
    {
        var coAnh = new SearchHit(Guid.NewGuid(), "Có ảnh", "avatars/x.jpg");
        var khongAnh = new SearchHit(Guid.NewGuid(), "Không ảnh", null);
        var page = await new SearchService(new FakeSearch([coAnh, khongAnh]), new FakeAccounts([]), new FakeStorage())
            .SearchAsync(new SearchUsersQuery { Q = "co" }, default);
        Assert.Equal(["signed:avatars/x.jpg", null], page.Items.Select(i => i.AvatarUrl));

        var accounts = new FakeAccounts([]);
        var empty = await new SearchService(new FakeSearch([]), accounts, new FakeStorage())
            .SearchAsync(new SearchUsersQuery { Q = "zz" }, default);
        Assert.Empty(empty.Items);
        Assert.Equal(0, accounts.Calls);
    }

    private sealed class FakeSearch(IReadOnlyList<SearchHit> hits) : IProfileSearch
    {
        public (string Escaped, string Raw, int Take) Call { get; private set; }

        public Task<IReadOnlyList<SearchHit>> SearchAsync(string escapedTerm, string rawTerm, int take, CancellationToken ct)
        {
            Call = (escapedTerm, rawTerm, take);
            return Task.FromResult(hits);
        }
    }

    private sealed class FakeAccounts(IReadOnlyCollection<Guid> inactive) : IAccountStatusReader
    {
        public int Calls { get; private set; }

        public Task<IReadOnlySet<Guid>> GetInactiveAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult<IReadOnlySet<Guid>>(userIds.Where(inactive.Contains).ToHashSet());
        }
    }

    private sealed class FakeStorage : IObjectStorage
    {
        public string CreatePresignedGet(string key) => "signed:" + key;

        public Task<ObjectHead?> HeadAsync(string key, CancellationToken ct = default) => throw new NotSupportedException();

        public string CreatePresignedPut(string key, string contentType, long contentLength) => throw new NotSupportedException();

        public Task DeleteAsync(string key, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<ObjectPage> ListAsync(string prefix, string? continuationToken, int maxKeys, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
