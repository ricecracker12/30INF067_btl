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
| **C1** | `/metrics` RED trên API | `/metrics` 200 không cần token; lỗi 500 được đếm **đúng là 500** | ✅ **Đóng** — deploy + nghiệm thu trên staging (2026-09-23) |
| **C2** | Bốn chỉ số nghiệp vụ | Đăng một bài trên staging → counter tăng đúng 1 | ✅ **Đóng** — đủ 7 chuỗi sau deploy, `posts_created` tăng đúng 1 (2026-09-23) |
| **C3** | Prometheus trong stack ops | Trang Targets: mọi target **UP** | ✅ **Đóng** — cả ba target UP trên VM (2026-09-23); còn ảnh Targets |
| **C4** | Grafana + dashboard | Biểu đồ có số liệu thật từ staging | 🟡 File cấu hình xong (2026-09-23), **chờ thi công trên VM** |
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

### Nghiệm thu trên staging (2026-09-23) ✅

Chạy trên VM (user `deploy`) sau khi C1–C3 merge vào `develop` và CD deploy:

| Kiểm | Lệnh | Kết quả |
|---|---|---|
| `/metrics` trong VM | `curl … http://127.0.0.1:18080/metrics` | **200** |
| Lỗi 500 đếm đúng | `curl …/api/v1/ping/boom` → 500, rồi grep `Boom` trên `/metrics` | `http_requests_received_total{code="500",method="GET",controller="Ping",action="Boom",endpoint=""} 1` |
| Không lộ ra Internet | `curl … https://mxh.banhgao.net/metrics` | **404**; đếm `http_request_duration_seconds` trong thân trả về = **0** |

*Ghi chú cho C4:* nhãn `endpoint` rỗng ở request lỗi 500 — `UseExceptionHandler` chạy lại pipeline với đường xử lý lỗi
và xóa endpoint gốc. Dashboard và cảnh báo **nhóm theo `controller`/`action`**, không theo `endpoint`, kẻo mọi lỗi 500
dồn vào một dòng không tên.

---

## 2. C2 — Bốn chỉ số nghiệp vụ ✅

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

### Cách đã làm: bốn counter ở một chỗ trong SharedKernel

[`SocialApp.SharedKernel/Observability/BusinessMetrics.cs`](../../src/backend/SocialApp.SharedKernel/Observability/BusinessMetrics.cs)
khai báo cả bốn counter bằng prometheus-net và chỉ lộ ra **phương thức tĩnh** — code nghiệp vụ không chạm kiểu
`Counter` của Prometheus. SharedKernel thêm gói `prometheus-net` **8.2.1**, cùng version với gói của Api (lệch là hai bản
prometheus-net trong một process) — dependency thứ tư của SharedKernel, cùng loại với `AWSSDK.S3` (Đ-2.14).

| Chỉ số | Nhãn | Gọi ở đâu | Một dòng |
|---|---|---|---|
| `socialapp_login_failed_total` | — | `LoginService` — **cả hai** nhánh 401: email không tồn tại và sai mật khẩu | `BusinessMetrics.LoginFailed()` |
| `socialapp_posts_created_total` | — | `PostService.CreateAsync` — **sau** `AddWithMediaAsync` thành công | `BusinessMetrics.PostCreated()` |
| `socialapp_presign_issued_total` | `purpose` = `post` \| `avatar` | `UploadTicketService.Create` — cộng **số URL** của lô, không phải 1 | `BusinessMetrics.PresignIssued(…, tickets.Count)` |
| `socialapp_media_cleanup_runs_total` | `result` = `ran` \| `lock` \| `failed` | `MediaCleanupWorker` — cuối `RunOnceAsync`, nhánh không lấy được khóa, và `catch` của vòng lặp | `BusinessMetrics.MediaCleanupRun(…)` |

### Thực tế thi công — ba chỗ lệch kế hoạch

**1. Chỉ số thứ tư đổi từ "số object mồ côi đã dọn" sang "số lượt worker chạy".** Mục đích ghi trong Mục 5.2 là *chứng
minh worker thật sự chạy*. Staging gần như không có ảnh mồ côi, nên số object đã dọn nằm ở 0 nhiều ngày liền — và số 0
không phân biệt được "chạy mà không có gì để dọn" với "worker chết". Đếm lượt chạy thì tăng đều mỗi 60 phút khi worker
sống. Số object đã dọn vẫn nằm trong log mỗi lượt như trước.

