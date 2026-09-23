namespace SocialApp.Modules.Content.Application.Feed;

/// <summary>
/// Truy vấn feed vượt <c>CommandTimeout</c> 5s riêng của nó (Đ-4.10). Khai ở <c>Application</c> vì <c>Application</c> không
/// bắt được kiểu Npgsql (luật 7): <c>FeedStore</c> dịch hai dạng timeout của Npgsql thành kiểu này, <c>FeedService</c> dịch nó
/// thành 503. Client tự ngắt (<see cref="OperationCanceledException"/>) KHÔNG đi đường này.
/// </summary>
public sealed class FeedQueryTimeoutException(Exception inner)
    : Exception("Truy vấn feed vượt thời hạn.", inner);
