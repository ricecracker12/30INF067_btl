using SocialApp.Modules.Content.Domain;

namespace SocialApp.Modules.Content.Application.Posts;

/// <summary>
/// Bảng <c>content.posts</c> + <c>content.media_attachments</c> cho các luồng của khối D. Hiện thực EF nằm ở
/// <c>Infrastructure/Persistence</c> — <c>Application</c> không chạm EF (<c>PersistenceBoundaryTests</c> canh bằng máy).
///
/// Sáu phương thức sau D7 (GĐ2), bảy từ C4 (GĐ4). Không khai trước thứ chưa có người gọi — cùng nếp <c>IProfileStore</c>.
/// </summary>
public interface IPostStore
{
    /// <summary>
    /// INSERT bài và toàn bộ ảnh của nó trong MỘT transaction, để không bao giờ tồn tại một bài có
    /// <c>media_count</c> khác số dòng ảnh thật.
    /// </summary>
    /// <returns>
    /// <c>true</c> khi ghi xong; <c>false</c> khi đụng UNIQUE <c>storage_key</c> — nghĩa là một object đã được gắn vào
    /// bài khác (POST-08), và service dịch thành <b>409</b>. Mọi lỗi DB khác PHẢI ném ra ngoài thành 500: nuốt chúng
    /// thành <c>false</c> là báo cho người dùng "ảnh đã dùng" khi sự thật là hạ tầng hỏng.
    /// </returns>
    Task<bool> AddWithMediaAsync(Post post, IReadOnlyList<MediaAttachment> attachments, CancellationToken ct);

    /// <summary>
    /// Một bài theo khóa chính, hoặc <c>null</c>. Global query filter đã loại bài xóa mềm (Đ-2.10), nên "đã xóa" và
    /// "không tồn tại" đến đây là cùng một thứ — đúng ý: hợp đồng trả cùng một 404 cho cả hai.
    ///
    /// KHÔNG theo dõi thay đổi (<c>AsNoTracking</c>): đây là đường ĐỌC. D7 cần bản tracked thì thêm
    /// <c>FindForUpdateAsync</c> riêng — một hàm trả entity vừa để đọc vừa để ghi là chỗ người sau sửa nhầm rồi
    /// <c>SaveChanges</c> ở một luồng chỉ định đọc.
    /// </summary>
    Task<Post?> FindAsync(Guid postId, CancellationToken ct);

    /// <summary>
    /// Một trang bài của <paramref name="authorId"/>, mới nhất trước, theo keyset (Đ-2.11).
    ///
    /// <b>BR-02 nằm TRONG câu truy vấn</b>, không lọc sau khi đã lấy đủ <paramref name="take"/> dòng (Mục 7.4, B.6 nhấn
    /// mạnh): lọc sau thì trang trả về ít hơn <c>limit</c> một cách ngẫu nhiên và FE tưởng đã hết dữ liệu.
    /// <paramref name="areFriends"/> là hằng đã tính MỘT lần ở service — endpoint này là bài của MỘT người nên không có
    /// lý do hỏi quan hệ bạn bè nhiều lần.
    /// </summary>
    /// <param name="take">
    /// Số dòng lấy, thường là <c>limit + 1</c>: dòng thừa thứ <c>limit+1</c> chỉ để biết "còn trang sau không", không đi
    /// vào phản hồi. Đếm tổng số bài bằng <c>COUNT(*)</c> là câu quét cả bảng ở mỗi trang.
    /// </param>
    Task<IReadOnlyList<Post>> ListByAuthorAsync(
        Guid authorId, Guid actorId, bool areFriends, PostCursor? cursor, int take, CancellationToken ct);

    /// <summary>
    /// Ảnh của nhiều bài trong MỘT câu. Nhận danh sách id chứ không nhận một id — cùng lý do với
    /// <c>IUserDirectory.GetManyAsync</c>: bản đơn "cho tiện" là mở đường N+1 ở đúng endpoint trọng điểm hiệu năng của
    /// GĐ4, và không ai thấy cho tới lúc chạy k6.
    /// </summary>
    /// <returns>Gom theo <c>postId</c>. Bài không có ảnh thì <b>vắng mặt</b> trong dictionary, không phải danh sách rỗng.</returns>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<MediaAttachment>>> MediaOfAsync(
        IReadOnlyCollection<Guid> postIds, CancellationToken ct);

    /// <summary>
    /// C4 (GĐ4): nạp nhiều bài theo khóa chính trong MỘT câu, chỉ <c>published</c> — cho đường TRÚNG cache trang đầu của
    /// feed, nơi cache chỉ giữ id (Đ-4.9) và dòng bài phải đọc tươi (bộ đếm, <c>privacy</c> vừa đổi, bài vừa xóa/ẩn).
    /// Trả <b>không</b> theo thứ tự nào — người gọi sắp lại theo thứ tự id của mình. Id không còn (đã xóa, <c>hidden</c>)
    /// thì vắng mặt.
    /// </summary>
    Task<IReadOnlyList<Post>> FindManyPublishedAsync(IReadOnlyCollection<Guid> postIds, CancellationToken ct);

    /// <summary>
    /// Một bài để SỬA — bản <b>tracked</b>, khác <see cref="FindAsync"/> (no-tracking, đường đọc). Hai hàm chứ không
    /// một cờ <c>bool tracked</c>: một hàm trả entity vừa để đọc vừa để ghi là chỗ người sau lỡ <c>SaveAsync</c> trên
    /// một luồng chỉ định đọc, và không có gì báo.
    ///
    /// Global query filter vẫn áp, nên bài đã xóa mềm trả <c>null</c> — service dịch thành <b>403</b> cùng với "không
    /// tồn tại" và "không phải của bạn" (quy ước 3b, <c>TC-A03</c>).
    /// </summary>
    Task<Post?> FindForUpdateAsync(Guid postId, CancellationToken ct);

    /// <summary>
    /// Lưu thay đổi của entity đang được theo dõi. <c>ContentDbContext.SaveChangesAsync</c> đóng dấu <c>updated_at</c>
    /// (A5) nên service KHÔNG gán tay cột đó — gán tay là hai nguồn thời gian cho một cột.
    /// </summary>
    Task SaveAsync(CancellationToken ct);
}
