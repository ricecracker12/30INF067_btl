# GĐ 7 — Vận hành: sao lưu, giám sát, chịu lỗi (GOAL-04, NFR-OBS-01, NFR-REL-02) · Ngày 21–23

> Nguồn: [`ke-hoach-trien-khai.md`](../ke-hoach-trien-khai.md) mục "GĐ 7 — Lên PRODUCTION + Observability + Backup/DR",
> và báo cáo PTTK v5.0 (Mục 6.3 triển khai, 6.8 vận hành, 6.9 sao lưu).
> Nền móng: **GĐ0B** — VPS OCI, Docker, Cloudflare, TLS, CD đã chạy thật từ 2026-09-04 (`oci-setup.md`).
> GĐ7 **không dựng lại** hạ tầng; nó biến hạ tầng đang chạy thành hạ tầng **vận hành được**.
>
> **Sửa phạm vi 2026-09-23 (nhóm chốt): không dựng môi trường production riêng — staging `mxh.banhgao.net` là môi
> trường cuối**, nơi demo và nơi được chấm. Xem Đ-7.4 (quyết định + ba cái giá chấp nhận) và Đ-7.13 (Swagger). Khối
> D còn 3 việc, không còn D4. Kế hoạch gốc còn chữ "production" ở đâu thì đọc theo hai quyết định này.
>
> **Ba mốc không lùi được của giai đoạn này:**
> 1. **Biên bản restore drill** — khôi phục thật một lần, có văn bản. Không có biên bản thì NFR-REL-02 chưa đạt,
>    dù `pg_basebackup` có chạy đẹp tới đâu.
> 2. **Đồng hồ uptime phải bật SỚM NHẤT** — GOAL-04 đo bằng thời gian tích lũy, không đo bằng công sức. Bật muộn
>    ba ngày là mất ba ngày bằng chứng, và không có cách nào lấy lại.
> 3. **Từng thấy cảnh báo đỏ ít nhất một lần** — alert chưa bao giờ kêu thì không ai biết nó có kêu được không.

## Tài liệu này có hai phần

| Phần | Trả lời câu hỏi | Đọc khi |
|---|---|---|
| **A — Thiết kế và quyết định** | *Cái gì* và *vì sao* | Trước khi gõ dòng đầu tiên; lúc review PR |
| **B — Kế hoạch triển khai** | *Ai làm gì, theo thứ tự nào* | Lúc chia việc; lúc kiểm tiến độ |

Hướng dẫn thi công từng bước (lệnh nào, file nào, cạm bẫy nào) nằm ở các file `huong-dan-khoi-*.md` cùng thư mục,
**viết khi khối đó bắt đầu** — đúng nếp GĐ1/GĐ2, không viết trước cả sáu khối rồi để lệch với thực tế thi công.

---

# Phần A — Thiết kế và quyết định

## 0. Thuật ngữ

| Từ | Nghĩa trong tài liệu này |
|---|---|
| **RPO** | *Recovery Point Objective* — mất tối đa bao nhiêu **dữ liệu** khi sự cố. Cam kết: ≤ 15 phút |
| **RTO** | *Recovery Time Objective* — mất tối đa bao nhiêu **thời gian** để chạy lại. Cam kết: ≤ 2 giờ |
| **WAL** | *Write-Ahead Log* của PostgreSQL. Lưu lại WAL cho phép khôi phục tới **một thời điểm bất kỳ** (PITR) |
| **base backup** | Bản sao vật lý toàn bộ thư mục dữ liệu, làm mốc để WAL tua tiếp lên |
| **restore drill** | Diễn tập khôi phục: dựng DB trống, nạp backup, đối chiếu dữ liệu, **ghi biên bản** |
| **scrape** | Prometheus tự gọi `/metrics` của API theo chu kỳ để lấy số liệu |
| **RED metrics** | *Rate · Errors · Duration* — ba chỉ số tối thiểu mô tả sức khỏe một dịch vụ HTTP |
| **edge** | Container cân tải nằm **trong** mạng docker, đứng trước các bản sao `api` (xem Đ-7.5) |
| **stack ops** | Cụm giám sát (Prometheus + Grafana + Uptime Kuma), chạy tách khỏi stack ứng dụng |
| **expand–contract** | Migration chia hai bước để bản cũ và bản mới của app cùng chạy được trên một schema |

## 1. Mục tiêu giai đoạn

### Phát biểu một câu

> **Người ngoài đội mở một trang là biết hệ thống đang sống hay chết và đã sống được bao nhiêu phần trăm thời gian;
> đội có văn bản chứng minh đã khôi phục được dữ liệu từ backup; và tắt một container thì người dùng không nhận ra.**

### Mục tiêu chính thức và khối nào gánh

| Mã | Mục tiêu | Đạt bằng | Kiểm bằng |
|---|---|---|---|
| **NFR-REL-02** | Sao lưu + khôi phục được, RPO ≤ 15 phút | A1–A5 | **Biên bản restore drill** (Mục 7) |
| **GOAL-04** | Uptime ≥ 99%/tháng | B1–B3 | Ảnh lịch sử Uptime Kuma + monitor ngoài |
| **NFR-OBS-01** | Quan sát được: metrics + dashboard + cảnh báo | C1–C6 | Grafana có số liệu thật; alert đã từng đỏ |
| **Nợ GĐ0B** | HSTS trên mọi đường; Swagger mở có chủ đích, đã rà | D1–D2 | Đúng một header HSTS ở cả trang lẫn API; Swagger chỉ liệt kê endpoint trong hợp đồng |
| **NFR-SEC-03** | Không còn khóa đã lộ đang sống | D3 | Danh sách khóa đã xoay, có ngày |
| **NFR-REL-01** | Một instance chết thì dịch vụ vẫn phục vụ | E1–E4 *(cắt được)* | `docker kill` một container, site vẫn 200 |
| **NFR-USE** | UI bản cuối: responsive + a11y cơ bản | F1 | Kiểm tay trên 3 kích thước màn hình |

### Vì sao GĐ7 khác mọi giai đoạn khác

Ba điểm, và cả ba đều làm đổi cách tổ chức công việc:

1. **Không có endpoint mới, không có màn hình mới.** Mọi giai đoạn trước, "xong" tự hiện ra: người dùng bấm được
   nút mới. GĐ7 thì "xong" **phải được định nghĩa trước bằng bằng chứng**, nếu không nó trượt thành cảm giác
   "chắc ổn rồi". Đây là lý do Mục 12 (checklist nghiệm thu) của giai đoạn này liệt kê **artifact**, không liệt kê
   tính năng.
2. **Đây là giai đoạn duy nhất sinh ra tài liệu nộp kèm báo cáo A&D.** Biên bản restore, ảnh Grafana, ảnh lịch sử
   uptime — ba thứ này đi thẳng vào phần chứng minh NFR của báo cáo. Code của GĐ7 gần như không được chấm; **artifact
   của GĐ7 thì được chấm**.
3. **Hai đầu việc phụ thuộc THỜI GIAN, không phụ thuộc công sức.** Uptime cần ngày để tích lũy; alert cần một lần
   sự cố thật (hoặc giả) để chứng minh. Cả hai không thể "làm bù vào ngày cuối" như viết thêm một endpoint.

### Vì sao GĐ7 nằm ngoài nhịp lát cắt dọc

Từ GĐ1, mọi giai đoạn chạy nhịp **cổng mở hợp đồng → hai lane → cổng đóng** (Mục 0C kế hoạch gốc). GĐ7 **không có
hợp đồng API nào để mở**: nó không thêm endpoint, nên không có OpenAPI stub, nên không có cổng mở. Hệ quả thực tế
rất có lợi: **khối A–E không phụ thuộc GĐ2–GĐ6**, chúng chỉ phụ thuộc hạ tầng GĐ0B đã có. Xem Đ-7.2.

---

## 2. Phạm vi

### Trong phạm vi

| Nhóm | Nội dung |
|---|---|
| **Sao lưu** | WAL archiving + base backup định kỳ + `pg_dump` logic hằng ngày; đẩy bản sao **rời khỏi VM** |
| **Khôi phục** | Kịch bản khôi phục có thể lặp lại; **một lần diễn tập thật** kèm biên bản |
| **Uptime** | Uptime Kuma trên VM + **một monitor ngoài** làm trọng tài (Đ-7.3) |
| **Metrics** | `/metrics` trên API (RED + 4 chỉ số nghiệp vụ); Prometheus scrape; Grafana dashboard |
| **Cảnh báo** | Grafana alerting: error rate, độ trễ, dịch vụ chết, đĩa đầy |
| **Log** | Serilog JSON đã có — GĐ7 thêm **redact PII** và cổng CI chặn log rò |
| **Nợ bảo mật GĐ0B** | HSTS cho đường API; Swagger mở có chủ đích + chặn endpoint demo lỗi ở apache; xoay các khóa đã lộ |
| **Môi trường cuối** | **Staging** (`mxh.banhgao.net`) — không dựng production riêng (Đ-7.4, sửa 2026-09-23) |
| **Chịu lỗi** | Cân tải 2 bản sao `api`; rolling update; rollback theo tag *(cắt được — Đ-7.1)* |
| **Lane frontend** | Build production; responsive + a11y cơ bản *(chỉ làm được ở đúng nhịp GĐ7)* |

### Ngoài phạm vi — hoãn có địa chỉ

