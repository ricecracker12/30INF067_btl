using System.Text.Json;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using SocialApp.SharedKernel.Http;
using Xunit;

namespace SocialApp.UnitTests.SharedKernel;

/// <summary>
/// Làm sạch ModelState cho <c>errors</c> của 400 (D9). Thông điệp mong đợi viết tay — cố ý không đọc hằng số của ValidationErrors,
/// để đổi chữ trong SharedKernel là test này đỏ và phải nhìn lại hợp đồng.
/// </summary>
public sealed class ValidationErrorsTests
{
    private const string InvalidField = "Giá trị không hợp lệ hoặc trường không được hỗ trợ.";
    private const string InvalidBody = "Nội dung yêu cầu không đúng định dạng JSON.";

    [Fact]
    public void Loi_FluentValidation_giu_nguyen_key_va_thong_diep()
    {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("email", "Email là bắt buộc.");

        var errors = ValidationErrors.From(modelState);

        Assert.Equal(["Email là bắt buộc."], Assert.Single(errors, e => e.Key == "email").Value);
    }

    /// <summary>
    /// <b>D6</b> — key theo TÊN THUỘC TÍNH C# hạ về camelCase của hợp đồng.
    ///
    /// Model <c>[FromQuery]</c> cho key PascalCase ở CẢ HAI nhánh hỏng: model binding (<c>?limit=abc</c>) và
    /// FluentValidation (<c>?limit=51</c>) — <c>ValidatorOptions.Global.PropertyNameResolver</c> của host không với tới
    /// đường query. Không hạ ở đây thì <c>GET /users/{userId}/posts</c> trả <c>errors.Limit</c> trong khi hợp đồng và
    /// type sinh cho FE ghi <c>errors.limit</c>, và FE hiện lỗi dưới… không ô nào.
    /// </summary>
    [Theory]
    [InlineData("Limit", "limit")]
    [InlineData("Cursor", "cursor")]
    [InlineData("MediaKeys", "mediaKeys")]
    public void Key_PascalCase_ha_ve_camelCase_cua_hop_dong(string key, string expected)
    {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError(key, "Giá trị không hợp lệ.");

        Assert.Equal([expected], ValidationErrors.From(modelState).Keys);
    }

    /// <summary>
    /// Key vốn đã đúng hợp đồng thì phép hạ camelCase là ĐỒNG NHẤT — canh gác chống hồi quy cho mọi endpoint đã có:
    /// body qua FluentValidation, tham số route, và <see cref="ValidationErrors.BodyKey"/>.
    /// </summary>
    [Theory]
    [InlineData("email")]
    [InlineData("password")]
    [InlineData("body")]
    [InlineData("mediaKeys")]
    [InlineData("postId")]
    [InlineData("userId")]
    [InlineData("displayName")]
    public void Key_da_dung_hop_dong_thi_khong_doi(string key)
    {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError(key, "Giá trị không hợp lệ.");

        Assert.Equal([key], ValidationErrors.From(modelState).Keys);
    }

    /// <summary>Kể cả khi thông điệp gốc lọt vào (AllowInputFormatterExceptionMessages bị bật lại): key $… không bao giờ giữ thông điệp.</summary>
    [Fact]
    public void Duong_dan_JSON_doi_thanh_ten_truong_va_thong_diep_goc_bi_thay()
    {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("$.role",
            "The JSON property 'role' could not be mapped to any .NET member contained in type 'SocialApp.Modules.X.LoginRequest'.");
        modelState.AddModelError("$.items[0].name", "The JSON value could not be converted to System.String.");

        var errors = ValidationErrors.From(modelState);

        Assert.Equal([InvalidField], errors["role"]);
        Assert.Equal([InvalidField], errors["items[0].name"]);
        Assert.DoesNotContain(errors.Values.SelectMany(m => m), m => m.Contains("SocialApp") || m.Contains("System."));
    }

    [Fact]
    public void Loi_chi_co_exception_khong_co_thong_diep_nhan_thong_diep_chung()
    {
        var modelState = new ModelStateDictionary();
        var metadata = new EmptyModelMetadataProvider().GetMetadataForType(typeof(string));
        modelState.AddModelError("$.password", new JsonException("internal"), metadata);

        Assert.Equal([InvalidField], ValidationErrors.From(modelState)["password"]);
    }

    [Theory]
    [InlineData("$")]
    [InlineData("$[0]")]
    public void Loi_o_goc_body_gom_ve_key_body(string key)
    {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError(key, "'k' is an invalid start of a value. Path: $ | LineNumber: 0 | BytePositionInLine: 0.");

        Assert.Equal([InvalidBody], Assert.Single(ValidationErrors.From(modelState)).Value);
    }

    [Fact]
    public void Body_rong_key_rong_thanh_body_giu_thong_diep_da_Viet_hoa_va_gop_trung()
    {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("", "Thiếu nội dung yêu cầu.");
        modelState.AddModelError("$", "anything");
        modelState.AddModelError("$", "another");

        var body = Assert.Single(ValidationErrors.From(modelState));

        Assert.Equal("body", body.Key);
        Assert.Equal(["Thiếu nội dung yêu cầu.", InvalidBody], body.Value);
    }

    /// <summary>Thông điệp model binding mặc định lặp lại giá trị client gửi ("The value 'abc' is not valid") — Việt hóa và không lặp.</summary>
    [Fact]
    public void Localize_thong_diep_model_binding_khong_lap_gia_tri_client_gui()
    {
        var provider = new DefaultModelBindingMessageProvider();

        ValidationErrors.Localize(provider);

        var messages = new[]
        {
            provider.AttemptedValueIsInvalidAccessor("abc<script>", "page"),
            provider.NonPropertyAttemptedValueIsInvalidAccessor("abc<script>"),
            provider.ValueMustNotBeNullAccessor("abc<script>"),
            provider.UnknownValueIsInvalidAccessor("page"),
            provider.ValueIsInvalidAccessor("abc<script>"),
            provider.ValueMustBeANumberAccessor("page"),
            provider.MissingBindRequiredValueAccessor("page"),
            provider.MissingKeyOrValueAccessor(),
            provider.MissingRequestBodyRequiredValueAccessor(),
            provider.NonPropertyUnknownValueIsInvalidAccessor(),
            provider.NonPropertyValueMustBeANumberAccessor(),
        };
        Assert.All(messages, m =>
        {
            Assert.DoesNotContain("abc", m);
            Assert.DoesNotContain("page", m);
            Assert.DoesNotContain("value", m, StringComparison.OrdinalIgnoreCase);
        });
        Assert.Equal("Thiếu nội dung yêu cầu.", provider.MissingRequestBodyRequiredValueAccessor());
    }
}
