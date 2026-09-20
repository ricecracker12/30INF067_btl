using SocialApp.Modules.Content.Application;
using SocialApp.Modules.Content.Application.Posts;
using SocialApp.Modules.Content.Domain;
using SocialApp.SharedKernel.Storage;

namespace SocialApp.UnitTests.Content;

/// <summary>
/// D5 — <see cref="CreatePostRequestValidator"/>. Ở tầng unit vì đây là hàm thuần trên DTO; integration chỉ cần vài ca
/// chứng minh validator được nối vào đường request với đúng key.
///
/// Key ở đây là tên CLR (<c>Privacy</c>, <c>MediaKeys</c>) cho luật gắn với thuộc tính, nhưng là <b>key hợp đồng</b>
/// (<c>body</c>, <c>mediaKeys</c>) cho luật BR-01 — vì luật đó gọi <c>ctx.AddFailure(key, ...)</c> với key do
/// <see cref="PostContentPolicy"/> chọn, không qua <c>PropertyNameResolver</c> của host. Sự lệch đó là có thật và
/// <c>CreatePostTests</c> canh phía đầu kia.
/// </summary>
public sealed class CreatePostRequestValidatorTests
{
    private static readonly CreatePostRequestValidator Validator = new();

    private static IReadOnlyList<(string Key, string Message)> Errors(CreatePostRequest request) =>
        Validator.Validate(request).Errors.Select(e => (e.PropertyName, e.ErrorMessage)).ToList();

    private static MediaKeyDeclaration Media(Guid owner, string contentType = "image/jpeg", long sizeBytes = 1024) =>
        new() { MediaKey = StorageKeys.ForPost(owner, contentType), ContentType = contentType, SizeBytes = sizeBytes };

    /// <summary>Bài chỉ có chữ, và bài chỉ có ảnh — hai nhánh hợp lệ của mệnh đề 3 trong BR-01.</summary>
    [Fact]
    public void Bai_chi_chu_va_bai_chi_anh_deu_hop_le()
    {
        Assert.Empty(Errors(new CreatePostRequest { Body = "Chỉ chữ.", Privacy = PostPrivacy.Private }));
        Assert.Empty(Errors(new CreatePostRequest { Privacy = PostPrivacy.Public, MediaKeys = [Media(Guid.NewGuid())] }));
    }

    /// <summary>
    /// <c>mediaKeys</c> vắng mặt (<c>null</c>) và <c>[]</c> là CÙNG một ý — hợp đồng để trường này ngoài <c>required</c>.
    /// Cả hai đều phải đi tới cùng một kết luận, ở đây là "thiếu chữ" chứ không phải một câu về ảnh.
    /// </summary>
    [Fact]
    public void mediaKeys_vang_mat_va_rong_cho_cung_mot_ket_qua()
    {
        var vangMat = Errors(new CreatePostRequest { Privacy = PostPrivacy.Public });
        var rong = Errors(new CreatePostRequest { Privacy = PostPrivacy.Public, MediaKeys = [] });

        Assert.Equal(rong, vangMat);
        Assert.Equal((PostContentPolicy.BodyKey, PostContentPolicy.Empty), Assert.Single(vangMat));
    }

    /// <summary>
    /// <b>Q-D2</b> — thiếu <c>privacy</c> ra lỗi dưới key <c>Privacy</c>, không âm thầm thành <c>public</c>. Bỏ dấu
    /// <c>?</c> khỏi DTO thì test này xanh vĩnh viễn vì <c>default(PostPrivacy)</c> không bao giờ null.
    /// </summary>
    [Fact]
    public void Q_D2_thieu_privacy_ra_loi_rieng()
    {
        var errors = Errors(new CreatePostRequest { Body = "Có chữ." });

        var error = Assert.Single(errors);
        Assert.Equal(nameof(CreatePostRequest.Privacy), error.Key);
        Assert.Equal(CreatePostRequestValidator.PrivacyRequired, error.Message);
    }