| Việc | Hoãn tới | Lý do |
|---|---|---|
| Loki (log tập trung), Tempo (tracing) | **Roadmap** | Báo cáo v5.0 đã xếp vào Roadmap; Serilog JSON + `docker logs` đủ cho quy mô đồ án |
| Chaos engineering có kịch bản | **Roadmap** | GĐ7 chỉ làm một phép thử `docker kill` thủ công |
| Autoscaling, orchestrator (k8s/Swarm) | **Ngoài MVP** | Một VM 2 OCPU; thêm orchestrator là thêm thứ để hỏng |
| Blue–green / canary deploy | **Ngoài MVP** | Rolling + rollback theo tag đã đủ cho ràng buộc uptime 99% |
| Alertmanager riêng | **Không làm** | Grafana alerting làm được việc tương đương, ít hơn một container (Đ-7.9) |
| Replica Postgres (HA tầng dữ liệu) | **Roadmap** | RTO ≤ 2 giờ không đòi hỏi; và một VM thì replica cùng máy vô nghĩa |
| Sao lưu Redis | **Không làm** | Redis chỉ giữ cache + phiên + vé; mất là dựng lại được, không phải nguồn sự thật |
| Dashboard cho từng module | **GĐ8** | GĐ7 làm một dashboard tổng; GĐ8 thêm bảng feed khi có số liệu k6 |
| Đo p95 feed dưới tải | **GĐ8** | GĐ7 dựng *chỗ để nhìn*; GĐ8 mới *tạo ra tải* bằng k6 |
| Môi trường production riêng (stack thứ hai, deploy theo tag) | **Không làm** | Nhóm chốt 2026-09-23 — staging là môi trường cuối (Đ-7.4) |

---

## 3. Quyết định thiết kế đã chốt

Mười bốn quyết định. Đ-7.1 và Đ-7.2 quyết định **cách tổ chức cả giai đoạn**, nên chốt trước; phần còn lại là quyết
định kỹ thuật, chốt lúc mở khối tương ứng.

### Đ-7.1 Thứ tự ưu tiên: bằng chứng trước, kỹ thuật sau — HA là phần cắt được duy nhất

Kế hoạch gốc liệt kê năm nhóm việc GĐ7 ngang hàng nhau. Chúng **không** ngang hàng:

| Hạng | Việc | Vì sao ở hạng này |
|---|---|---|
| 1 | Backup + restore drill | Báo cáo A&D đã **cam kết chạy thật**; mất dữ liệu là mất tất cả, không sửa được |
| 2 | Đồng hồ uptime | Cam kết chạy thật; **phụ thuộc thời gian tích lũy** nên trễ là mất vĩnh viễn |
| 3 | Prometheus + Grafana | Cam kết chạy thật; không phụ thuộc thời gian nên xếp sau hạng 2 |
| 4 | HSTS + rà Swagger + xoay khóa | Rẻ (dưới 1 giờ), rủi ro cao nếu quên |
| 5 | **HA 2 instance** | Báo cáo v5.0 xếp "≥ 2 instance" vào **Roadmap** — tức là đã tuyên bố không làm trong MVP |

> ⚠️ Trước khi cắt khối E, **mở lại báo cáo v5.0 xác nhận dòng đó đúng là nằm ở Roadmap.** Cắt một thứ đã trót
> cam kết trong báo cáo thì lúc bảo vệ không có đường đỡ.

Hệ quả: khi vỡ tiến độ, cắt **từ dưới lên** — bỏ khối E trước, không bao giờ rút ngắn khối A.

### Đ-7.2 Khối A–E tách khỏi nhịp lát cắt dọc, làm song song được ngay từ bây giờ

GĐ7 không có hợp đồng API nên không có cổng mở (Mục 1). Khối A–E chỉ chạm `deploy/`, cấu hình apache, và **một chỗ
duy nhất** trong `Program.cs` (dòng `MapMetrics`). Chúng không chạm module nào, không chạm schema nào.

Vì vậy: **một người làm được toàn bộ khối A–E ngay từ bây giờ, song song với người đang làm GĐ2**, không cần chờ tới
Ngày 21. Chỉ **khối F** (lane frontend) phải đợi đúng nhịp, vì nó đánh bóng những màn hình mà GĐ6 mới sinh ra.

Đây cũng là lý do đồng hồ uptime nên bật **hôm nay** chứ không phải Ngày 21: xem Đ-7.3.

### Đ-7.3 Đồng hồ uptime có hai nguồn, vì máy đo đang nằm trên chính máy bị đo

Uptime Kuma chạy bằng container trên VM. Nếu VM chết — mất điện, OCI bảo trì, kernel panic — thì **Kuma cũng chết
cùng lúc** và khoảng downtime đó **không được ghi lại**. Kết quả: con số uptime luôn đẹp hơn sự thật, và đó là loại
sai lệch không ai phát hiện ra khi chấm bài, nhưng cũng là loại sai lệch không trung thực.

Chốt hai nguồn:

| Nguồn | Vai trò | Ghi chú |
|---|---|---|
| **Uptime Kuma trên VM** | Chi tiết: từng dịch vụ của staging (`/health/ready`, `/api/v1/ping`, frontend, backup Push) | Dữ liệu giàu, dùng để chẩn đoán |
| **Một monitor miễn phí bên ngoài** (UptimeRobot, Better Stack, hoặc Cloudflare Health Check) | **Trọng tài**: chỉ theo dõi `https://<domain>/health/ready` từ ngoài Internet | Con số báo cáo **lấy từ đây** |

Khi hai nguồn lệch nhau, **tin nguồn ngoài** và ghi lý do lệch vào biên bản. Dựng monitor ngoài tốn khoảng 10 phút
và là thứ rẻ nhất trong cả giai đoạn.

### Đ-7.4 Staging là môi trường cuối — không dựng production riêng *(sửa 2026-09-23, nhóm chốt)*

Kế hoạch gốc viết "nâng staging lên production". Bản đầu của tài liệu này chốt **hai stack trên cùng VM** (staging
theo `develop`, production theo tag `v*`). Nhóm đã bỏ phương án đó: **staging `mxh.banhgao.net` là môi trường cuối**
— nơi demo, nơi được chấm, nơi mọi bằng chứng GĐ7 lấy số.

Lý do: một VM, một người làm GĐ7, và thời gian còn lại cần cho GĐ2–GĐ6. Stack thứ hai là thêm một bộ compose, vhost,
`.env`, workflow deploy và monitor phải giữ cho đúng — trong khi thứ được chấm là **một** hệ thống đang chạy.

Hệ quả trực tiếp:

| Chỗ | Bản đầu (hai stack) | Bây giờ |
|---|---|---|
| Dữ liệu staging | "Rác, xóa được" | **Dữ liệu thật** — chính thứ khối A bảo vệ |
| Con số uptime báo cáo | Monitor production | Monitor staging — Kuma + UptimeRobot đang chạy |
| Tiền tố bản sao trên R2 | `production/` | `staging/` — giữ nguyên như đang chạy |
| Khối D | 4 việc, có D4 dựng stack | **3 việc**: D1 HSTS · D2 rà Swagger · D3 xoay khóa |
| Khối E (nếu làm) | `edge` cho stack production | `edge` cho chính staging |

**Ba cái giá chấp nhận, và cách chặn từng cái:**

1. **GĐ8 bắn k6 và quét ZAP vào đúng môi trường đang demo** — lý do ban đầu của phương án hai stack. Chặn bằng quy
   tắc: báo nhóm và chốt khung giờ trước; `./backup.sh full` ngay trước buổi; dùng tài khoản test riêng và dọn dữ liệu
   test sau buổi (runbook Kịch bản C, hoặc PITR về mốc trước buổi); **không** chạy trong 48 giờ trước ngày bảo vệ; ghi
   khoảng chậm/gián đoạn vào báo cáo uptime, không tắt monitor cho đẹp số; canh đĩa vì tải nặng sinh WAL thật (R7-02).
2. **Mỗi lần merge `develop` là thay thẳng bản demo** — không còn tầng tag chặn giữa. Chặn bằng đóng băng `develop`
   trước ngày bảo vệ (Mục 8 quy tắc 1) và rollback theo tag `:sha` khi lỡ (Mục 8 quy tắc 3).
3. **Swagger vẫn bật và mở công khai**, vì staging chạy `ASPNETCORE_ENVIRONMENT=Staging`. Nhóm chốt để mở có chủ đích
   (Đ-7.13), kèm rà những gì nó phơi ra (D2).

### Đ-7.5 Không thêm Caddy làm TLS; thêm một container `edge` làm cân tải bên trong mạng docker

Kế hoạch gốc vẽ "Caddy (TLS) → 2 API container". Thực tế VM **đã có apache giữ 80/443** từ GĐ0B nên `deploy/Caddyfile`
chưa bao giờ được dùng; thứ đang chạy là `docker-compose.staging.apache.yml` + `apache-socialapp.conf.example`.

Chốt: **apache giữ nguyên vai trò TLS + định tuyến theo path, không sửa một dòng nào.** Việc cân tải đẩy vào **một
container `edge`** (Caddy, bỏ phần TLS) nằm trong mạng docker, publish đúng một cổng localhost, upstream trỏ tới
tên service `api` — Docker DNS tự round-robin qua các bản sao.

```
apache (host, TLS)  ──▶  127.0.0.1:18080  ──▶  edge (trong docker)  ──▶  api ×2
```

