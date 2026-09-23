// Kịch bản k6 đo GET /feed (GĐ4 · C6 · Đ-4.13). Chạy trên môi trường đo socialapp-perf — README.md cùng thư mục.
//
// Gọi THẲNG API (không qua BFF): GOAL-01 là độ trễ của API. Mỗi VU là MỘT người dùng riêng (rate limit 100 req/phút theo
// user), token tự ký trong setup() bằng khóa của môi trường đo — không đăng nhập thật (BCrypt + rate limit auth).
//
// Biến môi trường:
//   PERF_JWT_KEY  bắt buộc — CÙNG giá trị Jwt__SigningKey của api đo (tests/load/feed/.env). KHÔNG gõ vào file này.
//   BASE_URL      mặc định http://localhost:18080 (chạy k6 trong mạng compose thì http://api:8080)
//   USERS_CSV     mặc định ./users.csv (xuất từ seed, gitignore)
//   SMOKE=1       tự kiểm trước lượt thật: 5 VU × 30s (Bước 2 của C6)
import http from 'k6/http';
import { check, fail, sleep } from 'k6';
import { hmac } from 'k6/crypto';
import encoding from 'k6/encoding';
import { SharedArray } from 'k6/data';
import { Counter } from 'k6/metrics';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:18080';
const KEY = __ENV.PERF_JWT_KEY;
const SMOKE = __ENV.SMOKE === '1';

// Kiểm lại với src/backend/SocialApp.Api/appsettings.json ("Jwt") trước mỗi lượt — lệch là 401 toàn bộ.
const ISSUER = 'https://mxh.banhgao.net';
const AUDIENCE = 'socialapp-api';

// Kịch bản chính: 2 phút lên 1.000 VU, giữ 5 phút, 1 phút xuống — 8 phút. Token sống 30 phút: phủ cả lượt + dự phòng.
const VUS = 1000;
const TOKEN_SECONDS = 30 * 60;

// Tỷ lệ vòng đi tiếp trang 2 bằng nextCursor.
const NEXT_PAGE_RATE = 0.3;

const users = new SharedArray('users', () =>
  open(__ENV.USERS_CSV || './users.csv')
    .split('\n')
    .slice(1) // bỏ header user_id
    .map((line) => line.trim())
    .filter((line) => line.length > 0),
);

const byStatus = new Counter('feed_status');

export const options = SMOKE
  ? {
      vus: 5,
      duration: '30s',
      thresholds: { checks: ['rate==1'] },
    }
  : {
      stages: [
        { duration: '2m', target: VUS },
        { duration: '5m', target: VUS },
        { duration: '1m', target: 0 },
      ],
      thresholds: {
        http_req_duration: ['p(95)<500'],
        http_req_failed: ['rate<0.01'],
      },
      summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
    };

function b64url(value) {
  return encoding.b64encode(value, 'rawurl');
}

function uuid() {
  // jti chỉ cần khác nhau giữa các token; app không tra jti trên đường đọc.
  const hex = '0123456789abcdef';
  let s = '';
  for (let i = 0; i < 32; i++) s += hex[Math.floor(Math.random() * 16)];
  return `${s.slice(0, 8)}-${s.slice(8, 12)}-4${s.slice(13, 16)}-a${s.slice(17, 20)}-${s.slice(20)}`;
}

// HS256, claim giống JwtAccessTokenIssuer: sub, role, jti + iat/exp/iss/aud. Khóa = byte UTF-8 của chuỗi (như app).
function sign(userId, now) {
  const header = b64url(JSON.stringify({ alg: 'HS256', typ: 'JWT' }));
  const payload = b64url(
    JSON.stringify({
      sub: userId,
      role: 'USER',
      jti: uuid(),
      iat: now,
      nbf: now,
      exp: now + TOKEN_SECONDS,
      iss: ISSUER,
      aud: AUDIENCE,
    }),
  );
  const signature = b64url(hmac('sha256', KEY, `${header}.${payload}`, 'binary'));
  return `${header}.${payload}.${signature}`;
}

export function setup() {
  if (!KEY) fail('Thiếu PERF_JWT_KEY — đọc tests/load/feed/README.md');
  const count = SMOKE ? 5 : VUS;
  if (users.length < count * 10) fail(`users.csv có ${users.length} người, cần ≥ ${count * 10}`);

  // Bước 10 trên danh sách seed: VU trải đều cả phân bố — 5% đầu (n ≤ 500) là người ~500 bạn (Đ-4.13).
  const now = Math.floor(Date.now() / 1000);
  const tokens = [];
  for (let i = 0; i < count; i++) tokens.push(sign(users[i * 10], now));
  return { tokens };
}

function getFeed(token, cursor) {
  const url = cursor ? `${BASE_URL}/api/v1/feed?cursor=${encodeURIComponent(cursor)}` : `${BASE_URL}/api/v1/feed`;
  const res = http.get(url, {
    headers: { Authorization: `Bearer ${token}` },
    tags: { name: cursor ? 'feed-page2' : 'feed-page1' },
  });
  byStatus.add(1, { status: String(res.status) });
  check(res, { '200': (r) => r.status === 200 });
  return res;
}

export default function (data) {
  const token = data.tokens[(__VU - 1) % data.tokens.length];

  const first = getFeed(token, null);
  if (first.status === 200 && Math.random() < NEXT_PAGE_RATE) {
    const next = first.json('nextCursor');
    if (next) getFeed(token, next);
  }

  // 1–3s giữa hai vòng: ≤ ~60 req/phút/người dùng, dưới rate limit 100. Không tăng số này cho đẹp p95 (PERF-02).
  sleep(1 + Math.random() * 2);
}
