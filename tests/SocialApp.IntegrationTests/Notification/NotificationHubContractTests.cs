using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.SignalR;
using SocialApp.Modules.Notification.Application;
using SocialApp.Modules.Notification.Presentation;
using Xunit;

namespace SocialApp.IntegrationTests.Notification;

/// <summary>
/// Cổng hợp đồng HUB thông báo (C6, giai-doan-6.md Mục 8.4) — khuôn <c>ChatHubContractTests</c>: hub không có Swagger nên không dùng
/// <c>ContractTestsBase</c>. Đọc <c>notification-hub-v1.examples.json</c> (nguồn chung với fixture FE) và khẳng định:
/// 1. Ví dụ sự kiện deserialize được thành ĐÚNG DTO rồi serialize lại ra ĐÚNG JSON đó.
/// 2. Hub KHÔNG có phương thức public nào (<c>methods</c> rỗng) và tập sự kiện = <c>events</c>.
/// </summary>
[Trait("Category", "Contract")]
public sealed class NotificationHubContractTests
{
    private static readonly JsonSerializerOptions Hub = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private static readonly JsonNode Examples = JsonNode.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Contracts", "notification-hub-v1.examples.json")))!;

    [Fact]
    public void Vi_du_NotificationUpserted_khu_hoi_dung_DTO()
    {
        var original = Examples["events"]!["NotificationUpserted"]!.ToJsonString();

        var value = JsonSerializer.Deserialize<NotificationUpsertedEvent>(original, Hub);
        var roundTrip = JsonSerializer.Serialize(value, Hub);

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(original), JsonNode.Parse(roundTrip)),
            $"Ví dụ và DTO lệch nhau.\n  ví dụ : {original}\n  DTO   : {roundTrip}");
    }

    /// <summary>Đ-6.18: chỉ server → client. Một phương thức public ở hub là một cửa client gọi được — đổi hợp đồng, phải có tầng 3.</summary>
    [Fact]
    public void Hub_khong_phuong_thuc_nao_tap_su_kien_bang_file_vi_du()
    {
        var methods = typeof(NotificationHub)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(m => m.Name);

        Assert.Empty(methods);
        Assert.Empty(Examples["methods"]!.AsObject());
        Assert.Equal(Examples["events"]!.AsObject().Select(kv => kv.Key).Order(), NotificationHub.Events.Order());
        Assert.True(typeof(Hub).IsAssignableFrom(typeof(NotificationHub)));
    }
}
