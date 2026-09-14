namespace SocialApp.IntegrationTests.Auth;

/// <summary>
/// Dựng thứ tự đan xen CÓ ĐIỀU KHIỂN cho hai thao tác trên cùng một family refresh token (RT-06 của D5, logout song song với
/// xoay vòng của D6). Trigger <c>BEFORE INSERT</c> chỉ tồn tại trong database test: lượt xoay đang giữ khóa dòng thì ngủ 2 giây
/// ngay sau khi INSERT token mới (chưa commit), để thao tác thứ hai chen vào đúng khoảnh khắc đó. Không có cờ hay hook nào trong
/// <c>src/</c>.
/// </summary>
internal static class RefreshFamilyRace
{
    /// <summary>Giữ mọi INSERT vào family <paramref name="familyId"/> lại 2 giây. Dispose để gỡ trigger.</summary>
    public static async Task<IAsyncDisposable> HoldInsertsIntoFamilyAsync(AuthTestClient auth, Guid familyId)
    {
        await auth.ExecuteSqlAsync($"""
            CREATE OR REPLACE FUNCTION identity.test_giu_luot_xoay() RETURNS trigger AS $body$
            BEGIN
              IF NEW.family_id = '{familyId}' THEN PERFORM pg_sleep(2); END IF;
              RETURN NEW;
            END $body$ LANGUAGE plpgsql;
            CREATE TRIGGER test_giu_luot_xoay BEFORE INSERT ON identity.refresh_tokens
              FOR EACH ROW EXECUTE FUNCTION identity.test_giu_luot_xoay();
            """);
        return new TriggerRemover(auth);
    }

    /// <summary>Chờ tới khi có session đang ngủ trong trigger — tức lượt xoay đang giữ khóa và token mới chưa commit.</summary>
    public static async Task WaitUntilSessionSleepsAsync(AuthTestClient auth)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var row = await auth.QueryRowAsync("SELECT count(*) AS n FROM pg_stat_activity WHERE wait_event = 'PgSleep'");
            if ((long)row!["n"]! > 0)
                return;
            await Task.Delay(50);
        }
        throw new TimeoutException("Lượt xoay không dừng trong trigger sau 5 giây — kịch bản đan xen không dựng được.");
    }

    private sealed class TriggerRemover(AuthTestClient auth) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() =>
            await auth.ExecuteSqlAsync("""
                DROP TRIGGER IF EXISTS test_giu_luot_xoay ON identity.refresh_tokens;
                DROP FUNCTION IF EXISTS identity.test_giu_luot_xoay();
                """);
    }
}