`edge` nhận lại đúng cổng `18080` mà `api` đang giữ, nên `ProxyPass` của apache giữ nguyên. Ba cái lợi: apache không cần `mod_proxy_balancer`; thêm/bớt bản sao không phải sửa cấu hình apache; và `Caddyfile`
đã viết sẵn từ GĐ0B được tái sử dụng thay vì vứt đi.

> ⚠️ **Cạm bẫy đã có tiền sử trên chính máy này:** Docker DNS từng hỏng trên OCI (container không resolve được
> `postgres`/`redis`, lỗi `getaddrinfo EAGAIN`), phải `update-alternatives --set iptables ...-legacy` rồi restart
> Docker. `edge` phụ thuộc hoàn toàn vào Docker DNS — nếu lỗi đó tái phát, triệu chứng sẽ là 502 ngẫu nhiên.

### Đ-7.6 Bỏ ánh xạ cổng cố định của `api` là **điều kiện cần** để có hai bản sao

Compose hiện ghim `ports: ["127.0.0.1:18080:8080"]` cho service `api`. Với dòng này, `--scale api=2` **luôn thất bại**:
bản sao thứ hai không bind được cổng đã bị chiếm. Đây là chỗ chặn kỹ thuật thật sự của khối E, không phải chi tiết
cấu hình — ai bắt đầu khối E mà chưa biết điều này sẽ mất nửa ngày đoán mò.

Sau khi có `edge` (Đ-7.5), service `api` **không publish cổng ra host nữa**; chỉ `edge` publish.

### Đ-7.7 `/metrics` không bao giờ ra Internet

`/metrics` phơi bày đường dẫn nội bộ, tên controller, phân bố độ trễ và số lỗi — đủ để vẽ bản đồ hệ thống. Nó chỉ
được truy cập **trong mạng docker**, bởi Prometheus.

Ba lớp giữ:
1. apache **không** thêm `ProxyPass /metrics` *(mặc định đã an toàn: catch-all `/` đi về frontend, không về API)*;
2. `edge` chặn tường minh path `/metrics`;
3. Prometheus scrape bằng tên service nội bộ `api:8080/metrics`, không qua domain công khai.

Có một test hồi quy cho việc này ở C6 — vì lớp 1 là "an toàn nhờ tình cờ", mà thứ an toàn nhờ tình cờ thì sẽ mất
an toàn vào ngày ai đó thêm một dòng ProxyPass.

### Đ-7.8 Metrics bằng `prometheus-net`, không OpenTelemetry

`prometheus-net.AspNetCore` cho RED metrics bằng đúng hai dòng (`UseHttpMetrics()` + `MapMetrics()`), không cần
collector, không cần thêm container. OpenTelemetry mạnh hơn nhưng chỉ đáng giá khi có tracing tập trung — mà Tempo
đã nằm ở Roadmap (Mục 2), nên phần mạnh hơn đó không ai dùng.

Ghim **đúng một version dòng 8.x** cho khớp lineage net8 của dự án (EF Core 8.0.10, Serilog.AspNetCore 8.0.3…).
Kiểm arm64 trước khi ghim — luật vàng số 8.

### Đ-7.9 Cảnh báo bằng Grafana alerting, không dựng Alertmanager

Grafana đã phải có để vẽ dashboard; alerting nằm sẵn trong đó, gửi được email/Telegram/webhook. Alertmanager là
container thứ tư trong stack ops chỉ để làm việc Grafana đã làm được.

Kênh nhận cảnh báo chốt **một** cái và phải **thử cho kêu thật một lần** (C5) — kênh chưa bao giờ kêu là kênh chưa
tồn tại.

### Đ-7.10 Bản sao còn nằm trên VM thì chưa phải bản sao

Backup ghi ra volume trên chính VM chỉ cứu được sự cố *logic* (xóa nhầm bảng, migration hỏng). Nó **không cứu được**
sự cố mất máy — mà mất máy đúng là kịch bản mà NFR-REL-02 sinh ra để phòng.

Chốt: mỗi bản sao được đẩy **rời khỏi VM**, dùng lại chính hạ tầng R2 đã có từ GĐ2, nhưng:
- **bucket riêng** `socialmedia-backup`, **không** dùng chung với `-dev`/`-staging`;
- **token riêng, phạm vi chỉ bucket đó**, chỉ nằm trên VM và trong kho bí mật nhóm;
- vòng đời: giữ 7 bản ngày + 4 bản tuần, cũ hơn thì xóa.

> ⚠️ Khóa R2 cũ đã lộ trong một phiên trao đổi ngày 2026-09-04. **Token backup phải là token mới**, và việc xoay các
> khóa cũ là đầu việc D3 của chính giai đoạn này.

### Đ-7.11 Hai lớp sao lưu: vật lý cho RPO, logic cho sự tiện

| Lớp | Công cụ | Chu kỳ | Cứu được gì | Vì sao cần cả hai |
|---|---|---|---|---|
| **Vật lý** | `pg_basebackup` + WAL archiving | base: hằng ngày · WAL: liên tục | Tua tới **bất kỳ thời điểm nào** → RPO ≤ 15 phút | Đây là thứ đáp ứng NFR-REL-02 |
| **Logic** | `pg_dump -Fc` | Hằng ngày | Khôi phục **một bảng**, đọc được, chuyển máy được | Lớp vật lý khôi phục nguyên cụm, không lấy riêng một bảng |

RPO ≤ 15 phút cần `archive_timeout = 15min` — nếu không, một segment WAL chưa đầy thì chưa được archive, và dữ liệu
15 phút cuối nằm trên đúng cái đĩa vừa hỏng. Đây là một dòng cấu hình, quên là hỏng cam kết.

### Đ-7.12 HSTS đặt ở apache, không dùng `UseHsts()` của ASP.NET

TLS kết thúc ở apache (và ở Cloudflare phía trước), nên API không nhìn thấy HTTPS thật — `UseHsts()` trong ASP.NET
dễ trở thành no-op hoặc gửi header sai. Đặt ở apache thì nó áp cho **cả** frontend lẫn API.

*Thực tế (kiểm 2026-09-23):* frontend **đã tự gửi** `max-age=31536000` từ GĐ1 (`src/frontend/next.config.ts`, chỉ khi
`NODE_ENV=production`); `/api`, `/health`, `/swagger` thì **không**. Vì vậy apache phải `unset` header của backend
trước khi `always set` — thiếu dòng `unset` thì trang frontend nhận **hai** header HSTS.

Bắt đầu bằng `max-age=31536000` **không** `includeSubDomains`, **không** `preload`. Lý do: domain cha đang phục vụ
dịch vụ khác; `preload` thì không rút lại được trong nhiều tháng. Siết thêm là việc của Roadmap.

### Đ-7.13 Swagger mở công khai có chủ đích — không sửa code, không đổi environment *(nhóm chốt 2026-09-23)*

[`Program.cs`](../../src/backend/SocialApp.Api/Program.cs) bật Swagger ở `Development` + `Staging`, tắt ở
`Production`. Khi staging thành môi trường cuối (Đ-7.4), nhóm cân nhắc ba đường và chọn **để mở**:

| Đường | Được | Mất |
|---|---|---|
| **Để mở công khai** *(chốt)* | Giảng viên và cả nhóm xem hợp đồng không cần mật khẩu; Swagger trên staging vẫn là nguồn sự thật ở cổng đóng | Bản đồ toàn bộ API phơi ra Internet |
| Khóa `/swagger` bằng Basic Auth ở apache | Người lạ nhận 401 | Thêm một mật khẩu phải chia cho người chấm; một khối `<Location>` + file `htpasswd` |
| Đổi staging sang `ASPNETCORE_ENVIRONMENT=Production` | Swagger tắt, không sửa code | Đổi **mọi** chỗ đọc `IHostEnvironment` / `appsettings.{Env}.json` ngay trên bản demo — cấu hình chưa từng chạy thử |

**Vì sao chấp nhận được:** an ninh của hệ thống không dựa vào việc giấu danh sách endpoint — nó dựa vào ba tầng kiểm
soát truy cập và AuthZ matrix trên CI (GOAL-03). Người dò có bản đồ vẫn phải qua tầng 1–3 như mọi người.

**Điều kiện đi kèm (D2):** thứ Swagger phơi ra phải đúng bằng hợp đồng. Rà ngày 2026-09-23 thấy khớp, **trừ hai
endpoint demo của GĐ0** trong nhóm Platform: `GET /api/v1/ping/app-error` (409) và `GET /api/v1/ping/boom` (500) —
công khai, không cần đăng nhập. Trên môi trường cuối, `boom` cho **bất kỳ ai** tự tạo lỗi 500 theo ý muốn: làm bẩn
số liệu tỷ lệ lỗi và kích hoạt giả cảnh báo "5xx > 1%" (Mục 5.3). Chặn hai đường này **ở apache**, không sửa code:
integration test vẫn dùng chúng, và C5 vẫn bắn được từ chính VM qua `127.0.0.1:18080` (đi thẳng vào API, không qua
apache).

Sau này muốn khóa Swagger thì chỉ cần thêm một khối `<Location /swagger>` với Basic Auth vào vhost.

### Đ-7.14 Redact PII trong log theo danh sách trường cố định, có cổng CI

Serilog đã ghi JSON. GĐ7 thêm việc **không để email, mật khẩu, token, khóa R2, presigned URL lọt vào log** — trong đó
presigned URL là thứ dễ quên nhất và nguy hiểm nhất, vì nó là **một URL cầm là dùng được** (đã ghi thành luật review
ở GĐ2, B.9 điểm 4).

