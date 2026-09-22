-- seed.sql — bộ dữ liệu tải feed (Đ-4.13 / C5).
-- Chạy SAU --migrate bốn schema, bằng:
--   psql -v ON_ERROR_STOP=1 "…" -f tests/load/feed/seed.sql
-- Thiếu ON_ERROR_STOP thì psql in lỗi rồi chạy tiếp phần còn lại.

BEGIN;

DO $$
BEGIN
    IF current_database() <> 'socialapp_perf' THEN
        RAISE EXCEPTION 'seed.sql chỉ chạy trên socialapp_perf, đang ở %', current_database();
    END IF;
    IF EXISTS (SELECT 1 FROM identity.users) THEN
        RAISE EXCEPTION 'identity.users đã có dữ liệu — đây không phải DB đo rỗng. Dừng.';
    END IF;
END $$;

-- Tăng tốc ghi trong transaction seed; không ảnh hưởng cấu hình server lâu dài.
SET LOCAL synchronous_commit = off;

-- Id người dùng ổn định theo số thứ tự — C6 / users.csv dùng chung bảng này (không TEMP).
DROP TABLE IF EXISTS public.perf_users;
CREATE TABLE public.perf_users (
    n        int  PRIMARY KEY,
    user_id  uuid NOT NULL UNIQUE
);

-- 10.000 hồ sơ. Cột khớp migration InitialProfile — không đoán.
INSERT INTO public.perf_users (n, user_id)
SELECT gs, gen_random_uuid()
FROM generate_series(1, 10000) AS gs;

INSERT INTO profile.profiles (user_id, display_name, bio, avatar_key, created_at, updated_at)
SELECT
    u.user_id,
    'Perf User ' || u.n,
    NULL,
    NULL,
    now(),
    now()
FROM public.perf_users u;

-- Friendships: 5% (n ≤ 500) có ~500 bạn; còn lại ~100 bạn.
-- Chỉ nối "về phía trước" trên vòng — mỗi cạnh một lần; bậc ≈ 2 × số offset
-- nên offset = 50 / 250 để bậc trung bình ≈ 100 / 500 (Đ-4.13).
INSERT INTO socialgraph.friendships (
    user_min_id, user_max_id, requester_id, status, created_at, updated_at, accepted_at
)
SELECT
    LEAST(a.user_id, b.user_id),
    GREATEST(a.user_id, b.user_id),
    a.user_id,
    'accepted',
    now(),
    now(),
    now()
FROM public.perf_users a
CROSS JOIN LATERAL (
    SELECT p.user_id
    FROM generate_series(
        1,
        CASE WHEN a.n <= 500 THEN 250 ELSE 50 END
    ) AS g(off)
    JOIN public.perf_users p
      ON p.n = ((a.n - 1 + g.off) % 10000) + 1
    WHERE p.n <> a.n
) AS b
ON CONFLICT DO NOTHING;

-- Follows: trung bình 20/người, không tự theo dõi (CHECK ck_follows_not_self).
-- Offset 251..270 nằm NGOÀI vùng bạn (bạn xa nhất ±250 trên vòng): theo dõi một người đã là bạn thì
-- FeedSourceReader loại khỏi FollowingOnly, và nhánh "bài public của người chỉ theo dõi" (Đ-4.5) không bao giờ
-- chạy trong EXPLAIN của C2 lẫn k6 của C6. Bản đầu dùng 1..20 — trọn trong vùng bạn, FollowingOnly rỗng cả 10.000 người.
INSERT INTO socialgraph.follows (follower_id, followee_id, created_at)
SELECT
    a.user_id,
    p.user_id,
    now()
FROM public.perf_users a
CROSS JOIN generate_series(251, 270) AS g(off)
JOIN public.perf_users p
  ON p.n = ((a.n - 1 + g.off) % 10000) + 1
WHERE p.n <> a.n
ON CONFLICT DO NOTHING;

-- 1.000.000 bài trải 180 ngày; privacy 70/20/10; 1% hidden. Không ảnh (media_count = 0).
-- LATERAL phải tham chiếu gs — không thì planner gọi random() MỘT lần cho cả bảng.
INSERT INTO content.posts (
    post_id, author_id, body, privacy, status, media_count,
    comment_count, reaction_counts, hidden_reason, edited_at,
    created_at, updated_at, deleted_at
)
SELECT
    gen_random_uuid(),
    (SELECT user_id FROM public.perf_users WHERE n = 1 + ((gs - 1) % 10000)),
    'seed post ' || gs,
    CASE
        WHEN rnd.r < 0.70 THEN 'public'
        WHEN rnd.r < 0.90 THEN 'friends'
        ELSE 'private'
    END,
    CASE WHEN rnd.r_hidden < 0.01 THEN 'hidden' ELSE 'published' END,
    0::smallint,
    0,
    '{}'::jsonb,
    CASE WHEN rnd.r_hidden < 0.01 THEN 'perf seed' ELSE NULL END,
    NULL,
    now() - (rnd.r_time * interval '180 days'),
    now(),
    NULL
FROM generate_series(1, 1000000) AS gs
CROSS JOIN LATERAL (
    SELECT gs AS _row, random() AS r, random() AS r_hidden, random() AS r_time
) AS rnd;

ANALYZE profile.profiles;
ANALYZE socialgraph.friendships;
ANALYZE socialgraph.follows;
ANALYZE content.posts;
ANALYZE public.perf_users;

COMMIT;
