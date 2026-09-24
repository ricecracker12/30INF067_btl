# Báo cáo p95 gửi→nhận của chat (GĐ5 F3 · NFR-PERF-03 · GOAL-02)

> Nguồn phương pháp: `giai-doan-5.md` Mục 10.7. Công cụ: `src/frontend/e2e/chat-latency.spec.ts` + `features/chat/latency*.ts`.
> **Trạng thái 2026-09-24: ĐẠT trên staging — p95 = 174,7 ms** (ngưỡng GOAL-02 là 1 s), 400/400 mẫu, bản `develop@6503549`.
> Phân rã phần server (histogram trên `/metrics`) CHƯA lấy: `/metrics` không mở ra Internet (404 qua Cloudflare, đúng thiết kế) — cần
> người có quyền VM đọc Prometheus cùng khung giờ. Lượt local ở cuối chỉ chứng minh công cụ đo chạy đúng.

## Cách đo

- **Hai trình duyệt, MỘT máy, MỘT đồng hồ**: hai `BrowserContext` của Playwright trong một tiến trình Chrome. Đồng hồ của hai máy lệch
  nhau cỡ hàng trăm ms — bằng chính con số cần đo — nên không đo giữa hai máy.
- **Đo trên màn chat thật** (`/messages/{id}?latency=1`), không trang HTML tạm. Tab gửi ghi `t0` theo `clientMsgId` lúc bấm Enter; tab
  nhận ghi `t1` sau khi tin được **vẽ** (hai lần `requestAnimationFrame`). p = `t1 − t0`.
- **Nhịp 1 tin/giây** — đo độ trễ, không đo thông lượng. N tin A→B rồi N tin B→A (mặc định N = 200 → 400 mẫu).
- **Phần server** tách riêng bằng histogram `socialapp_message_push_seconds` (từ `COMMIT` tới khi gọi xong `SendAsync`) trên `/metrics`
  — khi p95 không đạt thì biết chậm ở DB/server hay ở đường truyền.

Lệnh:

```bash
cd src/frontend
# Staging — hai tài khoản ĐÃ là bạn của nhau; không bật webServer (PLAYWRIGHT_BASE_URL trỏ staging)
# Tài khoản: biến E2E_A_EMAIL / E2E_A_PASSWORD / E2E_B_EMAIL / E2E_B_PASSWORD hoặc file .env.e2e.local (gitignore). Thiếu khóa
# mà trỏ máy không phải localhost → spec DỪNG, không rơi sang nhánh tự đăng ký tài khoản.
CHAT_LATENCY=1 PLAYWRIGHT_BASE_URL=https://mxh.banhgao.net PLAYWRIGHT_API_URL=https://mxh.banhgao.net/api/v1 \
pnpm exec playwright test e2e/chat-latency.spec.ts --retries=0
# Kết quả: src/frontend/test-results/chat-latency.json
```

## Staging — số nghiệm thu GOAL-02 (p95 ≤ 1 s)

| Ngày giờ | Trình duyệt | Mạng máy đo | Mẫu (gửi / nhận) | p50 | p95 | p99 | max | Đạt? |
|---|---|---|---|---|---|---|---|---|
| 2026-09-24 09:33–09:41 UTC | Chrome 153.0.8010.53 (channel `chrome`), Windows 10 | Máy dev ở VN → Cloudflare → apache → Kestrel (VM OCI) | 400 / 400 (N = 200 mỗi chiều) | 145,4 ms | **174,7 ms** | 452,6 ms | 522,5 ms | **Đạt** |

Theo chiều:

| Chiều | Gửi / nhận | p50 | p95 | p99 | max |
|---|---|---|---|---|---|
| A→B | 200 / 200 | 116,3 ms | 176,4 ms | 508,1 ms | 522,5 ms |
| B→A | 200 / 200 | 152,0 ms | 169,8 ms | 177,9 ms | 182,2 ms |

**Đọc số:** p95 hai chiều sát nhau (170–176 ms), cỡ một vòng máy đo ↔ VM qua Cloudflare (F1 đo một mẫu ~240 ms). Đuôi A→B (p99 508 ms)
là vài mẫu lẻ, không lặp ở chiều B→A — chưa có histogram thì không nói được là server hay đường truyền. Không mất mẫu nào (400/400).

**Bối cảnh:** lượt đầu (sáng cùng ngày) bị hoãn vì VM trả byte đầu 0,4–13,6 s. Ngay trước lượt này đo lại `/api/v1/ping`: TTFB
0,30–0,57 s (5 mẫu) — VM đã ổn. Số trên là số lúc VM bình thường.

Phân rã phần server (`socialapp_message_push_seconds`, 09:33–09:41 UTC): **chờ người có quyền VM** — Prometheus `127.0.0.1:9090` /
Grafana `127.0.0.1:2998` trên VM (GĐ7 khối C). Truy vấn gợi ý:
`histogram_quantile(0.95, sum(rate(socialapp_message_push_seconds_bucket[8m])) by (le))`. p95 đầu-cuối đã đạt nên phân rã chỉ để lưu
hồ sơ, không chặn cổng.

Lượt nào có kết nối lại giữa chừng: không — 400/400 mẫu tới trong hạn chờ, lượt chạy `--retries=0` xanh lần đầu.

**Fallback (Đ-5.12, cùng buổi):** chặn mọi WebSocket `/hubs/*` của B (`e2e/chat.spec.ts` ca thứ hai) → sau 3 lần hỏng B vào fallback;
tin A gửi tới B sau **2 888 ms** (hạn Mục 12: ≤ ~3 s), tin B gửi bằng REST tới A qua hub.

## Local — lượt thử công cụ (2026-09-24, không phải nghiệm thu)

| Ngày giờ | Môi trường | Trình duyệt | Mẫu | p50 | p95 | p99 | max |
|---|---|---|---|---|---|---|---|
| 2026-09-24 ~09:30 (+07) | API + FE dev trên một máy Windows, Postgres/Redis Docker Desktop | Chrome đã cài (channel `chrome`) | 60 / 60 (N = 30 mỗi chiều) | 29,6 ms | 37,7 ms | 43 ms | 43 ms |

Theo chiều: A→B p95 31,8 ms · B→A p95 39 ms. Không lần nào nối lại giữa chừng. Số local chỉ loại trừ "công cụ đo sai" (p95 nhỏ, không
số âm, đủ mẫu) — mọi độ trễ thật của Cloudflare → apache → Kestrel chỉ có ở lượt staging.
