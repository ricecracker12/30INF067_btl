# Hướng dẫn thực hiện — Khối C. Metrics, dashboard, cảnh báo (GĐ7)

> Bản triển khai chi tiết của **B.6 Khối C** trong [giai-doan-7.md](giai-doan-7.md). Tài liệu gốc trả lời *cái gì*
> và *vì sao* (Đ-7.7 `/metrics` không ra Internet, Đ-7.8 prometheus-net, Đ-7.9 Grafana alerting, Mục 5 chỉ số và
> ngưỡng); tài liệu này trả lời *làm thế nào, kiểm gì, và thực tế thi công lệch kế hoạch ở đâu*.
>
> **Nguồn sự thật vẫn là `giai-doan-7.md`** (Mục 5, Mục 12 checklist, B.6). Tài liệu này **viết dần theo từng đầu
> việc** — mục nào ghi "chưa làm" là chưa có thực tế thi công để ghi.

| | |
|---|---|
| **Người làm** | Một người. C1, C2, C6 chạm code backend; C3–C5 thao tác trên VM (user `deploy`, stack ops ở `~/app/ops/`) |
| **Khối này cần trước** | B1 (stack ops + kênh Telegram); D2 (chặn `ping/boom` từ Internet — C5 bắn nó từ VM) |
| **Khối này chặn** | NFR-OBS-01; điều kiện 3 của GĐ7 (≥ 3 cảnh báo đã kêu thật); GĐ8 (đọc p95 k6 từ Grafana) |

---

## 0. Danh sách công việc và trạng thái

| Mã | Đầu việc | Kết quả mong đợi | Trạng thái |
|---|---|---|---|
| **C1** | `/metrics` RED trên API | `/metrics` 200 không cần token; lỗi 500 được đếm **đúng là 500** | ✅ Code + test xong (2026-09-23), **chưa deploy** |
| **C2** | Bốn chỉ số nghiệp vụ | Đăng một bài trên staging → counter tăng đúng 1 | ⏸️ Chờ nhóm chốt cách làm (Mục 2) |
| **C3** | Prometheus trong stack ops | Trang Targets: mọi target **UP** | ⬜ Chưa làm — cần C1 lên staging trước |
| **C4** | Grafana + dashboard | Biểu đồ có số liệu thật từ staging | ⬜ Chưa làm |
| **C5** | Cảnh báo + thử cho kêu ⭐ | Ảnh ≥ 3 cảnh báo đã kêu thật, kèm giờ | ⬜ Chưa làm |
| **C6** | Redact PII + cổng CI + canh `/metrics` | CI đỏ khi cố tình log email; `/metrics` công khai không lộ | ⬜ Chưa làm |

---

## 1. C1 — `/metrics` RED trên API ✅

### Mục tiêu

API tự đếm mọi request nó xử lý và công bố ở `/metrics` theo định dạng Prometheus: **R**ate (bao nhiêu request),
**E**rrors (bao nhiêu 5xx), **D**uration (histogram thời gian — p50/p95/p99). Đây là nguồn số cho cảnh báo "5xx > 1%"
(C5) và cho **GOAL-01** (feed p95 ≤ 500ms) ở GĐ8.

### Đã làm

| File | Thay đổi |
|---|---|
| `src/backend/SocialApp.Api/SocialApp.Api.csproj` | `prometheus-net.AspNetCore` **8.2.1** — bản ổn định mới nhất, dòng net8 (Đ-7.8). Chỉ kéo theo `prometheus-net` 8.2.1 và `Microsoft.Extensions.ObjectPool` 7.0.0 — không có thư viện net9/10 |
| `src/backend/SocialApp.Api/Program.cs` | `app.UseHttpMetrics()` đặt **ngay trước** `app.UseSharedKernel()`; `app.MapMetrics().AllowAnonymous()` trước `MapControllers()` |
| `tests/SocialApp.IntegrationTests/MetricsEndpointTests.cs` *(mới)* | `Metrics_khong_can_token` · `Loi_500_duoc_dem_dung_ma_500` |

Đây là **toàn bộ** phần chạm code backend của C1 — đúng như Đ-7.2 dự kiến ("một chỗ duy nhất trong `Program.cs`"),
cộng một test.

### Thực tế thi công — hai điều kế hoạch không nói

**1. Vị trí của `UseHttpMetrics()` quyết định cảnh báo có kêu được hay không.** `UseSharedKernel()` chứa
`UseExceptionHandler()`. Nếu bộ đếm đứng **sau** nó (tức ở phía trong pipeline), một exception đi xuyên qua bộ đếm lúc
status code còn là 200 → **mọi lỗi 500 bị đếm thành 200**. Biểu đồ luôn xanh, cảnh báo "5xx > 1%" không bao giờ kêu,
và không ai phát hiện ra vì mọi thứ trông bình thường. Đứng **trước** thì bộ bắt lỗi đổi exception thành 500 xong mới
quay ra, bộ đếm thấy đúng 500.

Test `Loi_500_duoc_dem_dung_ma_500` gọi `/api/v1/ping/boom` rồi đọc `/metrics`, khẳng định dòng đếm của action `Boom`
mang `code="500"`. **Thử cho đỏ:** dời `UseHttpMetrics()` xuống sau `UseSharedKernel()` → test **đỏ**; trả lại →
xanh. Không có test này thì một lần "sắp xếp lại cho gọn" `Program.cs` là tắt cảnh báo mà không ai biết.