    /// <summary>
    /// BR-01 đi qua HÀM THUẦN của Domain nên key là key hợp đồng: không chữ không ảnh → <c>body</c> (AC-02), quá 10 ảnh
    /// → <c>mediaKeys</c> (AC-03), chữ quá dài → <c>body</c>.
    /// </summary>
    [Theory]
    [MemberData(nameof(Br01Cases))]
    public void BR01_ra_dung_key_cua_hop_dong(CreatePostRequest request, string key, string message)
    {
        var error = Assert.Single(Errors(request));

        Assert.Equal(key, error.Key);
        Assert.Equal(message, error.Message);
    }

    public static TheoryData<CreatePostRequest, string, string> Br01Cases()
    {
        var owner = Guid.NewGuid();
        return new TheoryData<CreatePostRequest, string, string>
        {
            {
                new CreatePostRequest { Body = "   ", Privacy = PostPrivacy.Public },
                PostContentPolicy.BodyKey, PostContentPolicy.Empty
            },
            {
                new CreatePostRequest { Body = null, Privacy = PostPrivacy.Public, MediaKeys = [] },
                PostContentPolicy.BodyKey, PostContentPolicy.Empty
            },
            {
                new CreatePostRequest
                {
                    Body = new string('x', PostContentPolicy.MaxBodyLength + 1),
                    Privacy = PostPrivacy.Public,
                },
                PostContentPolicy.BodyKey, PostContentPolicy.BodyTooLong
            },
            {
                new CreatePostRequest
                {
                    Body = "Nhiều ảnh quá.",
                    Privacy = PostPrivacy.Public,
                    MediaKeys = [.. Enumerable.Range(0, PostContentPolicy.MaxMediaCount + 1).Select(_ => Media(owner))],
                },
                PostContentPolicy.MediaKeysKey, PostContentPolicy.TooManyMedia
            },
        };
    }

    /// <summary>Đúng 5000 ký tự và đúng 10 ảnh đều là biên TRÊN được nhận, không phải biên bị chặn.</summary>
    [Fact]
    public void Bien_tren_cua_BR01_van_hop_le()
    {
        var owner = Guid.NewGuid();

        Assert.Empty(Errors(new CreatePostRequest
        {
            Body = new string('x', PostContentPolicy.MaxBodyLength),
            Privacy = PostPrivacy.Public,
            MediaKeys = [.. Enumerable.Range(0, PostContentPolicy.MaxMediaCount).Select(_ => Media(owner))],
        }));
    }

    /// <summary>
    /// Key do chính <see cref="StorageKeys.ForPost"/> sinh ra phải LUÔN qua regex của hợp đồng. Đây là khẳng định quan
    /// trọng nhất của lớp này — cùng lý do với D3: regex ở yaml và hàm sinh key ở SharedKernel là hai nguồn độc lập,
    /// lệch nhau thì luồng hợp lệ đứt ở bước cuối mà không có gì báo trước.
    /// </summary>
    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    [InlineData("image/webp")]
    public void Key_do_StorageKeys_sinh_ra_luon_qua_regex(string contentType) =>
        Assert.Empty(Errors(new CreatePostRequest
        {
            Body = "Ảnh hợp lệ.",
            Privacy = PostPrivacy.Public,
            MediaKeys = [Media(Guid.NewGuid(), contentType)],
        }));

    /// <summary>
    /// Các cách sai dạng key. Ca <c>avatars/{id}/…</c> quan trọng nhất: key hợp lệ của CHÍNH người gọi, chỉ sai tiền tố
    /// loại — nới regex thành "key nào cũng được" thì ảnh đại diện gắn được vào bài. Ca có tiền tố rác canh việc regex
    /// neo hai đầu: <see cref="StorageKeys.BelongsTo"/> dùng <c>StartsWith</c> nên một mình nó không bắt được.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("posts/abc.jpg")]
    [InlineData("avatars/0192f3c1-8a4e-7c31-9f2a-6b5d4e3c2a10/0123456789abcdef0123456789abcdef.jpg")]
    [InlineData("posts/khong-phai-guid/0123456789abcdef0123456789abcdef.jpg")]
    [InlineData("posts/0192f3c1-8a4e-7c31-9f2a-6b5d4e3c2a10/0123456789abcdef0123456789abcdef.gif")]
    [InlineData("posts/0192f3c1-8a4e-7c31-9f2a-6b5d4e3c2a10/0123456789ABCDEF0123456789abcdef.jpg")]
    [InlineData("rác/posts/0192f3c1-8a4e-7c31-9f2a-6b5d4e3c2a10/0123456789abcdef0123456789abcdef.jpg")]
    [InlineData("posts/0192f3c1-8a4e-7c31-9f2a-6b5d4e3c2a10/0123456789abcdef0123456789abcdef.jpg/x")]
    public void Key_sai_dang_ra_loi_duoi_mediaKeys(string mediaKey)
    {
        var errors = Errors(new CreatePostRequest
        {
            Body = "Có chữ.",
            Privacy = PostPrivacy.Public,
            MediaKeys = [new MediaKeyDeclaration { MediaKey = mediaKey, ContentType = "image/jpeg", SizeBytes = 1024 }],
        });

        var error = Assert.Single(errors);
        Assert.Equal(nameof(CreatePostRequest.MediaKeys), error.Key);
        Assert.Equal(CreatePostRequestValidator.MediaKeyInvalid, error.Message);
    }

