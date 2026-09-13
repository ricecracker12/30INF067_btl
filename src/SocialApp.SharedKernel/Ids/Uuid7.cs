namespace SocialApp.SharedKernel.Ids;

/// <summary>
/// Sinh UUID v7 — có tiền tố timestamp nên khóa chính tăng dần theo thời gian, index B-tree không
/// bị phân mảnh như với <c>Guid.NewGuid()</c> (v4 ngẫu nhiên). GĐ4 sẽ trả giá cho chuyện đó khi
/// feed cần index tốt, nên chốt từ GĐ1.
///
/// Gói UUIDNext lại một lớp: đây là CHỖ DUY NHẤT trong repo gọi thẳng vào thư viện. Đổi thư viện,
/// hoặc lên .NET 9 để dùng <c>Guid.CreateVersion7()</c> có sẵn trong BCL, chỉ phải sửa ở file này.
/// </summary>
public static class Uuid7
{
    /// <summary>Sinh một UUID v7 mới, tối ưu cho khóa chính Postgres.</summary>
    public static Guid New() => UUIDNext.Uuid.NewDatabaseFriendly(UUIDNext.Database.PostgreSql);
}
