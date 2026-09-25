using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using SocialApp.Modules.Profile.Application.Search;

namespace SocialApp.Modules.Profile.Infrastructure.Persistence;

/// <summary>
/// Hiện thực <see cref="IProfileSearch"/> — một câu SQL thô tham số hóa trên kết nối của <see cref="ProfileDbContext"/> (khuôn
/// <c>ReportStore</c> của Moderation).
///
/// <b>Hai vế cùng một hàm:</b> cột là <c>profile.search_norm(display_name)</c> — ĐÚNG biểu thức của
/// <c>idx_profiles_display_name_search</c> (A4) — và tham số là <c>profile.search_norm($1)</c>. Chuẩn hóa tham số ở DB, không ở C#
/// (<c>RemoveDiacritics</c> lệch từ điển <c>unaccent</c> ở chữ hiếm, "đ" xử lý khác — cạm bẫy 2). <c>$1</c> đã escape ở C#:
/// <c>unaccent</c>/<c>lower</c> không đụng <c>\ % _</c> nên escape trước chuẩn hóa là an toàn.
///
/// <b>Khớp tiền tố của TỪNG từ</b> (Đ-6.19): cả tên bắt đầu bằng từ khóa, HOẶC có một từ sau dấu cách bắt đầu bằng nó — "van" ra
/// "Nguyễn Văn An". Hai vế <c>LIKE</c> thành <c>BitmapOr</c> của hai lần quét GIN trigram (<c>tests/load/search/explain.sql</c>).
///
/// <b>Xếp hạng:</b> tên BẮT ĐẦU bằng từ khóa trước ("An Bình" trước "Bảo An"), rồi <c>similarity</c> trigram với từ khóa (chưa escape),
/// rồi tên, rồi id — thứ tự ổn định giữa hai lần gọi.
/// </summary>
internal sealed class ProfileSearch(ProfileDbContext db) : IProfileSearch
{
    private const string SearchSql = """
        SELECT user_id, display_name, avatar_key
        FROM profile.profiles
        WHERE profile.search_norm(display_name) LIKE profile.search_norm($1) || '%' ESCAPE '\'
           OR profile.search_norm(display_name) LIKE '% ' || profile.search_norm($1) || '%' ESCAPE '\'
        ORDER BY (profile.search_norm(display_name) LIKE profile.search_norm($1) || '%' ESCAPE '\') DESC,
                 public.similarity(profile.search_norm(display_name), profile.search_norm($2)) DESC,
                 display_name,
                 user_id
        LIMIT $3
        """;

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string escapedTerm, string rawTerm, int take, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var cmd = new NpgsqlCommand(SearchSql, (NpgsqlConnection)db.Database.GetDbConnection());
            cmd.Parameters.Add(new NpgsqlParameter { Value = escapedTerm, NpgsqlDbType = NpgsqlDbType.Text });
            cmd.Parameters.Add(new NpgsqlParameter { Value = rawTerm, NpgsqlDbType = NpgsqlDbType.Text });
            cmd.Parameters.Add(new NpgsqlParameter { Value = take, NpgsqlDbType = NpgsqlDbType.Integer });

            var hits = new List<SearchHit>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                hits.Add(new SearchHit(reader.GetGuid(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
            return hits;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