**2. `disabled` cố ý không đếm.** Worker tắt thì `ExecuteAsync` thoát trước vòng lặp, nên nhánh `disabled` của
`RunOnceAsync` chỉ tới được khi test gọi thẳng — đếm ở đó là số liệu giả. Hệ quả cần nhớ: **worker tắt trên staging thì
chỉ số này không có dòng nào**, không phải dòng bằng 0. Muốn biết worker bật hay tắt thì xem log lúc khởi động
("Worker dọn rác media đang TẮT") hoặc biến `Media__Cleanup__Enabled` trong `.env`.

**3. Đếm login thất bại ở cả hai nhánh 401.** Kế hoạch chỉ ghi "đăng nhập thất bại". Chỉ đếm nhánh sai mật khẩu thì kẻ dò
bằng danh sách email ngẫu nhiên — phần lớn không tồn tại — không hiện lên biểu đồ. Nhánh 423 (đang khóa) và 403 (chưa
xác minh) không đếm: đó không phải sai thông tin.

### Test — counter tĩnh dùng chung cả process

Bốn test mới, mỗi test nằm trong đúng lớp đã dựng sẵn dữ liệu cho luồng đó, và khẳng định **tăng đúng số** chứ không
"tăng ít nhất":

| Test | Khẳng định |
|---|---|
| `LoginTests.C2_login_failed_dem_dung_ca_hai_nhanh_401_khong_dem_dang_nhap_dung` | +1 sai mật khẩu, +1 email không tồn tại, +0 đăng nhập đúng |
| `CreatePostTests.C2_posts_created_tang_dung_1_khi_luu_khong_tang_khi_bi_tu_choi` | +0 khi 403 (chưa hồ sơ), +1 khi lưu thành công |
| `MediaUploadsTests.C2_presign_issued_dem_dung_so_url_theo_muc_dich` | Lô 3 file → `post` +3; một avatar → `avatar` +1 |
| `MediaCleanupWorkerTests.C2_media_cleanup_runs_dem_ran_va_lock_khong_dem_disabled` | `ran` +1; không Redis → `lock` +1; worker tắt → không đổi |

"Tăng đúng số" chỉ đáng tin khi không test nào khác chạm cùng counter **song song**. Đã kiểm: mọi lớp test đi qua bốn
luồng này đều ở `PostgresCollection` — xUnit chạy tuần tự trong một collection. Các lớp chạy song song còn lại (CORS,
ProblemDetails, ForwardedClientIp) dùng `ApiFactory` không có DB nên không tới được chỗ tăng counter. Luật này ghi ngay
trong [`MetricsReader`](../../tests/SocialApp.IntegrationTests/Harness/MetricsReader.cs): **test mới chạm các luồng này
phải vào `PostgresCollection`**, đặt ngoài là số đếm lệch và test đỏ chập chờn.

### Bằng chứng

| Kiểm | Kết quả |
|---|---|
| 4 test C2 | 4/4 xanh |
| Thử cho đỏ | Tắt cả 7 lời gọi `BusinessMetrics` → **4/4 đỏ**, mỗi test vì đúng chỗ của nó; trả lại → 4/4 xanh |
| Toàn bộ suite, **chạy hai lần liên tiếp** | Cả hai lần: Unit 295 · Architecture 16 · Integration **489** (485 → 489) — 0 fail, không đỏ chập chờn |
| Luật kiến trúc | Không đổi — không có luật cấm thư viện ngoài; module chỉ phụ thuộc SharedKernel như trước |

### Lần deploy đầu (2026-09-23): `/metrics` không có dòng `socialapp_` nào — đã sửa

Deploy xong, `curl -s http://127.0.0.1:18080/metrics | grep '^socialapp_'` ra **rỗng**. Code không sai chỗ đếm; lỗi ở
chỗ **tạo** counter:

- Bốn counter là field `static readonly` của `BusinessMetrics`. Field static chỉ khởi tạo khi có ai **chạm vào lớp** —
  tức lúc sự kiện đầu tiên xảy ra. Trước đó counter chưa đăng ký với prometheus-net, `/metrics` không có gì.
