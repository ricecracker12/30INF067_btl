using Microsoft.Extensions.Logging.Abstractions;
using SocialApp.Modules.Content.Application.Posts;
using SocialApp.Modules.Content.Domain;
using SocialApp.SharedKernel.Contracts;
using SocialApp.SharedKernel.Storage;

namespace SocialApp.UnitTests.Content;

/// <summary>
/// D5 — <see cref="PostResponseMapper"/>. Ở tầng unit vì <see cref="IObjectStorage"/> là bề mặt trừu tượng: ba nhánh
/// dưới đây kiểm được bằng một lưu trữ giả vài dòng, không cần Postgres lẫn R2.
///
/// Lớp này canh những thứ <c>CreatePostTests</c> không với tới: dự phòng tác giả của <b>Q-D8</b> (Đ-2.4 làm nó gần như
/// không xảy ra qua HTTP), và bản lô <see cref="PostResponseMapper.ToResponses"/> mà D6 sẽ dùng.
/// </summary>
public sealed class PostResponseMapperTests
{
    /// <summary>
    /// Lưu trữ giả tối thiểu — CHỈ ký URL, mọi thao tác khác ném. Không dùng <c>FakeObjectStorage</c> của
    /// IntegrationTests: project unit không tham chiếu project đó, và mapper chỉ cần đúng một trong năm phương thức.
    /// </summary>
    private sealed class SigningOnlyStorage : IObjectStorage
    {
        public string CreatePresignedPut(string key, string contentType, long contentLength) =>
            throw new NotSupportedException();

        public string CreatePresignedGet(string key) => $"https://ky.invalid/{key}";

