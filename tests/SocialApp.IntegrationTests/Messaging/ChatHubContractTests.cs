using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.SignalR;
using SocialApp.Modules.Messaging.Application.Conversations;
using SocialApp.Modules.Messaging.Presentation;
using Xunit;

namespace SocialApp.IntegrationTests.Messaging;

/// <summary>
/// Cổng hợp đồng HUB (giai-doan-5.md Mục 8.3, Mục 10.5 #2) — hub không có Swagger nên không dùng <c>ContractTestsBase</c>. Đọc
/// <c>chat-hub-v1.examples.json</c> (nguồn chung với fixture FE, luật frontend Mục 8) và khẳng định:
/// 1. Mỗi ví dụ deserialize được thành ĐÚNG DTO của hub rồi serialize lại ra ĐÚNG JSON đó — bắt đổi tên trường, đổi kiểu, đổi
///    camelCase, đổi enum.
/// 2. Tập phương thức public của <see cref="ChatHub"/> = tập <c>methods</c>; tập sự kiện = <c>events</c>; tập mã lỗi = <c>errors</c>.
///
/// Serializer cùng cấu hình giao thức JSON của hub (<c>AddSharedKernelRealtime</c>): camelCase + enum chữ thường, và CẤM trường
/// lạ để một trường thừa trong ví dụ không lọt qua.
/// </summary>
[Trait("Category", "Contract")]
public sealed class ChatHubContractTests
{
    private static readonly JsonSerializerOptions Hub = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private static readonly JsonNode Examples = JsonNode.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Contracts", "chat-hub-v1.examples.json")))!;

    public static IEnumerable<object[]> Payloads =>
    [
        ["methods.SendMessage.args", typeof(SendMessageArgs)],
        ["methods.SendMessage.result", typeof(SendMessageResult)],
        ["methods.SendReceipt.args", typeof(SendReceiptArgs)],
        ["events.MessageReceived", typeof(MessageResponse)],
        ["events.ReceiptUpdated", typeof(ReceiptUpdatedEvent)],
    ];

    [Theory]
    [MemberData(nameof(Payloads))]
    public void Vi_du_khu_hoi_dung_DTO_cua_hub(string path, Type dto)
    {
        var node = path.Split('.').Aggregate(Examples, (n, key) => n[key]!);
        var original = node.ToJsonString();

        var value = JsonSerializer.Deserialize(original, dto, Hub);
        var roundTrip = JsonSerializer.Serialize(value, dto, Hub);

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(original), JsonNode.Parse(roundTrip)),
            $"{path}: ví dụ và DTO {dto.Name} lệch nhau.\n  ví dụ : {original}\n  DTO   : {roundTrip}");
    }

    [Fact]
    public void SendReceipt_tra_null()
    {
        Assert.Null(Examples["methods"]!["SendReceipt"]!["result"]);
        Assert.Equal(typeof(Task), typeof(ChatHub).GetMethod(nameof(ChatHub.SendReceipt))!.ReturnType);
    }

    [Fact]
    public void Tap_phuong_thuc_su_kien_ma_loi_bang_file_vi_du()
    {
        var methods = typeof(ChatHub)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .Order();
        var events = Examples["events"]!.AsObject().Select(kv => kv.Key).Order();
        var errors = Examples["errors"]!.AsArray().Select(e => e!.GetValue<string>()).Order();

        Assert.Equal(Examples["methods"]!.AsObject().Select(kv => kv.Key).Order(), methods);
        Assert.Equal(events, ChatHub.Events.Order());
        Assert.Equal(errors, HubErrorCodes.All.Order());
    }

    /// <summary>Hub KHÔNG có phương thức nào nhận id người gửi: actorId luôn từ <c>Context.UserIdentifier</c> (B.10 điều 1).</summary>
    [Fact]
    public void Khong_tham_so_hub_nao_mang_id_nguoi_gui()
    {
        var suspicious = new[] { typeof(SendMessageArgs), typeof(SendReceiptArgs) }
            .SelectMany(t => t.GetProperties())
            .Where(p => p.Name.Contains("Sender", StringComparison.OrdinalIgnoreCase)
                     || p.Name.Contains("Actor", StringComparison.OrdinalIgnoreCase)
                     || p.Name.Equals("UserId", StringComparison.OrdinalIgnoreCase))
            .Select(p => $"{p.DeclaringType!.Name}.{p.Name}")
            .ToList();

        Assert.Empty(suspicious);
        Assert.True(typeof(Hub).IsAssignableFrom(typeof(ChatHub)));
    }
}