Danh sách trường bị che chốt cứng, và thêm một bước grep vào cổng CI để chặn `Log.Information` in thẳng object chứa
các trường đó. Cổng này phải **thử cho đỏ một lần** mới tính là có.

---

## 4. Kiến trúc vận hành sau GĐ7

```
                           Internet
                              │
                    Cloudflare (proxy, TLS Full strict)
                              │
         ┌────────────────────┴─────────────────────┐
         │   apache trên host — 80/443, cert wildcard│
         │   mxh.banhgao.net      → 127.0.0.1:18080  │  (môi trường cuối — Đ-7.4)
         │   HSTS mọi đường · /swagger mở (Đ-7.13)   │
         └────────────────────┬─────────────────────┘
                              │
   ┌──────────────────────────┴───────────────────────────┐
   │  project socialapp-staging  (mạng internal)          │
   │                                                       │
   │   edge (Caddy, không TLS) ──round-robin──▶ api ×2     │
   │        │                                    │         │
   │        └──▶ frontend                        ├──▶ postgres ──▶ volume pgdata
   │                                             │              └▶ ./backups (wal, base, dump) ──▶ R2
   │                                             └──▶ redis                          ▲
   └───────────────────────────────────────────────────────┘                         │
                              ▲ scrape api:8080/metrics                              │ backup.sh (cron)
                              │ (mạng nội bộ — KHÔNG qua apache, Đ-7.7)              │
   ┌──────────────────────────┴───────────────────────────┐
   │  project socialapp-ops                                │
   │   prometheus ──▶ grafana (dashboard + alerting)       │
   │   uptime-kuma  ──probe──▶ https://<domain>/health/ready│
   │   node-exporter (đĩa, RAM, CPU của host)              │
   └───────────────────────────────────────────────────────┘
                              ▲
                              │ (trọng tài, Đ-7.3)
                  monitor bên ngoài Internet
```

**Stack ops đứng riêng project** để `docker compose down` stack ứng dụng lúc deploy không kéo theo đồng hồ uptime —
nếu chung project thì mỗi lần deploy đồng hồ tự tắt và tự ghi nhận… chính mình đang chết.

---

## 5. Chỉ số phải có

### 5.1 RED — do `prometheus-net` cấp sẵn, không phải viết

| Chỉ số | Dùng để trả lời |
|---|---|
| `http_requests_received_total` (theo `code`, `method`, `controller`) | Đang có bao nhiêu tải? Bao nhiêu phần trăm lỗi? |
| `http_request_duration_seconds` (histogram) | p50/p95/p99 — **đây là chỗ GOAL-01 được nhìn thấy ở GĐ8** |
| `http_requests_in_progress` | Có đang bị nghẽn không? |

### 5.2 Chỉ số nghiệp vụ — phải tự thêm, mỗi cái một dòng

| Chỉ số | Kiểu | Vì sao chọn nó |
|---|---|---|
| `socialapp_login_failed_total` | Counter | Tăng đột biến = đang bị dò mật khẩu (ISS-04) |
| `socialapp_posts_created_total` | Counter | Dấu hiệu sống của hệ thống: số 0 kéo dài = hỏng ở đâu đó dù health vẫn xanh |
| `socialapp_presign_issued_total` | Counter | Đối chiếu với số object trên R2 → phát hiện rác upload dở (GĐ2 Đ-2.13) |
| `socialapp_orphan_media_cleaned_total` | Counter | Chứng minh worker dọn rác **thật sự chạy**, không phải chỉ được đăng ký |

Bốn cái, không hơn. Mỗi chỉ số thêm vào là một thứ phải giữ cho đúng; chỉ số không ai nhìn là nợ, không phải tài sản.

### 5.3 Ngưỡng cảnh báo

| Cảnh báo | Điều kiện | Mức | Vì sao ngưỡng này |
|---|---|---|---|
| Dịch vụ chết | `/health/ready` fail 2 lần liên tiếp | **Khẩn** | Kế hoạch gốc yêu cầu; là thứ GOAL-04 đo |
| Tỷ lệ lỗi cao | 5xx > **1%** trong **5 phút** | **Khẩn** | Ngưỡng do kế hoạch gốc chốt, giữ nguyên |
| Chậm bất thường | p95 > **500ms** trong 10 phút | Cảnh báo | Bằng đúng ngưỡng GOAL-01 để GĐ8 không phải chỉnh lại |
| **Đĩa sắp đầy** | dung lượng còn < **20%** | **Khẩn** | Backup và WAL cùng ăn đĩa; đĩa đầy thì Postgres **dừng ghi**, không phải chậm đi |
| Backup không chạy | không có bản mới trong 26 giờ | Cảnh báo | Backup hỏng im lặng là kiểu hỏng tệ nhất — chỉ lộ ra đúng lúc cần dùng |

Hai dòng cuối không có trong kế hoạch gốc. Chúng được thêm vì đó là hai cách thật sự làm chết một VM đơn lẻ.
Dòng cuối **không cần Grafana**: `backup.sh` gọi một monitor loại *Push* của Uptime Kuma (B1) khi xong; Kuma không
thấy push trong 26 giờ là kêu — nên cảnh báo này có ngay từ khối A (A3), không đợi khối C.

---

## 6. Chính sách sao lưu

| Khoản | Cam kết | Cơ chế |
|---|---|---|
| **RPO** | ≤ 15 phút | WAL archiving + `archive_timeout = 15min` |
| **RTO** | ≤ 2 giờ | Kịch bản khôi phục viết sẵn, đã diễn tập một lần nên biết thời gian thật |
| Base backup | Hằng ngày, 03:00 giờ VN | `pg_basebackup -Ft -z -X stream` |
| Dump logic | Hằng ngày, sau base backup | `pg_dump -Fc` |
| Nơi lưu | VM (nóng) **+ R2 bucket riêng** (nguội) | Đ-7.10 |
| Giữ | 7 bản ngày + 4 bản tuần | Xóa tự động trong `backup.sh` |
| Kiểm tra | Mỗi lần backup xong ghi kích thước + mã băm vào log | Không có bước này thì "file 0 byte" cũng tính là thành công |
| **Diễn tập** | **≥ 1 lần trong GĐ7**, có biên bản | Mục 7 |

---

## 7. Biên bản restore drill — khuôn bắt buộc

Lưu tại `docs/giai-doan-7/bien-ban-restore-YYYY-MM-DD.md`. Đây là **sản phẩm được chấm**, không phải ghi chú nội bộ.

```markdown
# Biên bản diễn tập khôi phục dữ liệu

- Ngày giờ thực hiện:            (giờ VN)
- Người thực hiện / người chứng kiến:
- Môi trường đích:               (stack socialapp-restore, KHÔNG phải DB đang chạy)

## 1. Bản sao được dùng
- Base backup:        tên file · thời điểm tạo · kích thước · mã băm
- Khoảng WAL:         từ … đến …
- Nguồn lấy về:       (R2 bucket backup / volume trên VM)

## 2. Mốc thời gian
| Bước | Bắt đầu | Kết thúc | Ghi chú |
|---|---|---|---|
| Tải bản sao về |  |  |  |
| Dựng Postgres trống |  |  |  |
| Khôi phục base |  |  |  |
| Tua WAL tới mốc mục tiêu |  |  |  |
| Chạy `--migrate` kiểm tra |  |  | phải là no-op |
| Đối chiếu dữ liệu |  |  |  |
| **Tổng thời gian (RTO thực đo)** | | | **so với cam kết 2 giờ** |

## 3. Đối chiếu dữ liệu
| Bảng | Số bản ghi trên DB gốc | Sau khôi phục | Khớp |
|---|---|---|---|
| identity.users |  |  |  |
| profile.profiles |  |  |  |
| content.posts |  |  |  |
| content.media_attachments |  |  |  |

- Mốc thời gian dữ liệu cuối cùng khôi phục được: …
- **RPO thực đo** (khoảng cách tới thời điểm sự cố giả định): … · so với cam kết 15 phút

## 4. Sự cố gặp phải trong lúc diễn tập
(Ghi cả những thứ đã tự khắc phục. Đây là phần có giá trị nhất của biên bản —
diễn tập không gặp sự cố nào thường có nghĩa là diễn tập chưa đủ thật.)

## 5. Kết luận
- [ ] Đạt RPO ≤ 15 phút
- [ ] Đạt RTO ≤ 2 giờ
- [ ] Kịch bản khôi phục đã được cập nhật theo những gì học được
- Việc phải sửa sau diễn tập:
```

---

## 8. Quy tắc deploy lên môi trường cuối

Ba quy tắc, áp cho mọi lần deploy lên staging kể từ GĐ7 — staging giờ là môi trường cuối (Đ-7.4):

1. **`develop` vẫn tự deploy, nhưng đóng băng trước ngày bảo vệ.** Không còn tầng tag chặn giữa: mỗi merge vào
   `develop` là thay thẳng bản demo. Từ **3 ngày trước ngày bảo vệ** chỉ merge hotfix đã thống nhất cả nhóm, và sau
   mỗi merge phải thấy staging xanh lại (Kuma + `/health/ready`) rồi mới merge cái tiếp theo.