**2. `/metrics` phải `AllowAnonymous`** — fallback policy của dự án chặn mọi endpoint không khai gì (default deny,
GĐ1). Prometheus scrape không có token → thiếu dòng này thì target báo DOWN với lỗi 401. Công khai trong mạng docker
**không** có nghĩa là công khai ra Internet: apache chỉ `ProxyPass` `/api`, `/swagger`, `/health` về API, nên
`https://mxh.banhgao.net/metrics` rơi về Next → 404 (Đ-7.7). C6 thêm một monitor canh chuyện này không đổi.

### Bằng chứng

| Kiểm | Kết quả |
|---|---|
| `MetricsEndpointTests` | 2/2 xanh |
| Thử cho đỏ vị trí `UseHttpMetrics` | Đỏ khi đặt sai, xanh khi trả lại |
| Toàn bộ suite | Unit **295** · Architecture **16** · Integration **485** (483 → 485) — 0 fail, 0 skip |
| Swagger / cổng hợp đồng | Không đổi — `/metrics` không có `GroupName` nên không lọt vào trang Swagger nào |

### Còn lại để đóng C1

- [ ] Commit + merge vào `develop` → CD deploy lên staging
- [ ] Trên VM, từ **trong** mạng docker: `docker compose -f docker-compose.staging.apache.yml exec api curl -s localhost:8080/metrics | head`
      → thấy các dòng `http_request_duration_seconds…`
- [ ] Từ Internet: `curl -s -o /dev/null -w '%{http_code}' https://mxh.banhgao.net/metrics` → **404** (không phải 200)

---

## 2. C2 — Bốn chỉ số nghiệp vụ ⏸️

### Phát hiện khi thử (2026-09-23): API chuẩn của .NET không tự xuất ra `/metrics`

Hướng dự định ban đầu: mỗi module ghi chỉ số bằng `System.Diagnostics.Metrics` (API có sẵn của .NET, không phụ thuộc
thư viện Prometheus — giữ ranh giới module sạch), và prometheus-net ở host tự xuất chúng ra `/metrics`.

Đã thử bằng test tạm trong `IntegrationTests` — **cả bốn lần counter không xuất hiện trên `/metrics`**:

| Lần | Cấu hình | Kết quả |
|---|---|---|
| 1 | Mặc định, chờ 1,5 giây | Không có |
| 2 | Mặc định, đọc lại tùy chọn `MeterAdapterOptions` (có `InstrumentFilterPredicate`, `MetricsExpireAfter = 5 phút`) | — |
| 3 | Mặc định, chờ tới 12 giây | Không có |
| 4 | `Metrics.ConfigureMeterAdapter(o => o.InstrumentFilterPredicate = i => i.Meter.Name.StartsWith("SocialApp"))` | Không có |

Chưa tìm ra nguyên nhân và **cố ý không đào tiếp** — không đáng công bằng một cách chắc chắn chạy được. Test tạm đã xóa.
**Đừng làm C2 theo hướng này** mà không có một test đỏ-rồi-xanh chứng minh counter hiện trên `/metrics`.

### Phương án đề xuất — chờ nhóm chốt

Khai báo **cả bốn counter ở một chỗ** trong SharedKernel (ví dụ `SocialApp.SharedKernel/Observability/BusinessMetrics.cs`),
dùng thẳng prometheus-net; mỗi module chỉ thêm **một dòng** `.Inc()` vào service của mình.

| | |
|---|---|
| **Được** | Một file, tên chỉ số cố định (Mục 5.2), test được đúng kiểu C1 |
| **Mất** | SharedKernel thêm một gói phụ thuộc — cùng loại với `AWSSDK.S3` đã duyệt ở GĐ2 (Đ-2.14); chạm code của Identity, Content và worker dọn rác — phải báo người đang làm các module đó |
| **Hoãn được không** | **Được.** RED (C1) đã đủ cho GOAL-01 và cảnh báo tỷ lệ lỗi; C2 không nằm trong ba điều kiện để GĐ7 xong |

---

## 3. C3–C6 — chưa làm

Viết khi bắt đầu từng đầu việc (đúng nếp các khối trước). Những điều đã biết cần mang theo:

- **C3:** Prometheus scrape `api:8080/metrics` — stack ops phải gắn vào mạng `internal` của staging dưới dạng
  *external network*. Chỉ lấy được số **sau khi C1 đã lên staging**. Đặt giới hạn dung lượng lưu trữ (đĩa VM đang 72%).
- **C4:** Grafana publish `127.0.0.1` thôi, xem qua SSH tunnel như Kuma. Cổng `3001` trên VM đã có người dùng,
  `3002` là Kuma — kiểm `ss -ltnp` trước khi chọn.
- **C5:** Trong năm cảnh báo của Mục 5.3, **hai cái đã có** nhờ Kuma: *dịch vụ chết* (B1) và *backup không chạy*
  (monitor Push, A3). Grafana chỉ cần thêm ba: tỷ lệ lỗi, độ trễ, đĩa. Bắn thử tỷ lệ lỗi **từ VM** qua
  `127.0.0.1:18080/api/v1/ping/boom` — đường công khai đã chặn ở D2.
- **C6:** Canh `/metrics` không lộ ra Internet bằng một monitor Kuma loại **Keyword**, URL
  `https://mxh.banhgao.net/metrics`, keyword `http_request_duration_seconds`, bật **Upside Down Mode** — monitor xanh
  khi **không** thấy keyword, đỏ ngay khi `/metrics` lộ ra ngoài. Canh liên tục thay vì một test chạy một lần.
