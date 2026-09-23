using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.Content.Application.Media;
using SocialApp.Modules.Content.Domain;
using SocialApp.SharedKernel.Errors;

namespace SocialApp.IntegrationTests.Content;

/// <summary>
/// D4 — <c>POST /media/uploads</c>. Endpoint duy nhất của khối D <b>không chạm DB</b>: mọi khẳng định ở đây là về hình
/// dạng phản hồi, hai mức quyền của Đ-2.6, và thứ KHÔNG được xuất hiện trong log.
///
/// Vẫn cần Postgres dù endpoint không đọc bảng nào: tầng 2 tra <c>identity.role_permissions</c> để biết vai trò
/// <c>USER</c> có <c>post.create</c> hay không (Q-D5), và bảng đó do <see cref="ModulesApiFactory"/> seed.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class MediaUploadsTests(PostgresFixture postgres, ModulesApiFactory factory)
    : IClassFixture<ModulesApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.UseFreshDatabaseAsync(postgres);

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Dạng key hợp đồng ghi cho ảnh bài (<c>MediaKeyDeclaration.mediaKey.pattern</c> của <c>content-v1.yaml</c>), có
    /// chèn id người gọi. Gõ regex ở test thay vì gọi <c>StorageKeys</c>: dùng chung nguồn với code sản phẩm thì code
    /// sinh sai dạng test cũng sai theo và vẫn xanh.
    /// </summary>
    private static Regex PostKeyPattern(Guid actor) =>
        new($"^posts/{actor:D}/[0-9a-f]{{32}}[.](jpg|png|webp)$", RegexOptions.None, TimeSpan.FromSeconds(1));

    private static object File(string contentType, long sizeBytes) => new { contentType, sizeBytes };

    private static async Task<IReadOnlyList<UploadTicket>> ReadTicketsAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<List<UploadTicket>>())!;
    }

    /// <summary>
    /// Nhánh chính: một request, ba file, ba ticket <b>cùng thứ tự</b> — FE đang cầm mảng <c>File</c> và ghép theo chỉ
    /// số, không có gì khác để ghép. Kiểm luôn bốn thứ hợp đồng hứa cho mỗi ticket: dạng key mang tiền tố người GỌI
    /// (Đ-2.7 — không phải id nào trong body, vì body không có id nào), đuôi suy từ <c>contentType</c>, <c>expiresIn</c>
    /// đúng 600, và <c>requiredHeaders</c> khớp khai báo.
    ///
    /// Key phải ĐÔI MỘT KHÁC NHAU kể cả khi hai file khai giống hệt nhau: <c>StorageKeys</c> sinh UUID v7 mới mỗi lần,
    /// và hai ticket trùng key nghĩa là file thứ hai ghi đè file thứ nhất trên bucket.
    /// </summary>
    [Fact]
    public async Task Lo_ba_file_tra_201_ba_ticket_cung_thu_tu_voi_key_cua_nguoi_goi()
    {
        var client = new ModulesTestClient(factory);
        var actor = Guid.NewGuid();
        var files = new[] { File("image/jpeg", 1_048_576), File("image/png", 204_800), File("image/webp", 1) };

        using var response = await client.CreateUploadsAsync(actor, new { purpose = "post", files });
        var tickets = await ReadTicketsAsync(response);

        Assert.Equal(3, tickets.Count);
        Assert.Equal(["jpg", "png", "webp"], tickets.Select(t => t.MediaKey.Split('.')[^1]));
        Assert.Equal(["1048576", "204800", "1"], tickets.Select(t => t.RequiredHeaders.ContentLength));
        Assert.Equal(["image/jpeg", "image/png", "image/webp"], tickets.Select(t => t.RequiredHeaders.ContentType));
        Assert.All(tickets, t =>
        {
            Assert.Matches(PostKeyPattern(actor), t.MediaKey);
            Assert.Equal(600, t.ExpiresIn);
            Assert.Equal($"https://fake.invalid/put/{t.MediaKey}", t.UploadUrl);
        });
        Assert.Equal(3, tickets.Select(t => t.MediaKey).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// <c>requiredHeaders</c> ra JSON với ĐÚNG hai key <c>Content-Type</c> và <c>Content-Length</c> — có gạch nối, viết
    /// hoa như header HTTP. Khẳng định trên JSON THÔ chứ không trên DTO đã deserialize: chính sách camelCase toàn cục
    /// của <c>Program.cs</c> sẽ đổi chúng thành <c>contentType</c>/<c>contentLength</c> nếu ai bỏ
    /// <c>[JsonPropertyName]</c> đi, và DTO thì vẫn đọc lại được bản sai đó nên test qua DTO sẽ xanh.
    ///
    /// <c>Content-Length</c> là CHUỖI (<c>"1024"</c>, không phải <c>1024</c>): header HTTP là chuỗi, và hợp đồng ghi
    /// <c>type: string</c> — FE sinh type từ yaml thì so sánh kiểu sẽ đỏ compile nếu ta trả số.
    /// </summary>
    [Fact]
    public async Task requiredHeaders_ra_JSON_dung_hai_key_co_gach_noi_va_Content_Length_la_chuoi()
    {
        var client = new ModulesTestClient(factory);

        using var response = await client.CreateUploadsAsync(
            Guid.NewGuid(), new { purpose = "avatar", files = new[] { File("image/jpeg", 1024) } });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();

        Assert.Contains(
            "\"requiredHeaders\":{\"Content-Type\":\"image/jpeg\",\"Content-Length\":\"1024\"}",
            json,
            StringComparison.Ordinal);
        Assert.DoesNotContain("contentLength", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>purpose=avatar</c> cho key dưới tiền tố <c>avatars/</c> — cùng dạng mà <c>PUT /users/me/avatar</c> (D3) đòi,
    /// nên ticket của lô này dùng thẳng được ở đó. Đây là chỗ hai đầu việc gặp nhau; lệch tiền tố thì D4 xanh, D3 xanh,
    /// mà luồng thật của FE đứt ở bước cuối.
    /// </summary>
    [Fact]
    public async Task purpose_avatar_cho_key_duoi_tien_to_avatars()
    {
        var client = new ModulesTestClient(factory);
        var actor = Guid.NewGuid();

        using var response = await client.CreateUploadsAsync(
            actor, new { purpose = "avatar", files = new[] { File("image/png", 2048) } });
        var ticket = Assert.Single(await ReadTicketsAsync(response));

        Assert.Matches(
            new Regex($"^avatars/{actor:D}/[0-9a-f]{{32}}[.]png$", RegexOptions.None, TimeSpan.FromSeconds(1)),
            ticket.MediaKey);
    }

    /// <summary>
    /// GĐ7 C2: <c>socialapp_presign_issued_total</c> đếm SỐ URL đã ký (một lô 3 file là 3, không phải 1), tách theo nhãn
    /// <c>purpose</c> — số này đem đối chiếu với số object trên R2 nên phải đếm theo object.
    /// </summary>
    [Fact]
    public async Task C2_presign_issued_dem_dung_so_url_theo_muc_dich()
    {
        const string Metric = "socialapp_presign_issued_total";
        var client = new ModulesTestClient(factory);
        using var http = factory.CreateClient();
        var actor = Guid.NewGuid();
        var truocPost = await MetricsReader.ReadAsync(http, Metric, "purpose=\"post\"");
        var truocAvatar = await MetricsReader.ReadAsync(http, Metric, "purpose=\"avatar\"");

        using (var lo = await client.CreateUploadsAsync(actor, new
               {
                   purpose = "post",
                   files = new[] { File("image/jpeg", 1024), File("image/png", 1024), File("image/webp", 1024) },
               }))
            Assert.Equal(3, (await ReadTicketsAsync(lo)).Count);
        using (var avatar = await client.CreateUploadsAsync(
                   actor, new { purpose = "avatar", files = new[] { File("image/png", 2048) } }))
            Assert.Single(await ReadTicketsAsync(avatar));

        Assert.Equal(truocPost + 3, await MetricsReader.ReadAsync(http, Metric, "purpose=\"post\""));
        Assert.Equal(truocAvatar + 1, await MetricsReader.ReadAsync(http, Metric, "purpose=\"avatar\""));
    }

    /// <summary>
    /// <b>Đ-2.6 + Q-D5 — hai mức quyền tách nhau thật.</b> CÙNG một vai trò <c>GUEST</c> (không có dòng nào trong
    /// <c>role_permissions</c> nên thiếu <c>post.create</c>): <c>purpose=post</c> → <b>403</b>, <c>purpose=avatar</c> →
    /// <b>201</b>. Hai lượt gọi trong MỘT test là cố ý — tách đôi thì bản đặt <c>[RequirePermission("post.create")]</c>
    /// lên cả action vẫn làm một nửa xanh.
    ///
    /// Vai trò không tồn tại thay vì xóa dòng <c>role_permissions</c> của <c>USER</c>: <c>PermissionCache</c> có TTL và
    /// là của app, còn DB là của riêng lớp test — xóa dòng thì đỏ/xanh tùy thứ tự chạy.
    /// </summary>
    [Fact]
    public async Task Q_D5_vai_tro_thieu_post_create_bi_403_o_purpose_post_nhung_van_201_o_purpose_avatar()
    {
        var client = new ModulesTestClient(factory);
        var actor = Guid.NewGuid();
        var files = new[] { File("image/jpeg", 1024) };

        using var post = await client.CreateUploadsAsync(actor, new { purpose = "post", files }, role: "GUEST");
        var (status, title, _) = await ModulesTestClient.ReadProblemAsync(post);
        Assert.Equal((int)HttpStatusCode.Forbidden, status);
        Assert.Equal(ProblemTitles.Forbidden, title);

        using var avatar = await client.CreateUploadsAsync(actor, new { purpose = "avatar", files }, role: "GUEST");
        Assert.Single(await ReadTicketsAsync(avatar));
    }

    /// <summary>
    /// <b>Q-D5, nửa còn lại.</b> ADMIN không có dòng <c>role_permissions</c> nào (<c>RBAC-01</c>) nên
    /// <c>IPermissionCache.GetAsync("ADMIN")</c> trả RỖNG — gọi thẳng cache như Đ-2.6 viết ban đầu thì chính Admin
    /// không xin được URL tải ảnh bài. Đi qua <c>IAuthorizationService</c> thì short-circuit của
    /// <c>PermissionHandler</c> làm việc của nó.
    ///
    /// Đây là test bắt được đúng loại hỏng mà kiểm tay KHÔNG bắt được: người kiểm bằng tài khoản USER thấy mọi thứ chạy
    /// tốt, chỉ Admin hỏng — và ngược lại với bản gõ sai mã quyền.
    /// </summary>
    [Fact]
    public async Task Q_D5_ADMIN_khong_co_dong_role_permissions_van_xin_duoc_URL_anh_bai()
    {
        var client = new ModulesTestClient(factory);

        using var response = await client.CreateUploadsAsync(
            Guid.NewGuid(), new { purpose = "post", files = new[] { File("image/jpeg", 1024) } }, role: "ADMIN");

        Assert.Single(await ReadTicketsAsync(response));
    }

    /// <summary>
    /// Mọi cách hỏng của <c>files</c> ra 400 dưới ĐÚNG key <c>files</c> của hợp đồng — tầng integration canh chính cái
    /// mà unit test không canh được: hạ camelCase do <c>ValidatorOptions.Global.PropertyNameResolver</c> của host, và
    /// đường đi qua <c>ValidationProblemDetails</c>.
    ///
    /// Ca biên đi kèm ở <c>CreateUploadsRequestValidatorTests</c>; ở đây là năm ca của bảng Mục 0.
    /// </summary>
    [Fact]
    public async Task Nam_cach_hong_cua_files_deu_ra_400_duoi_key_files()
    {
        var client = new ModulesTestClient(factory);
        var actor = Guid.NewGuid();

        object[] bodies =
        [
            new { purpose = "post", files = Enumerable.Repeat(File("image/jpeg", 1024), PostContentPolicy.MaxMediaCount + 1) },
            new { purpose = "post", files = new[] { File("image/gif", 1024) } },
            new { purpose = "post", files = new[] { File("image/jpeg", 0) } },
            new { purpose = "post", files = new[] { File("image/jpeg", MediaAttachment.MaxSizeBytes + 1L) } },
            new { purpose = "post", files = Array.Empty<object>() },
        ];

        foreach (var body in bodies)
        {
            using var response = await client.CreateUploadsAsync(actor, body);
            var (status, title, errors) = await ModulesTestClient.ReadProblemAsync(response);

            Assert.Equal((int)HttpStatusCode.BadRequest, status);
            Assert.Equal(ProblemTitles.BadRequest, title);
            Assert.Equal(["files"], errors.Keys);
        }
    }

    /// <summary>
    /// <b>Q-D2</b> — <c>purpose</c> vắng mặt và <c>purpose</c> lạ đều ra 400 dưới key <c>purpose</c>, KHÔNG âm thầm
    /// thành <c>post</c>. Hai nhánh đi hai đường khác nhau và cùng phải tới một chỗ: vắng mặt do validator
    /// (<c>NotNull</c> trên <c>UploadPurpose?</c>), giá trị lạ do System.Text.Json ném ở input formatter — lỗi đó vào
    /// <c>ModelState</c> với key <c>$.purpose</c> và <c>ValidationErrors.From</c> hạ thành <c>purpose</c>.
    /// </summary>
    [Fact]
    public async Task Q_D2_purpose_vang_mat_hay_la_deu_ra_400_duoi_key_purpose()
    {
        var client = new ModulesTestClient(factory);
        var actor = Guid.NewGuid();
        var files = new[] { File("image/jpeg", 1024) };

        object[] bodies = [new { files }, new { purpose = "everyone", files }];

        foreach (var body in bodies)
        {
            using var response = await client.CreateUploadsAsync(actor, body);
            var (status, _, errors) = await ModulesTestClient.ReadProblemAsync(response);

            Assert.Equal((int)HttpStatusCode.BadRequest, status);
            Assert.Contains("purpose", errors.Keys, StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// <b>Mục 1.3 luật 9 — không log <c>uploadUrl</c>.</b> URL mang chữ ký, nên một dòng log là một quyền ghi vào bucket
    /// còn hạn 10 phút cho bất kỳ ai đọc được log. Khẳng định trên <b>cả</b> template lẫn giá trị đã render: log có cấu
    /// trúc giữ tham số riêng khỏi câu, nên chỉ đọc <c>MessageTemplate</c> là bỏ sót đúng trường hợp nguy hiểm
    /// (<c>Log("Cấp ticket {Url}", url)</c>).
    ///
    /// Kiểm luôn <c>mediaKey</c> không lọt vào log: key kèm id người dùng là bản đồ ảnh riêng tư của họ.
    /// Dòng log hợp lệ vẫn phải CÓ, nếu không thì test này xanh cả khi ai đó xóa hẳn log đi.
    /// </summary>
    [Fact]
    public async Task Khong_log_uploadUrl_lan_mediaKey()
    {
        var logs = new CapturingLogSink();
        await using var app = factory.WithWebHostBuilder(b =>
            b.ConfigureTestServices(s => s.AddSingleton<ILogEventSink>(logs)));
        using var http = app.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/media/uploads")
        {
            Content = JsonContent.Create(new { purpose = "post", files = new[] { File("image/jpeg", 1024) } }),
        };
        request.Headers.Authorization = ModulesTestClient.Bearer(Guid.NewGuid());
        using var response = await http.SendAsync(request);
        var ticket = Assert.Single(await ReadTicketsAsync(response));

        var lines = logs.Events
            .Select(e => string.Join(
                " ",
                e.RenderMessage(),
                e.MessageTemplate.Text,
                string.Join(" ", e.Properties.Select(p => p.Value.ToString()))))
            .ToList();

        Assert.DoesNotContain(lines, line => line.Contains("fake.invalid/put", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains(ticket.MediaKey, StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("ticket tải lên", StringComparison.Ordinal));
    }

    /// <summary>Tầng 1: không token thì không tới được validator, và không tốn lượt nào của hạn mức nghiệp vụ.</summary>
    [Fact]
    public async Task An_danh_tra_401()
    {
        var client = new ModulesTestClient(factory);

        using var response = await client.Http.PostAsJsonAsync(
            "/api/v1/media/uploads", new { purpose = "post", files = new[] { File("image/jpeg", 1024) } });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
