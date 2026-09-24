using SocialApp.Modules.Moderation.Application.Reports;

namespace SocialApp.UnitTests.Moderation;

/// <summary>
/// GĐ6 D6: body của <c>POST /reports</c> (Mục 8.1). Tập giá trị so chính xác — <c>"Post"</c> lọt qua đây thì CHECK của DB chặn
/// muộn thành 500. <c>detail</c> trim trước khi đo; chỉ khoảng trắng coi như vắng mặt.
/// </summary>
public sealed class CreateReportRequestValidatorTests
{
    private static readonly CreateReportRequestValidator Validator = new();

    private static CreateReportRequest Hop_le(
        string? targetType = "post", Guid? targetId = null, string? reasonCode = "spam", string? detail = null) =>
        new() { TargetType = targetType, TargetId = targetId ?? Guid.NewGuid(), ReasonCode = reasonCode, Detail = detail };

    /// <summary>
    /// So tên trường KHÔNG phân biệt hoa thường: camelCase do <c>PropertyNameResolver</c> toàn cục của host đặt (Program.cs), mà
    /// unit test không dựng host. Key camelCase thật trên dây do integration <c>REP_04_body_sai_400_dung_truong</c> canh.
    /// </summary>
    private static string[] Loi(CreateReportRequest request, string truong) =>
        [.. Validator.Validate(request).Errors
            .Where(e => string.Equals(e.PropertyName, truong, StringComparison.OrdinalIgnoreCase))
            .Select(e => e.ErrorMessage)];

    [Theory]
    [InlineData("post")]
    [InlineData("comment")]
    [InlineData("user")]
    public void Ba_loai_doi_tuong_hop_le(string targetType) =>
        Assert.True(Validator.Validate(Hop_le(targetType)).IsValid);

    [Theory]
    [InlineData(null)]
    [InlineData("Post")]
    [InlineData("video")]
    [InlineData("")]
    public void Loai_doi_tuong_sai_loi_targetType(string? targetType) =>
        Assert.Equal([CreateReportRequestValidator.TargetTypeInvalid], Loi(Hop_le(targetType), "targetType"));

    [Fact]
    public void Thieu_hoac_rong_targetId_loi_targetId()
    {
        Assert.Equal([CreateReportRequestValidator.TargetIdRequired],
            Loi(new CreateReportRequest { TargetType = "post", ReasonCode = "spam" }, "targetId"));
        Assert.Equal([CreateReportRequestValidator.TargetIdRequired], Loi(Hop_le(targetId: Guid.Empty), "targetId"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("SPAM")]
    [InlineData("abuse")]
    public void Ly_do_sai_loi_reasonCode(string? reasonCode) =>
        Assert.Equal([CreateReportRequestValidator.ReasonCodeInvalid], Loi(Hop_le(reasonCode: reasonCode), "reasonCode"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Other_khong_mo_ta_loi_detail(string? detail) =>
        Assert.Equal([CreateReportRequestValidator.DetailRequired], Loi(Hop_le(reasonCode: "other", detail: detail), "detail"));

    [Fact]
    public void Other_co_mo_ta_hop_le() => Assert.True(Validator.Validate(Hop_le(reasonCode: "other", detail: "x")).IsValid);

    [Fact]
    public void Mo_ta_501_ky_tu_loi_do_dai_500_sau_cat_hop_le()
    {
        Assert.Equal([CreateReportRequestValidator.DetailTooLong], Loi(Hop_le(detail: new string('x', 501)), "detail"));
        Assert.True(Validator.Validate(Hop_le(detail: "  " + new string('x', 500) + "  ")).IsValid);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    [InlineData("  Lừa đảo.  ", "Lừa đảo.")]
    public void NormalizeDetail_cat_va_khoang_trang_thanh_null(string? input, string? expected) =>
        Assert.Equal(expected, CreateReportRequestValidator.NormalizeDetail(input));
}
