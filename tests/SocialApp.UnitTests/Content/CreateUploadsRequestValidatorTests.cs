using FluentValidation;
using SocialApp.Modules.Content.Application.Media;
using SocialApp.Modules.Content.Domain;

namespace SocialApp.UnitTests.Content;

/// <summary>
/// D4 — <see cref="CreateUploadsRequestValidator"/>. Ở tầng unit vì đây là hàm thuần trên DTO: mười ca biên ở đây rẻ hơn
/// mười lượt HTTP, và integration chỉ cần vài ca chứng minh validator được nối vào đường request đúng key.
/// </summary>
public sealed class CreateUploadsRequestValidatorTests
{
    private static readonly CreateUploadsRequestValidator Validator = new();

    /// <summary>
    /// Key ở đây là tên CLR (<c>Purpose</c>, <c>Files</c>), không phải <c>purpose</c>/<c>files</c> của hợp đồng: hạ về
    /// camelCase là việc của <c>ValidatorOptions.Global.PropertyNameResolver</c>, thứ HOST đặt một lần trong
    /// <c>Program.cs</c> và unit test không dựng. Key đúng hợp đồng được canh ở tầng integration
    /// (<c>MediaUploadsTests</c>), nơi có host thật — dùng <c>nameof</c> ở đây để hai tầng không lệch tên trường.
    /// </summary>
    private static IReadOnlyList<(string Key, string Message)> Errors(CreateUploadsRequest request) =>
        Validator.Validate(request).Errors.Select(e => (e.PropertyName, e.ErrorMessage)).ToList();

    private static CreateUploadsRequest Request(UploadPurpose? purpose, params (string ContentType, long SizeBytes)[] files) =>
        new()
        {
            Purpose = purpose,
            Files = files.Select(f => new UploadFileDeclaration { ContentType = f.ContentType, SizeBytes = f.SizeBytes }).ToList(),
        };

    [Theory]
    [InlineData(UploadPurpose.Post)]
    [InlineData(UploadPurpose.Avatar)]
    public void Lo_hop_le_qua_duoc_ca_hai_purpose(UploadPurpose purpose) =>
        Assert.Empty(Errors(Request(purpose, ("image/jpeg", 1_048_576), ("image/png", 204_800))));

    /// <summary>Đúng 10 file là biên TRÊN được nhận (Đ-2.15) — 11 mới hỏng. Xem <see cref="Qua_10_file_bi_chan"/>.</summary>
    [Fact]
    public void Dung_10_file_van_hop_le() =>
        Assert.Empty(Errors(Request(
            UploadPurpose.Post,
            Enumerable.Repeat(("image/webp", 1L), PostContentPolicy.MaxMediaCount).ToArray())));

    /// <summary>
    /// <b>Q-D2</b> — <c>purpose</c> vắng mặt phải ra <c>errors.purpose</c>, không âm thầm thành <c>post</c> (mức quyền
    /// cao hơn). Đây là lý do DTO khai <c>UploadPurpose?</c>: bỏ dấu <c>?</c> đi thì test này xanh vĩnh viễn vì
    /// <c>default(UploadPurpose)</c> không bao giờ null.
    /// </summary>
    [Fact]
    public void Q_D2_thieu_purpose_ra_loi_duoi_key_purpose()
    {
        var errors = Errors(Request(null, ("image/jpeg", 1024)));

        var error = Assert.Single(errors);
        Assert.Equal(nameof(CreateUploadsRequest.Purpose), error.Key);
        Assert.Equal(CreateUploadsRequestValidator.PurposeRequired, error.Message);
    }

    /// <summary>
    /// Mọi cách sai về file đều nằm dưới <b>đúng một</b> key <c>files</c> — hợp đồng ghi <c>errors.files</c>, và FE hiện
    /// câu đó dưới ô chọn ảnh (một ô, không phải mười ô).
    ///
    /// Khẳng định "đúng MỘT lỗi" là phần quan trọng: <c>Cascade(CascadeMode.Stop)</c> giữ cho lô vừa quá 10 file vừa có
    /// file sai loại không đổ hai câu cùng lúc xuống một ô.
    /// </summary>
    [Theory]
    [MemberData(nameof(LoHongCases))]
    public void Moi_loi_ve_file_deu_nam_duoi_dung_mot_key_files(UploadFileDeclaration[] files, string expected)
    {
        var errors = Errors(new CreateUploadsRequest { Purpose = UploadPurpose.Post, Files = [.. files] });

        var error = Assert.Single(errors);
        Assert.Equal(nameof(CreateUploadsRequest.Files), error.Key);
        Assert.Equal(expected, error.Message);
    }