2. **Migration theo expand–contract.** Mỗi migration phải chạy được với **cả bản app cũ lẫn bản mới** trong một
   phiên bản: thêm cột nullable trước, điền dữ liệu, đổi code, **rồi mới** siết ràng buộc ở phiên bản sau. Không có
   luật này thì rolling update tự mâu thuẫn — trong lúc rolling, hai phiên bản app cùng nói chuyện với một schema.
3. **Rollback là đổi tag rồi `up -d`,** không phải `git revert` rồi build lại. CD đã gắn sẵn tag `:${{ github.sha }}`
   cho mỗi image nên đường quay về **đã có sẵn từ GĐ0B** — chỉ cần viết nó ra thành một dòng lệnh trong runbook.

---

## 9. Kế hoạch thi công (1 người · làm sớm được — Đ-7.2)

Không có cổng mở (Mục 1). Thay vào đó, **giờ đầu tiên** làm hai việc rẻ nhất mà phụ thuộc thời gian nhất:

| Thứ tự | Việc | Thời lượng | Vì sao đứng đây |
|---|---|---|---|
| **Giờ đầu** | B1 + B2 — Uptime Kuma + monitor ngoài | ~1 giờ | Đồng hồ phải bắt đầu chạy trước mọi thứ khác (Đ-7.3) |
| Ngày 1 | Khối **A** — backup + WAL + đẩy lên R2 | 1 ngày | Hạng 1 theo Đ-7.1 |
| Ngày 2 sáng | **A5 — restore drill + biên bản** | nửa ngày | Sản phẩm được chấm; đừng dồn về cuối |
| Ngày 2 chiều | Khối **D** — HSTS, rà Swagger, xoay khóa | nửa ngày | Rẻ, rủi ro cao nếu quên |
| Ngày 3 | Khối **C** — metrics + Prometheus + Grafana + alert | 1 ngày | Cần alert kêu thật một lần trước khi đóng |
| *(nếu còn)* | Khối **E** — edge + 2 bản sao + rolling | 1 ngày | **Cắt được** (Đ-7.1) |
| Đúng nhịp GĐ7 | Khối **F** — frontend bản cuối + gom artifact | nửa ngày | Phải đợi GĐ6 xong |

Tổng khối A–D: **khoảng 2,5 ngày công của một người**, và không đụng ai đang làm GĐ2–GĐ6.

### Thư viện / image cần thêm

| Thứ | Ở đâu | Ghi chú |
|---|---|---|
| `prometheus-net.AspNetCore` | `SocialApp.Api.csproj` | Ghim một version dòng 8.x. **Kiểm arm64 trước khi ghim** (luật vàng 8) |
| `prom/prometheus` | stack ops | Có arm64 chính thức |
| `grafana/grafana-oss` | stack ops | Có arm64 chính thức |
| `louislam/uptime-kuma` | stack ops | **Kiểm arm64 trước khi dùng** |
| `prom/node-exporter` | stack ops | Cho cảnh báo đĩa (Mục 5.3) |
| — | frontend | Không thêm gói nào |

---

## 10. Chiến lược nghiệm thu

GĐ7 gần như không có unit test — thứ nó tạo ra là *cấu hình* và *quy trình*. Nghiệm thu bằng ba cách, theo thứ tự
tin cậy giảm dần:

**10.1 Phá rồi xem nó có báo không.** Cách duy nhất chứng minh được phần cảnh báo:

| Phá cái gì | Phải thấy gì |
|---|---|
| `docker stop` container `api` của staging *(báo nhóm trước)* | Uptime Kuma đỏ; alert "dịch vụ chết" gửi tới kênh thật |
| Từ VM, bắn ~200 request vào `127.0.0.1:18080/api/v1/ping/boom` (đường công khai đã chặn ở D2) | Alert error rate > 1% kêu trong vòng 5 phút |
| Đổi tên file backup mới nhất | Alert "backup không chạy" kêu sau 26 giờ *(hoặc hạ tạm ngưỡng để thử)* |
| `docker kill` **một** bản sao `api` *(khối E)* | Site vẫn trả 200 suốt quá trình; không có request nào lỗi |

**10.2 Đối chiếu bằng số.** Restore drill (Mục 7) — so số bản ghi từng bảng trước và sau khôi phục.

**10.3 Kiểm bằng mắt, có ảnh lưu lại.** Grafana có số liệu thật; lịch sử uptime; `curl -I` thấy đúng một HSTS ở cả
trang lẫn API; `/api/v1/ping/boom` từ Internet trả 403; `/metrics` qua domain công khai **không** truy cập được.

### Cổng CI mở rộng

Một cổng mới duy nhất: **grep chặn log PII** (Đ-7.14). Thêm vào `.github/workflows/ci.yml`, và **thử cho đỏ một lần**
bằng cách cố tình log một object chứa email — cổng chưa từng đỏ là cổng chưa chứng minh được gì (bài học B4/B5 của GĐ2).

---

## 11. Definition of Done

Theo Mục 3.5 báo cáo, diễn giải cho giai đoạn không có endpoint:

- [ ] Mọi thay đổi cấu hình **nằm trong repo** (`deploy/`), không phải chỉ sửa tay trên VM rồi quên
- [ ] `deploy/.env.example` có đủ khóa mới, **giá trị trống** — không commit secret (luật vàng 3)
- [ ] Runbook viết xong: backup, khôi phục, rollback, xử lý khi alert kêu
- [ ] Mỗi cảnh báo đã kêu **thật** ít nhất một lần
- [ ] Biên bản restore drill đã ký
- [ ] `README.md` Mục 1 cập nhật trạng thái GĐ7 (luật vàng 7)
- [ ] Artifact cho báo cáo đã gom vào `docs/giai-doan-7/bang-chung/`

## 12. Checklist nghiệm thu cuối GĐ7

**Sao lưu (A)**
- [ ] `archive_timeout = 15min` đang có hiệu lực trên staging
- [ ] Có ≥ 2 base backup liên tiếp, sinh tự động, không do tay
- [ ] Bản sao mới nhất **tồn tại trên R2**, không chỉ trên VM
- [ ] Bản sao cũ hơn hạn giữ **đã tự bị xóa** (kiểm bằng cách nhìn danh sách, không suy đoán)
- [ ] Biên bản restore drill đầy đủ Mục 1–5, có RPO/RTO **thực đo**

**Uptime (B)**
- [ ] Uptime Kuma theo dõi `/health/ready` của staging
- [ ] Monitor ngoài đang chạy, có số liệu
- [ ] Có ảnh lịch sử uptime ≥ 7 ngày liên tục *(vì vậy phải bật sớm)*

**Quan sát (C)**
- [ ] Grafana hiển thị RED + 4 chỉ số nghiệp vụ, số liệu **thật** từ staging
- [ ] Năm cảnh báo Mục 5.3 đều đã cấu hình
- [ ] Ít nhất ba trong số đó **đã kêu thật**
- [ ] `https://<domain>/metrics` **không** truy cập được từ Internet
- [ ] Cổng CI chặn log PII đã xanh, và đã từng đỏ một lần

**Nợ GĐ0B (D)**
- [ ] Đúng **một** header `Strict-Transport-Security` ở `/`, `/api/v1/ping`, `/health/ready`, `/swagger/index.html`
- [ ] Swagger chỉ liệt kê endpoint có trong file hợp đồng; `/api/v1/ping/boom` và `/app-error` từ Internet → 403
- [ ] Danh sách khóa đã xoay, ghi ngày xoay

**Chịu lỗi (E — cắt được)**
- [ ] `docker kill` một bản sao `api` → site vẫn phục vụ, không mất request
- [ ] Rollback bằng tag đã **thử thật** một lần
- [ ] Quy tắc expand–contract đã ghi vào `AGENTS.md`

## 13. Sai khác so với kế hoạch gốc và báo cáo v5.0

| # | Kế hoạch gốc / v5.0 | Thực hiện | Lý do |
|---|---|---|---|
| 1 | "Caddy (TLS) → 2 API container" | apache giữ TLS; thêm `edge` cân tải **trong** docker | VM đã có apache giữ 80/443 từ GĐ0B; `Caddyfile` gốc chưa từng được dùng (Đ-7.5) |
| 2 | Năm nhóm việc ngang hàng | Xếp hạng 1–5; HA là phần cắt được duy nhất | Báo cáo v5.0 đã xếp "≥ 2 instance" vào Roadmap, còn backup/Grafana là cam kết chạy thật (Đ-7.1) |
| 3 | "Nâng staging lên production" | **Không có production; staging là môi trường cuối** *(nhóm chốt 2026-09-23, bỏ phương án hai stack của bản đầu)* | Một VM, một người làm GĐ7; thứ được chấm là một hệ thống đang chạy. Giá chấp nhận: k6/ZAP ở GĐ8 chạm môi trường demo — có quy tắc chặn (Đ-7.4) |
| 4 | "Tắt Swagger ở production" | Swagger **để mở công khai có chủ đích** trên staging (nhóm chốt 2026-09-23); chặn hai endpoint demo lỗi của GĐ0 ở apache | Không còn production để tắt; an ninh dựa vào ba tầng kiểm soát truy cập, không dựa vào giấu bản đồ API (Đ-7.13) |
| 5 | Uptime Kuma là nguồn uptime duy nhất | Thêm **monitor ngoài** làm trọng tài | Kuma chạy trên chính máy nó đo → không ghi được sự cố toàn máy (Đ-7.3) |
| 6 | Cảnh báo: chỉ "error rate > 1%/5 phút" | Thêm cảnh báo **đĩa đầy** và **backup không chạy** | Hai cách thật sự làm chết một VM đơn lẻ, cả hai đều im lặng cho tới lúc quá muộn |
| 7 | Backup lưu trên VM | Bắt buộc đẩy rời khỏi VM (R2 bucket riêng) | Backup cùng máy không cứu được kịch bản mất máy — đúng kịch bản NFR-REL-02 nhắm tới (Đ-7.10) |
| 8 | GĐ7 nằm ở Ngày 21–23 | Khối A–E **làm sớm được ngay** | GĐ7 không có hợp đồng API nên không phụ thuộc GĐ2–GĐ6 (Đ-7.2) |
| 9 | "Chưa có header HSTS" (ghi chú GĐ0B) | Trang frontend **đã có** từ GĐ1; D1 chỉ bổ sung đường API | Kiểm trực tiếp staging 2026-09-23 (Đ-7.12) |

