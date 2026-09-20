using System.Text.Json;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;

namespace SocialApp.SharedKernel.Http;

/// <summary>
/// <c>ModelState</c> → <c>errors</c> của Problem Details 400, ĐÃ LÀM SẠCH (D9). Không làm thì body hỏng trả thẳng thông điệp của
/// System.Text.Json: tên kiểu .NET nội bộ (<c>'SocialApp.Modules.Identity.Application.Login.LoginRequest'</c>), vị trí byte, tiếng
/// Anh, và key dạng <c>$.password</c>/<c>$</c>/<c>""</c> thay vì tên trường như hợp đồng.
///
/// Luật:
/// <list type="bullet">
/// <item>Key là đường dẫn JSON (<c>$…</c>) → tên trường client gửi (<c>$.items[0].name</c> → <c>items[0].name</c>); gốc (<c>$</c>,
/// <c>$[0]</c>) và body rỗng (<c>""</c>) → <see cref="BodyKey"/>.</item>
/// <item><b>Mọi key hạ về camelCase</b> bằng CHÍNH <c>JsonNamingPolicy.CamelCase</c> mà serializer dùng (D6). Cần vì model
/// <c>[FromQuery]</c> cho key theo TÊN THUỘC TÍNH C# (<c>Limit</c>, <c>Cursor</c>) ở cả hai nhánh hỏng — model binding
/// (<c>?limit=abc</c>) lẫn FluentValidation (<c>?limit=51</c>) — trong khi hợp đồng ghi <c>limit</c>, <c>cursor</c>. Với key
/// đã camelCase sẵn (body qua FluentValidation, tham số route <c>postId</c>/<c>userId</c>) đây là phép đồng nhất.</item>
/// <item>Lỗi ở đường dẫn JSON LUÔN nhận thông điệp cố định — kể cả khi ai đó bật lại
/// <c>AllowInputFormatterExceptionMessages</c>: key <c>$…</c> chỉ do input formatter sinh, không bao giờ do FluentValidation.</item>
/// <item>Lỗi còn lại (FluentValidation, thông điệp model binding đã Việt hóa ở <see cref="Localize"/>) giữ nguyên thông điệp.</item>
/// </list>
/// Dùng qua <c>SharedKernelProblemDetailsFactory</c> — mọi đường 400 của MVC đi qua đó, module không gọi trực tiếp.
/// </summary>
public static class ValidationErrors
{
    public const string BodyKey = "body";

    public const string InvalidBody = "Nội dung yêu cầu không đúng định dạng JSON.";
    public const string InvalidField = "Giá trị không hợp lệ hoặc trường không được hỗ trợ.";
    public const string MissingBody = "Thiếu nội dung yêu cầu.";
    public const string MissingValue = "Thiếu giá trị bắt buộc.";
    public const string InvalidValue = "Giá trị không hợp lệ.";
    public const string MustBeNumber = "Giá trị phải là số.";

    public static Dictionary<string, string[]> From(ModelStateDictionary modelState)
    {
        ArgumentNullException.ThrowIfNull(modelState);

        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (key, entry) in modelState)
        {
            if (entry is null || entry.Errors.Count == 0)
                continue;

            var fromJsonFormatter = key.StartsWith('$');
            var field = FieldName(key);
            if (!errors.TryGetValue(field, out var messages))
                errors[field] = messages = [];

            foreach (var error in entry.Errors)
            {
                var message = fromJsonFormatter || string.IsNullOrEmpty(error.ErrorMessage)
                    ? field == BodyKey ? InvalidBody : InvalidField
                    : error.ErrorMessage;
                if (!messages.Contains(message))
                    messages.Add(message);
            }
        }

        return errors.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray(), StringComparer.Ordinal);
    }

    /// <summary>
    /// Thông điệp model binding của MVC (body rỗng; từ GĐ2: query/route sai kiểu, thiếu tham số) — tiếng Việt và KHÔNG lặp lại giá
    /// trị client gửi: mặc định "The value 'abc' is not valid for X" đưa nguyên dữ liệu người dùng vào response.
    /// </summary>
    public static void Localize(DefaultModelBindingMessageProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        provider.SetMissingRequestBodyRequiredValueAccessor(() => MissingBody);
        provider.SetMissingBindRequiredValueAccessor(_ => MissingValue);
        provider.SetMissingKeyOrValueAccessor(() => MissingValue);
        provider.SetValueMustNotBeNullAccessor(_ => MissingValue);
        provider.SetAttemptedValueIsInvalidAccessor((_, _) => InvalidValue);
        provider.SetNonPropertyAttemptedValueIsInvalidAccessor(_ => InvalidValue);
        provider.SetUnknownValueIsInvalidAccessor(_ => InvalidValue);
        provider.SetNonPropertyUnknownValueIsInvalidAccessor(() => InvalidValue);
        provider.SetValueIsInvalidAccessor(_ => InvalidValue);
        provider.SetValueMustBeANumberAccessor(_ => MustBeNumber);
        provider.SetNonPropertyValueMustBeANumberAccessor(() => MustBeNumber);
    }

    private static string FieldName(string key)
    {
        var name =
            key.StartsWith("$.", StringComparison.Ordinal) && key.Length > 2 ? key[2..]
            : key.Length == 0 || key.StartsWith('$') ? BodyKey
            : key;

        // Cùng policy với serializer, không tự viết `char.ToLower(name[0])`: hai cách hạ chữ là hai cách lệch nhau, và
        // policy của System.Text.Json là thứ quyết định tên trường ĐI RA trong mọi DTO.
        return JsonNamingPolicy.CamelCase.ConvertName(name);
    }
}
