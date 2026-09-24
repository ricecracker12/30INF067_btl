# Báo cáo p95 gửi→nhận của chat (GĐ5 F3 · NFR-PERF-03 · GOAL-02)

> Nguồn phương pháp: `giai-doan-5.md` Mục 10.7. Công cụ: `src/frontend/e2e/chat-latency.spec.ts` + `features/chat/latency*.ts`.
> **Trạng thái 2026-09-24: CHƯA đo trên staging** — chờ bản `gd5` lên staging (điểm dừng 2 của GĐ5). Mục "Staging" dưới đây để trống
> có chủ đích, không xóa. Lượt local chỉ chứng minh công cụ đo chạy đúng, KHÔNG phải số nghiệm thu.

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
CHAT_LATENCY=1 PLAYWRIGHT_BASE_URL=https://mxh.banhgao.net PLAYWRIGHT_API_URL=https://mxh.banhgao.net/api/v1 \
E2E_A_EMAIL=… E2E_A_PASSWORD=… E2E_B_EMAIL=… E2E_B_PASSWORD=… \
pnpm exec playwright test e2e/chat-latency.spec.ts --retries=0
# Kết quả: src/frontend/test-results/chat-latency.json
```

## Staging — số nghiệm thu GOAL-02 (p95 ≤ 1 s)

| Ngày giờ | Trình duyệt | Mạng máy đo | Mẫu (gửi / nhận) | p50 | p95 | p99 | max | Đạt? |
|---|---|---|---|---|---|---|---|---|
| *(chờ F3)* | | | | | | | | |

Phân rã phần server (`socialapp_message_push_seconds`, cùng khung giờ): *(chờ F3)*

Lượt nào có kết nối lại giữa chừng: *(chờ F3)*

## Local — lượt thử công cụ (2026-09-24, không phải nghiệm thu)

| Ngày giờ | Môi trường | Trình duyệt | Mẫu | p50 | p95 | p99 | max |
|---|---|---|---|---|---|---|---|
| 2026-09-24 ~09:30 (+07) | API + FE dev trên một máy Windows, Postgres/Redis Docker Desktop | Chrome đã cài (channel `chrome`) | 60 / 60 (N = 30 mỗi chiều) | 29,6 ms | 37,7 ms | 43 ms | 43 ms |

Theo chiều: A→B p95 31,8 ms · B→A p95 39 ms. Không lần nào nối lại giữa chừng. Số local chỉ loại trừ "công cụ đo sai" (p95 nhỏ, không
số âm, đủ mẫu) — mọi độ trễ thật của Cloudflare → apache → Kestrel chỉ có ở lượt staging.