## 14. Rủi ro cần theo dõi

| Mã | Rủi ro | Dấu hiệu sớm | Ứng phó |
|---|---|---|---|
| **R7-01** | Đồng hồ uptime bật muộn → không đủ dữ liệu để báo cáo con số nào | Ngày 21 mới dựng Kuma | Bật ngay hôm nay (Đ-7.2, Đ-7.3) — rẻ nhất, mất mát không thể bù |
| **R7-02** | Đĩa đầy vì WAL + backup | Đĩa qua 70% | Cảnh báo ở 80%; giới hạn hạn giữ; đẩy sớm lên R2 rồi xóa bản nóng |
| **R7-03** | Backup "thành công" nhưng file rỗng | Kích thước bản sao đột ngột nhỏ đi | Ghi kích thước + mã băm mỗi lần; cảnh báo backup không chạy |
| **R7-04** | Docker DNS hỏng lại trên OCI → `edge` 502 ngẫu nhiên | 502 rải rác sau khi bật khối E | Đã có cách xử từ GĐ0B (`iptables-legacy`); nếu tái phát thì hoãn khối E |
| **R7-05** | Rolling update vấp migration không tương thích | Lỗi 500 ở đúng lúc đang rolling | Luật expand–contract (Mục 8 điểm 2); khi nghi ngờ thì chấp nhận dừng ngắn thay vì rolling |
| **R7-06** | Grafana/Prometheus bị phơi ra Internet | apache có dòng ProxyPass mới | Không mở cổng ra host; truy cập qua SSH tunnel; test hồi quy C6 |
| **R7-07** | Khóa đã lộ vẫn còn sống | — | D3: xoay **ngay** — staging đang giữ dữ liệu thật (Đ-7.4) |
| **R7-08** | k6/ZAP ở GĐ8 làm chậm hoặc làm bẩn chính môi trường demo | Kế hoạch GĐ8 chưa có khung giờ | Quy tắc ở Đ-7.4 cái giá 1: báo nhóm, backup trước, tài khoản test riêng, dọn sau, không chạy 48 giờ trước bảo vệ |

---

# Phần B — Kế hoạch triển khai

## B.0 Cách đọc phần này

Phần A nói *cái gì* và *vì sao*. Phần B chia việc thành **sáu khối A–F**, mỗi khối một chuỗi đầu việc có mã
(`A1`, `C4`, …). Mã việc đi vào **tiêu đề commit** (`.claude/rules/commit-rules.md`, scope dạng `gd7-a`) và vào bảng
theo dõi tiến độ.

Một đầu việc xong khi: chạy được · **có bằng chứng lưu lại** · tài liệu/runbook sửa **trong cùng commit**. Với GĐ7,
"có test" của các giai đoạn khác được thay bằng "**có bằng chứng**" — ảnh màn hình, log, hoặc biên bản.

Thứ tự trong mỗi khối là thứ tự phụ thuộc; thứ tự **giữa** các khối là thứ tự ưu tiên của Đ-7.1.

## B.1 Điểm xuất phát — cái gì đã có sẵn

Kiểm ngày 2026-09-19 trên nhánh `develop`. GĐ7 **không** dựng lại thứ nào trong bảng dưới:

| Đã có | Ở đâu | GĐ7 dùng để làm gì |
|---|---|---|
| VPS OCI + Docker + user `deploy` + firewall | VM, `docs/oci-setup.md` | Nền của mọi thứ trong giai đoạn này |
| apache giữ 80/443 + cert wildcard Cloudflare | `deploy/apache-socialapp.conf.example` | Thêm HSTS cho mọi đường + chặn hai endpoint demo lỗi (D1, D2) |
| Compose staging đang chạy thật | `deploy/docker-compose.staging.apache.yml` | Môi trường cuối — khối A thêm WAL archiving vào đây |
| CD: build arm64 → GHCR → scp compose → ssh `pull`/`migrate`/`up --wait` | `.github/workflows/deploy-staging.yml` | Giữ nguyên — `develop` tự deploy (Mục 8) |
| **Image đã gắn tag `:${{ github.sha }}`** | cùng file trên | Đường rollback **đã có sẵn**, chỉ cần viết thành runbook |
| `docker image prune -f` chỉ xóa image mồ côi | cùng file trên | Image theo tag sha vẫn còn để quay về |
| Serilog JSON (`CompactJsonFormatter`) + correlation ID | `Program.cs:35` | Chỉ cần thêm redact PII (Đ-7.14) |
| `/health/live` + `/health/ready` (Postgres + Redis), `AllowAnonymous` | `Program.cs:389` | Đích probe của Uptime Kuma và của healthcheck compose |
| Swagger bật ở `Development` / `Staging` | `Program.cs:364` | **Không sửa** — để mở có chủ đích (Đ-7.13) |
| HSTS `max-age=31536000` trên các trang frontend | `src/frontend/next.config.ts` (từ GĐ1) | D1 chỉ bổ sung đường API; apache `unset` rồi `set` để không thành hai header |
| Healthcheck từng service + `--wait --wait-timeout 120` | compose staging | Nền của rolling update |
| Hạ tầng R2 + khuôn cấu hình 4 khóa `R2__*` | `deploy/.env.example` | Khuôn cho bucket backup (khóa **mới**, Đ-7.10) |
| `deploy/Caddyfile` | `deploy/` | Tái sử dụng làm `edge`, bỏ phần TLS (Đ-7.5) |
| `deploy/rotate-jwt-signing-key.sh` | `deploy/` | Dùng trong D3 |

**Ba chỗ có sẵn nhưng phải sửa:**

| Chỗ | Sửa gì | Vì sao |
|---|---|---|
| `SocialApp.Api.csproj` + `Program.cs` | 1 gói, 2 dòng (`UseHttpMetrics`, `MapMetrics`) | Đây là **toàn bộ** phần chạm code backend của GĐ7 |
| `deploy/.env.example` | Thêm khóa Grafana, **giá trị trống** (khóa backup R2 đã tách sang `backup.env.example`) | Luật vàng 3 |
| `.github/workflows/ci.yml` | Thêm cổng grep log PII | Đ-7.14 |

## B.2 Bản đồ công việc

| Khối | Nội dung | Hạng (Đ-7.1) | Số việc | Cần trước | Chặn |
|---|---|---|---|---|---|
| **A. Sao lưu & khôi phục** | WAL, base backup, đẩy R2, drill, runbook | **1** | 5 | B1 (monitor Push) | Nghiệm thu NFR-REL-02 |
| **B. Đồng hồ uptime** | Kuma + monitor ngoài + dashboard trạng thái | **2** | 3 | Không gì cả | GOAL-04 |
| **C. Metrics & cảnh báo** | `/metrics`, Prometheus, Grafana, alert, redact PII | **3** | 6 | B (dùng chung stack ops) | NFR-OBS-01, GĐ8 |
| **D. Nợ bảo mật GĐ0B** | HSTS, rà Swagger, xoay khóa | **4** | 3 | Không gì cả | — |
| **E. Chịu lỗi** | `edge`, 2 bản sao, rolling, rollback | **5 — cắt được** | 4 | Không gì cả | — |
| **F. Frontend + bàn giao** | Build production, a11y, gom artifact, đóng GĐ | — | 3 | GĐ6 xong | GĐ8 |

**Mọi khối chạy trên staging** — môi trường cuối (Đ-7.4). Đường đi đúng bảng Mục 9: **B → A → D → C → (E) → F**.

---

## B.3 Khối B — Đồng hồ uptime *(làm trong giờ đầu tiên)*

> **Mục tiêu khối:** bắt đầu tích lũy bằng chứng cho GOAL-04 **trước khi** làm bất cứ việc gì khác trong giai đoạn.

> **Hướng dẫn thi công từng bước:** [huong-dan-khoi-b-dong-ho-uptime.md](huong-dan-khoi-b-dong-ho-uptime.md)
> — lệnh nào, bấm chỗ nào, phá thử thế nào, ảnh bằng chứng lưu ở đâu. File compose stack ops đã có sẵn:
> [`deploy/docker-compose.ops.yml`](../../deploy/docker-compose.ops.yml).

### B1 — Uptime Kuma trong stack ops

Dựng `deploy/docker-compose.ops.yml` (project `socialapp-ops`, **tách khỏi** stack ứng dụng — Mục 4) với service
`uptime-kuma`, volume riêng, publish `127.0.0.1` thôi. Tạo monitor cho `/health/ready` của staging, chu kỳ 60s.