- Counter có nhãn còn thêm một tầng: kể cả đã đăng ký, chuỗi `{purpose="post"}` chỉ có khi giá trị nhãn đó xuất hiện
  lần đầu.

Test C2 không bắt được chuyện này vì `MetricsReader` đọc "không có dòng" thành 0 — đúng cho phép đo *tăng bao nhiêu*,
nhưng che mất việc chuỗi không tồn tại.

Hậu quả không chỉ là "chưa thấy": mỗi lần deploy, mọi chuỗi biến mất cho tới sự kiện đầu tiên; `increase()` của
Prometheus **mất luôn lần tăng đầu tiên** (không có mẫu 0 để so); và cảnh báo kiểu "đứng yên ở 0" không kêu được trên
một chuỗi không tồn tại.

**Sửa:** `BusinessMetrics.Initialize()` — host gọi một lần lúc khởi động (`Program.cs`, ngay dưới `MapMetrics`), tạo sẵn
cả **bảy** chuỗi với giá trị 0: 2 không nhãn + `purpose` × {post, avatar} + `result` × {ran, lock, failed}.

| Kiểm | Kết quả |
|---|---|
| Test mới `MetricsEndpointTests.Chi_so_nghiep_vu_co_mat_tu_luc_khoi_dong` | Xanh |
| Thử cho đỏ: tắt dòng `Initialize()` | **Đỏ, thiếu cả 7 chuỗi** — đúng triệu chứng trên staging; trả lại → xanh |
| Toàn bộ suite | Unit 303 · Architecture 16 · Integration **491** — 0 fail |

### Còn lại để đóng C2 (sau khi deploy bản sửa)

- [x] Trên VM: `curl -s http://127.0.0.1:18080/metrics | grep '^socialapp_'` — **đủ 7 dòng** (2026-09-23)
- [x] Đăng một bài trên staging qua UI → `socialapp_posts_created_total` tăng đúng 1 (Prometheus đọc được `1`, 2026-09-23)
- [ ] Sau ≥ 60 phút (nếu `Media__Cleanup__Enabled=true`): `socialapp_media_cleanup_runs_total{result="ran"}` > 0

---

## 3. C3 — Prometheus trong stack ops ✅

> Thi công trên VM và nghiệm thu 2026-09-23 (xem cuối mục). Làm được cả khi C1 chưa lên
> staging — xem "Đọc kết quả" ở bước 5: target API báo 404 chính là bằng chứng mạng đã thông.

### Mục tiêu

Prometheus chạy trong stack ops, 15 giây một lần gọi `api:8080/metrics` **qua mạng docker** (không qua domain, không
qua `127.0.0.1:18080` — Đ-7.7 lớp 3) và gọi node-exporter để lấy đĩa/RAM/CPU của host. Dữ liệu có trần để không ăn
đĩa VM (đang 72%).

### Đã chuẩn bị

| File | Nội dung |
|---|---|
| [`ops/prometheus.yml`](../../ops/prometheus.yml) | 3 job: `socialapp-api` (`api:8080`), `node` (`node-exporter:9100`), `prometheus` (chính nó). Không có rule — cảnh báo làm ở Grafana (Đ-7.9) |
| [`ops/docker-compose.ops.yml`](../../ops/docker-compose.ops.yml) | Thêm `prometheus` (`v3.5.0`, LTS) và `node-exporter` (`v1.9.1`); mạng external `socialapp-staging_internal`; volume `prometheus-data` |

Quyết định trong file, kèm lý do:

| Quyết định | Vì sao |
|---|---|
| Gắn prometheus vào mạng staging bằng `external: true` | Hai project Compose không thấy nhau. `external` nghĩa là stack ops **dùng** mạng đó nhưng không tạo, không xóa — `down` stack ops không đụng tới staging |
| Chỉ **prometheus** gắn vào mạng staging, Kuma và node-exporter thì không | Mạng staging nằm trong `ReverseProxy__TrustedNetworks`: container nào gắn vào là "proxy tin được" với API. Gắn ít nhất có thể |
| Retention `30d` **và** `2GB` | Chạm trần nào trước thì cắt theo trần đó. 30 ngày đủ phủ GĐ8; 2GB chặn trường hợp số series phình |
| Prometheus publish `127.0.0.1:9090`, node-exporter không publish | Xem trang Targets qua SSH tunnel như Kuma; node-exporter chỉ prometheus cần gọi |
| `api:8080` tĩnh, chưa dùng DNS discovery | Một bản sao thì tĩnh là đủ và nhãn `instance` không đổi qua mỗi lần deploy. **Khối E** (api ×2) phải đổi sang `dns_sd_configs` — ghi sẵn trong `prometheus.yml` |

