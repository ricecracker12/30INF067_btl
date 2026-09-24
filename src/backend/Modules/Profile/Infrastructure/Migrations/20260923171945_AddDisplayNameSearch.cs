using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SocialApp.Modules.Profile.Infrastructure.Migrations
{
    /// <summary>
    /// GĐ6 A4 — tìm tên không dấu (Đ-6.19, FR-017): gõ "nguyen" ra "Nguyễn …" mà không Seq Scan cả bảng hồ sơ.
    ///
    /// Model EF không đổi nên <c>dotnet ef migrations add</c> sinh <c>Up()</c> rỗng — đúng; toàn bộ là SQL viết tay (extension,
    /// hàm, index biểu thức: EF không biểu diễn được).
    ///
    /// Bẫy mà migration này gỡ: <c>unaccent()</c> là <c>STABLE</c> (phụ thuộc từ điển) nên Postgres từ chối index biểu thức trên nó.
    /// Hàm bọc <c>profile.search_norm</c> khai <c>IMMUTABLE</c> — một lời hứa mà ta giữ bằng cách gọi <c>unaccent</c> dạng HAI tham
    /// số với từ điển ghi rõ schema: kết quả không còn phụ thuộc <c>search_path</c> của phiên nào.
    /// </summary>
    public partial class AddDisplayNameSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Đ-6.19. Thứ tự bắt buộc: extension → hàm → index.
            // WITH SCHEMA public vì hàm gọi public.unaccent nguyên tên — không phụ thuộc search_path của phiên nào. Cả hai là trusted
            // extension (PG13+): chủ database tạo được, không cần superuser (staging: user migrate là superuser + chủ DB, Mục 6
            // bước 4 hướng dẫn khối A+C). IF NOT EXISTS: chủ dự án đã tạo tay trước (đường lùi R6-07) thì chạy lại vẫn qua.
            //
            // D12 PHẢI gọi đúng profile.search_norm(display_name) ở vế cột và profile.search_norm(@q) ở vế tham số — viết
            // lower(unaccent(…)) ở chỗ khác là biểu thức khác, planner không dùng index này (Seq Scan).
            //
            // Đổi từ điển unaccent sau này (nâng Postgres, sửa unaccent.rules) thì hàm "khai man" IMMUTABLE: index giữ giá trị cũ,
            // phải REINDEX INDEX profile.idx_profiles_display_name_search. Không làm gì thêm ở GĐ6.
            //
            // KHÔNG CONCURRENTLY: EF bọc migration trong transaction (giai-doan-6.md Mục 4 chỗ dễ sai 5).
            migrationBuilder.Sql("""
                CREATE EXTENSION IF NOT EXISTS unaccent WITH SCHEMA public;
                CREATE EXTENSION IF NOT EXISTS pg_trgm WITH SCHEMA public;

                CREATE FUNCTION profile.search_norm(text) RETURNS text
                    LANGUAGE sql IMMUTABLE PARALLEL SAFE STRICT
                    AS $$ SELECT lower(public.unaccent('public.unaccent'::regdictionary, $1)) $$;

                COMMENT ON FUNCTION profile.search_norm(text) IS
                    'Đ-6.19: chuẩn hóa tên để tìm không dấu. D12 phải gọi đúng hàm này ở cả hai vế, không lower(unaccent(...)). '
                    'Đổi từ điển unaccent thì REINDEX idx_profiles_display_name_search.';

                CREATE INDEX idx_profiles_display_name_search ON profile.profiles
                    USING gin (profile.search_norm(display_name) public.gin_trgm_ops);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // GIỮ extension: module khác có thể đã dùng unaccent/pg_trgm — DROP EXTENSION trong Down của một module là phá module
            // kia (và DROP ... CASCADE thì xóa luôn đối tượng của nó).
            migrationBuilder.Sql("""
                DROP INDEX profile.idx_profiles_display_name_search;
                DROP FUNCTION profile.search_norm(text);
                """);
        }
    }
}
