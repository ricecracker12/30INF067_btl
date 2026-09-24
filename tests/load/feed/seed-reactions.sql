-- seed-reactions.sql — cảm xúc cho lượt k6 GĐ3 (F5, giai-doan-3.md Mục 9 bước 6).
-- Chạy SAU seed.sql, trên CÙNG DB đo:
--   psql -v ON_ERROR_STOP=1 -U socialapp -d socialapp_perf -f tests/load/feed/seed-reactions.sql
--
-- Vì sao cần: GĐ3 thêm MỘT câu tra `myReaction` theo lô vào đường hydrate feed (Đ-3.11). seed.sql của GĐ4 không có cảm xúc
-- nào, nên câu đó tra trên bảng rỗng — rẻ hơn thực tế. File này cho mỗi người dùng thả cảm xúc vào 100 bài GẦN NHẤT của 5
-- người bạn (5 × 20), tức đúng loại bài hay hiện trên feed của họ: câu tra trúng dòng thật, không chỉ lướt qua index trống.
-- Tổng khoảng 1.000.000 dòng. Bộ đếm `posts.reaction_counts` dựng lại cho KHỚP bảng gốc (câu đối soát Mục 12 phải ra 0 dòng).

BEGIN;

DO $$
BEGIN
    IF current_database() <> 'socialapp_perf' THEN
        RAISE EXCEPTION 'seed-reactions.sql chỉ chạy trên socialapp_perf, đang ở %', current_database();
    END IF;
    IF NOT EXISTS (SELECT 1 FROM public.perf_users) THEN
        RAISE EXCEPTION 'Chưa có public.perf_users — chạy seed.sql trước.';
    END IF;
    IF EXISTS (SELECT 1 FROM content.reactions) THEN
        RAISE EXCEPTION 'content.reactions đã có dữ liệu — chỉ seed một lần trên DB đo mới.';
    END IF;
END $$;

SET LOCAL synchronous_commit = off;

-- Bạn của mỗi người: hai chiều của cặp chuẩn hóa (user_min_id, user_max_id), đi qua PK và idx_friendships_user_max.
-- Năm bạn chọn theo băm của cặp id — rải đều, ổn định giữa các lần seed. Đo 2026-09-25: "năm bạn id nhỏ nhất" dồn cả triệu
-- dòng vào ~15.000 bài; theo băm thì ~198.000 bài, ~5 cảm xúc mỗi bài. random() đặt ở danh sách SELECT ngoài cùng nên tính
-- MỖI dòng (bẫy LATERAL của seed.sql không áp dụng ở đây).
INSERT INTO content.reactions (user_id, target_type, target_id, type, created_at, updated_at)
SELECT
    u.user_id,
    'post',
    p.post_id,
    (ARRAY['like','love','haha','wow','sad','angry'])[1 + floor(random() * 6)::int],
    now(),
    now()
FROM public.perf_users u
CROSS JOIN LATERAL (
    SELECT friend FROM (
        SELECT f.user_max_id AS friend FROM socialgraph.friendships f
        WHERE f.user_min_id = u.user_id AND f.status = 'accepted'
        UNION ALL
        SELECT f.user_min_id FROM socialgraph.friendships f
        WHERE f.user_max_id = u.user_id AND f.status = 'accepted'
    ) both_sides
    ORDER BY md5(friend::text || u.user_id::text)
    LIMIT 5
) fr
CROSS JOIN LATERAL (
    SELECT post_id FROM content.posts
    WHERE author_id = fr.friend AND status = 'published'
    ORDER BY created_at DESC, post_id DESC
    LIMIT 20
) p
ON CONFLICT DO NOTHING;

-- Bộ đếm khớp bảng gốc — cùng dạng hợp đồng: không khóa nào mang giá trị 0.
UPDATE content.posts p
SET reaction_counts = t.counts
FROM (
    SELECT target_id, jsonb_object_agg(type, n) AS counts
    FROM (
        SELECT target_id, type, count(*) AS n
        FROM content.reactions
        WHERE target_type = 'post'
        GROUP BY target_id, type
    ) x
    GROUP BY target_id
) t
WHERE p.post_id = t.target_id;

COMMIT;

ANALYZE content.reactions;
ANALYZE content.posts;