Kiểm cục bộ: `docker compose config -q` hợp lệ; `promtool check config` → `SUCCESS`; cả hai image có bản **arm64**
(`docker manifest inspect`, luật vàng 8).

### Thi công trên VM

**Bước 1 — Kiểm cổng và tên mạng.**

```bash
ss -ltnp | grep -E ':9090\b' || echo "9090 trống"
docker network ls --format '{{.Name}}' | grep socialapp-staging
```

- Phải thấy `9090 trống`. Nếu 9090 đã có người dùng (Oracle Linux hay để cockpit ở đây), chỉ đổi **vế trái** trong
  compose, ví dụ `127.0.0.1:9091:9090` — bài học cổng Kuma ở khối B.
- Phải thấy đúng `socialapp-staging_internal`. Khác tên thì sửa `networks.staging.name` trong compose cho khớp.

**Bước 2 — Chép hai file lên `~/app/ops/`** (từ máy có repo, PowerShell):

```powershell
scp ops/docker-compose.ops.yml ops/prometheus.yml deploy@<VM>:~/app/ops/
```

**Bước 3 — Kiểm cú pháp trên VM, rồi up.**

```bash
cd ~/app/ops
docker run --rm -v ~/app/ops/prometheus.yml:/p.yml:ro --entrypoint promtool prom/prometheus:v3.5.0 check config /p.yml
docker compose -f docker-compose.ops.yml up -d
docker compose -f docker-compose.ops.yml ps
```

- `promtool` phải in `SUCCESS`.
- `ps`: ba container `Up`. **uptime-kuma không bị tạo lại**: cột `STATUS` vẫn là `Up <nhiều giờ>`, vì cấu hình của nó
  không đổi. Nếu Kuma bị tạo lại thì cũng không mất dữ liệu (nằm trong volume), chỉ mất vài giây theo dõi.

**Bước 4 — Đọc trạng thái target từ VM** (không cần trình duyệt; mỗi dòng một target: tên · lỗi gần nhất · up/down):

```bash
curl -s http://127.0.0.1:9090/api/v1/targets \
  | grep -oE '"(scrapePool|lastError|health)":"([^\"]|\\\")*"' | paste - - -
```

**Bước 5 — Đọc kết quả.**

| Target | Trước khi C1 lên staging | Sau khi C1 lên staging |
|---|---|---|
| `node` | `up` | `up` |
| `prometheus` | `up` | `up` |
| `socialapp-api` | `down`, lastError `server returned HTTP status 404 Not Found` | `up` |

Target API báo **404** trước deploy là tín hiệu **tốt**: Prometheus đã phân giải được tên `api` và đã nói chuyện được
với API qua mạng docker, chỉ là API chưa có `/metrics`. Các lỗi khác chỉ ra chỗ hỏng:

| `lastError` | Nghĩa | Sửa |
|---|---|---|
| `lookup api ... no such host` | Prometheus không nằm trong mạng staging | Kiểm tên mạng (bước 1); `docker inspect socialapp-ops-prometheus-1 --format '{{json .NetworkSettings.Networks}}'` phải có `socialapp-staging_internal` |
| `connection refused` | Đúng host nhưng sai cổng | Target phải là `api:8080` (cổng **trong** container), không phải 18080 |
| `context deadline exceeded` | API treo hoặc quá tải | `docker compose -f docker-compose.staging.apache.yml ps` bên staging |
| `network socialapp-staging_internal not found` (lúc `up`) | Stack staging chưa chạy | Up stack staging trước |

**Bước 6 — Xem bằng mắt và chụp ảnh** (máy cá nhân):

```powershell
ssh -L 9090:127.0.0.1:9090 deploy@<VM>
```

