using SocialApp.Modules.Moderation.Application.Audit;

namespace SocialApp.UnitTests.Moderation;

/// <summary>GĐ6 D8 — phần thuần của <c>GET /admin/audit-logs</c>: cursor <c>id</c> và validator (L-D14: <c>action</c> đứng một mình được).</summary>
public sealed class AuditLogQueryValidatorTests
{
    [Theory]
    [InlineData(1L)]
    [InlineData(1042L)]
    [InlineData(long.MaxValue)]
    public void Cursor_ma_hoa_roi_giai_ma_ra_dung_id(long id)
    {
        var raw = new AuditLogCursor(id).Encode();

        Assert.DoesNotContain(raw, c => c is '+' or '/' or '=');
        Assert.True(AuditLogCursor.TryDecode(raw, out var decoded));
        Assert.Equal(id, decoded.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("rac!")]
    [InlineData("MA")]      // "0"
    [InlineData("LTE")]     // "-1"
    [InlineData("MS41")]    // "1.5"
    public void Cursor_rac_hoac_khong_duong_tra_false(string raw) =>
        Assert.False(AuditLogCursor.TryDecode(raw, out _));

    public static TheoryData<ListAuditLogsQuery, bool> Queries => new()
    {
        { new ListAuditLogsQuery(), true },
        { new ListAuditLogsQuery { Action = "user.lock" }, true },   // L-D14: một mình
        { new ListAuditLogsQuery { Action = "access.denied", ActorId = Guid.NewGuid(), Limit = 100 }, true },
        { new ListAuditLogsQuery { TargetType = "post", TargetId = Guid.NewGuid() }, true },
        { new ListAuditLogsQuery { TargetType = "post" }, true },
        { new ListAuditLogsQuery { TargetId = Guid.NewGuid() }, false },   // thiếu targetType
        { new ListAuditLogsQuery { Action = "abc" }, false },
        { new ListAuditLogsQuery { Action = "USER.LOCK" }, false },
        { new ListAuditLogsQuery { Limit = 0 }, false },
        { new ListAuditLogsQuery { Limit = 101 }, false },
        { new ListAuditLogsQuery { TargetType = new string('x', 21) }, false },
        { new ListAuditLogsQuery { Cursor = "rac!" }, false },
    };

    [Theory]
    [MemberData(nameof(Queries))]
    public void Validator(ListAuditLogsQuery query, bool valid) =>
        Assert.Equal(valid, new ListAuditLogsQueryValidator().Validate(query).IsValid);
}