        public Task<ObjectHead?> HeadAsync(string key, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(string key, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<ObjectPage> ListAsync(string prefix, string? token, int maxKeys, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private static readonly PostResponseMapper Mapper =
        new(new SigningOnlyStorage(), NullLogger<PostResponseMapper>.Instance);

    private static Post PostOf(Guid author) => new()
    {
        AuthorId = author,
        Body = "Nội dung.",
        Privacy = PostPrivacy.Public,
        MediaCount = 0,
        CreatedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch,
    };

    private static MediaAttachment AttachmentOf(Guid postId, short position, string key) => new()
    {
        OwnerType = MediaOwnerType.Post,
        OwnerId = postId,
        StorageKey = key,
        ContentType = "image/jpeg",
        SizeBytes = 1024,
        Position = position,
    };

    /// <summary>
    /// <c>canEdit</c> do SERVER tính từ <c>author_id == actorId</c>, không phải FE tự so id (Mục 8.2). Hai lời gọi trên
    /// CÙNG một bài, khác mỗi người đọc.
    /// </summary>
    [Fact]
    public void canEdit_dung_theo_nguoi_doc_chu_khong_theo_bai()
    {
        var author = Guid.NewGuid();
        var post = PostOf(author);
        var card = new UserCard(author, "An Nguyễn", null);

        Assert.True(Mapper.ToResponse(post, [], card, author).CanEdit);
        Assert.False(Mapper.ToResponse(post, [], card, Guid.NewGuid()).CanEdit);
    }

    /// <summary>
    /// Ảnh sắp theo <c>Position</c>, KHÔNG theo thứ tự store trả về. Truyền vào theo thứ tự đảo để phân biệt được: một
    /// câu truy vấn không <c>ORDER BY</c> thì Postgres được phép trả bất kỳ thứ tự nào, và vị trí là thứ người dùng
    /// nhìn thấy.
    /// </summary>
    [Fact]
    public void Anh_sap_theo_Position_khong_theo_thu_tu_dau_vao()
    {
        var author = Guid.NewGuid();
        var post = PostOf(author);
        IReadOnlyList<MediaAttachment> attachments =
        [
            AttachmentOf(post.PostId, 2, "posts/c.jpg"),
            AttachmentOf(post.PostId, 0, "posts/a.jpg"),
            AttachmentOf(post.PostId, 1, "posts/b.jpg"),
        ];

        var response = Mapper.ToResponse(post, attachments, new UserCard(author, "An", null), author);

        Assert.Equal([0, 1, 2], response.Media.Select(m => m.Position));
        Assert.Equal(
            ["https://ky.invalid/posts/a.jpg", "https://ky.invalid/posts/b.jpg", "https://ky.invalid/posts/c.jpg"],
            response.Media.Select(m => m.Url));
    }

    /// <summary>
    /// Avatar của tác giả được ký; <c>null</c> khi chưa đặt. <see cref="PostAuthor"/> mang URL chứ không mang key —
    /// cùng lý do với <see cref="PostMedia"/>.
    /// </summary>
    [Fact]
    public void avatarUrl_cua_tac_gia_duoc_ky_va_null_khi_chua_dat()
    {
        var author = Guid.NewGuid();
        var post = PostOf(author);

        Assert.Equal(
            "https://ky.invalid/avatars/x.jpg",
            Mapper.ToResponse(post, [], new UserCard(author, "An", "avatars/x.jpg"), author).Author.AvatarUrl);
        Assert.Null(Mapper.ToResponse(post, [], new UserCard(author, "An", null), author).Author.AvatarUrl);
    }

    /// <summary>
    /// <b>Q-D8</b> — <c>IUserDirectory</c> không trả về tác giả thì dùng tên dự phòng, KHÔNG ném. Đ-2.4 làm ca này gần
    /// như không xảy ra ở GĐ2, nhưng GĐ8 sẽ có xóa tài khoản và một bài mồ côi tác giả không được làm 500 cả trang feed
    /// của GĐ4.
    ///
    /// <c>userId</c> vẫn là id thật của tác giả: chỉ tên hiển thị là dự phòng.
    /// </summary>
    [Fact]
    public void Q_D8_khong_tra_duoc_tac_gia_thi_dung_ten_du_phong_chu_khong_nem()
    {
        var author = Guid.NewGuid();

        var response = Mapper.ToResponse(PostOf(author), [], null, author);

        Assert.Equal(PostResponseMapper.UnknownAuthorName, response.Author.DisplayName);
        Assert.Equal(author, response.Author.UserId);
        Assert.Null(response.Author.AvatarUrl);
    }

    /// <summary>
    /// <c>reactionCounts</c> là <c>{}</c> khi rỗng, không <c>null</c> (Đ-2.12) — và là chính dictionary của entity khi
    /// GĐ3 bắt đầu ghi vào đó.
    /// </summary>
    [Fact]
    public void reactionCounts_rong_la_dictionary_rong_chu_khong_phai_null()
    {
        var author = Guid.NewGuid();
        var post = PostOf(author);
        post.ReactionCounts["like"] = 3;

        Assert.Empty(Mapper.ToResponse(PostOf(author), [], null, author).ReactionCounts);
        Assert.Equal(3, Mapper.ToResponse(post, [], null, author).ReactionCounts["like"]);
    }

    /// <summary>
    /// Bản lô (D6 dùng): ảnh gom theo <c>postId</c>, tác giả tra từ MỘT dictionary đã lấy sẵn cho cả trang (Đ-2.3).
    /// Bài không có ảnh và bài có tác giả vắng mặt đều phải đi qua được — một trang feed không được vỡ vì một dòng.
    /// </summary>
    [Fact]
    public void ToResponses_ghep_dung_anh_va_tac_gia_cho_ca_trang()
    {
        var reader = Guid.NewGuid();
        var authorA = Guid.NewGuid();
        var authorB = Guid.NewGuid();
        var postA = PostOf(authorA);
        var postB = PostOf(authorB);

        var responses = Mapper.ToResponses(
            [postA, postB],
            new Dictionary<Guid, IReadOnlyList<MediaAttachment>>
            {
                [postA.PostId] = [AttachmentOf(postA.PostId, 0, "posts/a.jpg")],
            },
            new Dictionary<Guid, UserCard> { [authorA] = new(authorA, "An", null) },
            reader);

        Assert.Equal(2, responses.Count);
        Assert.Equal("An", responses[0].Author.DisplayName);
        Assert.Equal("https://ky.invalid/posts/a.jpg", Assert.Single(responses[0].Media).Url);

        // authorB vắng mặt trong `cards` → dự phòng Q-D8, và bài không có ảnh → mảng rỗng, không phải null.
        Assert.Equal(PostResponseMapper.UnknownAuthorName, responses[1].Author.DisplayName);
        Assert.Empty(responses[1].Media);
        Assert.All(responses, r => Assert.False(r.CanEdit));
    }
}