Mở `http://localhost:9090/targets`. Sau khi C1 đã lên staging và cả ba target **UP**: chụp ảnh, lưu vào
`docs/giai-doan-7/bang-chung/` (ảnh nghiệm thu C3).

**Bước 7 — Sau khi C1 + C2 lên staging, thử một truy vấn thật.** Ở tab *Query*:

- `up` → ba dòng, giá trị 1.
- `socialapp_posts_created_total` → có số. Đăng một bài trên staging, chờ 15–30 giây, giá trị tăng 1 (C2 đóng luôn ở đây).
- `prometheus_tsdb_storage_blocks_bytes` → dung lượng TSDB. Ghi lại con số sau một ngày để kiểm ước lượng 30–60 MB/ngày.

### Nghiệm thu trên VM (2026-09-23) ✅

| Kiểm | Kết quả |
|---|---|
| `promtool check config` | `SUCCESS` |
| `up -d` | Tạo `prometheus` + `node-exporter` + volume `prometheus-data`; **uptime-kuma không bị tạo lại** (`Up 3 days`) |
| Trạng thái target (bước 4) | `node` · `prometheus` · `socialapp-api` — cả ba `up`, `lastError` rỗng |
| Truy vấn `socialapp_posts_created_total` | `{env="staging", instance="api:8080", job="socialapp-api"} = 1` — Prometheus thu được chỉ số C2 qua mạng docker |

*Vấp khi thi công:* chạy `promtool` lúc `prometheus.yml` **chưa có** trên VM (đã chép nhầm vào `~/app/deploy/`) →
Docker tự tạo **thư mục rỗng** trùng tên, chủ `root`, và `promtool` báo `'p.yml' is a directory`. Sửa:
`sudo rmdir ~deploy/app/ops/prometheus.yml`, chuyển file thật sang `~/app/ops/`. Bài học: bind mount một file chưa tồn
tại **không báo lỗi**, nó tạo thư mục — kiểm `ls -l` (dòng bắt đầu bằng `-`) trước khi `promtool` hay `up`.

### Còn lại

- [ ] Chụp ảnh trang Targets (`ssh -L 9090:127.0.0.1:9090 …` → `http://localhost:9090/targets`) vào `bang-chung/`
- [ ] Sau một ngày: ghi dung lượng TSDB thực tế (`prometheus_tsdb_storage_blocks_bytes`) vào mục này

---

## 4. C4 — Grafana + dashboard 🟡

> File cấu hình xong và đã kiểm cục bộ (2026-09-23). **Chờ thi công trên VM.**

### Mục tiêu

Một dashboard đọc số **thật** từ Prometheus (C3): RED của API, bốn chỉ số nghiệp vụ (C2), đĩa/RAM/CPU của VM. Xem qua
SSH tunnel, không ra Internet (Đ-7.7, R7-06). Grafana cũng là chỗ đặt ba cảnh báo của C5.

### Đã chuẩn bị

| File | Nội dung |
|---|---|
| [`ops/docker-compose.ops.yml`](../../ops/docker-compose.ops.yml) | Thêm `grafana` (`grafana-oss:12.1.1`, có arm64), publish `127.0.0.1:3003`, volume `grafana-data`, **chỉ mạng default** |
| [`ops/.env.example`](../../ops/.env.example) *(mới)* | `GRAFANA_ADMIN_PASSWORD=` — chép thành `~/app/ops/.env` |
| [`ops/grafana/provisioning/datasources/prometheus.yml`](../../ops/grafana/provisioning/datasources/prometheus.yml) | Datasource `http://prometheus:9090`, **uid cố định `prometheus`** |
| [`ops/grafana/provisioning/dashboards/provider.yml`](../../ops/grafana/provisioning/dashboards/provider.yml) | Nạp mọi JSON trong `grafana/dashboards/` vào thư mục *SocialApp* |
| [`ops/grafana/dashboards/socialapp-overview.json`](../../ops/grafana/dashboards/socialapp-overview.json) | Dashboard *SocialApp — Tổng quan staging*, 17 panel trong 4 hàng |
| `ops/grafana/provisioning/{plugins,alerting}/.gitkeep` | Thư mục rỗng — thiếu thì Grafana ghi log lỗi mỗi lần khởi động. `alerting/` là chỗ C5 có thể dùng |

