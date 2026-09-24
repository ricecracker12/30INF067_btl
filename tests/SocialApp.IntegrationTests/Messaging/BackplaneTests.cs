using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR.Client;
using SocialApp.IntegrationTests.Harness;
using SocialApp.Modules.Messaging.Application.Conversations;
using Xunit;

namespace SocialApp.IntegrationTests.Messaging;

/// <summary>
/// C4 (giai-doan-5.md Đ-5.13, R5-07): HAI bản sao API chung Postgres + Redis — A nối bản sao 1, B nối bản sao 2. Có backplane thì
/// tin A gửi tới B ở bản sao kia; tắt backplane thì KHÔNG tới — ca đối chứng chứng minh ca chính có nghĩa (không phải "tới vì
/// cùng process").
///
/// Lệch B.5 C4 (chốt 2026-09-24): tài liệu gợi ý thử tay bằng compose <c>--scale api=2</c> sau Caddy tạm. Hai host TestServer
/// thật chung Redis thật cho cùng bằng chứng mà thành test tự động chạy mỗi lần — GĐ7 khối E bật cờ là biết ngay có vỡ không.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class BackplaneTests(PostgresFixture postgres, RedisFixture redis)
    : IClassFixture<RedisFixture>
{
    private async Task<(ModulesApiFactory Host1, ModulesApiFactory Host2)> TwoHostsAsync(bool backplane)
    {
        var host1 = new ModulesApiFactory();
        await host1.UseFreshDatabaseAsync(postgres);
        var host2 = new ModulesApiFactory();
        host2.UseDatabase(host1.ConnectionString);

        foreach (var host in new[] { host1, host2 })
        {
            host.UseRedis(redis.ConnectionString);
            host.UseSetting("Realtime:Backplane:Enabled", backplane ? "true" : "false");
        }

        return (host1, host2);
    }

    private static async Task<bool> BReceivesAcrossHostsAsync(ModulesApiFactory host1, ModulesApiFactory host2)
    {
        var rest = new MessagingTestClient(host1);
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        await rest.FriendsAsync(a, b);
        var conversationId = (await rest.OpenOkAsync(a, b)).ConversationId;

        await using var ca = await RealtimeTestClient.ConnectAsync(host1, new ModulesTestClient(host1).Http, a);
        await using var cb = await RealtimeTestClient.ConnectAsync(host2, new ModulesTestClient(host2).Http, b);
        var received = new ConcurrentQueue<MessageResponse>();
        cb.On<MessageResponse>("MessageReceived", received.Enqueue);

        var ack = await ca.InvokeAsync<SendMessageResult>(
            "SendMessage", new SendMessageArgs(conversationId, "qua bản sao kia", Guid.NewGuid()));

        var until = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < until)
        {
            if (received.Any(m => m.MessageId == ack.Message.MessageId))
                return true;
            await Task.Delay(50);
        }

        return false;
    }

    [Fact]
    public async Task Co_backplane_tin_toi_nguoi_nhan_o_ban_sao_kia()
    {
        var (host1, host2) = await TwoHostsAsync(backplane: true);
        await using (host1)
        await using (host2)
            Assert.True(await BReceivesAcrossHostsAsync(host1, host2), "B ở bản sao 2 không nhận được tin A gửi qua bản sao 1.");
    }

    [Fact]
    public async Task Doi_chung_tat_backplane_tin_khong_qua_ban_sao_kia()
    {
        var (host1, host2) = await TwoHostsAsync(backplane: false);
        await using (host1)
        await using (host2)
            Assert.False(await BReceivesAcrossHostsAsync(host1, host2), "Tắt backplane mà B vẫn nhận — ca chính không chứng minh gì.");
    }
}
