-- Đếm CHÍNH XÁC số bản ghi mọi bảng trong ba schema ứng dụng — dùng cho biên bản restore drill (Mục 3):
-- chạy trên DB gốc trước, chạy trên DB khôi phục sau, hai cột phải khớp.
--   docker compose exec -T postgres psql -U socialapp -d socialapp -At -f - < deploy/dem-ban-ghi.sql
-- (query_to_xml là cách đếm count(*) thật cho từng bảng bằng một câu; pg_stat_user_tables chỉ là ước lượng.)
select
  format('%I.%I', table_schema, table_name) as bang,
  (xpath('/row/c/text()',
         query_to_xml(format('select count(*) as c from %I.%I', table_schema, table_name), false, true, ''))
  )[1]::text::bigint as so_ban_ghi
from information_schema.tables
where table_schema in ('identity', 'profile', 'content', 'socialgraph', 'messaging', 'notification', 'moderation')
  and table_type = 'BASE TABLE'
order by 1;
