using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SocialApp.IntegrationTests.Harness;
using SocialApp.SharedKernel.Audit;
using SocialApp.SharedKernel.Moderation;
using SocialApp.SharedKernel.Redis;
using Xunit;

namespace SocialApp.IntegrationTests.Moderation;

/// <summary>
/// GĐ6 D7c — mốc 3 qua API: ẩn bài + đóng báo cáo + audit là MỘT transaction. Lỗi ở bất kỳ bước nào → không gì thay đổi. Bản hạ tầng
/// (gọi thẳng hợp đồng C2) là <c>ModerationTargetsTests.TX_02_…</c>; đây là bản đi hết đường <c>PATCH /reports</c>.
///
/// Hai decorator đăng ký vào host của lớp này: <b>ghi THẬT rồi mới ném</b>, trên đúng <see cref="DbTransaction"/> được truyền vào (cạm bẫy
/// 2 — decorator mở kết nối riêng là <c>TX-01</c> xanh giả). Nhờ ghi thật, đột biến "audit ghi <c>tx: null</c>" để lại một dòng audit đã
/// COMMIT trên kết nối riêng, và "provider ghi trên kết nối khác" để lại bài <c>hidden</c> — cả hai làm ca đỏ. Decorator ném ngay từ đầu
/// thì không phân biệt được hai đột biến đó với code đúng.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ModerationTransactionTests(PostgresFixture postgres, RedisFixture redis, ModulesApiFactory factory)
    : IClassFixture<RedisFixture>, IClassFixture<ModulesApiFactory>, IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        factory.UseRedis(redis.ConnectionString);
        factory.UseTestServices(services =>
        {
            services.AddSingleton<Faults>();
            Decorate<IAuditTrail>(services, _ => true,
                (sp, inner) => new AuditThenThrow((IAuditTrail)inner, sp.GetRequiredService<Faults>()));
            Decorate<IModerationTargetProvider>(services, d => d.ImplementationType?.Name == "ContentModerationTargets",
                (sp, inner) => new HideThenThrow((IModerationTargetProvider)inner, sp.GetRequiredService<Faults>()));
        });
        await factory.UseFreshDatabaseAsync(postgres);
        Assert.True((await factory.Services.GetRequiredService<RedisConnection>().GetAsync()).IsConnected);
    }

    public Task DisposeAsync()
    {
        using var conn = new NpgsqlConnection(factory.ConnectionString);
        NpgsqlConnection.ClearPool(conn);
        return Task.CompletedTask;
    }

    /// <summary>
    /// <c>TX-01</c> ⭐: <see cref="IAuditTrail"/> GHI dòng <c>report.hide</c> rồi ném → 500; bài VẪN <c>published</c>, báo cáo VẪN
    /// <c>open</c>, 0 dòng audit. Đột biến bắt buộc của B5 (bản qua API): audit ghi <c>tx: null</c> → ca này đỏ.
    /// </summary>
    [Fact]
    public async Task TX_01_audit_hong_giua_chung_khong_gi_thay_doi()
    {
        var (client, p, r) = await BaiCoBaoCaoAsync();
        factory.Services.GetRequiredService<Faults>().AuditThrowsOn = AuditActions.ReportHide;

        try
        {
            using var response = await QuyetAsync(client, r, "hide");
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }
        finally
        {
            factory.Services.GetRequiredService<Faults>().AuditThrowsOn = null;
        }

        await KhongGiThayDoiAsync(client, p);
    }

    /// <summary><c>TX-02</c>: provider bài ẨN THẬT (trên tx) rồi ném → 500; không gì thay đổi — kể cả bài, vì bản ghi của provider rollback.</summary>
    [Fact]
    public async Task TX_02_provider_hong_sau_khi_an_khong_gi_thay_doi()
    {
        var (client, p, r) = await BaiCoBaoCaoAsync();
        factory.Services.GetRequiredService<Faults>().HideThrows = true;

        try
        {
            using var response = await QuyetAsync(client, r, "hide");
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }
        finally
        {
            factory.Services.GetRequiredService<Faults>().HideThrows = false;
        }

        await KhongGiThayDoiAsync(client, p);
    }

    /// <summary>Đối chứng: không bật lỗi nào → cùng đường đó 200 và ghi đủ. Không có ca này thì hai ca trên xanh cả khi decorator hỏng host.</summary>
    [Fact]
    public async Task Doi_chung_khong_loi_thi_hide_200_ghi_du()
    {
        var (client, p, r) = await BaiCoBaoCaoAsync();

        using var response = await QuyetAsync(client, r, "hide");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("hidden", (await client.QueryRowAsync("select status from content.posts where post_id = $1", p))!["status"]);
        Assert.Equal(1L, (await client.QueryRowAsync(
            "select count(*) as n from moderation.audit_logs where target_id = $1", p))!["n"]);
    }

    private async Task<(ModulesTestClient Client, Guid Post, Guid Report)> BaiCoBaoCaoAsync()
    {
        var client = new ModulesTestClient(factory);
        var tacGia = Guid.NewGuid();
        await client.PutProfileOkAsync(tacGia, new { displayName = "Tác giả TX" });
        var p = (await client.CreatePostOkAsync(tacGia, new { body = "Bài TX.", privacy = "public", mediaKeys = Array.Empty<object>() })).PostId;
        using var bao = await client.CreateReportAsync(Guid.NewGuid(), new { targetType = "post", targetId = p, reasonCode = "spam" });
        Assert.Equal(HttpStatusCode.Created, bao.StatusCode);
        using var doc = JsonDocument.Parse(await bao.Content.ReadAsStringAsync());
        return (client, p, doc.RootElement.GetProperty("reportId").GetGuid());
    }

    private static async Task<HttpResponseMessage> QuyetAsync(ModulesTestClient client, Guid reportId, string decision)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/reports/{reportId}")
        {
            Content = JsonContent.Create(new { decision }),
        };
        request.Headers.Authorization = ModulesTestClient.Bearer(Guid.NewGuid(), "MODERATOR");
        return await client.Http.SendAsync(request);
    }

    private static async Task KhongGiThayDoiAsync(ModulesTestClient client, Guid post)
    {
        var bai = await client.QueryRowAsync("select status, hidden_reason from content.posts where post_id = $1", post);
        Assert.Equal("published", bai!["status"]);
        Assert.Null(bai["hidden_reason"]);
        Assert.Equal("open", (await client.QueryRowAsync(
            "select status from moderation.reports where target_id = $1", post))!["status"]);
        Assert.Equal(0L, (await client.QueryRowAsync(
            "select count(*) as n from moderation.audit_logs where target_id = $1", post))!["n"]);
    }

    /// <summary>
    /// Thay đăng ký <typeparamref name="T"/> khớp <paramref name="match"/> bằng decorator bọc CHÍNH hiện thực cũ (dựng bằng
    /// <see cref="ActivatorUtilities"/> — hiện thực là <c>internal</c> của module, test không gọi <c>new</c> được).
    /// </summary>
    private static void Decorate<T>(
        IServiceCollection services, Func<ServiceDescriptor, bool> match, Func<IServiceProvider, object, T> decorate)
        where T : class
    {
        var descriptor = services.Last(d => d.ServiceType == typeof(T) && match(d));
        var index = services.IndexOf(descriptor);
        services[index] = new ServiceDescriptor(typeof(T), sp => decorate(sp, descriptor switch
        {
            { ImplementationInstance: { } instance } => instance,
            { ImplementationFactory: { } create } => create(sp),
            _ => ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType!),
        }), descriptor.Lifetime);
    }

    private sealed class Faults
    {
        public volatile string? AuditThrowsOn;

        public volatile bool HideThrows;
    }

    private sealed class AuditThenThrow(IAuditTrail inner, Faults faults) : IAuditTrail
    {
        public async Task AppendAsync(DbTransaction? tx, AuditEntry entry, CancellationToken ct = default)
        {
            await inner.AppendAsync(tx, entry, ct);
            if (faults.AuditThrowsOn == entry.Action)
                throw new InvalidOperationException("TX-01: audit hỏng sau khi đã ghi.");
        }
    }

    private sealed class HideThenThrow(IModerationTargetProvider inner, Faults faults) : IModerationTargetProvider
    {
        public ModerationTargetType Type => inner.Type;

        public Task<IReadOnlyDictionary<Guid, TargetSnapshot>> GetSnapshotsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            inner.GetSnapshotsAsync(ids, ct);

        public Task<bool> CanViewAsync(Guid actorId, Guid id, CancellationToken ct) => inner.CanViewAsync(actorId, id, ct);

        public async Task<HideOutcome> HideAsync(DbTransaction tx, Guid id, string reasonCode, CancellationToken ct)
        {
            var outcome = await inner.HideAsync(tx, id, reasonCode, ct);
            if (faults.HideThrows)
                throw new InvalidOperationException("TX-02: provider hỏng sau khi đã ẩn.");
            return outcome;
        }

        public Task<RestoreOutcome> RestoreAsync(DbTransaction tx, Guid id, CancellationToken ct) => inner.RestoreAsync(tx, id, ct);
    }
}
