-- seed-profiles.sql — 20.000 hồ sơ tên Việt có dấu cho EXPLAIN của tìm kiếm (GĐ6 A4, L-A10; D12 SRCH-07 và k6 Mục 10.6 dùng lại).
--
-- Seed feed (tests/load/feed/seed.sql) không dùng được: 10.000 tên "Perf User N" — không dấu, không họ, không kiểm được "đ"
-- hay tiền tố từ thứ hai. Và bảng vài chục dòng trong integration test thì planner LUÔN chọn Seq Scan — không chứng minh gì.
--
-- Chỉ chạy trên DB VỨT ĐƯỢC tên socialapp_search, SAU --migrate (cần migration AddDisplayNameSearch của Profile):
--   docker exec socialapp-dev-postgres-1 psql -U socialapp -d postgres -c 'CREATE DATABASE socialapp_search'
--   ConnectionStrings__Postgres="Host=localhost;Port=5432;Database=socialapp_search;Username=socialapp;Password=…" \
--     dotnet run --project src/backend/SocialApp.Api -- --migrate
--   docker cp tests/load/search/seed-profiles.sql socialapp-dev-postgres-1:/tmp/seed-profiles.sql
--   docker exec socialapp-dev-postgres-1 psql -v ON_ERROR_STOP=1 -U socialapp -d socialapp_search -f /tmp/seed-profiles.sql
-- Rồi chạy explain.sql cùng thư mục, dán kết quả, và DROP DATABASE socialapp_search.
-- Thiếu ON_ERROR_STOP thì psql in lỗi rồi chạy tiếp phần còn lại.
--
-- Chỉ chèn profile.profiles: tìm kiếm ở tầng này không đọc bảng nào khác. user_id ngẫu nhiên, không có dòng identity.users
-- tương ứng (không FK chéo schema — Đ-2.2).

BEGIN;

DO $$
BEGIN
    IF current_database() <> 'socialapp_search' THEN
        RAISE EXCEPTION 'seed-profiles.sql chỉ chạy trên socialapp_search, đang ở %', current_database();
    END IF;
    IF EXISTS (SELECT 1 FROM profile.profiles) THEN
        RAISE EXCEPTION 'profile.profiles đã có dữ liệu — đây không phải DB đo rỗng. Dừng.';
    END IF;
END $$;

SET LOCAL synchronous_commit = off;

-- Hạt giống cố định: hai lượt chạy ra cùng một bộ tên, kết quả EXPLAIN so được với nhau.
SELECT setseed(0.42);

-- 16 họ × 10 tên đệm × 27 tên = 4.320 tổ hợp — trùng tên nhiều như thật. Có "Đ" ở cả ba vị trí (Đặng, Đỗ · Đức tên đệm ·
-- Đức tên) để bẫy "đ" có mặt.
-- Cột khớp migration InitialProfile — không đoán.
INSERT INTO profile.profiles (user_id, display_name, bio, avatar_key, created_at, updated_at)
SELECT gen_random_uuid(),
       (ARRAY['Nguyễn','Trần','Lê','Phạm','Hoàng','Huỳnh','Phan','Vũ','Võ','Đặng','Bùi','Đỗ','Hồ','Ngô','Dương','Lý'])
           [1 + floor(random() * 16)::int]
       || ' ' || (ARRAY['Văn','Thị','Đức','Minh','Ngọc','Thu','Hữu','Quốc','Thanh','Gia'])[1 + floor(random() * 10)::int]
       || ' ' || (ARRAY['An','Bình','Châu','Dũng','Đức','Giang','Hà','Hải','Hạnh','Hoa','Hùng','Khánh','Lan','Linh','Long',
                        'Mai','Nam','Nga','Phúc','Quân','Sơn','Tâm','Thảo','Trang','Tuấn','Việt','Yến'])[1 + floor(random() * 27)::int],
       NULL,
       NULL,
       now(),
       now()
FROM generate_series(1, 20000);

COMMIT;

-- Ngoài transaction: thống kê mới cho planner — thiếu bước này planner đoán theo bảng rỗng.
ANALYZE profile.profiles;

SELECT count(*) AS profiles FROM profile.profiles;