    /// <summary>
    /// Key trùng nhau bị chặn ở đây, TRƯỚC khi chạm UNIQUE của DB — để lọt xuống thì người dùng nhận 409 "ảnh đã dùng ở
    /// bài khác", mà "bài khác" đó chính là bài họ đang gửi.
    /// </summary>
    [Fact]
    public void Hai_key_trung_nhau_ra_cau_rieng_khong_phai_cau_ve_dang_key()
    {
        var media = Media(Guid.NewGuid());

        var errors = Errors(new CreatePostRequest
        {
            Body = "Cùng một ảnh hai lần.",
            Privacy = PostPrivacy.Public,
            MediaKeys = [media, media],
        });

        var error = Assert.Single(errors);
        Assert.Equal(nameof(CreatePostRequest.MediaKeys), error.Key);
        Assert.Equal(ContentErrors.DuplicateMediaKeysMessage, error.Message);
    }

    /// <summary>Khai báo loại/dung lượng ngoài allowlist — cùng câu với <c>POST /media/uploads</c> của D4.</summary>
    [Theory]
    [InlineData("image/gif", 1024L)]
    [InlineData("image/jpeg", 0L)]
    [InlineData("image/jpeg", -1L)]
    [InlineData("image/jpeg", (long)MediaAttachment.MaxSizeBytes + 1)]
    public void Khai_bao_ngoai_allowlist_ra_cau_cua_D4(string contentType, long sizeBytes)
    {
        // Key vẫn đúng dạng (đuôi do ta sinh, không do client chọn) nên lớp regex cho qua — đúng thứ ta muốn kiểm.
        var errors = Errors(new CreatePostRequest
        {
            Body = "Khai sai.",
            Privacy = PostPrivacy.Public,
            MediaKeys =
            [
                new MediaKeyDeclaration
                {
                    MediaKey = StorageKeys.ForPost(Guid.NewGuid(), "image/jpeg"),
                    ContentType = contentType,
                    SizeBytes = sizeBytes,
                },
            ],
        });

        var error = Assert.Single(errors);
        Assert.Equal(nameof(CreatePostRequest.MediaKeys), error.Key);
        Assert.Equal(CreatePostRequestValidator.MediaDeclarationInvalid, error.Message);
    }

    /// <summary>
    /// Quá 10 ảnh mà trong đó có ảnh sai dạng: chỉ ra MỘT câu — câu về số lượng. Không có <c>When(...)</c> thì hai luật
    /// cùng bắn và người dùng đọc hai câu dưới một ô để biết sửa gì trước.
    /// </summary>
    [Fact]
    public void Qua_muoi_anh_kem_anh_sai_dang_chi_ra_mot_cau_ve_so_luong()
    {
        var media = Enumerable.Range(0, PostContentPolicy.MaxMediaCount + 1)
            .Select(_ => new MediaKeyDeclaration { MediaKey = "rác", ContentType = "image/gif", SizeBytes = 0 })
            .ToList();

        var error = Assert.Single(Errors(new CreatePostRequest
        {
            Body = "Vừa nhiều vừa sai.",
            Privacy = PostPrivacy.Public,
            MediaKeys = media,
        }));

        Assert.Equal(PostContentPolicy.MediaKeysKey, error.Key);
        Assert.Equal(PostContentPolicy.TooManyMedia, error.Message);
    }
}
