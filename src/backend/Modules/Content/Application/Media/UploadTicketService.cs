using System.Globalization;
using Microsoft.Extensions.Logging;
using SocialApp.SharedKernel.Storage;

namespace SocialApp.Modules.Content.Application.Media;

/// <summary>
/// Cấp URL đã ký cho một lô file (Đ-2.15). Nghiệp vụ mỏng nhất của khối D và cố ý mỏng: endpoint này <b>không chạm DB</b>
/// và <b>không chạm mạng</b> — ký là HMAC cục bộ (xem <see cref="IObjectStorage.CreatePresignedPut"/>), nên cả phương
/// thức là đồng bộ. Đặt <c>async</c> "cho đồng bộ với các service khác" là bịa ra một <c>Task</c> không bao giờ chờ gì.
///
/// Object tải lên mà không được gắn vào bài hay hồ sơ trong 24 giờ sẽ bị worker dọn (Đ-2.13, C4) — đó là lý do cấp ticket
/// không cần ghi lại gì: không có trạng thái nào để rò rỉ, và không có dòng DB nào để dọn khi người dùng bỏ giữa chừng.
/// </summary>
public sealed class UploadTicketService(IObjectStorage storage, ILogger<UploadTicketService> logger)
{
    /// <summary>
    /// Một ticket cho mỗi file, <b>cùng thứ tự</b> với <paramref name="files"/> — hợp đồng ghi rõ, và FE ghép theo chỉ số.
    ///
    /// <paramref name="actorId"/> đến từ <c>User.GetUserId()</c> ở controller (Mục 1.3 luật 5) và đi thẳng vào tiền tố
    /// key, nên tầng 3 của endpoint này là <b>không có gì để kiểm</b>: người gọi chỉ ký được key mang id của chính mình.
    ///
    /// Quyền <c>post.create</c> cho <see cref="UploadPurpose.Post"/> đã kiểm ở controller (Q-D5) — ở đây không có
    /// <c>ClaimsPrincipal</c>, và service chỉ nói được từ vựng "ai, mục đích gì, những file nào".
    /// </summary>
    public IReadOnlyList<UploadTicket> Create(Guid actorId, UploadPurpose purpose, IReadOnlyList<UploadFileDeclaration> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        // Cùng hằng số mà C2 dùng để ký (R2Options.PutUrlMinutes): trả một con số khác là FE đếm ngược sai hạn.
        const int expiresIn = R2Options.PutUrlMinutes * 60;

        var tickets = new List<UploadTicket>(files.Count);
        foreach (var file in files)
        {
            var key = purpose == UploadPurpose.Post
                ? StorageKeys.ForPost(actorId, file.ContentType)
                : StorageKeys.ForAvatar(actorId, file.ContentType);

            tickets.Add(new UploadTicket(
                key,
                storage.CreatePresignedPut(key, file.ContentType, file.SizeBytes),
                expiresIn,
                new RequiredHeaders(file.ContentType, file.SizeBytes.ToString(CultureInfo.InvariantCulture))));
        }

        // Đếm và mục đích, KHÔNG key và KHÔNG url (Mục 1.3 luật 9): url mang chữ ký nên một dòng log là một quyền ghi
        // vào bucket còn hạn 10 phút, và key kèm id người dùng là bản đồ ảnh riêng tư của họ.
        // MediaUploadsTests.Khong_log_uploadUrl canh dòng này bằng máy.
        logger.LogInformation("Cấp {Count} ticket tải lên ({Purpose})", tickets.Count, purpose);

        return tickets;
    }
}