**Nghiệm thu:** tắt container `api` staging → Kuma chuyển đỏ trong vòng 2 phút.
**Cạm bẫy:** để chung project với stack ứng dụng thì mỗi lần deploy đồng hồ tự tắt và tự ghi nhận downtime giả.

### B2 — Monitor ngoài làm trọng tài

Một dịch vụ miễn phí bên ngoài theo dõi `https://mxh.banhgao.net/health/ready`, chu kỳ 5 phút. Ghi lại vào runbook:
tài khoản nào, ai có quyền vào, số liệu lấy ở đâu.

**Nghiệm thu:** nhận được thông báo khi dừng dịch vụ thật.
**Cạm bẫy:** Cloudflare đứng trước — nếu chọn chế độ "Always Online"/cache thì trang vẫn 200 khi backend đã chết.
Probe phải trỏ `/health/ready` (không cache) chứ không trỏ `/`.

### B3 — Kênh nhận cảnh báo

Chốt **một** kênh (email nhóm, hoặc Telegram). Cả B1, B2 và C5 sau này đều đổ về đây.

**Nghiệm thu:** ba người trong nhóm đều nhận được một tin thử.

---

## B.4 Khối D — Nợ bảo mật GĐ0B

> **Mục tiêu khối:** đóng ba khoản nợ bảo mật còn treo từ GĐ0B, trên chính môi trường cuối.
>
> *Sửa 2026-09-23:* bỏ **D4** (dựng stack production) theo Đ-7.4. Mã D1–D3 giữ nguyên để commit cũ không gãy tham chiếu.

> **Hướng dẫn thi công từng bước:** [huong-dan-khoi-d-bao-mat.md](huong-dan-khoi-d-bao-mat.md). Cấu hình apache đã
> viết sẵn trong [`deploy/apache-socialapp.conf.example`](../../deploy/apache-socialapp.conf.example).

### D1 — HSTS cho mọi đường

Frontend đã tự gửi HSTS từ GĐ1 (Đ-7.12); còn thiếu `/api`, `/health`, `/swagger`. Trong vhost 443:
`Header onsuccess unset Strict-Transport-Security` rồi `Header always set Strict-Transport-Security "max-age=31536000"`
— cặp `unset` + `set` để trang frontend không nhận hai header. Không `includeSubDomains`, không `preload`.

**Nghiệm thu:** đúng **một** header HSTS ở `/`, `/api/v1/ping`, `/health/ready`, `/swagger/index.html`.

### D2 — Rà thứ Swagger phơi ra + chặn endpoint demo lỗi

Swagger để mở có chủ đích (Đ-7.13), nên thứ nó liệt kê phải đúng bằng hợp đồng: liệt kê mọi nhóm, đối chiếu từng
endpoint với file `*-v1.yaml`. Chặn `/api/v1/ping/boom` và `/api/v1/ping/app-error` ở apache bằng `Require all denied`
— không sửa code.

**Nghiệm thu:** mọi endpoint trên Swagger có trong hợp đồng; hai endpoint demo từ Internet → 403, từ VM qua
`127.0.0.1:18080` vẫn chạy (C5 cần); `/api/v1/ping` vẫn 200.
**Cạm bẫy:** viết `<Location /api/v1/ping>` thay vì hai đường cụ thể là chặn luôn `ping` — monitor `api ping` đỏ.

### D3 — Xoay các khóa đã lộ

Khóa R2, SMTP (Resend), `Jwt__SigningKey`, mật khẩu Postgres đã lộ trong một phiên trao đổi ngày 2026-09-04. Xoay
hết **ngay** — staging là môi trường cuối, đang giữ dữ liệu thật (Đ-7.4); `deploy/rotate-jwt-signing-key.sh` đã có
sẵn cho khóa JWT. `Jwt__SigningKey` đã được xoay một lần sau GĐ1 F5 — nếu sau đó không lộ lại thì gạch khỏi danh sách.

**Nghiệm thu:** bảng "khóa · ngày xoay · ai xoay" trong runbook; staging vẫn chạy sau khi xoay.
**Cạm bẫy:** xoay `Jwt__SigningKey` làm mọi phiên đăng nhập hiện tại mất hiệu lực — báo nhóm trước, đừng làm giữa
lúc ai đó đang demo.

---

## B.5 Khối A — Sao lưu và khôi phục *(hạng 1)*

> **Mục tiêu khối:** khi mất VM, dựng lại được hệ thống với dữ liệu cách thời điểm sự cố không quá 15 phút — và
> **chứng minh được** điều đó bằng một lần đã làm thật.

> **Hướng dẫn thi công từng bước:** [huong-dan-khoi-a-sao-luu.md](huong-dan-khoi-a-sao-luu.md). Đã viết sẵn:
> [`deploy/backup.sh`](../../deploy/backup.sh) · [`deploy/restore.sh`](../../deploy/restore.sh) ·
> [`deploy/dem-ban-ghi.sql`](../../deploy/dem-ban-ghi.sql) · [`deploy/backup.env.example`](../../deploy/backup.env.example) ·
> [`deploy/docker-compose.restore.yml`](../../deploy/docker-compose.restore.yml) · [runbook-khoi-phuc.md](runbook-khoi-phuc.md) ·
> [bien-ban-restore-mau.md](bien-ban-restore-mau.md). Cấu hình WAL đã nằm trong `docker-compose.staging.apache.yml`.

### A1 — Bật WAL archiving trên Postgres staging

Bind `./backups` (cạnh compose) vào `/backups` của postgres; cấu hình `archive_mode = on`, `archive_command` ghi vào
`/backups/wal`, và **`archive_timeout = 15min`**
(Đ-7.11 — thiếu dòng này là hỏng cam kết RPO).

**Nghiệm thu:** để yên 20 phút không có giao dịch nào → vẫn thấy file WAL mới xuất hiện.
**Cạm bẫy:** `archive_command` trả về mã lỗi khác 0 thì Postgres **giữ lại WAL không xóa** → đĩa đầy dần trong im
lặng. Kiểm log Postgres sau khi bật, đừng chỉ kiểm có file mới.

### A2 — `backup.sh`: base backup + dump logic + hạn giữ

Script trong `deploy/`: `pg_basebackup -Ft -z -X stream` → `pg_dump -Fc` → ghi **kích thước + mã băm** vào log → xóa
bản quá hạn (7 ngày + 4 tuần). Chạy bằng cron/systemd timer **dưới user `deploy`**.

**Nghiệm thu:** chạy tay một lần ra file có kích thước hợp lý; đặt hạn giữ tạm 1 ngày để thấy việc xóa **thật sự** xảy ra.
**Cạm bẫy:** mọi thao tác phải `sudo -iu deploy` — đừng tạo file dưới `/home/deploy` bằng `ubuntu`/`sudo`, sẽ sai chủ
sở hữu và cron chạy hỏng.

### A3 — Đẩy bản sao lên R2 (bucket + token riêng)

Bucket `socialmedia-backup`, token phạm vi chỉ bucket đó, **khóa mới** (Đ-7.10). Thêm bước upload vào `backup.sh`;
thêm 4 khóa vào `.env.example` với **giá trị trống**.

**Nghiệm thu:** thấy object trên R2 dashboard; tải một bản về máy khác và giải nén được.
**Cạm bẫy:** đừng dùng lại token `-staging` của GĐ2 — phạm vi token là thứ chặn backup ghi nhầm chỗ.

### A4 — Runbook khôi phục

`docs/giai-doan-7/runbook-khoi-phuc.md`: từng lệnh, từ máy trắng tới hệ thống chạy. Viết **trước** A5 để A5 kiểm
chính nó.

**Nghiệm thu:** một người **khác** trong nhóm đọc và làm theo được mà không hỏi lại.

### A5 — Restore drill + biên bản ⭐

Dựng Postgres trống (compose riêng, cổng riêng — **tuyệt đối không chạm DB đang chạy**), khôi phục base, tua WAL, chạy
`--migrate` (phải là no-op), đối chiếu số bản ghi, bấm giờ từng bước, điền biên bản Mục 7.

**Nghiệm thu:** biên bản đủ Mục 1–5, có RPO/RTO **thực đo**.
**Cạm bẫy:** làm drill trên chính DB đang chạy. Tên volume sai một ký tự là ghi đè dữ liệu thật — đọc kỹ lệnh hai
lần trước khi Enter.

---

## B.6 Khối C — Metrics, dashboard, cảnh báo *(hạng 3)*

> **Mục tiêu khối:** có một chỗ để **nhìn** hệ thống, và một cơ chế **gọi người** khi nó hỏng. Đây cũng là chỗ GĐ8
> sẽ đọc kết quả k6.

### C1 — `/metrics` trên API

Thêm `prometheus-net.AspNetCore` (ghim version, kiểm arm64) + `UseHttpMetrics()` + `MapMetrics()`. **Hai dòng, và
đây là toàn bộ phần chạm code backend của GĐ7.**

**Nghiệm thu:** trong mạng docker, `curl api:8080/metrics` ra số liệu; qua domain công khai thì **không** vào được.

### C2 — Bốn chỉ số nghiệp vụ

Counter theo Mục 5.2, đặt ở tầng service của module tương ứng (không đặt ở controller — chúng đo nghiệp vụ, không
đo HTTP).

**Nghiệm thu:** đăng một bài trên staging → `socialapp_posts_created_total` tăng đúng 1.

### C3 — Prometheus trong stack ops

