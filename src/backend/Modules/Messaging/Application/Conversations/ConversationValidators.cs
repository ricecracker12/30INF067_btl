using FluentValidation;
using SocialApp.Modules.Messaging.Domain;

namespace SocialApp.Modules.Messaging.Application.Conversations;

public sealed class CreateConversationRequestValidator : AbstractValidator<CreateConversationRequest>
{
    public const string UserIdRequired = "Người nhận là bắt buộc.";

    public CreateConversationRequestValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().WithMessage(UserIdRequired);
    }
}

public sealed class SendMessageRequestValidator : AbstractValidator<SendMessageRequest>
{
    public const string ClientMsgIdRequired = "Mã tin phía client là bắt buộc.";

    public SendMessageRequestValidator()
    {
        // Cùng luật với hub — MessageContentPolicy là nguồn duy nhất (Đ-5.7: một nghiệp vụ, hai cửa vào).
        RuleFor(x => x.Content)
            .Must(content => MessageContentPolicy.Validate(content) is null)
            .WithMessage(x => MessageContentPolicy.Validate(x.Content) ?? "");
        RuleFor(x => x.ClientMsgId).NotEmpty().WithMessage(ClientMsgIdRequired);
    }
}

public sealed class ReceiptRequestValidator : AbstractValidator<ReceiptRequest>
{
    public const string KindRequired = "Loại biên nhận là bắt buộc (delivered hoặc seen).";
    public const string UpToSeqInvalid = "upToSeq phải từ 1 trở lên.";

    public ReceiptRequestValidator()
    {
        RuleFor(x => x.Kind).NotNull().WithMessage(KindRequired);
        RuleFor(x => x.UpToSeq).GreaterThanOrEqualTo(1).WithMessage(UpToSeqInvalid);
    }
}

/// <summary>Tham số query của <c>GET /conversations</c>: cursor mờ, limit 1..50 mặc định 20 (AGENTS.md Mục 9).</summary>
public sealed class ListConversationsQuery
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 50;

    public string? Cursor { get; init; }

    public int? Limit { get; init; }

    public int EffectiveLimit => Limit ?? DefaultLimit;
}

public sealed class ListConversationsQueryValidator : AbstractValidator<ListConversationsQuery>
{
    public static readonly string LimitOutOfRange = $"Số hội thoại mỗi trang phải từ 1 đến {ListConversationsQuery.MaxLimit}.";
    public const string CursorInvalid = "Cursor không hợp lệ.";

    public ListConversationsQueryValidator()
    {
        RuleFor(x => x.Limit).InclusiveBetween(1, ListConversationsQuery.MaxLimit).WithMessage(LimitOutOfRange);
        RuleFor(x => x.Cursor)
            .Must(raw => raw is null || ConversationCursor.TryDecode(raw, out _))
            .WithMessage(CursorInvalid);
    }
}

/// <summary>
/// Tham số query của <c>GET /conversations/{id}/messages</c> (Mục 7.6): <c>cursor</c> (cuộn ngược, <c>seq DESC</c>) HOẶC
/// <c>afterSeq</c> (lấp chỗ hở, <c>seq ASC</c>) — không cả hai. Limit mặc định 30, tối đa 50.
/// </summary>
public sealed class MessageHistoryQuery
{
    public const int DefaultLimit = 30;
    public const int MaxLimit = 50;

    public string? Cursor { get; init; }

    public long? AfterSeq { get; init; }

    public int? Limit { get; init; }

    public int EffectiveLimit => Limit ?? DefaultLimit;
}

public sealed class MessageHistoryQueryValidator : AbstractValidator<MessageHistoryQuery>
{
    public static readonly string LimitOutOfRange = $"Số tin mỗi trang phải từ 1 đến {MessageHistoryQuery.MaxLimit}.";
    public const string CursorInvalid = "Cursor không hợp lệ.";
    public const string CursorWithAfterSeq = "Không dùng cursor cùng afterSeq.";
    public const string AfterSeqInvalid = "afterSeq phải từ 0 trở lên.";

    public MessageHistoryQueryValidator()
    {
        RuleFor(x => x.Limit).InclusiveBetween(1, MessageHistoryQuery.MaxLimit).WithMessage(LimitOutOfRange);
        RuleFor(x => x.Cursor)
            .Must(raw => raw is null || MessageCursor.TryDecode(raw, out _))
            .WithMessage(CursorInvalid);
        RuleFor(x => x.AfterSeq).GreaterThanOrEqualTo(0).WithMessage(AfterSeqInvalid);
        RuleFor(x => x.Cursor)
            .Null()
            .When(x => x.AfterSeq is not null)
            .WithMessage(CursorWithAfterSeq);
    }
}
