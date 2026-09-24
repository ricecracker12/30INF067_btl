namespace SocialApp.SharedKernel.Realtime;

/// <summary>
/// Tùy chọn realtime (section <c>Realtime</c>). Không đặt gì là đúng cho mọi môi trường — lớp này tồn tại để test (HUB-10) rút
/// ngắn tuổi thọ kết nối xuống vài giây thay vì chờ 15 phút.
/// </summary>
public sealed class RealtimeOptions
{
    public const string Section = "Realtime";

    /// <summary>
    /// Tuổi thọ tối đa của một kết nối hub (Đ-5.10). <c>null</c> = bằng TTL access token (<c>Jwt:AccessTokenSeconds</c>, 900 giây)
    /// — cùng cửa sổ mà REST đang chấp nhận cho người đã bị thu hồi. Đừng đặt dài hơn TTL access token.
    /// </summary>
    public TimeSpan? MaxConnectionLifetime { get; set; }
}