Dashboard:

| Hàng | Panel |
|---|---|
| **Tổng quan** | API UP/DOWN · Tỷ lệ 5xx (5 phút, vàng 0,5% · đỏ 1%) · p95 toàn API (vàng 300ms · đỏ 500ms) · Đĩa % (đỏ 85%) · RAM % |
| **RED** | Request/giây theo mã · % 5xx theo thời gian (vạch ngưỡng 1%) · p50/p95/p99 · p95 theo action (top 10) · 5xx theo action |
| **Nghiệp vụ** | Đăng nhập sai / giờ · Bài mới / giờ · URL ký theo `purpose` / giờ · Lượt worker dọn theo `result` / 3 giờ |
| **Host** | CPU % · RAM % · Đĩa % theo thời gian |

Quyết định, kèm lý do:

| Quyết định | Vì sao |
|---|---|
| Mọi biểu thức RED lọc `controller!=""` | Bỏ request của Prometheus vào `/metrics` (15 giây một lần) và `/health/*` — không lọc thì "request/giây" chủ yếu là chính Prometheus, và tỷ lệ lỗi bị pha loãng |
| Nhóm theo `controller`/`action`, **không** theo `endpoint` | Nghiệm thu C1: lỗi 500 có `endpoint=""` (bộ xử lý lỗi xóa endpoint gốc) — nhóm theo endpoint là mọi 500 dồn vào một dòng không tên |
| Tỷ lệ lỗi chia cho `clamp_min(…, 1e-9)` | Không có traffic thì 0/0 = NaN, ô hiện "No data" thay vì 0% |
| Dashboard là **file trong repo**, `allowUiUpdates: false` | Mất volume không mất dashboard; mọi thay đổi có lịch sử git. Sửa trên giao diện → *Export → JSON* → chép vào repo |
| uid datasource cố định | Dashboard JSON và cảnh báo C5 trỏ vào uid; để Grafana tự sinh là "datasource not found" sau mỗi lần dựng lại |
| Mật khẩu admin qua `${GRAFANA_ADMIN_PASSWORD:?…}` | Thiếu biến thì `up` **dừng** — không bao giờ lên với `admin/admin` |
| Cổng `3003` | 3000 frontend, 3001 đã có người dùng, 3002 Kuma |
| Grafana không gắn mạng staging | Chỉ cần gọi `prometheus:9090`. Mạng staging là mạng "tin được" của API — càng ít container gắn vào càng tốt |

*Lệch kế hoạch:* Mục 11 ghi "thêm khóa Grafana vào `deploy/.env.example`". Không làm vậy — `.env` đó là của stack
staging; stack ops đọc `.env` **của thư mục mình** (`~/app/ops/`), nên khóa nằm ở file mẫu riêng `ops/.env.example`.

Kiểm cục bộ: `compose config` hợp lệ, và **thiếu** mật khẩu thì dừng đúng thông báo; chạy Grafana + Prometheus trong
một mạng tạm → datasource `OK`, dashboard nạp đúng thư mục *SocialApp*, `provisioned: true`; **19/19** biểu thức PromQL
qua được Prometheus không lỗi cú pháp. Chưa có số thật (máy dev không có API) — đó là việc của bước trên VM.

### Thi công trên VM

**Bước 1 — Kiểm cổng.**

```bash
ss -ltnp | grep -E ':3003\b' || echo "3003 trống"
```

Bận thì chỉ đổi **vế trái** trong compose (`127.0.0.1:3004:3000`) và sửa `GF_SERVER_ROOT_URL` cho khớp.

**Bước 2 — Chép file** (máy có repo, PowerShell, trong thư mục `mxh`). Thư mục `ops/` **trong repo** trùng với
`~/app/ops/` **trên VM** — chép sang y nguyên (không đụng `.env` thật trên VM):

```powershell
scp -r ops/docker-compose.ops.yml ops/prometheus.yml ops/.env.example ops/grafana deploy@<VM>:~/app/ops/
```

**Bước 3 — Tạo `.env` có mật khẩu** (trên VM, user `deploy`):

