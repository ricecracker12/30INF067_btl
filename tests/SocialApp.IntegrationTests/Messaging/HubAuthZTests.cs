using System.Net;
using SocialApp.IntegrationTests.Harness;
using SocialApp.SharedKernel.Authentication;
using Xunit;

namespace SocialApp.IntegrationTests.Messaging;

/// <summary>
/// Test hub — "cửa thứ hai mà matrix HTTP không thấy" (giai-doan-5.md Mục 6.4). Category <c>AuthZ</c> để nằm trong CÙNG cổng CI
/// "AuthZ matrix": quên trait là nhóm này chạy lẫn vào test thường và cổng không canh hub (Mục 10.5 #3).
///
/// Mọi kết nối đi đúng đường client thật: WebSockets + SkipNegotiation, vé trên query (<see cref="RealtimeTestClient"/>).
/// Redis THẬT (vé nằm trong Redis) — <see cref="RedisFixture"/>.
/// </summary>
[Trait("Category", "AuthZ")]
[Collection(PostgresCollection.Name)]
public sealed class HubAuthZTests(PostgresFixture postgres, RedisFixture redis, ModulesApiFactory factory)
    : IClassFixture<RedisFixture>, IClassFixture<ModulesApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync()
    {
        factory.UseRedis(redis.ConnectionString);
        return factory.UseFreshDatabaseAsync(postgres);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient Http => new ModulesTestClient(factory).Http;

    /// <summary>HUB-01: bắt tay không vé → 401.</summary>
    [Fact]
    public async Task HUB_01_ket_noi_khong_ve_401()
    {
        await using var connection = RealtimeTestClient.Build(factory, () => Task.FromResult<string?>(null));

        await RealtimeTestClient.AssertHandshakeRejectedAsync(connection);
    }

    /// <summary>Đối chứng của cả nhóm: vé hợp lệ → bắt tay xong. Không có ca này thì mọi ca "401" xanh cả khi hub hỏng hẳn.</summary>
    [Fact]
    public async Task HUB_00_ve_hop_le_bat_tay_thanh_cong()
    {
        await using var connection = await RealtimeTestClient.ConnectAsync(factory, Http, Guid.NewGuid());

        Assert.Equal(Microsoft.AspNetCore.SignalR.Client.HubConnectionState.Connected, connection.State);
    }

    /// <summary>HUB-02: dùng lại vé đã dùng → 401 (GETDEL — vé dùng một lần).</summary>
    [Fact]
    public async Task HUB_02_dung_lai_ve_da_dung_401()
    {
        var ticket = await RealtimeTestClient.IssueTicketAsync(Http, Guid.NewGuid());
        await using (var first = RealtimeTestClient.Build(factory, () => Task.FromResult<string?>(ticket)))
            await first.StartAsync();

        await using var second = RealtimeTestClient.Build(factory, () => Task.FromResult<string?>(ticket));

        await RealtimeTestClient.AssertHandshakeRejectedAsync(second);
    }

    /// <summary>HUB-04: người dùng bị <c>revoked:user</c> SAU lúc cấp vé → bắt tay 401.</summary>
    [Fact]
    public async Task HUB_04_ve_cua_nguoi_da_bi_thu_hoi_401()
    {
        var userId = Guid.NewGuid();
        var ticket = await RealtimeTestClient.IssueTicketAsync(Http, userId, issuedAt: DateTimeOffset.UtcNow.AddSeconds(-60));
        await factory.Service<ITokenRevocationStore>().RevokeUserAsync(userId, DateTimeOffset.UtcNow);

        await using var connection = RealtimeTestClient.Build(factory, () => Task.FromResult<string?>(ticket));

        await RealtimeTestClient.AssertHandshakeRejectedAsync(connection);
    }

    /// <summary>
    /// HUB-05: access token JWT thật đặt vào <c>?access_token=</c> → 401. Hub không nhận bearer — nếu nhận thì JWT 15 phút nằm
    /// trong log truy cập, đúng thứ Đ-E16 sinh ra để tránh.
    /// </summary>
    [Fact]
    public async Task HUB_05_jwt_that_tren_query_bi_tu_choi()
    {
        var jwt = TestJwt.Create("USER", Guid.NewGuid());

        await using var connection = RealtimeTestClient.Build(factory, () => Task.FromResult<string?>(jwt));

        await RealtimeTestClient.AssertHandshakeRejectedAsync(connection);
    }

    /// <summary>HUB-06: vé đặt vào <c>Authorization: Bearer</c> của REST → 401 (REST không nhận vé).</summary>
    [Fact]
    public async Task HUB_06_ve_lam_bearer_o_REST_401()
    {
        var http = Http;
        var ticket = await RealtimeTestClient.IssueTicketAsync(http, Guid.NewGuid());

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/friends");
        request.Headers.Authorization = new("Bearer", ticket);
        using var response = await http.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>Đ-5.9: policy <c>realtime-ticket</c> 20 lần/phút/user — lần 21 là 429; người khác không bị ảnh hưởng.</summary>
    [Fact]
    public async Task Xin_ve_qua_20_lan_moi_phut_429_nguoi_khac_khong_anh_huong()
    {
        var http = Http;
        var userId = Guid.NewGuid();
        for (var i = 0; i < 20; i++)
            await RealtimeTestClient.IssueTicketAsync(http, userId);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/realtime/tickets");
        request.Headers.Authorization = new("Bearer", TestJwt.Create("USER", userId));
        using var response = await http.SendAsync(request);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        await RealtimeTestClient.IssueTicketAsync(http, Guid.NewGuid());
    }

    /// <summary>TC-A01-ticket (AuthZ matrix cũng có dòng này): xin vé không kèm JWT → 401.</summary>
    [Fact]
    public async Task Xin_ve_khong_JWT_401()
    {
        using var response = await Http.PostAsync("/api/v1/realtime/tickets", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
