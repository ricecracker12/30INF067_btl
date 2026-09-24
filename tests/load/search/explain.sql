-- explain.sql — ba câu EXPLAIN của tìm kiếm không dấu (GĐ6 A4; D12 dùng lại cho SRCH-07). Chạy SAU seed-profiles.sql:
--   docker cp tests/load/search/explain.sql socialapp-dev-postgres-1:/tmp/explain.sql
--   docker exec socialapp-dev-postgres-1 psql -v ON_ERROR_STOP=1 -U socialapp -d socialapp_search -f /tmp/explain.sql
--
-- Đạt khi CẢ BA kế hoạch có BitmapOr của hai "Bitmap Index Scan on idx_profiles_display_name_search", không Seq Scan.
--
-- Sửa 2026-09-25 khi thi công D12: ba câu là ĐÚNG câu SQL của ProfileSearch (Profile/Infrastructure/Persistence) — WHERE, ORDER BY,
-- LIMIT — chỉ thay tham số bằng chữ: $1 = từ khóa đã escape, $2 = từ khóa thô, $3 = limit mặc định 10 + lấy dư 5. Bản A4 viết vế
-- tham số bằng chữ đã chuẩn hóa sẵn ('ng%'); D12 chuẩn hóa tham số ở DB bằng profile.search_norm(…) — hàm IMMUTABLE trên hằng nên
-- planner gập thành hằng, cùng kế hoạch như khi Npgsql gửi tham số. Sửa câu của ProfileSearch thì sửa ba câu này theo.
-- Hai vế theo Đ-6.19: tiền tố của từ đầu, hoặc tiền tố của bất kỳ từ nào sau dấu cách.

\echo '--- q = ng (2 ký tự — ngưỡng tối thiểu)'
EXPLAIN (ANALYZE, BUFFERS)
SELECT user_id, display_name, avatar_key
FROM profile.profiles
WHERE profile.search_norm(display_name) LIKE profile.search_norm('ng') || '%' ESCAPE '\'
   OR profile.search_norm(display_name) LIKE '% ' || profile.search_norm('ng') || '%' ESCAPE '\'
ORDER BY (profile.search_norm(display_name) LIKE profile.search_norm('ng') || '%' ESCAPE '\') DESC,
         public.similarity(profile.search_norm(display_name), profile.search_norm('ng')) DESC,
         display_name,
         user_id
LIMIT 15;

\echo '--- q = nguy (tiền tố họ)'
EXPLAIN (ANALYZE, BUFFERS)
SELECT user_id, display_name, avatar_key
FROM profile.profiles
WHERE profile.search_norm(display_name) LIKE profile.search_norm('nguy') || '%' ESCAPE '\'
   OR profile.search_norm(display_name) LIKE '% ' || profile.search_norm('nguy') || '%' ESCAPE '\'
ORDER BY (profile.search_norm(display_name) LIKE profile.search_norm('nguy') || '%' ESCAPE '\') DESC,
         public.similarity(profile.search_norm(display_name), profile.search_norm('nguy')) DESC,
         display_name,
         user_id
LIMIT 15;

\echo '--- q = van (tiền tố từ thứ hai)'
EXPLAIN (ANALYZE, BUFFERS)
SELECT user_id, display_name, avatar_key
FROM profile.profiles
WHERE profile.search_norm(display_name) LIKE profile.search_norm('van') || '%' ESCAPE '\'
   OR profile.search_norm(display_name) LIKE '% ' || profile.search_norm('van') || '%' ESCAPE '\'
ORDER BY (profile.search_norm(display_name) LIKE profile.search_norm('van') || '%' ESCAPE '\') DESC,
         public.similarity(profile.search_norm(display_name), profile.search_norm('van')) DESC,
         display_name,
         user_id
LIMIT 15;