```bash
cd ~/app/ops
cp .env.example .env && chmod 600 .env
sed -i "s|^GRAFANA_ADMIN_PASSWORD=.*|GRAFANA_ADMIN_PASSWORD=$(openssl rand -hex 24)|" .env
```

Lưu mật khẩu vào trình quản lý mật khẩu của nhóm (xem bằng `grep GRAFANA .env`, **không** dán vào chat hay commit).
Biến này chỉ có tác dụng ở **lần khởi động đầu** (lúc Grafana tạo DB trong volume); đổi sau đó thì dùng
`docker compose -f docker-compose.ops.yml exec grafana grafana cli admin reset-admin-password <mới>`.

**Bước 4 — Kiểm là file/thư mục thật** (bài học C3: bind mount thứ chưa có là Docker tự tạo thư mục rỗng):

```bash
ls -la .env grafana/provisioning/datasources/ grafana/dashboards/
```

Phải thấy `.env` là file (`-rw-------`), `prometheus.yml` trong `datasources/`, `socialapp-overview.json` trong `dashboards/`.

**Bước 5 — Up.**

```bash
docker compose -f docker-compose.ops.yml up -d
docker compose -f docker-compose.ops.yml ps
```

`grafana` `Up`; `uptime-kuma`, `prometheus`, `node-exporter` **không bị tạo lại** (cấu hình không đổi).

**Bước 6 — Kiểm từ VM** (đọc mật khẩu từ `.env` vào biến, không gõ ra lịch sử shell):

```bash
set -a; . ./.env; set +a
curl -s -u "admin:$GRAFANA_ADMIN_PASSWORD" http://127.0.0.1:3003/api/datasources/uid/prometheus/health; echo
curl -s -u "admin:$GRAFANA_ADMIN_PASSWORD" http://127.0.0.1:3003/api/dashboards/uid/socialapp-overview | grep -o '"title":"SocialApp[^"]*"'
docker compose -f docker-compose.ops.yml logs grafana | grep 'level=error' | grep -v 'plugin table'
```

Phải thấy: `"status":"OK"`; `"title":"SocialApp — Tổng quan staging"`; lệnh thứ ba **không in gì**.

**Bước 7 — Xem bằng mắt với số thật** (máy cá nhân):

```powershell
ssh -L 3003:127.0.0.1:3003 deploy@<VM>
```

Mở `http://localhost:3003`, đăng nhập `admin`, *Dashboards → SocialApp → SocialApp — Tổng quan staging*. Tạo chút
traffic để các panel có hình: lướt feed vài lần, đăng một bài, đăng nhập sai một lần; từ VM bắn
`curl -s -o /dev/null http://127.0.0.1:18080/api/v1/ping/boom` hai–ba lần để panel 5xx có một vạch. Chờ 1–2 phút.

Chụp ảnh dashboard (khung *Last 1 hour*) vào `docs/giai-doan-7/bang-chung/` — ảnh nghiệm thu C4 và NFR-OBS-01.

### Còn lại để đóng C4

- [ ] Bước 1–6 trên VM: datasource `OK`, dashboard nạp, log không lỗi
- [ ] Bước 7: các hàng RED, Nghiệp vụ, Host có số **thật**; chụp ảnh

---

## 5. C5–C6 — chưa làm

Viết khi bắt đầu từng đầu việc (đúng nếp các khối trước). Những điều đã biết cần mang theo:

- **C5:** Trong năm cảnh báo của Mục 5.3, **hai cái đã có** nhờ Kuma: *dịch vụ chết* (B1) và *backup không chạy*
  (monitor Push, A3). Grafana chỉ cần thêm ba: tỷ lệ lỗi, độ trễ, đĩa. Bắn thử tỷ lệ lỗi **từ VM** qua
  `127.0.0.1:18080/api/v1/ping/boom` — đường công khai đã chặn ở D2.
- **C6:** Canh `/metrics` không lộ ra Internet bằng một monitor Kuma loại **Keyword**, URL
  `https://mxh.banhgao.net/metrics`, keyword `http_request_duration_seconds`, bật **Upside Down Mode** — monitor xanh
  khi **không** thấy keyword, đỏ ngay khi `/metrics` lộ ra ngoài. Canh liên tục thay vì một test chạy một lần.