`deploy/prometheus.yml`: scrape `api:8080/metrics` của stack staging + `node-exporter`. Cần cho stack ops nhìn thấy
mạng của stack staging (gắn stack ops vào mạng `internal` của staging dưới dạng external network).

**Nghiệm thu:** trang Targets của Prometheus: tất cả **UP**.
**Cạm bẫy:** đây là đầu việc dễ mất thời gian nhất của khối — hai project Compose khác nhau thì mạng không tự thấy nhau.

### C4 — Grafana + dashboard

Một dashboard: RED (rate, error %, p50/p95/p99) + 4 chỉ số nghiệp vụ + đĩa/RAM. Mật khẩu admin qua biến môi trường,
**không** publish ra host (truy cập qua SSH tunnel — Đ-7.7, R7-06).

**Nghiệm thu:** biểu đồ có dữ liệu **thật** từ staging, không phải dữ liệu mẫu.

### C5 — Năm cảnh báo + thử cho kêu ⭐

Cấu hình đủ năm dòng Mục 5.3, đổ về kênh của B3. Rồi **phá thật** theo Mục 10.1.

**Nghiệm thu:** ảnh chụp ít nhất **ba** cảnh báo đã kêu thật, kèm giờ.
**Cạm bẫy:** cấu hình xong rồi tin là nó chạy. Cảnh báo chưa bao giờ kêu = cảnh báo chưa tồn tại.

### C6 — Redact PII + cổng CI + test hồi quy `/metrics`

Danh sách trường bị che (email, mật khẩu, token, khóa R2, presigned URL — Đ-7.14); grep trong `ci.yml`; một test
kiểm `/metrics` **không** phục vụ qua đường công khai.

**Nghiệm thu:** cố tình log một object chứa email → **CI đỏ**; sửa lại → xanh.

---

## B.7 Khối E — Chịu lỗi *(hạng 5 — cắt được)*

> **Mục tiêu khối:** một bản sao chết thì người dùng không nhận ra. Cắt khối này **không** làm mất cam kết nào trong
> báo cáo (Đ-7.1), nhưng làm được thì đây là bằng chứng mạnh nhất cho GOAL-04.

### E1 — `edge` cân tải trong mạng docker
Chuyển `deploy/Caddyfile` thành cấu hình `edge` (**bỏ phần TLS** — apache đã lo), upstream `api:8080`. **Bỏ**
`ports` của service `api`, chỉ `edge` publish `127.0.0.1:18080` — nhận lại cổng của `api`, apache giữ nguyên
(Đ-7.5, Đ-7.6). Chặn `/metrics` tại `edge`.
**Nghiệm thu:** site chạy y như trước khi có `edge` — đây là thay đổi người dùng không được phép thấy.

### E2 — Hai bản sao `api`
`--scale api=2`. **Nghiệm thu:** `docker kill` một bản sao → site vẫn 200 suốt; đếm số request lỗi phải bằng 0.

### E3 — Rolling update
Script thay từng bản sao một, chờ health xanh mới sang con tiếp theo. `up -d --wait` hiện tại thay **cả hai cùng lúc**
→ vẫn có downtime ngắn. **Nghiệm thu:** deploy trong lúc đang có tải nhẹ, không request nào lỗi.

### E4 — Rollback theo tag + luật expand–contract
Viết đường rollback thành một lệnh trong runbook (image `:sha` đã có sẵn); ghi luật expand–contract (Mục 8) vào
`AGENTS.md`. **Nghiệm thu:** rollback thật về bản trước rồi tiến lại, hệ thống bình thường ở cả hai chiều.

---

## B.8 Khối F — Frontend bản cuối và bàn giao *(đợi GĐ6)*

### F1 — Build production + responsive + a11y cơ bản
Service `frontend` **đã có sẵn** trong compose từ GĐ1 (F1 của khối F GĐ1), nên việc còn lại là đánh bóng: kiểm ba
kích thước màn hình, tương phản, tab order, `alt` cho ảnh.

### F2 — Gom artifact cho báo cáo
`docs/giai-doan-7/bang-chung/`: ảnh Grafana · ảnh lịch sử uptime · ảnh cảnh báo đã kêu · biên bản restore · ảnh
`curl -I` có HSTS ở cả trang lẫn API · sổ xoay khóa của D3. **Đây là phần được chấm** (Mục 1).

### F3 — Cập nhật `README.md` + đóng giai đoạn
Trạng thái GĐ7 ở Mục 1 của README (luật vàng 7); ghi những gì để lại cho GĐ8.

---

## B.9 Thứ tự thực thi và đường găng

**Đường găng:** `B1 → A1 → A2 → A3 → A5` — tất cả nằm ở khối phải-có. Khối C bám sau, khối E cắt được.

```
Giờ đầu   B1 ─ B2 ─ B3        (đồng hồ bắt đầu chạy — không có gì phụ thuộc, nhưng mọi ngày trễ đều mất vĩnh viễn)
Ngày 1    A1 ─ A2 ─ A3 ─ A4   (trên staging; B1 phải xong trước vì A3 dùng monitor Push của Kuma)
Ngày 2    A5 ⭐               │ D1 ─ D2 ─ D3
Ngày 3    C1 ─ C2 ─ C3 ─ C4 ─ C5 ⭐ ─ C6
(nếu còn) E1 ─ E2 ─ E3 ─ E4
(đúng nhịp GĐ7) F1 ─ F2 ─ F3
```

**Bốn chỗ dễ mất dứt điểm nhất:**

| Nguy cơ | Việc canh |
|---|---|
| Backup chạy nhưng không khôi phục được | **A5** — chỉ drill thật mới biết; đừng thay bằng "đã kiểm tra file tồn tại" |
| Cảnh báo cấu hình xong nhưng không kêu | **C5** — phá thật ba lần |
| Đồng hồ uptime đo chính nó | **B2** — monitor ngoài là thứ không thể thay bằng Kuma |
| `/metrics` lộ ra Internet vào ngày ai đó thêm ProxyPass | **C6** — phải là test, không phải lời dặn |

**Ba thứ không test tự động nào bắt được — bắt buộc người kiểm:**

1. **Bản sao có thật sự rời khỏi VM không.** Script chạy xanh nhưng upload im lặng thất bại là kịch bản rất thường gặp.
2. **`archive_timeout` có hiệu lực không.** Không có nó, RPO thành "tới lần WAL đầy gần nhất" — có thể là nhiều giờ.
3. **Drill có chạm nhầm DB đang chạy không.** Đọc lại tên volume và cổng trước mỗi lệnh.

## B.10 Mục tiêu từng khối — chúng cộng lại thành cái gì

| Khối | Mục tiêu | Thiếu nó thì mất gì |
|---|---|---|
| **A** | Mất máy vẫn còn dữ liệu, và **chứng minh được** | NFR-REL-02 trượt; và rủi ro thật: một lệnh sai là mất trắng công của cả nhóm |
| **B** | GOAL-04 có con số, đo từ ngoài | Không có cách nào nói "uptime 99%" mà không phải đoán |
| **C** | Nhìn thấy hệ thống, và được gọi khi nó hỏng | NFR-OBS-01 trượt; GĐ8 không có chỗ đọc kết quả k6 |
| **D** | Nợ bảo mật GĐ0B khép lại trên môi trường cuối | Khóa đã lộ vẫn sống trên bản demo; ai cũng tự tạo được lỗi 500 để làm bẩn số liệu |
| **E** | Một container chết không thành sự cố | Không mất cam kết nào, nhưng mất bằng chứng mạnh nhất cho GOAL-04 |
| **F** | Bằng chứng thành tài liệu nộp được | Làm hết mà không ai chấm được |

## B.11 Mục tiêu của GĐ7

### Ba điều kiện để tuyên bố GĐ7 xong

Thiếu bất kỳ điều nào thì **chưa xong**, dù mọi container đều xanh:

1. **Một biên bản restore drill đã ký**, có RPO và RTO **thực đo**, và dữ liệu sau khôi phục khớp số bản ghi.
2. **Một con số uptime lấy từ nguồn ngoài**, liên tục ≥ 7 ngày.
3. **Ít nhất ba cảnh báo đã kêu thật**, có ảnh chụp kèm giờ.

### GĐ7 để lại gì cho GĐ8

| Di sản | Ai thừa hưởng |
|---|---|
| Histogram `http_request_duration_seconds` + Grafana | **GĐ8 k6** — p95 feed đọc thẳng từ dashboard, không phải tự tính |
| Quy tắc chạy k6/ZAP trên môi trường cuối (Đ-7.4 cái giá 1, R7-08) | GĐ8 — không có môi trường riêng để bắn tải, phải theo quy tắc |
| Cảnh báo đĩa + backup | GĐ8 — chạy tải nặng là lúc đĩa đầy nhanh nhất |
| Runbook khôi phục + rollback theo tag | GĐ8 và cả sau khi bàn giao |
| Khóa đã xoay, HSTS mọi đường, Swagger đã rà | GĐ8 — phần security scan bắt đầu từ mặt bằng sạch, không phải vá lại từ đầu |
| Cổng CI chặn log PII | GĐ8 — một phần của NĐ 13/2023 (GOAL-05) đã có cơ chế chặn tự động |

**Một câu để nhớ:** các giai đoạn trước chứng minh hệ thống **làm được gì**; GĐ7 chứng minh nó **còn làm được vào
ngày mai** — và đó là điều duy nhất mà không có bài test nào chạy trong 10 giây có thể nói thay.