    public static TheoryData<UploadFileDeclaration[], string> LoHongCases()
    {
        static UploadFileDeclaration File(string contentType, long sizeBytes) =>
            new() { ContentType = contentType, SizeBytes = sizeBytes };

        return new TheoryData<UploadFileDeclaration[], string>
        {
            // Danh sách rỗng và `files` vắng mặt là cùng một ý, nên cùng một câu.
            { [], CreateUploadsRequestValidator.FilesRequired },
            {
                [.. Enumerable.Repeat(File("image/jpeg", 1024), PostContentPolicy.MaxMediaCount + 1)],
                CreateUploadsRequestValidator.TooManyFiles
            },
            { [File("image/gif", 1024)], CreateUploadsRequestValidator.FileNotAllowed },
            { [File("", 1024)], CreateUploadsRequestValidator.FileNotAllowed },
            { [File("image/jpeg", 0)], CreateUploadsRequestValidator.FileNotAllowed },
            { [File("image/jpeg", -1)], CreateUploadsRequestValidator.FileNotAllowed },
            { [File("image/jpeg", MediaAttachment.MaxSizeBytes + 1)], CreateUploadsRequestValidator.FileNotAllowed },
            // File thứ ba hỏng: luật chạy trên CẢ danh sách, không chỉ phần tử đầu.
            {
                [File("image/jpeg", 1024), File("image/png", 2048), File("image/jpeg", 20_000_000)],
                CreateUploadsRequestValidator.FileNotAllowed
            },
        };
    }

    /// <summary>
    /// Allowlist của <c>StorageKeys</c> so khớp KHÔNG phân biệt hoa thường, nên <c>IMAGE/JPEG</c> được nhận dù hợp đồng
    /// chỉ liệt kê chữ thường. Nới hơn hợp đồng, chấp nhận — cùng lập luận với chiều đọc enum của Q-D2. Ghi lại ở đây để
    /// ai siết lại thì thấy đó là một quyết định, không phải một lỗ hổng vừa phát hiện.
    /// </summary>
    [Fact]
    public void Content_type_viet_hoa_van_duoc_nhan_noi_hon_hop_dong() =>
        Assert.Empty(Errors(Request(UploadPurpose.Post, ("IMAGE/JPEG", 1024))));

    /// <summary>Biên DƯỚI và biên TRÊN của dung lượng đều được nhận — hợp đồng ghi <c>1..10485760</c> là khoảng ĐÓNG.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(MediaAttachment.MaxSizeBytes)]
    public void Bien_dung_luong_van_hop_le(long sizeBytes) =>
        Assert.Empty(Errors(Request(UploadPurpose.Avatar, ("image/jpeg", sizeBytes))));

    /// <summary>
    /// <c>"files": null</c> không được làm validator ném. DTO mặc định là danh sách rỗng nên chỉ JSON gửi thẳng
    /// <c>null</c> mới tới được trạng thái này; <c>Cascade(CascadeMode.Stop)</c> là thứ chặn <c>Must</c> phía sau chạm
    /// vào null.
    /// </summary>
    [Fact]
    public void Files_null_ra_400_chu_khong_nem()
    {
        var errors = Errors(new CreateUploadsRequest { Purpose = UploadPurpose.Post, Files = null! });

        var error = Assert.Single(errors);
        Assert.Equal(nameof(CreateUploadsRequest.Files), error.Key);
        Assert.Equal(CreateUploadsRequestValidator.FilesRequired, error.Message);
    }

    /// <summary>
    /// Ghi lại BẰNG MÁY lý do không dùng <c>RuleForEach(...).ChildRules(...).OverridePropertyName("files")</c> như bản
    /// nháp của hướng dẫn D4 bước 2: FluentValidation vẫn ghép chỉ số và tên trường con vào sau tên đã ghi đè, cho ra
    /// <c>files[0].SizeBytes</c> — không phải <c>files</c> mà hợp đồng đòi.
    ///
    /// Test này canh chính khẳng định đó thay vì canh code sản phẩm: bản FluentValidation nào đổi hành vi này thì nó đỏ,
    /// và lúc ấy bản viết bằng <c>RuleForEach</c> (thông điệp chỉ đúng file nào hỏng) mới đáng cân nhắc lại.
    /// </summary>
    [Fact]
    public void Vi_sao_khong_dung_RuleForEach_ChildRules_key_van_mang_chi_so()
    {
        var probe = new InlineValidator<CreateUploadsRequest>();
        probe.RuleForEach(x => x.Files)
            .ChildRules(f => f.RuleFor(d => d.SizeBytes).GreaterThan(0))
            .OverridePropertyName("files");

        var errors = probe.Validate(Request(UploadPurpose.Post, ("image/jpeg", 0))).Errors;

        Assert.Equal("files[0].SizeBytes", Assert.Single(errors).PropertyName);
    }
}
