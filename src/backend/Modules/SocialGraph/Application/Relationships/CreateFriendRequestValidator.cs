using FluentValidation;

namespace SocialApp.Modules.SocialGraph.Application.Relationships;

/// <summary>
/// Lớp "không cần I/O" của <c>POST /friends/requests</c>: <c>userId</c> bắt buộc và khác
/// <see cref="Guid.Empty"/>. "Khác chính mình" không kiểm được ở đây — validator không biết
/// <c>actorId</c>; đó là bước 2 của Đ-4.14 trong <see cref="RelationshipService.SendRequestAsync"/>.
/// </summary>
public sealed class CreateFriendRequestValidator : AbstractValidator<CreateFriendRequest>
{
    public const string UserIdRequired = "Người được mời là bắt buộc.";

    public CreateFriendRequestValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().WithMessage(UserIdRequired);
    }
}
