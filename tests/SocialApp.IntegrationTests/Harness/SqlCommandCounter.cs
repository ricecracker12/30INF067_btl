using System.Collections.Concurrent;
using System.Diagnostics;
using Npgsql;

namespace SocialApp.IntegrationTests.Harness;

/// <summary>
/// B1 (GĐ4): đếm lệnh SQL APP gửi tới MỘT database, qua ActivitySource "Npgsql" — bắt cả EF, FromSql lẫn Npgsql trần mà
/// không sửa Add*Module (L4). Lọc theo tên database vì các collection khác chạy song song trong cùng tiến trình.
/// </summary>
public sealed class SqlCommandCounter : IDisposable
{
    private readonly ActivityListener _listener;
    private readonly ConcurrentQueue<string> _statements = new();

    public SqlCommandCounter(string connectionString)
    {
        var database = new NpgsqlConnectionStringBuilder(connectionString).Database;
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Npgsql",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a =>
            {
                if (Equals(a.GetTagItem("db.name"), database))
                    _statements.Enqueue(a.GetTagItem("db.statement") as string ?? a.DisplayName);
            },
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public IReadOnlyCollection<string> Statements => _statements;

    public void Reset() => _statements.Clear();

    public void Dispose() => _listener.Dispose();
}
