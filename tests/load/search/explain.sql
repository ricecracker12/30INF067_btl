-- explain.sql — ba câu EXPLAIN của tìm kiếm không dấu (GĐ6 A4; D12 dùng lại cho SRCH-07). Chạy SAU seed-profiles.sql:
--   docker cp tests/load/search/explain.sql socialapp-dev-postgres-1:/tmp/explain.sql
--   docker exec socialapp-dev-postgres-1 psql -v ON_ERROR_STOP=1 -U socialapp -d socialapp_search -f /tmp/explain.sql
--
-- Đạt khi CẢ BA kế hoạch có BitmapOr của hai "Bitmap Index Scan on idx_profiles_display_name_search", không Seq Scan.
-- Vế tham số viết bằng chữ đã chuẩn hóa sẵn ('ng', 'nguy', 'van') — D12 truyền profile.search_norm(@q) đã escape %, _, \.
-- Hai vế theo Đ-6.19: tiền tố của từ đầu, hoặc tiền tố của bất kỳ từ nào sau dấu cách.

\echo '--- q = ng (2 ký tự — ngưỡng tối thiểu)'
EXPLAIN (ANALYZE, BUFFERS)
SELECT user_id FROM profile.profiles
 WHERE profile.search_norm(display_name) LIKE 'ng%' OR profile.search_norm(display_name) LIKE '% ng%';

\echo '--- q = nguy (tiền tố họ)'
EXPLAIN (ANALYZE, BUFFERS)
SELECT user_id FROM profile.profiles
 WHERE profile.search_norm(display_name) LIKE 'nguy%' OR profile.search_norm(display_name) LIKE '% nguy%';

\echo '--- q = van (tiền tố từ thứ hai)'
EXPLAIN (ANALYZE, BUFFERS)
SELECT user_id FROM profile.profiles
 WHERE profile.search_norm(display_name) LIKE 'van%' OR profile.search_norm(display_name) LIKE '% van%';
