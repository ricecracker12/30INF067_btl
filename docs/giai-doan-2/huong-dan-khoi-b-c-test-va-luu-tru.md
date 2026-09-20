# Hướng dẫn thực hiện — Khối B. Test và cổng CI + Khối C. Lưu trữ đối tượng (GĐ2)

> Bản triển khai chi tiết của **B.4 Khối B** và **B.5 Khối C** trong [giai-doan-2.md](giai-doan-2.md). Tài
> liệu gốc trả lời *cái gì* và *vì sao*; tài liệu này trả lời *gõ vào file nào, theo thứ tự nào, và nhìn vào
> đâu để biết đã xong thật*.
>
> Gộp hai khối vào một file là **có chủ đích**: B.2 giao cả hai cho **cùng một người (BE-2)**, và hai khối
> gặp nhau ở đúng một chỗ — `C5` (`FakeObjectStorage`) là thứ làm cho `B3` và mọi test của khối D chạy được
> trên CI **không có khóa R2**. Tách hai file thì chỗ gặp đó rơi vào khoảng giữa. Đây cũng là nếp GĐ1 đã
> chạy: [huong-dan-khoi-b-c-test-va-authz.md](../giai-doan-1/huong-dan-khoi-b-c-test-va-authz.md).
>
> **Nguồn sự thật vẫn là** `giai-doan-2.md` (Mục 3 quyết định `Đ-2.1`–`Đ-2.15`, Mục 6.3 sáu dòng matrix,
> Mục 7.2 SEQ-01, Mục 7.5 luồng dọn rác, Mục 9.0 chuẩn bị R2, Mục 10 chiến lược test) và `AGENTS.md`. Chỗ
> nào tài liệu này lệch với hai file đó thì sửa ở đây — không sửa ngược. Muốn đổi một `Đ-2.*` thì đó là
> **quyết định mới**, có ngày tháng, ghi vào `giai-doan-2.md` trong cùng commit.


|                              |                                                                                                                                                                                                                                                              |
| ---------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| **Người làm**                | BE-2 (cùng người làm nửa Content của khối D)                                                                                                                                                                                                                 |
| **Thời lượng**               | Ngày 6 (`C1`–`C2`) → Ngày 7 (`C3`, `C5`, `C4`) → Ngày 8 chiều (`B1`–`B3`) → Ngày 9 (`B4`–`B5`)                                                                                                                                                               |
| **Khối C chặn**              | `D3` (avatar), `D4` (`POST /media/uploads`), `D5` (`POST /posts`) — và **ISS-02**, rủi ro duy nhất có thể buộc cả nhóm đổi phương án                                                                                                                         |
| **Khối B chặn**              | `F4`, và gián tiếp toàn bộ cổng đóng — không có `B3`/`B5` thì "xanh" ở Ngày 9 không chứng minh được gì                                                                                                                                                       |
| **Cần trước**                | `C`: chỉ Mục 9.0 (bucket + CORS + token đã xong **trước** sáng Ngày 6). `B`: `A3`+`A5` cho `B1`; `D5`/`D7`/`D8` cho `B2`–`B3`; `D0` + hai `.yaml` cho `B4`; `E1` cho `B5`                                                                                    |
| **Không thuộc hai khối này** | Controller/DTO/validator của `/media/uploads` (`D4`) · test chức năng Mục 10.1 trừ bốn test BR-01 (khối D) · unit test BR-01 dạng hàm thuần (`A4`) · Playwright và kiểm tay CORS ở cổng đóng (`E`/`F2`/`F3`) · nới CSP cho host R2 (`E7`, ghi thành `Đ-E17`) |


---



## 0. Danh sách công việc — mục tiêu và kết quả mong đợi

Mười đầu việc, hai khối. **Khối C đi trước khối B về thời gian** dù B.4 xếp B trước trong tài liệu gốc — vì
`C2` là đầu việc rủi ro nhất của cả giai đoạn và phải xong **trong Ngày 6** (B.9). Các mục hướng dẫn chi tiết
bên dưới vì vậy xếp theo **thứ tự thi công**: `C1`→`C5` ở Mục 2–6, `B1`→`B5` ở Mục 7–11.

### 0.1 Khối C — Lưu trữ đối tượng (R2)

> **Mục tiêu khối:** đóng rủi ro ISS-02 sớm nhất có thể, và để lại một interface mà GĐ5 dùng lại được cho
> media tin nhắn mà không phải sửa gì.


| Mã     | Đầu việc                                                          | Mục tiêu — việc này tồn tại để làm gì                                                                                                                                                                              | Kết quả mong đợi — thứ kiểm chứng được                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                        |
| ------ | ----------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **C1** | `IObjectStorage` + `R2Options` ở `SharedKernel/Storage/`          | Cho hai module (Profile: avatar, Content: ảnh bài) và GĐ5 (media tin nhắn) **một** bề mặt dùng chung, hẹp đúng năm thao tác, để nếu có ngày bỏ SDK thì đó là thay một class chứ không phải viết lại luồng (Đ-2.14) | `SharedKernel/Storage/` có `IObjectStorage` (đúng **5** phương thức), `R2Options`, `AddSharedKernelR2`; `AWSSDK.S3` **ghim chính xác version** trong `SocialApp.SharedKernel.csproj`; **không kiểu nào của AWS SDK lọt ra bề mặt interface**; fail-fast ngoài Development khi thiếu `R2__Endpoint` \| `Bucket` \| `AccessKey` \| `SecretKey`, thông báo nêu **đúng tên biến**; test mới trong `StartupConfigurationTests` theo khuôn `Missing_email_config_must_fail_fast_outside_development`; **`ApiFactory` vẫn khởi động được khi không có khóa R2** (mất tính chất này là cổng hợp đồng và smoke đỏ trên CI) |
| **C2** | `R2ObjectStorage` + kiểm chứng presign `PUT` **bằng trình duyệt** | Đóng ISS-02 trong Ngày 6. Đây là đầu việc duy nhất có thể buộc cả nhóm đổi phương án — biết muộn một ngày là mất một ngày (B.9)                                                                                    | `R2ObjectStorage` dùng `AWSSDK.S3` trỏ endpoint R2, `ForcePathStyle = true`; presigned `PUT` hạn **10 phút**, `Content-Type` **và** `Content-Length` **nằm trong signed headers** (Đ-2.8 lớp 1); presigned `GET` hạn **15 phút** (Đ-2.9); key sinh đúng dạng `posts/{userId}/{uuid7}.{ext}` / `avatars/{userId}/{uuid7}.{ext}` (Đ-2.7); **một ảnh thật** `PUT` **được lên bucket** `-dev` **từ tab Network của trình duyệt**, có ảnh chụp Network + ảnh object trong bucket dán vào PR; unit test chuỗi ký / dạng key / allowlist / hạn — **không chạm mạng** |
| **C3** | Kiểm lúc commit: `HeadAsync` + đối chiếu khai báo                 | Dựng **lớp 2** của Đ-2.8 — lớp duy nhất nói được sự thật, vì nó đọc đúng thứ đang nằm trong bucket. Presigned `PUT` không tự giới hạn dung lượng, nên tin chữ ký là chưa đủ                                        | Một **hàm thuần** nhận `(khai báo, kết quả HEAD)` trả `Result`, unit test được **không cần mạng**; **ba nhánh, ba thông điệp, cùng mã 400**: object không tồn tại · lệch `sizeBytes` · lệch `contentType`; unit test đủ bốn nhánh (ba lỗi + một hợp lệ); tài liệu ghi rõ HEAD đứng **trước** transaction ở `D5`                                                                                                                                                                                                                                               |
| **C5** | `FakeObjectStorage` cho test                                      | Cho CI **không có khóa R2** vẫn chạy được integration test của `D3`/`D4`/`D5` và `B3` — thay vì viết test gọi R2 thật rồi `Skip`, mà test bị skip là test không tồn tại (Mục 10.2)                                 | In-memory, cài **cùng** `IObjectStorage`, cho test **dựng sẵn** kết quả HEAD (size/type/không tồn tại); đăng ký trong `ConfigureTestServices`, **không** `#if DEBUG` trong code sản phẩm, **không** nằm trong project sản phẩm; `grep -rn "Skip" tests/` không có dòng nào mới                                                                                                                                                                                                                                                                                |
| **C4** | Worker dọn rác + khóa Redis (Đ-2.13)                              | Object mồ côi và object của bài xóa mềm không nằm lại vĩnh viễn — và **khóa viết ngay ở GĐ2**, vì GĐ7 chạy 2 container api và lúc đó nó là lỗi chỉ xuất hiện trên production, không tái hiện được ở dev            | `IHostedService` trong `Content.Infrastructure`, đăng ký trong `AddContentModule`; công tắc `Media:Cleanup:Enabled` **mặc định tắt**; chu kỳ 1 giờ; `SET key val NX EX 3000` trên `RedisConnection` chung của SharedKernel; **hai nhánh**: object mồ côi > 24 giờ · object của bài `deleted_at` > 7 ngày; `ListAsync` phân trang bằng continuation token, **giới hạn 1000 object/lượt**; log số object đã xóa + số byte thu hồi; **Redis chết → bỏ lượt, không chạy**                                                                                         |




### 0.2 Khối B — Test và cổng CI

> **Mục tiêu khối:** biến mọi luật của Phần A thành thứ **chặn merge**, và giữ nguyên tinh thần GĐ1: khung
> không sửa, chỉ thêm dòng.


| Mã     | Đầu việc                                                               | Mục tiêu — việc này tồn tại để làm gì                                                                                                                                                                                 | Kết quả mong đợi — thứ kiểm chứng được                                                                                                                                                                                                                                                                                                                                                                          |
| ------ | ---------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **B1** | `SeededContentDatabaseAsync` trong `PostgresFixture`                   | Cho test đọc một database **đã migrate cả ba module** dùng chung, thay vì mỗi lớp tự dựng lại — và giữ nguyên luật chọn hàm của GĐ1 (test **sửa** dữ liệu dùng `CreateDatabaseAsync`, test chỉ **đọc** dùng bản seed) | Một phương thức mới trong `PostgresFixture.cs`, **không** sửa `PostgresCollection`; `AuthZMatrixTests` và `OwnershipTemplateTests` đổi **đúng một dòng** mỗi file, **cùng lúc**; **số đo thời gian trước/sau** dán vào PR; nhóm test Postgres còn dưới ~3 phút (vượt thì đã tách collection **trong chính** `B1`)                                                                                               |
| **B2** | Sáu dòng AuthZ matrix mới (Mục 6.3)                                    | Biến bảng phân quyền ở Mục 6.1 thành thứ **chặn merge**: mỗi endpoint chạm tài nguyên có chủ đều có một dòng chạy qua **đủ ba tầng** trên app thật + Postgres thật đã seed                                            | Sáu dòng trong `AuthZMatrix.cs` (`TC-A03`, `TC-A03-delete`, `TC-A03-media`, `TC-A01-posts`, `TC-A01-profile`, `READ-01`), mỗi dòng mang `AddedIn: "GĐ2"`; `--filter "Category=AuthZ"` chạy **19** dòng; `Ma_tran_khong_rong_va_ma_khong_trung` vẫn xanh; `ArrangePath` tạo bài của B **qua API thật** (có tạo hồ sơ trước, Đ-2.4), **không** `INSERT` thẳng DB                                                  |
| **B3** | Viết cho đỏ trước + bảng đột biến + bốn test BR-01                     | Chứng minh sáu dòng trên **thật sự bắt được lỗi**, không phải xanh vì đường đi tình cờ đúng. Đây là đầu việc rẻ nhất để bỏ và đắt nhất để bỏ                                                                          | **Link CI run đỏ** (hoặc output local) trong PR, chụp lúc dòng matrix đã có mà `D7`/`D8` chưa có; **bảng đột biến ≥ 6 dòng** đã thử tay, mỗi đột biến làm **đúng** dòng dự kiến đỏ rồi hoàn tác; bốn test BR-01 mức integration (`AC-02`, `AC-03`, `BR01-05`, `BR01-06`) xanh, mỗi test khẳng định **hơn** status code; `git status` sạch                                                                       |
| **B4** | Hai `ContractTests` mới + hai dòng trong csproj                        | Mở rộng cổng `API contract` sang hai module mới, để hợp đồng đổi mà code không đổi (hoặc ngược lại) là **CI đỏ**, không phải "lane FE phát hiện ở Ngày 9"                                                             | Hai lớp test mang `[Trait("Category","Contract")]` **trên từng lớp con**, mỗi lớp 2 `[Fact]` hai chiều; hai dòng `Content Include … Link="Contracts\…"` trong `SocialApp.IntegrationTests.csproj`; bước `API contract (CI GATE)` chạy **6** test thay vì 2, `TreatNoTestsAsError=true` không kêu; **đã thử cho đỏ**: thêm status code vào controller mà không sửa yaml → cổng đỏ, thông điệp chỉ đúng operation |
| **B5** | Cổng bundle trong `ci.yml` (cổng codegen đã xong ở `Q-B4`, 2026-09-19) | Đóng lỗ "xanh giả" mà rủi ro `R2-01` chỉ đích danh: khóa R2 và chữ ký presign lọt vào bundle trình duyệt thì không ai grep                                                                                            | Cổng bundle grep thêm `R2__` và `X-Amz-Signature`; **đã được thấy đỏ một lần** rồi khôi phục; `git status` sạch trước và sau (luật frontend Mục 9). Cổng codegen **không còn là việc của** `B5` — xem `Q-B4`                                                                                                                                                                                                    |




### 0.3 Thứ tự thực thi

> **Sửa 2026-09-19, trước khi bắt đầu thi công:** bản đầu ghi `C2 → C3 → C5` là phụ thuộc — **sai**. `C3` (hàm thuần
> đối chiếu) và `C5` (fake) chỉ cần *kiểu* `ObjectHead`/`ObjectPage` của `C1`, không cần hiện thực R2 của `C2`. Ghi
> như cũ là bắt người làm ngồi chờ bucket Cloudflare mới được viết `C3`/`C5`. Đồ thị dưới đây là bản đã sửa.

```
 Mục 9.0 (bucket + CORS + token + user-secrets) ─────────────┐   ← việc NGOÀI code, làm song song
                                                            ▼
                     ┌─→ C2 (R2 thật, trình duyệt) ───────→ D3, D4
                     │
 C1 (interface) ─────┼─→ C3 (hàm thuần) ─┐
                     ├─→ C5 (fake)       ├──────────────→ D5
                     └─→ C4 (worker) ────┘

 A3 + A5 (đã xong) ──→ B1 ─→ B2 (cố ý đỏ) ──→ [khối D: D5, D7, D8] ──→ B3 (đỏ → xanh + đột biến)
 cổng mở: 2 yaml ─┬──→ [khối D: D0] ─────────────────────────────────→ B4
                  └──→ gen:api tự sinh lib/api/profile|content (Q-B4, đã xong — không ai phải làm gì)
 (không phụ thuộc) ──→ B5 (chỉ còn cổng bundle)
```

Ba phụ thuộc **thật**, không phải sở thích sắp xếp:

- `C1` **→ mọi thứ còn lại của khối C.** Không có interface thì `R2ObjectStorage`, `MediaHeadPolicy`, `FakeObjectStorage`,
worker đều không có gì để bám. Và `C1` là chỗ chốt *bề mặt hẹp năm thao tác*; viết `C2` trước rồi rút interface ra sau
thì interface sẽ mang hình dạng của AWS SDK, đúng thứ Đ-2.14 sinh ra để tránh.
- **Mục 9.0 →** `C2`**, và chỉ** `C2`**.** `C2` là đầu việc **duy nhất** của hai khối chạm hạ tầng bên ngoài. Chưa có bucket +
khóa thì làm `C3`, `C5`, `C4` trước — không có lý do gì để chúng chờ. Riêng bước "`HeadAsync` trả `null` khi object
không tồn tại" (Mục 4, Bước 2) là hiện thực của `R2ObjectStorage`, nó đi cùng `C2`; hàm thuần và bốn unit test của
`C3` **không** chờ nó.
- `B1` **→** `B2`**.** Dòng `TC-A03` gọi `/api/v1/posts/{id}`; app trong `AuthZApiFactory` trỏ vào database do
`SeededIdentityDatabaseAsync("authz")` dựng, mà database đó **chỉ migrate module Identity**. Không có `B1` thì mọi
dòng mới trả **500** (thiếu bảng `content.posts`) chứ không phải 403 — đỏ vì lý do sai.

Ba chỗ **không** phải phụ thuộc, đừng xếp hàng cho "gọn":

- `C4` không chặn ai — nhưng xem Mục 0.6 trước khi quyết định trượt nó.
- `B4` và `B5` độc lập hoàn toàn với `B1`–`B3`. `B5` giờ **không phụ thuộc gì** — làm đầu tiên, để lưới `R2__`/
`X-Amz-Signature` có sẵn **trước** khi `C2` bắt đầu sinh URL có chữ ký.
- `B2` viết **trước** `D5`/`D7`/`D8` là **điều kiện lý tưởng**, không phải vật cản — đó chính là `B3`.



#### Đường đi khi **một người** làm cả hai khối (và cả khối D)

Bảng Ngày 6–9 ở Mục 0.5 và lịch trong `giai-doan-2.md` Mục 9 giả định **ba lane song song**. Một người thì lane
không tồn tại; thứ tự tuần tự dưới đây tôn trọng đúng ba phụ thuộc thật ở trên và **đẩy mọi việc bị chặn bởi bên
ngoài ra sau cùng**, để không ngồi chờ:


| #   | Việc                                                                                            | Cần trước                                      | Ghi chú                                                                                                                       |
| --- | ----------------------------------------------------------------------------------------------- | ---------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------- |
| 0   | **Chốt** `Q-C1`, `Q-C2`, `Q-B1`, `Q-B2`, `Q-B3` (Mục 1.3) và **ghi ngược** vào `giai-doan-2.md` | —                                              | Một người thì "nhóm chốt" = bạn chốt; vẫn phải ghi, vì đó là thứ người đọc sau lật lại                                        |
| 1   | `B5` — cổng bundle + thử cho đỏ                                                                 | `pnpm build` chạy được                         | 15 phút, không phụ thuộc gì                                                                                                   |
| 2   | `B1` — harness ba module, **đo thời gian trước/sau**                                            | `A3`+`A5` (đã xong)                            | Độc lập với khối C                                                                                                            |
| 3   | `C1` — interface + `R2Options` + fail-fast (theo `Q-C1`)                                        | —                                              | `AWSSDK.S3` ghim chính xác version                                                                                            |
| 4   | `C3` — `MediaHeadPolicy` thuần + 4 unit test                                                    | `C1`                                           | Không mạng, không fake                                                                                                        |
| 5   | `C5` — `FakeObjectStorage`                                                                      | `C1`                                           | Trong project test                                                                                                            |
| 6   | `C4` — worker + khóa Redis, mặc định tắt (`Q-C2`)                                               | `C1`, `A5`                                     | Không chặn ai, nhưng làm lúc còn "đang ở trong khối C" rẻ hơn quay lại sau                                                    |
| 7   | **Mục 9.0** trên Cloudflare + `dotnet user-secrets init/set`                                    | —                                              | Việc ngoài code; làm bất kỳ lúc nào **trước** #8. Chưa làm thì #3–#6 vẫn tiến được                                            |
| 8   | `C2` — `R2ObjectStorage` + **PUT thật từ trình duyệt**                                          | `C1`, #7                                       | Đây là ISS-02: làm **ngay khi #7 xong**, không để trôi                                                                        |
| 9   | Cổng mở phần còn lại: viết `profile-v1.yaml` + `content-v1.yaml` theo Mục 8                     | —                                              | `pnpm gen:api` tự sinh `lib/api/profile/` và `lib/api/content/` — **kiểm rằng không ai phải sửa** `package.json`**/**`ci.yml` |
| 10  | `B2` — sáu dòng matrix, **cố ý đỏ**, chụp run đỏ                                                | `B1`, và (nếu chốt `Q-B2`) sửa khung tối thiểu | Push và **chờ CI xong** trước khi push tiếp                                                                                   |
| 11  | **Khối D** (`D0` → `D9`)                                                                        | `C2`, `C3`, `C5`, #9, và #10 **trước `D5`**    | Hướng dẫn riêng: [huong-dan-khoi-d-endpoint.md](huong-dan-khoi-d-endpoint.md) (viết 2026-09-19) — chốt `Q-D2`–`Q-D9` ở Mục 1.4 trước khi gõ |
| 12  | `B4` — `ContractTestsBase` + hai lớp con + hai dòng csproj                                      | `D0` + #9                                      | Csproj **không** đẩy sớm được (Mục 10)                                                                                        |
| 13  | `B3` — 17 dòng xanh, bảng đột biến, bốn test BR-01                                              | `D5`, `D7`, `D8`                               | Kết thúc hai khối                                                                                                             |


**Trạng thái lúc sửa mục này (2026-09-19):** khối A xong (`26afb47`…`7a09549`); `Q-B4` xong (`485ffd2`); **chưa có**
hai file `.yaml` của cổng mở; **chưa** `dotnet user-secrets init` cho `SocialApp.Api` (tức Mục 9.0 bước 4 chưa làm trên
máy này); `AWSSDK.S3`, `SharedKernel/Storage/`, `SeededContentDatabaseAsync` chưa tồn tại. Nghĩa là bắt đầu được ngay
từ #0–#6 mà không chờ gì.

**Cập nhật cuối ngày 2026-09-19:** #0 (năm `Q-`* chốt theo đề xuất, đã ghi ngược) · #1 `B5` · #2 `B1` (1 m 35 s → 1 m 23 s,
dưới ngưỡng) · #3 `C1` · #4 `C3` · #5 `C5` · #6 `C4` · **#7** (bucket `socialmedia-dev`/`-staging`, CORS, token, `user-secrets`)
· **#8** `C2` (code + PUT thật từ trình duyệt — **ISS-02 đóng trên dev**) — **xong**. Unit 92 → 113, Integration 176 → 191,
Arch 11. **Khối C xong.** **#9 xong** cùng ngày: `profile-v1.yaml` (4 operation) + `content-v1.yaml` (6 operation) khớp Mục 8
từng status code, `pnpm gen:api` tự sinh `lib/api/profile/` và `lib/api/content/` **không sửa** `package.json`**/**`ci.yml` (Q-B4
đúng như hứa); Q-D1 chốt và ghi vào Mục 8.2. **Chưa có cổng máy nào canh hai file này cho tới** `B4`**.** Còn lại: #10 `B2` →
#11 khối D → #12 `B4` → #13 `B3`.

### 0.4 Hai khối phụ thuộc lane khác ở đâu — và cách không bị chặn


| Cần từ đâu                                                 | Ai làm                                             | Chưa có thì làm gì                                                                          |
| ---------------------------------------------------------- | -------------------------------------------------- | ------------------------------------------------------------------------------------------- |
| Hai bucket + CORS + hai token R2, khóa đặt đúng chỗ        | **Mục 9.0**, chủ dự án, xong **trước** sáng Ngày 6 | **Dừng.** `C2` không khởi động được, và ISS-02 vẫn đang mở dù code có xanh                  |
| `content.posts`, `media_attachments` migrate được          | `A5` (BE-1, trưa Ngày 7)                           | `C3` viết được ngay (nó là hàm thuần); `C4` chờ repository                                  |
| `POST /posts`, `PATCH`, `DELETE`                           | `D5`, `D7`, `D8`                                   | `B2` viết trước — đúng nếp                                                                  |
| `ProfileApiGroup` / `ContentApiGroup` + controller có thật | `D0`                                               | `B4` chờ; hai dòng csproj **không** đẩy sớm được (cạm bẫy ở Mục 10)                         |
| `profile-v1.yaml`, `content-v1.yaml`                       | **Cổng mở**, đã commit từ Ngày 6                   | Không có thì dừng — `B4` không có gì để so                                                  |
| `pnpm gen:api` ~~chạy cả ba module~~                       | **Xong 2026-09-19** (`Q-B4`)                       | `gen:api` tự suy module từ glob hợp đồng → `B5` **không chờ** `E1` nữa, chỉ còn cổng bundle |
| Nới `img-src`/`connect-src` cho host R2                    | `E7`, ghi thành `Đ-E17`                            | Không chặn `C2` (kiểm chứng `C2` làm trên trang tạm/DevTools, chưa qua CSP của app)         |




### 0.5 Ba mốc để đo tiến độ


| Mốc             | Xong cái gì                                                                          | Mở khóa gì                                                                                                            |
| --------------- | ------------------------------------------------------------------------------------ | --------------------------------------------------------------------------------------------------------------------- |
| **Cuối Ngày 6** | `C1` + `C2`, **có ảnh chụp Network** chứng minh `PUT` thật thành công từ trình duyệt | ISS-02 **đóng**. `D3`/`D4` viết được. Nếu mốc này trượt thì báo cả nhóm **ngay trong ngày**, đừng đợi standup hôm sau |
| **Cuối Ngày 7** | `C3` + `C5` + `C4`                                                                   | `D5` viết được và **test được trên CI không có khóa R2**                                                              |
| **Cuối Ngày 8** | `B1` + `B2` + `B3` có link run đỏ                                                    | `D7`/`D8` có lưới đỡ thật khi viết                                                                                    |
| **Trưa Ngày 9** | `B3` xanh + bảng đột biến + `B4` + `B5`                                              | Năm cổng `CI GATE` xanh và **từng cổng đã được thấy đỏ một lần**                                                      |




### 0.6 Phần cắt được nếu trễ


| Cắt được                                               | Cái giá phải chấp nhận                                                                | Cắt **không** được                                                                                                            |
| ------------------------------------------------------ | ------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------- |
| `C4` nhánh (1) — object mồ côi 24 giờ                  | Bucket tích rác cho tới GĐ7. Tiền lưu trữ R2 không đáng kể ở quy mô đồ án             | `C4` **khóa Redis** — nếu có worker thì phải có khóa. Worker không khóa chạy trên 2 instance ở GĐ7 là hai tiến trình cùng xóa |
| Ba dòng matrix "nhẹ": `TC-A01-posts`, `TC-A01-profile` | Endpoint quên `[Authorize]` không bị bắt riêng, nhưng vẫn bị `DEFAULT-DENY` bắt chung | `TC-A03`, `TC-A03-delete`, `TC-A03-media`, `READ-01` — đúng bốn chỗ IDOR của giai đoạn                                        |
| —                                                      |                                                                                       | `C1`, `C2`, `C3`, `C5` · `B3` bảng đột biến · `B4` · `B5`                                                                     |


Cắt thì **ghi rõ vào PR và vào** `giai-doan-2.md`, không lặng lẽ bỏ.

---



## 1. Trước khi gõ dòng đầu tiên



### 1.1 Năm điều kiện cần

```bash
# 1. Mục 9.0 ĐÃ XONG — kiểm bằng mắt, không tin lời:
#    hai bucket tồn tại · CORS đã đặt cho từng bucket · hai token phạm vi một bucket
#    và khóa dev nằm trong user-secrets, KHÔNG nằm trong deploy/.env
dotnet user-secrets list -p src/backend/SocialApp.Api        # phải thấy 4 key R2:*
grep -n "R2__" deploy/.env 2>/dev/null                        # KHÔNG được có giá trị dev ở đây

# 2. Docker daemon chạy được — Testcontainers cần nó cho B1, B2
docker info

# 3. Solution build sạch từ điểm xuất phát
dotnet build SocialApp.sln

# 4. Toàn bộ test hiện có XANH — đây là mốc so sánh. Đỏ sẵn từ trước thì đừng bắt đầu
dotnet test SocialApp.sln

# 5. GHI LẠI thời gian chạy nhóm test Postgres TRƯỚC khi đụng vào (B1 cần con số này)
dotnet test tests/SocialApp.IntegrationTests/SocialApp.IntegrationTests.csproj --filter "Category=AuthZ"
```

Điều kiện 5 không phải thủ tục: `PostgresFixture` đã ghi sẵn ngưỡng *"khi nhóm này vượt ~3 phút thì tách
thành 2–3 collection"*. `B1` thêm migration của **hai** module vào mỗi lần dựng database — đó chính là loại
thay đổi làm vượt ngưỡng. Không có số trước thì không biết đã vượt hay chưa.

### 1.2 Bảy luật áp thẳng vào hai khối


| #   | Luật                                                                                                                                                                                                    | Nguồn                     |
| --- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------- |
| 1   | **Không bao giờ log** `uploadUrl`**, không log presigned GET.** Chúng mang chữ ký; log chúng là để lại một URL ghi được vào bucket nằm trong hệ thống log                                               | B.9 điểm 4, `D4`          |
| 2   | **Khóa dev không bao giờ vào** `deploy/.env`**.** File đó mang giá trị **staging**. Dev dùng `user-secrets`; `DevEnvFile` **không** mở rộng cho `R2__`*                                                 | Đ-2.14                    |
| 3   | **Cấm test gọi R2 thật rồi** `Skip` **khi thiếu khóa.** Test bị skip trên CI là test không tồn tại, mà lại tạo cảm giác đã có lưới                                                                      | Mục 10.2                  |
| 4   | **Nghiệm thu upload phải trên trình duyệt, không phải** `curl`**.** `curl` không bao giờ thấy CORS                                                                                                      | Mục 10.2 mức 3, B.9       |
| 5   | **Chỉ thêm dòng vào** `AuthZMatrix.cs`**.** Phải sửa `AuthZMatrixTests`/`AuthZCase`/`AuthZApiFactory` mới thêm được một dòng thì **dừng lại** — Mục 6.3 nói rõ sửa khung một lần ở GĐ2 rẻ hơn sửa ở GĐ5 | Mục 6.3                   |
| 6   | **Không sửa kỳ vọng cho khớp kết quả.** Nhận 404 mà dòng ghi 403 thì câu hỏi là "vì sao ra 404". Mã kỳ vọng lấy từ Mục 6.1 và hợp đồng, **không** từ output                                             | GĐ1 khối B                |
| 7   | **Thêm một cổng CI thì phải thử cho đỏ một lần rồi khôi phục.** `git status` sạch trước và sau                                                                                                          | `frontend-rules.md` Mục 9 |




### 1.3 Sáu quyết định phải chốt **trước** khi gõ — không tự quyết một mình

Sáu chỗ dưới đây là chỗ B.4/B.5 nói ngắn hơn thực tế cần. Mỗi chỗ có đề xuất kèm lý do; chốt ở cổng mở hoặc
đầu buổi mất năm phút, phát hiện giữa chừng mất nửa ngày.

#### Q-C1 — App phải khởi động được **không có** khóa R2 ở Development ✅ **chốt 2026-09-19 theo đề xuất, đã ghi vào** `giai-doan-2.md`

**Vấn đề.** `ApiFactory` (dùng cho smoke test và **cổng hợp đồng API**) chạy ở `Development` và cố ý khai
chuỗi kết nối trỏ vào `127.0.0.1:1`, không khai gì về R2. Nếu `C1` làm fail-fast R2 ở **mọi** môi trường thì
mọi test dùng `ApiFactory` đỏ ngay — tức là cổng `API contract (CI GATE)` đỏ, vì lý do không liên quan gì tới
hợp đồng.

**Đề xuất (khuyến nghị).** Chép **nguyên** khuôn cấu hình email của GĐ1: fail-fast **chỉ ngoài Development**,
và `StartupConfigurationTests` đã có sẵn cặp test tương ứng (`Missing_email_config_must_fail_fast_outside_development`

- `Staging_boots_when_email_config_is_complete`) để chép. Ở Development thiếu khóa thì app **khởi động bình
thường**; lời gọi `IObjectStorage` đầu tiên mới ném, với thông điệp nêu đúng bốn tên biến và đúng lệnh
`dotnet user-secrets set` phải chạy.

Lý do không chọn "Development cũng fail-fast": nó biến việc chạy được test hợp đồng thành việc phải có khóa
R2 trên máy — tức là CI cũng phải có khóa, tức là đúng thứ Đ-2.14 và Mục 10.2 vừa loại bỏ.

**Khẳng định phải thêm vào** `StartupConfigurationTests`**:** `ApiFactory` khởi động được và `/health` xanh khi
**không** có biến `R2__`* nào. Đây là lưới duy nhất canh chuyện này; thiếu nó thì ai đó sẽ "siết cho chặt"
ở GĐ5 và làm đỏ cổng hợp đồng mà không hiểu vì sao.

#### Q-C2 — `Media:Cleanup:Enabled` mặc định bật hay tắt? ✅ **chốt 2026-09-19 theo đề xuất, đã ghi vào** `giai-doan-2.md`

**Vấn đề.** `C4` là `IHostedService` đăng ký trong `AddContentModule` — tức là nó chạy trong **mọi** host,
kể cả `WebApplicationFactory` của test. Một worker nền gọi `ListAsync`/`DeleteAsync` trong lúc integration
test đang chạy nghĩa là: gọi R2 **thật** từ CI (không có khóa → ném), hoặc xóa object trong lúc test khác
đang dùng — đỏ ngẫu nhiên, không tái hiện được.

**Đề xuất (khuyến nghị).** Mặc định **tắt** (`false`). Bật **tường minh** ở `deploy/.env` của staging bằng
`Media__Cleanup__Enabled=true`. Ba lý do, theo thứ tự: test không phải nhớ tắt nó; dev không vô tình xóa
object của người khác trong bucket `-dev` dùng chung; và "quên bật trên staging" là lỗi thấy được (bucket
tích rác), còn "quên tắt trên CI" là lỗi không thấy được (đỏ ngẫu nhiên).

B.5 chỉ nói *"có công tắc cấu hình để tắt"* mà không nói mặc định. Chốt mặc định là **tắt** và ghi vào B.5.

#### Q-B1 — `AuthZMatrixTests` có được sửa một dòng không? ✅ **chốt 2026-09-19 theo đề xuất, đã ghi vào** `giai-doan-2.md`

**Vấn đề.** `AuthZMatrixTests.InitializeAsync` gọi `postgres.SeededIdentityDatabaseAsync("authz")`. Sáu dòng
mới chạm `content.posts` và `profile.profiles`, nên database đó phải migrate cả ba module. Mà luật 5 ở Mục
1.2 nói "không sửa `AuthZMatrixTests`".

**Đề xuất (khuyến nghị).** `B1` thêm `SeededContentDatabaseAsync(key)` **bên cạnh** `SeededIdentityDatabaseAsync`
(giữ nguyên hàm cũ, đúng tên B.4 đã đặt), rồi đổi **đúng một dòng** ở **mỗi chỗ gọi**. Impact analysis
(`impact SeededIdentityDatabaseAsync --upstream`) cho **bốn** chỗ, không phải hai như bản phác: `AuthZMatrixTests`,
`OwnershipTemplateTests`, `JwtAuthenticationTests`, `RolePermissionSourceTests` — tất cả cùng `key = "authz"`. Và vì
cache `_shared` khóa theo `key`, đổi lẻ là đỏ ngẫu nhiên theo thứ tự xUnit; nên `B1` còn khóa cache theo `<hàm>:<key>`
để trộn hai hàm cùng key **không thể** thành một database. Lý do: luật "không sửa khung" nhắm vào **hình dạng** khung — `AuthZCase` có
những trường gì, `Ma_tran_phan_quyen` khẳng định những gì — chứ không nhắm vào việc khai app chạy trên
database nào. Đổi một dòng chỉ định nguồn dữ liệu không làm khung mất khả năng "thêm dòng là đủ" cho GĐ3–GĐ8.

**Phương án thay thế bị loại:** sửa thẳng `SeededIdentityDatabaseAsync` để nó migrate ba module. Không sửa
file test nào, nhưng tên hàm nói dối, và GĐ5 thêm module thứ tư sẽ lại phải sửa nó thêm lần nữa.

#### Q-B2 — `ArrangePath` không biết id người gọi, và `TC-A03-media` xanh vì lý do sai ✅ **chốt 2026-09-19 theo đề xuất, đã ghi vào** `giai-doan-2.md`

**Vấn đề — đọc kỹ, đây là chỗ dễ có lưới giả nhất của cả hai khối.** Trong `AuthZMatrixTests.Ma_tran_phan_quyen`,
thứ tự hiện tại là:

```csharp
var path = c.ArrangePath is null ? c.Path : await c.ArrangePath(new AuthZArrange(client, _db));
using var request = new HttpRequestMessage(c.Method, path);
if (TestJwt.ForCaller(c.Caller) is { } token) …        // ← token sinh SAU ArrangePath
```

`TestJwt.ForCaller(Caller.User)` gọi `Create("USER")` với `userId ?? Guid.NewGuid()` — id của người gọi A
được sinh **ngẫu nhiên sau khi** `ArrangePath` **đã chạy xong**. `ArrangePath` không có cách nào biết A là ai.

Hệ quả với `TC-A03-media` (A đăng bài gắn ảnh dưới tiền tố của B, kỳ vọng **403**): theo Mục 6.1, `POST /posts`
trả 403 cho **hai** lý do khác nhau — "chưa có hồ sơ" (Đ-2.4) và "khóa của người khác" (Đ-2.7). A vừa sinh ra
nên chắc chắn **chưa có hồ sơ**. Dòng sẽ xanh — vì lý do sai. Bỏ hẳn kiểm tiền tố khóa trong `D5` thì dòng
này **vẫn xanh**. Đó đúng là định nghĩa của lưới giả.

`TC-A03`, `TC-A03-delete` và `READ-01` không dính (mỗi cái chỉ có một lý do). Chỉ `TC-A03-media` dính.

**Đề xuất (khuyến nghị).** Đây chính là trường hợp Mục 6.3 đã chừa đường: *"Nếu phải sửa khung mới thêm được
dòng nào ở bảng trên thì dừng lại — … sửa khung một lần ở GĐ2 rẻ hơn nhiều so với sửa ở GĐ5."* Sửa khung
**một lần**, tối thiểu, ở hai chỗ:

```csharp
// AuthZCase.cs — thêm MỘT trường vào record Arrange, không đụng AuthZCase
public sealed record AuthZArrange(HttpClient Client, string PostgresConnectionString, Guid CallerUserId);

// AuthZMatrixTests.cs — sinh token TRƯỚC ArrangePath, truyền id vào
var callerId = Guid.NewGuid();
var token = TestJwt.ForCaller(c.Caller, callerId);          // ForCaller nhận thêm userId tùy chọn
var path = c.ArrangePath is null ? c.Path : await c.ArrangePath(new AuthZArrange(client, _db, callerId));
```

`TestJwt.ForCaller` nhận thêm `Guid? userId = null` và chuyển xuống `Create` — mọi lời gọi cũ không đổi. Sau
đó `TC-A03-media` tạo hồ sơ cho A trước, rồi mới gọi `POST /posts` với khóa của B, và 403 nhận được **chỉ
còn một lý do**.

**Phương án thay thế nếu nhóm không muốn chạm khung:** để `TC-A03-media` ở matrix như một lưới thô, và viết
**thêm** một test riêng trong `B3` (ngoài matrix, nơi tự dựng được cả hai người dùng) khẳng định đúng lý do
403. Rẻ hơn về rủi ro khung, đắt hơn về chỗ để quên. Chọn phương án nào cũng được, **không chọn phương án
thứ ba là bỏ qua**.

#### Q-B3 — Bốn test BR-01 ở Mục 10.1 thuộc khối B hay khối D? ✅ **chốt 2026-09-19 theo đề xuất, đã ghi vào** `giai-doan-2.md`

**Vấn đề.** B.2 ghi nội dung khối B là *"Matrix, BR-01, contract test, mở rộng cổng CI"*, nhưng hướng dẫn
khối A đã nhận unit test BR-01 dạng hàm thuần về cho `A4`. Vậy "BR-01" của khối B là gì?

**Đề xuất.** Khối B nhận bốn test **integration** đi qua `POST /posts` — `AC-02`, `AC-03`, `BR01-05`,
`BR01-06`. Lý do gom về khối B chứ không để ở `D5`: cả bốn đều là *khẳng định BR-01 vẫn còn nguyên*, không
phải *khẳng định endpoint chạy được* — và `BR01-05` còn canh một thứ `D5` không tự canh được: HEAD phải đứng
**trước** transaction (B.9, chỗ thứ 2 trong năm thứ chỉ code review bắt được).

Còn lại của Mục 10.1 (`PROF-*`, `AC-01`, `AC-04`, `POST-*`, `READ-*`, `PAGE-*`) đi cùng endpoint sinh ra
chúng, tức khối D — đúng nếp GĐ1.

#### Q-B4 — Cổng codegen: bỏ hẳn danh sách đường dẫn ✅ **ĐÃ CHỐT VÀ ĐÃ LÀM (2026-09-19, trước Ngày 6)**

> **Không còn là việc của** `B5`**.** Cả hai phần đã thi công xong **trước cổng mở**, đúng lúc rẻ nhất: hai script
> `gen:api:profile`/`gen:api:content` chưa kịp được viết nên không phải gỡ gì. `B5` giờ chỉ còn **cổng bundle**
> (Mục 11.2). Mục 11.1 giữ lại để giải thích cổng codegen đang hoạt động thế nào và vì sao.
>
> Đã làm: `src/frontend/scripts/gen-api.mjs` (mới) · `package.json` còn một dòng `gen:api` ·
> `ci.yml` bỏ danh sách đường dẫn · `frontend-rules.md` Mục 7 và `giai-doan-2.md` Mục 8.3 sửa theo.
> Phần dưới giữ nguyên để lưu **vì sao** — đây là loại quyết định người đọc sau sẽ muốn lật lại.

**Vấn đề — bệnh nặng hơn vẻ ngoài.** `git status --porcelain -- <đường dẫn không tồn tại>` trả **rỗng và
exit 0**, không một lời cảnh báo (đã thử: `git status --porcelain -- lib/api/khong-he-co.d.ts` → rỗng,
`exit=0`; `git diff --exit-code` cũng vậy). Nhưng gõ sai chỉ là **triệu chứng**. Bệnh là **cổng tự liệt kê
mục tiêu của chính nó**: có **ba** danh sách phải khớp nhau mà không cơ chế nào ép chúng khớp.


| #   | Danh sách                  | Ở đâu                              | Hỏng thì                                                                               |
| --- | -------------------------- | ---------------------------------- | -------------------------------------------------------------------------------------- |
| 1   | Hợp đồng nào tồn tại       | `Modules/*/Presentation/*-v1.yaml` | — (nguồn sự thật)                                                                      |
| 2   | Sinh file cho hợp đồng nào | `package.json` → `gen:api`         | Thiếu module → file sinh **không bao giờ tồn tại** → không bao giờ lệch → **xanh giả** |
| 3   | Kiểm file nào              | `ci.yml`, đường dẫn gõ tay         | Thiếu hoặc gõ sai → **xanh vĩnh viễn**                                                 |


Bước `test -f` chỉ đóng **hàng 3 với đúng trường hợp gõ sai**. Ba lỗ còn nguyên: thêm module 4 mà quên thêm
dòng CI · thêm `gen:api:social` mà quên nối vào `gen:api` tổng · quên cả hai.

Đây chính là lớp lỗi repo **đã** xử lý ở ba chỗ khác và viết thành nguyên tắc — `TreatNoTestsAsError=true` ở
cổng Contract/AuthZ · `WithoutRequiringPositiveResults()` và test canh gác của `A7` · `CLAUDE.md`: *"một số
không nghĩa là chưa thấy, không phải không ảnh hưởng"*. Cổng codegen là chỗ duy nhất chưa áp.

**Cách đã làm — hai phần, đóng hai hàng:**


| Phần                                                             | Đóng hàng       | Chạm gì                                                                                          |
| ---------------------------------------------------------------- | --------------- | ------------------------------------------------------------------------------------------------ |
| **(a) Cổng kiểm cả worktree**, bỏ hẳn danh sách khỏi `ci.yml`    | **3, triệt để** | `ci.yml`                                                                                         |
| **(b)** `gen:api` **suy từ glob hợp đồng**, bỏ script per-module | **2, triệt để** | `scripts/gen-api.mjs` (mới), `package.json`, `frontend-rules.md` Mục 7, `giai-doan-2.md` Mục 8.3 |


Sau hai phần này **không còn danh sách nào**: thêm module = thả file `.yaml` vào `Modules/<Module>/Presentation/`,
không sửa `package.json`, không sửa `ci.yml`.

**Vì sao làm trước Ngày 6 chứ không ở** `B5` **Ngày 9:** thời điểm quyết định cái giá. Trước cổng mở thì hai script
`gen:api:profile`/`gen:api:content` **chưa được viết**, nên (b) gần như miễn phí. Để tới `B5` Ngày 9 thì phải gỡ
thứ FE vừa dựng, dưới sức ép cổng đóng, trên file của lane khác — và nhiều khả năng bị gạt đi vì "không phải lúc",
mà đúng là không phải lúc thật.

**Hai chỗ script cố ý đỏ thay vì im lặng** (cùng luật `TreatNoTestsAsError`): glob không khớp hợp đồng nào · hai
hợp đồng cùng một đích. Chỗ thứ hai lộ ra lúc thử: một module **được phép** có hai nhóm Swagger (luật frontend Mục
7 ghi `<nhóm>.yaml`), và khi đó cả hai cùng đổ vào `lib/api/<module>/schema.d.ts` — cái sau ghi đè cái trước,
không ai biết. Giờ là đỏ, kèm câu chỉ thẳng chỗ sửa.

**Đã dời luôn Identity** (`lib/api/schema.d.ts` → `lib/api/identity/schema.d.ts`, cùng ngày): chỉ tốn một import
(`lib/api/types.ts`) và một type-test, vì mọi màn đều đi qua `types.ts`. Nhờ đó script **không còn ngoại lệ đường dẫn
nào** — đích suy thẳng từ tên nhóm Swagger, và GĐ1 khối E Mục 13 đã hẹn sẵn "module thứ hai mới tách".

> **Cả sáu quyết định trên:** chốt xong thì **ghi ngược vào** `giai-doan-2.md` **trong cùng commit** của đầu việc
> tương ứng, mở bằng "Lệch B.4 (nhóm chốt): …" / "Lệch B.5 (nhóm chốt): …" / "Lệch Mục 6.3 (nhóm chốt): …".
> Không sửa lặng trong code.

---



# Phần I — Khối C (Ngày 6–7)



## 2. C1 — `IObjectStorage` + `R2Options` ở `SharedKernel/Storage/`

**Mục tiêu.** Một bề mặt dùng chung, **hẹp đúng năm thao tác**, để hai module GĐ2 và GĐ5 không module nào gọi
AWS SDK trực tiếp — và để nếu có ngày bỏ SDK thì đó là thay một class, không phải viết lại luồng (Đ-2.14).

**Xong khi.** `SharedKernel/Storage/` có đủ ba file; `dotnet build` xanh; `StartupConfigurationTests` có hai
khẳng định mới (fail-fast ngoài Development · `ApiFactory` khởi động được **không** có khóa R2).

### Các bước

**Bước 1 — thêm gói, ghim chính xác version.** Trong `src/backend/SocialApp.SharedKernel/SocialApp.SharedKernel.csproj`:

```xml
<!-- Đ-2.14 (chốt 2026-09-18): dependency bên thứ ba thứ ba của SharedKernel, sau Redis và UUIDNext.
     R2 tương thích S3 một cách cố ý; không có gói Cloudflare.R2 cho .NET. KHÔNG có traffic nào đi qua
     AWS (endpoint là https://<account-id>.r2.cloudflarestorage.com), không cần tài khoản AWS.
     Chỉ presign + HEAD + Delete + List — KHÔNG TransferUtility. -->
<PackageReference Include="AWSSDK.S3" Version="<ghim chính xác>" />
```

**Bước 2 —** `R2Options`, chép hình dạng `JwtOptions` (hằng `Section`, `init`-only property, XML doc nói rõ
cái nào là bí mật và nằm ở đâu):

```csharp
public sealed class R2Options
{
    public const string Section = "R2";

    /// <summary>https://<account-id>.r2.cloudflarestorage.com — KHÔNG kèm tên bucket.</summary>
    public string Endpoint { get; init; } = "";
    public string Bucket { get; init; } = "";
    public string AccessKey { get; init; } = "";   // bí mật
    public string SecretKey { get; init; } = "";   // bí mật

    /// <summary>Hạn presigned PUT. HẰNG SỐ 10 phút (SEQ-01 bước 3) — không cấu hình được.</summary>
    public const int PutUrlMinutes = 10;

    /// <summary>Hạn presigned GET. HẰNG SỐ 15 phút (Đ-2.9) — KHÁC PutUrlMinutes một cách có chủ đích.</summary>
    public const int GetUrlMinutes = 15;
}
```

Hai hằng số là **hai số khác nhau** và phải giữ khác nhau: `PUT` ngắn vì URL ghi được vào bucket; `GET` dài
hơn vì một trang feed hiển thị trong nhiều phút. Ai đó "thống nhất cho gọn" là làm hỏng một trong hai.

**Bước 3 —** `IObjectStorage`**, đúng năm phương thức, không kiểu AWS nào trên bề mặt.**

```csharp
public interface IObjectStorage
{
    /// <summary>URL đã ký để client PUT thẳng. contentType + contentLength NẰM TRONG signed headers (Đ-2.8 lớp 1).</summary>
    string CreatePresignedPut(string key, string contentType, long contentLength);

    /// <summary>URL đã ký để đọc, hạn ngắn (Đ-2.9). Ký là HMAC cục bộ, KHÔNG gọi mạng.</summary>
    string CreatePresignedGet(string key);

    /// <summary>Đọc size + content type THẬT từ bucket. null = object không tồn tại (Đ-2.8 lớp 2).</summary>
    Task<ObjectHead?> HeadAsync(string key, CancellationToken ct = default);

    Task DeleteAsync(string key, CancellationToken ct = default);

    /// <summary>Liệt kê theo tiền tố, phân trang bằng continuation token (C4 cần).</summary>
    Task<ObjectPage> ListAsync(string prefix, string? continuationToken, int maxKeys, CancellationToken ct = default);
}

public sealed record ObjectHead(long ContentLength, string ContentType, DateTimeOffset LastModified);
public sealed record ObjectPage(IReadOnlyList<ObjectItem> Items, string? NextContinuationToken);
public sealed record ObjectItem(string Key, long Size, DateTimeOffset LastModified);
```

`ObjectHead`/`ObjectPage`/`ObjectItem` là **record của ta**, không phải `GetObjectMetadataResponse` của SDK.
Trả kiểu của SDK ra ngoài nghĩa là module Content phải `using Amazon.S3.Model` — lúc đó "bỏ SDK" không còn là
thay một class, và `C5` phải dựng được kiểu của SDK trong test.

**Bước 4 —** `AddSharedKernelR2`, chép hình dạng `AddSharedKernelRedis` (một điểm đăng ký duy nhất, singleton,
`TryAddSingleton`). Đăng ký ở `Program.cs` cạnh `AddSharedKernelRedis`, **không** đăng ký trong module nào.

**Bước 5 — fail-fast ngoài Development** theo `Q-C1`, chép nguyên khuôn cấu hình email trong `Program.cs`.
Thông báo phải nêu **đúng bốn tên biến** và **đúng chỗ sửa**:

```
Thiếu cấu hình R2. Đặt R2__Endpoint, R2__Bucket, R2__AccessKey, R2__SecretKey trong deploy/.env
trên server (staging), hoặc `dotnet user-secrets set "R2:Endpoint" … -p src/backend/SocialApp.Api` (dev).
```

**Bước 6 — hai khẳng định mới trong** `StartupConfigurationTests`**:**


| Test                                                   | Khẳng định                                                                       |
| ------------------------------------------------------ | -------------------------------------------------------------------------------- |
| `Missing_r2_config_must_fail_fast_outside_development` | `Staging` thiếu từng key trong bốn key → ném, thông điệp **có chứa tên biến đó** |
| `Development_boots_without_r2_config`                  | `new ApiFactory()` khởi động được và `/health` xanh khi **không** có biến R2 nào |




### Cạm bẫy đã biết

- **Fail-fast R2 ở cả Development** → mọi test dùng `ApiFactory` đỏ, kéo theo cổng `API contract (CI GATE)`
đỏ vì lý do không liên quan gì tới hợp đồng. Xem `Q-C1`.
- **Nhầm hai dạng tên biến.** `deploy/.env` và biến môi trường dùng **hai gạch dưới** (`R2__Endpoint`) vì đó
là quy ước phân cấp của .NET configuration; `user-secrets` và `appsettings` dùng **hai chấm** (`R2:Endpoint`).
Cùng một khóa, hai cách viết — gõ nhầm thì giá trị là chuỗi rỗng và triệu chứng là `SignatureDoesNotMatch`.
- **Mở rộng** `DevEnvFile` **cho** `R2__`* cho "tiện". Đ-2.14 cấm: `deploy/.env` mang giá trị **staging**, và lẫn
hai bộ khóa là dev ghi ảnh rác thẳng vào bucket staging mà không ai thấy.
- **Đặt** `Endpoint` **kèm tên bucket** (`…r2.cloudflarestorage.com/socialmedia-dev`). SDK sẽ ghép thêm bucket lần
nữa và mọi lời gọi 404. Endpoint là gốc tài khoản, bucket là tham số riêng.
- **Thêm phương thức thứ sáu** ("tiện tay thêm `CopyAsync`"). Bề mặt năm thao tác là con số đã chốt ở Đ-2.14;
thêm là quyết định mới.
- **Commit khóa thật vào** `.env.example`**.** File đó chỉ có bốn dòng **trống** `R2__…=`; giữ nguyên như vậy.
- **Để lại một hiện thực "ném lúc resolve" cho trường hợp cấu hình đủ** (kiểu "C2 sẽ nối sau"). Đã dính thật ngày
2026-09-19: worker `C4` là hosted service và **resolve** `IObjectStorage` **ngay lúc host khởi động**, nên
`Staging_boots_when_email_config_is_complete` đỏ ngay khi `C4` vào. Bất kỳ hiện thực nào đứng sau `AddSharedKernelR2`
đều phải dựng được **không gọi mạng** — `AmazonS3Client` thỏa (chỉ giữ cấu hình tới lời gọi đầu). Hệ quả: phần code của
`C2` phải đi **cùng hoặc trước** `C4`, không để trống.

---



## 3. C2 — `R2ObjectStorage` + kiểm chứng presign `PUT` bằng trình duyệt

**Mục tiêu.** Đóng ISS-02 **trong Ngày 6**. Đây là đầu việc rủi ro nhất của giai đoạn và là đầu việc duy nhất
có thể buộc cả nhóm đổi phương án (B.9).

**Xong khi.** (a) `R2ObjectStorage` hiện thực đủ năm phương thức; (b) unit test chữ ký/dạng key/allowlist/hạn
xanh, **không chạm mạng**; (c) **một ảnh thật đã** `PUT` **lên bucket** `-dev` **từ tab Network của trình duyệt**, có
ảnh chụp Network **và** ảnh object trong bucket dán vào PR.

> **Trạng thái 2026-09-19 —** `C2` **XONG, ISS-02 đóng trên dev.** (a), (b): `SharedKernel/Storage/R2ObjectStorage.cs`,
> `StorageKeys.cs`, 5 + 5 unit test. (c) nghiệm thu trình duyệt lúc 12:14, Edge 153, trang probe tĩnh phục vụ tại
> `http://localhost:3000` (server tĩnh trần, **không** qua Next dev vì CSP của `proxy.ts` chưa mở `connect-src` cho R2 —
> `E7`): preflight OPTIONS **204** (`Allow-Headers: content-type`, `Allow-Methods: PUT, GET`, `Max-Age: 3600`); PUT **200**,
> `Access-Control-Allow-Origin: http://localhost:3000`, `Access-Control-Expose-Headers: etag`, ETag đọc được từ JS; URL mang
> `X-Amz-SignedHeaders=content-length;content-type;host`, `X-Amz-Expires=600`. Lớp 1 trước đó: HEAD key không tồn tại →
> `null`, LIST `posts/` OK (chữ ký/endpoint/bucket/quyền đúng). Sau đó HEAD từ server xác nhận 136 byte `image/png`, rồi
> xóa object probe. Bằng chứng giữ dạng **chữ** ngay tại đây (header, status, thời điểm, ETag `91f5250bd015aeec6b2861c52acc4c9c`),
> không giữ ảnh chụp — người làm chốt vậy 2026-09-19; PR khối C trỏ vào đoạn này. `F3` chỉ còn là lần kiểm lại trên **staging**.



### Các bước

**Bước 1 — cấu hình client.** Bốn thiết lập, thiếu cái nào cũng hỏng theo cách khó đoán:

```csharp
var config = new AmazonS3Config
{
    ServiceURL = options.Endpoint,
    ForcePathStyle = true,          // R2 YÊU CẦU path-style; thiếu → SDK dựng URL virtual-host và R2 từ chối
    // R2 không có khái niệm region; SDK vẫn cần một giá trị để ký SigV4.
    AuthenticationRegion = "auto",
    // SDK v4 (4.0.103.3 đã ghim) mặc định WHEN_SUPPORTED: gửi thêm header checksum CRC mà R2 không hiểu, và lỗi trả về
    // KHÔNG nói gì về checksum. Phát hiện lúc thi công 2026-09-19; hướng dẫn gốc chỉ có ba thiết lập.
    RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
    ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
};
```

**Bước 2 — presigned** `PUT`**, ký kèm hai header (Đ-2.8 lớp 1).** Đây là dòng quyết định của cả đầu việc:

```csharp
var request = new GetPreSignedUrlRequest
{
    BucketName = _options.Bucket,
    Key = key,
    Verb = HttpVerb.PUT,
    Expires = DateTime.UtcNow.AddMinutes(R2Options.PutUrlMinutes),
    ContentType = contentType,
};
// Content-Length phải NẰM TRONG signed headers, không chỉ là gợi ý cho client:
// presigned PUT KHÔNG tự giới hạn dung lượng — ký cho 2MB rồi client PUT 400MB vẫn vào bucket
// nếu Content-Length không được ký (Đ-2.8, đoạn mở đầu).
request.Headers["Content-Length"] = contentLength.ToString(CultureInfo.InvariantCulture);
```

**Bước 3 — sinh key đúng dạng Đ-2.7**, dùng `Uuid7.New()` của SharedKernel (đã có từ GĐ1):

```
posts/{userId}/{uuid7}.{ext}
avatars/{userId}/{uuid7}.{ext}
```

`ext` suy từ `contentType` theo allowlist (`image/jpeg`→`jpg`, `image/png`→`png`, `image/webp`→`webp`),
**không** lấy từ tên file client gửi lên — tên file là dữ liệu client, và `.jpg.exe` là một trò cũ.

**Bước 4 — unit test, không chạm mạng.** Bốn nhóm, đúng "mức 1" của Mục 10.2:


| Nhóm           | Khẳng định                                                                               |
| -------------- | ---------------------------------------------------------------------------------------- |
| Dạng key       | Sinh ra khớp `^(posts\|avatars)/{guid}/{uuid}\\.(jpg\|png\|webp)$`; `userId` nằm đúng chỗ    |
| Allowlist      | `image/gif`, `application/pdf`, chuỗi rỗng → từ chối; ba loại hợp lệ → qua               |
| Hạn            | URL `PUT` mang `X-Amz-Expires=600`; URL `GET` mang `900`                                 |
| Signed headers | Chuỗi ký của `PUT` **có** `content-length` và `content-type` trong `X-Amz-SignedHeaders` |


Nhóm cuối là nhóm duy nhất thật sự canh Đ-2.8 lớp 1 — ba nhóm kia đẹp nhưng không bắt được lỗi đắt nhất.

**Bước 5 — nghiệm thu trên trình duyệt (KHÔNG bỏ qua, KHÔNG thay bằng** `curl`**).**

1. Chạy api dev với khóa bucket `-dev` từ `user-secrets`.
2. Lấy một `uploadUrl` (gọi tạm `IObjectStorage` bằng một endpoint tạm, hoặc in ra từ một test thủ công —
  **không commit endpoint tạm đó**).
3. Mở `http://localhost:3000` (phải đúng origin đã khai trong CORS của bucket `-dev`), mở DevTools, dán vào
  Console một đoạn `fetch(uploadUrl, { method: 'PUT', headers: {...requiredHeaders}, body: file })`.
4. **Chụp tab Network**: request `PUT` phải 200, và phải thấy response header CORS.
5. **Chụp bucket** trên dashboard Cloudflare: object có thật, đúng size, đúng content type.

Hai ảnh này là bằng chứng đóng ISS-02. Không có chúng thì `C2` chưa xong, dù test có xanh hết.

### Cạm bẫy đã biết

- **Nghiệm thu bằng** `curl` **rồi báo xong.** `curl` không gửi `Origin`, không thực thi preflight, **không bao
giờ thấy CORS**. Mục 10.2 xếp đây là "mức 3 — không tự động hóa được" có lý do.
- `403 SignatureDoesNotMatch` **trông y hệt lỗi CORS trên trình duyệt** (Đ-2.14). Phân biệt: mở tab Network,
xem **body** của response 403 — chữ ký sai thì R2 trả XML có `SignatureDoesNotMatch`; CORS sai thì request
bị chặn **trước khi gửi** và Console báo "blocked by CORS policy", Network không có response nào.
- **Thiếu** `ForcePathStyle = true` → SDK dựng `https://bucket.<account>.r2.cloudflarestorage.com/...` và R2
trả lỗi tên miền, triệu chứng lại giống hệt "endpoint sai".
- **Client** `PUT` **thiếu một header đã ký** → 403. Đây chính là lý do `D4` phải trả `requiredHeaders` kèm theo
(SEQ-01 bước 3); đừng để FE tự đoán.
- **Origin trong CORS có dấu** `/` **cuối hoặc có path.** Phải đúng `scheme://host[:port]`. Lệch một ký tự thì
trình duyệt chặn im lặng (cùng cạm bẫy với `Cors:AllowedOrigins` của GĐ1, Mục 9.0 bước 2).
- **Log** `uploadUrl` **để debug** rồi quên gỡ. Luật 1 ở Mục 1.2. Debug thì log **key**, không log URL.
- **Ký** `GET` **bằng cách gọi mạng.** Ký là HMAC cục bộ (Đ-2.9) — nếu hiện thực nào đó gọi API để lấy URL thì
một trang feed 20 bài thành 20 lời gọi mạng.

---



## 4. C3 — Kiểm lúc commit: `HeadAsync` + đối chiếu khai báo

**Mục tiêu.** Dựng **lớp 2** của Đ-2.8 — lớp không ai nghĩ tới và là lớp duy nhất nói được sự thật, vì nó đọc
đúng thứ đang nằm trong bucket.

**Xong khi.** Một hàm thuần `(khai báo, kết quả HEAD) → Result`, unit test đủ bốn nhánh, **không** test nào
chạm mạng.

### Các bước

**Bước 1 — tách hàm thuần ra khỏi lời gọi mạng.** Đây là toàn bộ giá trị thiết kế của `C3`:

```csharp
// Modules/Content/Domain/ (hoặc Application/) — hàm THUẦN, không async, không IObjectStorage
public static class MediaHeadPolicy
{
    /// <summary>
    /// Đ-2.8 lớp 2. Ba nhánh hỏng, ba thông điệp, CÙNG mã 400 (Mục 6.1: "400 HEAD lệch").
    /// Tách thuần để unit test không cần mạng và không cần Fake — chỉ cần hai record.
    /// </summary>
    public static Result Check(MediaDeclaration declared, ObjectHead? actual) => actual switch
    {
        null                                          => Result.Invalid("mediaKeys", "Ảnh chưa được tải lên xong."),
        { ContentLength: var n } when n != declared.SizeBytes
                                                      => Result.Invalid("mediaKeys", "Dung lượng ảnh không khớp khai báo."),
        { ContentType: var t } when !string.Equals(t, declared.ContentType, StringComparison.OrdinalIgnoreCase)
                                                      => Result.Invalid("mediaKeys", "Loại ảnh không khớp khai báo."),
        _                                             => Result.Success(),
    };
}
```

**Bước 2 — hiện thực** `HeadAsync` **trả** `null` **cho "không tồn tại", không ném.** AWS SDK ném
`AmazonS3Exception` với `StatusCode = NotFound` khi object không có — bắt **đúng** trường hợp đó và trả
`null`; mọi ngoại lệ khác **để nó ném ra** (mạng hỏng, khóa sai — đó là 500 chứ không phải 400, và trộn hai
thứ này là biến sự cố hạ tầng thành "ảnh của bạn không hợp lệ").

**Bước 3 — bốn unit test**, không cần mock `IObjectStorage`, chỉ cần hai record:


| Test           | Đầu vào                                 | Kỳ vọng                                      |
| -------------- | --------------------------------------- | -------------------------------------------- |
| Object chưa có | `actual = null`                         | `Invalid`, thông điệp về "chưa tải lên xong" |
| Lệch size      | khai 1 MB, HEAD trả 12 MB               | `Invalid`, thông điệp về dung lượng          |
| Lệch type      | khai `image/png`, HEAD trả `image/jpeg` | `Invalid`, thông điệp về loại                |
| Khớp           | khai = HEAD                             | `Success`                                    |


**Bước 4 — ghi bằng chữ vào chỗ** `D5` **sẽ gọi nó**, ngay trong XML doc: *"HEAD đứng TRƯỚC transaction. Đảo lại
thì có bài rồi mới phát hiện ảnh sai, và phải rollback thủ công."* Đây là một trong năm thứ B.9 nói **không
test tự động nào bắt được** — comment là lưới duy nhất.

### Cạm bẫy đã biết

- **Gộp kiểm tiền tố key vào** `C3`**.** Tiền tố `posts/{actorId}/` là Đ-2.7 và trả **403**, còn `C3` là Đ-2.8 và
trả **400**. Hai mã khác nhau, hai lý do khác nhau, và `TC-A03-media` canh cái thứ nhất.
- `Check` **nhận** `IObjectStorage` **để "tiện gọi luôn"** — mất ngay tính thuần, và unit test lại phải có Fake.
- **So** `ContentType` **phân biệt hoa thường.** R2 trả lại đúng chuỗi client đặt lúc `PUT`; `Image/JPEG` và
`image/jpeg` là cùng một loại theo RFC.
- **Coi** `ContentType` **từ HEAD là sự thật về nội dung file.** Không phải — nó là thứ client **khai lúc** `PUT`.
`C3` chỉ khẳng định "khai lúc presign" khớp "khai lúc PUT"; lớp thật sự chặn là allowlist ở `C2` cộng
`CHECK` ở DB (Đ-2.8 lớp 3). Đừng viết comment nói quá.
- **HEAD ≤ 10 object tuần tự hay song song?** Tuần tự là đủ (`POST /posts` không phải đường nóng, Đ-2.8 đã
ghi). Song song thì phải chú ý huỷ bỏ khi một cái hỏng; không đáng ở GĐ2.

---



## 5. C5 — `FakeObjectStorage` cho test

**Mục tiêu.** Cho CI **không có khóa R2** vẫn chạy được integration test của `D3`/`D4`/`D5` và `BR01-05` —
thay vì viết test gọi R2 thật rồi `Skip`.

**Xong khi.** Fake nằm trong project **test**, cài cùng `IObjectStorage`, cho test dựng sẵn kết quả HEAD; và
`grep -rn "Skip" tests/` không có dòng nào mới.

### Các bước

**Bước 1 — đặt đúng chỗ.** `tests/SocialApp.IntegrationTests/Harness/FakeObjectStorage.cs`, cạnh
`FakeSmtpServer` và `FakeRemoteIpStartupFilter` — đã có tiền lệ, đừng tạo thư mục mới.

**Bước 2 — bề mặt tối thiểu cho test dựng sẵn tình huống:**

```csharp
public sealed class FakeObjectStorage : IObjectStorage
{
    private readonly ConcurrentDictionary<string, ObjectHead> _objects = new();

    /// <summary>Test gọi để dựng "object đã tồn tại với size/type này". Đây là cả lý do lớp này tồn tại.</summary>
    public void Put(string key, long contentLength, string contentType) =>
        _objects[key] = new ObjectHead(contentLength, contentType, DateTimeOffset.UtcNow);

    public Task<ObjectHead?> HeadAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(_objects.TryGetValue(key, out var h) ? h : null);

    // URL trả về CỐ Ý không giống URL thật: nó không mang chữ ký nào. Test nào khẳng định điều gì về
    // chữ ký thì phải là unit test của C2, không phải test dùng lớp này.
    public string CreatePresignedPut(string key, string contentType, long contentLength) => $"https://fake.invalid/put/{key}";
    public string CreatePresignedGet(string key) => $"https://fake.invalid/get/{key}";
    …
}
```

**Bước 3 — đăng ký trong** `ConfigureTestServices`, không phải trong code sản phẩm:

```csharp
builder.ConfigureTestServices(services =>
{
    services.RemoveAll<IObjectStorage>();
    services.AddSingleton<IObjectStorage>(_ => fake);
});
```



### Cạm bẫy đã biết

- `#if DEBUG` **trong code sản phẩm** để "chuyển sang fake khi dev". B.5 cấm thẳng. Fake sống trong project
test, và chỉ ở đó.
- **Dùng Fake để nghiệm thu** `C2`**.** Không. `C2` nghiệm thu bằng R2 thật trên trình duyệt; Fake chỉ phục vụ
các test *khác* cần một `IObjectStorage` nào đó.
- **Fake trả URL trông giống thật** (`https://…r2.cloudflarestorage.com/…?X-Amz-Signature=…`). Sớm muộn ai đó
viết test khẳng định về URL đó và tưởng đã kiểm chữ ký. Dùng `fake.invalid` — tên miền `.invalid` được RFC
2606 dành riêng cho đúng việc này.
- **Quên** `RemoveAll<IObjectStorage>()` → hai đăng ký, DI lấy cái cuối, và thứ tự phụ thuộc vào thứ tự gọi.
Đỏ ngẫu nhiên.

---



## 6. C4 — Worker dọn rác + khóa Redis (Đ-2.13)

**Mục tiêu.** Object mồ côi và object của bài xóa mềm không nằm lại vĩnh viễn — và **khóa viết ngay ở GĐ2**,
vì ở GĐ7 (2 container api) nó là lỗi chỉ xuất hiện trên production và không tái hiện được ở dev một container.

**Xong khi.** `IHostedService` chạy được khi bật công tắc, bỏ lượt khi không lấy được khóa, và **không** chạy
trong test.

### Các bước

**Bước 1 — công tắc trước, logic sau** (theo `Q-C2`, mặc định **tắt**):

```csharp
// AddContentModule
services.AddHostedService<MediaCleanupWorker>();   // tự kiểm công tắc bên trong ExecuteAsync
```

Đăng ký **luôn**, kiểm công tắc **bên trong** — đăng ký có điều kiện thì cấu hình sai sẽ im lặng, còn kiểm
bên trong thì log được một dòng "cleanup đang tắt" lúc khởi động.

**Bước 2 — khóa Redis, dùng** `RedisConnection` **chung của SharedKernel** (đã có từ GĐ1, đừng mở kết nối mới):

```csharp
// Đ-2.13: GĐ7 chạy 2 container api → không khóa thì hai worker cùng quét cùng xóa.
// EX 3000 giây (50 phút) < chu kỳ 1 giờ: khóa phải hết hạn TRƯỚC lượt kế, nếu không một lần
// instance chết giữa chừng là khóa kẹt và không ai dọn nữa.
var ok = await db.StringSetAsync("lock:media-cleanup", instanceId, TimeSpan.FromSeconds(3000), When.NotExists);
if (!ok) return;   // instance khác đang chạy — BỎ lượt, không chờ
```

**Redis chết thì bỏ lượt, không chạy** (Mục 7.5). Chạy khi không có khóa là đúng thứ khóa sinh ra để ngăn.

**Bước 3 — nhánh (1): object mồ côi > 24 giờ — CHỈ dưới tiền tố** `posts/`**.**

> **Chốt lúc thi công (2026-09-19):** không quét cả bucket. Avatar **không** có dòng `media_attachments` (nó ở
> `profile.profiles.avatar_key`, schema Content không được đọc — Đ-2.2) nên luật "24 giờ + không có dòng" áp lên `avatars/`
> là xóa nhầm avatar đang dùng. Avatar mồ côi là việc của module Profile, hoãn có địa chỉ — đã ghi vào Mục 7.5.

```
ListAsync("posts/", continuationToken: null, maxKeys: 1000)
  → với mỗi key: LastModified cũ hơn 24 giờ?  và  KHÔNG có dòng media_attachments?  → DeleteAsync
  → giới hạn 1000 object/lượt để một bucket lớn không giữ khóa suốt cả tiếng (Mục 7.5)
```

Ngưỡng **24 giờ**, không phải 10 phút bằng hạn presign: người dùng chọn ảnh rồi đi ăn cơm, quay lại bấm đăng
vẫn phải được (Đ-2.13). Xóa theo hạn presign là xóa ảnh của bài đang soạn.

**Bước 4 — nhánh (2): bài xóa mềm > 7 ngày.** Repository của worker là **chỗ duy nhất** được
`IgnoreQueryFilters()` (Đ-2.10, `A5` đã đặt filter và ghi chú sẵn). Xóa object **rồi mới** xóa dòng
`media_attachments` — ngược lại thì một lần lỗi giữa chừng là mất dấu vết object và nó thành mồ côi vĩnh viễn
(nhánh (1) sẽ dọn, nhưng phải chờ tới lượt và phải quét cả bucket).

**Bước 5 — log số object đã xóa và số byte thu hồi.** Không log key của người dùng, không log URL.

### Cạm bẫy đã biết

- **Worker chạy trong integration test** → gọi R2 thật từ CI (không có khóa → ném), hoặc xóa object trong lúc
test khác đang dùng. Đây là lý do `Q-C2` chốt mặc định **tắt**.
- `EX` **dài hơn chu kỳ.** Khóa 1 giờ + chu kỳ 1 giờ = có lượt không bao giờ chạy được.
- **Không đặt** `When.NotExists` (`NX`) → `StringSetAsync` ghi đè và hai instance cùng tưởng mình có khóa.
- **Quên** `IgnoreQueryFilters()` ở repository của worker → truy vấn không bao giờ thấy bài xóa mềm, nhánh (2)
chạy mãi mà không dọn được gì, và không có lỗi nào báo.
- **Dùng** `IgnoreQueryFilters()` **ở chỗ khác.** Chỗ duy nhất là đây. Thấy nó xuất hiện trong `D6`/`D8` là code
review phải chặn.
- **Xóa object mà không kiểm lại** `media_attachments` **trong cùng lượt.** `media_attachments` là bảng đa hình
nên **không có FK tới** `posts` (Đ-2.12) — toàn vẹn do service giữ, và đó chính là lý do thứ hai worker này
tồn tại. Kiểm bằng dữ liệu cũ trong bộ nhớ là xóa nhầm ảnh của bài vừa đăng xong.
- **Quét cả bucket trong một lượt.** Giới hạn 1000 và dùng continuation token; lượt sau tiếp tục.
- **Quét cả** `avatars/`**.** Đây là lỗi mất dữ liệu thật, không phải lỗi hiệu năng: avatar đang dùng không có dòng
`media_attachments`. Tiền tố quét là hằng `MediaCleanupWorker.OrphanPrefix = "posts/"`; test C4 có một object
`avatars/…` cũ 30 ngày và khẳng định nó **còn nguyên**.
- **Chạy lượt đầu ngay lúc khởi động.** Hai instance deploy cùng lúc tranh khóa khi còn warm-up, và test dựng host với
`Enabled=true` bị lượt quét bất ngờ chen vào. Lượt đầu **sau** một chu kỳ.
- **Dùng** `ConnectedOrNull()` **để lấy khóa.** Nó không chờ — đúng lúc app vừa khởi động trả `null` giả và lượt bị bỏ oan.
Worker nền chờ được: `GetAsync()` rồi kiểm `IsConnected`; Redis chết thì task vẫn xong với multiplexer chưa kết nối
(`AbortOnConnectFail=false`) → bỏ lượt, không ném.

---



# Phần II — Khối B (Ngày 8–9)



## 7. B1 — `SeededContentDatabaseAsync` trong `PostgresFixture`

**Mục tiêu.** Có một database dùng chung đã migrate **cả ba module**, để test chỉ đọc không phải trả giá dựng
lại schema mỗi lớp — và để `B2` có chỗ đứng.

**Xong khi.** `SeededContentDatabaseAsync("authz")` trả về chuỗi kết nối tới database có ba schema; gọi hai
lần với cùng `key` trả cùng một chuỗi; thời gian chạy nhóm AuthZ đã đo lại và dán vào PR.

### Các bước

**Bước 1 — thêm phương thức, giữ nguyên hàm cũ.** Trong `tests/SocialApp.IntegrationTests/Harness/PostgresFixture.cs`:

```csharp
/// <summary>
/// Database đã migrate CẢ BA module + seed Identity, tạo MỘT lần cho mỗi <paramref name="key"/>.
/// Dành cho test chỉ ĐỌC dữ liệu nền và cần bảng của Profile/Content (AuthZ matrix từ GĐ2).
/// Test nào SỬA dữ liệu nền thì vẫn dùng CreateDatabaseAsync — luật chọn hàm của GĐ1 không đổi.
///
/// Thứ tự Identity → Profile → Content là CỐ Ý ghi ra dù không có phụ thuộc nào giữa chúng
/// (Đ-2.2: không FK qua ranh giới schema). Ghi ra để người đọc sau không tưởng thứ tự là ngẫu nhiên
/// rồi đảo nó khi thêm module thứ tư ở GĐ5.
/// </summary>
public Task<string> SeededContentDatabaseAsync(string key) =>
    _shared.GetOrAdd(key, _ => new Lazy<Task<string>>(async () =>
    {
        var cs = await CreateDatabaseAsync();
        await using var services = new ServiceCollection()
            .AddIdentityModule(cs)
            .AddProfileModule(cs)
            .AddContentModule(cs)
            .BuildServiceProvider();

        await services.MigrateIdentityModuleAsync();   // migrate → seed vai trò/quyền
        await services.MigrateProfileModuleAsync();
        await services.MigrateContentModuleAsync();
        return cs;
    })).Value;
```

**Bước 2 — đổi đúng một dòng ở cả BỐN chỗ gọi** (theo `Q-B1`; danh sách từ impact analysis, không từ trí nhớ):


| File                           | `SeededIdentityDatabaseAsync("authz")` → |
| ------------------------------ | ---------------------------------------- |
| `AuthZ/AuthZMatrixTests.cs`    | `SeededContentDatabaseAsync("authz")`    |
| `OwnershipTemplateTests.cs`    | `SeededContentDatabaseAsync("authz")`    |
| `JwtAuthenticationTests.cs`    | `SeededContentDatabaseAsync("authz")`    |
| `RolePermissionSourceTests.cs` | `SeededContentDatabaseAsync("authz")`    |


Bốn file **phải đổi cùng lúc**, và `PostgresFixture` khóa cache theo `"identity:" + key` / `"content:" + key` thay vì
`key` trần. Không khóa như vậy thì hai hàm cùng `"authz"` dùng chung một ô cache, database thật là của hàm nào chạy
**trước** — khác nhau giữa các lần chạy tùy thứ tự xUnit. Đây là loại đỏ ngẫu nhiên tốn cả buổi để tìm; khóa theo hàm
làm nó không thể xảy ra, và cái giá (thêm một lượt migrate) chỉ trả khi ai đó **thật sự** trộn hai hàm.

**Bước 3 — đo lại thời gian và ghi số.**

```bash
dotnet test tests/SocialApp.IntegrationTests/SocialApp.IntegrationTests.csproj --filter "Category=AuthZ"
```

So với số đã ghi ở Mục 1.1. Dán cả hai số vào PR (`trước: … s → sau: … s`). Vượt ~3 phút thì tách collection
**ngay trong** `B1`, đừng để lại cho khối F — ngưỡng có sẵn mà không ai đo là ngưỡng không tồn tại.

### Cạm bẫy đã biết

- **Giữ** `AddIdentityModule` **nhưng quên** `MigrateIdentityModuleAsync`**.** Seeder vai trò/quyền nằm trong hàm
migrate, không nằm trong hàm `Add`. Thiếu nó thì `RBAC-02b` đỏ, và triệu chứng trông hệt "handler hỏng".
- **Đổi tên** `SeededIdentityDatabaseAsync` **thay vì thêm hàm mới.** Sẽ đỏ ở chỗ khác và làm diff to gấp ba lần
cần thiết. B.4 đã đặt sẵn tên `SeededContentDatabaseAsync` — dùng đúng tên đó.
- **Dùng** `key` **khác nhau cho matrix và ownership template** để "cho chắc". Mỗi `key` là một database mới, tức
là thêm một lượt migrate ba module vào mỗi lần chạy CI. Dùng chung là có chủ đích.
- **Bọc migrate trong** `try/catch`**.** Không. Migration hỏng phải ném ra để test đỏ ngay.

---



## 8. B2 — Sáu dòng AuthZ matrix (Mục 6.3)

**Mục tiêu.** Mỗi endpoint chạm tài nguyên có chủ của GĐ2 có một dòng chạy qua **đủ ba tầng** trên app thật.

**Xong khi.** Sáu dòng có trong `AuthZMatrix.cs`, `Category=AuthZ` chạy 17 dòng (18 test — xem "Thực tế thi công"), và mỗi dòng đã được nhìn
thấy đỏ ít nhất một lần (đó là `B3`).

### Sáu dòng — chép nguyên kỳ vọng từ Mục 6.3, không lấy từ output


| Id               | Kịch bản                             | Người gọi          | Gọi gì                            | Kỳ vọng | `ArrangePath`?  |
| ---------------- | ------------------------------------ | ------------------ | --------------------------------- | ------- | --------------- |
| `TC-A03`         | A `PATCH` bài của B                  | `Caller.User`      | `PATCH /api/v1/posts/{id của B}`  | **403** | Có              |
| `TC-A03-delete`  | A `DELETE` bài của B                 | `Caller.User`      | `DELETE /api/v1/posts/{id của B}` | **403** | Có              |
| `TC-A03-media`   | A tạo bài gắn ảnh dưới tiền tố của B | `Caller.User`      | `POST /api/v1/posts`              | **403** | Có (xem `Q-B2`) |
| `TC-A01-posts`   | Đăng bài không kèm JWT               | `Caller.Anonymous` | `POST /api/v1/posts`              | **401** | Không           |
| `TC-A01-profile` | Sửa hồ sơ không kèm JWT              | `Caller.Anonymous` | `PUT /api/v1/users/me/profile`    | **401** | Không           |
| `READ-01`        | A đọc bài `private` của B            | `Caller.User`      | `GET /api/v1/posts/{id của B}`    | **404** | Có              |


`TC-A03` **trả 403 còn** `READ-01` **trả 404 là cố ý**, không phải mâu thuẫn — quy ước 3b của GĐ1, đã ghi ở Mục
6.1: thao tác **ghi** cần ownership trả 403; đọc nội dung có mức hiển thị trả 404. Ai thấy "lệch" và muốn
thống nhất về một mã thì đọc lại Mục 6.1 trước, đừng sửa bảng.

### Các bước

**Bước 1 — một hàm dựng bài của B, dùng lại cho ba dòng.** Đặt `private static` ngay trong `AuthZMatrix.cs`,
không tạo file mới (khung không có chỗ cho file thứ tư):

```csharp
/// <summary>
/// Dựng "bài của user B" QUA API THẬT (B.4: không INSERT thẳng DB — INSERT thẳng thì test không đi qua
/// đúng đường mà người dùng đi, và bỏ lọt mọi lỗi nằm ở tầng controller/service).
/// B là người dùng mới tinh mỗi lần gọi: hồ sơ trước (Đ-2.4), rồi mới đăng bài.
/// </summary>
private static async Task<Guid> TaoBaiCuaNguoiKhacAsync(AuthZArrange a, string privacy)
{
    var b = Guid.NewGuid();
    var tokenB = TestJwt.Create("USER", userId: b);
    … PUT /api/v1/users/me/profile   (displayName hợp lệ 2–50 ký tự sau Trim)
    … POST /api/v1/posts { body = "…", privacy }   → đọc postId từ PostResponse
}
```

Ba chỗ dễ sai, mỗi chỗ có địa chỉ trong tài liệu gốc:

1. **Phải tạo hồ sơ trước khi đăng bài.** Đ-2.4: `POST /posts` kiểm "có hồ sơ" ở tầng 3. Bỏ bước này thì B
  nhận 403 lúc dựng dữ liệu, `ArrangePath` ném, và dòng matrix đỏ với thông báo không liên quan gì tới
   ownership.
2. `privacy` **của** `READ-01` **là** `private`**; của** `TC-A03`**/**`TC-A03-delete` **là** `public`**.** Để `private` cho
  `TC-A03` thì vẫn ra 403 nhưng ta không còn biết vì ownership hay vì BR-02.
3. **Khẳng định** `ArrangePath` **thành công.** `POST /posts` trả khác 201 thì ném ngay với thông điệp nêu status
  - body; đừng trả `Guid.Empty` rồi để dòng matrix đỏ ở chỗ khác.

**Bước 2 — sáu dòng vào** `AuthZMatrix.Cases`, sau cụm GĐ1, mở bằng một dòng comment phân giai đoạn đúng nếp
file đang có:

```csharp
// --- GĐ2 (B2). Mục 6.3. Kỳ vọng viết tay theo Mục 6.1 + hợp đồng, không lấy từ output. ---
```

Mỗi dòng ghi `AddedIn: "GĐ2"` — trường này đã có sẵn trong `AuthZCase` và là thứ duy nhất cho biết dòng nào
thuộc giai đoạn nào khi bảng dài ra ở GĐ5.

**Bước 3 — chạy và đọc kỹ lý do đỏ.** Ở thời điểm này `D5`/`D7`/`D8` **chưa có**, nên đỏ là đúng — nhưng phải
đỏ **đúng lý do**: 404 (chưa có route) chứ không phải 500 (thiếu bảng). 500 nghĩa là `B1` chưa xong hoặc đổi
thiếu một trong hai dòng ở Mục 7 Bước 2.

### Cạm bẫy đã biết

- `Caller.User` **sinh id ngẫu nhiên mỗi lần gọi** — là *tính năng* cho `TC-A03`/`TC-A03-delete` (A khác B là
chắc chắn) và là *vấn đề* cho `TC-A03-media` (xem `Q-B2`). Đừng "sửa" bằng cách hard-code một Guid cho A:
hai dòng dùng chung dữ liệu.
- **Đặt kỳ vọng** `READ-01` **thành 403** vì "A không được xem". Sai — Mục 6.1 ghi 404, và lý do nằm ngay đó:
*"không tồn tại và không được thấy trả CÙNG một thứ"*. 403 ở đây tự nó tố cáo bài có tồn tại.
- **Thêm mã quyền mới cho** `post.`***.** Đ-2.6 nói GĐ2 **không** thêm mã quyền nào; sáu mã `post.`* đã có từ
seed GĐ1 (`PermissionCodes.cs`). Thấy thiếu quyền thì kiểm lại `B1` Bước 1.
- **Quên** `[ProducesResponseType]` **khiến matrix xanh nhưng cổng hợp đồng đỏ.** Hai cổng khác nhau; đừng sửa
cổng này bằng cách nới cổng kia.

### Thực tế thi công

**Bằng chứng.** `--filter "Category=AuthZ"`: **12 → 18** test (17 dòng matrix + `Ma_tran_khong_rong_va_ma_khong_trung`),
trong đó **4 đỏ có chủ đích** và không dòng nào đỏ vì lý do khác dự kiến. Integration tổng: 239 → 245, **240 xanh**.
Unit 155 và Architecture 13 không đổi. Ngoài 4 dòng này còn đúng một đỏ nền của máy dev không liên quan
(`StartupConfigurationTests.Development_boots_without_r2_config_…` — user-secrets local có khóa R2; CI không có nên xanh).

Trạng thái từng dòng mới, và **vì sao** — Bước 3 đòi đỏ phải là 404 (chưa có route), không phải 500 (thiếu bảng):

| Dòng | Bây giờ | Lý do |
|---|---|---|
| `TC-A03` | 🔴 | `ArrangePath` ném: `POST /api/v1/posts` → **404**, chưa có `D5` |
| `TC-A03-delete` | 🔴 | như trên |
| `READ-01` | 🔴 | như trên (`privacy=private`) |
| `TC-A03-media` | 🔴 | arrange xong (hồ sơ A tạo được — `D2` đã có), gọi thật nhận **404** thay vì 403 |
| `TC-A01-posts` | 🟢 | xanh sẵn nhờ fallback policy — xem ghi chú bên dưới |
| `TC-A01-profile` | 🟢 | `PUT /users/me/profile` đã có từ `D2`, 401 thật |

Không dòng nào ra 500 → `B1` đã đúng, database của matrix có đủ ba module.

**Chỗ lệch so với các bước trên — đã làm như sau:**

- **Lệch luật 5 (Mục 1.2) lần thứ hai, ngoài `Q-B2`: khung phải mang được BODY.** `Q-B2` đã mở khung một lần cho
  `CallerUserId`; nhưng `AuthZMatrixTests` dựng `new HttpRequestMessage(c.Method, path)` **không có content**, mà
  `POST /posts` và `PATCH /posts/{id}` đều khai `requestBody: required: true` trong `content-v1.yaml`. Request không
  body dừng ở model binding với **400** — trước cả tầng 3 — nên `TC-A03` và `TC-A03-media` **không bao giờ chạm tới thứ
  chúng định canh**, dù `D5`/`D7` có đúng hay sai. Đây đúng là trường hợp Mục 6.3 chừa đường (*"sửa khung một lần ở GĐ2
  rẻ hơn nhiều so với sửa ở GĐ5"*), và làm **cùng lúc** với sửa của `Q-B2` để khung chỉ mở một lần.
  Cụ thể: `AuthZCase` thêm `object? Body = null` (tham số cuối, có mặc định → 11 dòng GĐ1 không đổi một ký tự), khung
  thêm hai dòng `if (c.Body is not null) request.Content = JsonContent.Create(c.Body);`.
  Ba khẳng định của `Ma_tran_phan_quyen` (mã trả về, `problem+json` cho 401/403, không trùng Id) **không đổi** — hình
  dạng khung theo nghĩa luật 5 vẫn nguyên.
- **`TC-A01-posts` xanh ngay từ bây giờ, và điều đó KHÔNG phải lỗi** — nhưng cũng không phải thứ nó tưởng mình canh.
  `POST /api/v1/posts` chưa có route; request ẩn danh vẫn nhận **401** vì `FallbackPolicy` của `C4` áp cho **mọi**
  request mà middleware authorization nhìn thấy, kể cả request **không khớp endpoint nào** (chính lý do `UseSwagger`
  phải đứng trước `UseAuthorization` — xem comment trong `Program.cs`). Giá trị thật của dòng này là **đối chứng với
  `DEFAULT-DENY`**: nó giữ cho `POST /posts` không bao giờ trả về gì khác 401 khi không có token.
  **Hệ quả cho bảng đột biến của `B3` (Bước 2), ghi trước để người làm không mất buổi:** dòng *"Bỏ `[Authorize]` trên
  controller của `POST /posts` → `TC-A01-posts` đỏ"` **sẽ không đỏ** — bỏ `[Authorize]` thì action không còn
  `IAuthorizeData` nào, fallback policy nhảy vào và vẫn trả 401. Đột biến thật sự làm đỏ dòng đó là **`[AllowAnonymous]`
  trên action** (thứ duy nhất khiến fallback policy bị bỏ qua). Sửa bảng đột biến, đừng sửa dòng matrix.
- **`ArrangePath` của `TC-A03-media` không phụ thuộc dữ liệu nào nhưng vẫn có mặt.** Nó tồn tại chỉ để tạo **hồ sơ cho
  chính người gọi** — đúng nội dung `Q-B2`. Nhìn qua tưởng thừa (path là hằng `/api/v1/posts`); xóa đi thì dòng xanh vì
  lý do sai kể từ lúc `D5` xong.
- **Khóa ảnh của "người khác" là một `Guid` hằng, không phải `Guid.NewGuid()`.** Người gọi luôn là một Guid ngẫu nhiên
  mới nên hai id không thể trùng; hằng số đọc được ngay tại chỗ hơn một giá trị phải lần ngược mới biết là của ai.
- **Ba hàm phụ đặt ngay trong `AuthZMatrix.cs`** (`TaoBaiCuaNguoiKhacAsync`, `TaoHoSoAsync`, `NemNeuKhongPhaiAsync`),
  đúng Bước 1: khung không có chỗ cho file thứ tư.
- **Bước 2 nói mỗi dòng ghi `AddedIn: "GĐ2"`** — đã làm, nhưng bằng **tham số vị trí** thứ ba như 11 dòng GĐ1 đang viết,
  không phải tham số tên. Một file, một cách viết.
- **Con số "19 dòng" của bản nháp không khớp thực tế: đúng ra là 17 dòng (18 test).** 11 dòng GĐ1 + 6 dòng GĐ2 = 17;
  `Category=AuthZ` chạy 18 vì có thêm `Ma_tran_khong_rong_va_ma_khong_trung`. 19 là **ước lượng cũ**, không phải kết quả
  đo; đã sửa thành 17 ở cả sáu chỗ trong hai file hướng dẫn (Mục 0.3 #13, Mục 8, Mục 9 Bước 2 và cạm bẫy của nó,
  `huong-dan-khoi-d` Mục 0.2 #7 và Mục 0.3).

---



## 9. B3 — Viết cho đỏ trước, rồi mới có `D7`/`D8`

**Mục tiêu.** Có **bằng chứng** rằng sáu dòng của `B2` bắt được lỗi thật. Đây là đầu việc rẻ nhất để bỏ và
đắt nhất để bỏ.

**Xong khi.** (a) Link CI run đỏ / output local đã dán vào PR; (b) bảng đột biến ≥ 6 dòng đã thử xong; (c)
bốn test BR-01 integration xanh (theo `Q-B3`); (d) `git status` sạch.

### Bước 1 — chụp bằng chứng đỏ

1. Commit **chỉ** sáu dòng matrix (`B2`), push lên `loveart1210`.
2. **Chờ CI chạy xong** rồi mới push commit tiếp theo. `ci.yml` có `cancel-in-progress: true` — push `D7` sớm
  thì run đỏ bị hủy và mất bằng chứng.
3. Dán link run đỏ vào mô tả PR.

**Ngoại lệ:** nếu PR `loveart1210 → develop` đang được review và nhóm dựa vào trạng thái xanh của nhánh, giữ
commit đỏ ở local và chép output vào PR thay cho link.

### Bước 2 — bảng đột biến (làm sau khi `D5`/`D7`/`D8` xong và 17 dòng đã xanh)

Mỗi dòng thử bằng tay **một lần**: sửa tạm → chạy `--filter "Category=AuthZ"` → thấy **đúng** dòng dự kiến đỏ
→ hoàn tác. Ghi kết quả vào PR.


| Đột biến                                                                | Dòng phải đỏ                                 | Bắt loại hỏng nào                                                                             |
| ----------------------------------------------------------------------- | -------------------------------------------- | --------------------------------------------------------------------------------------------- |
| Bỏ `post.AuthorId != actorId` trong `PostService.UpdateAsync`           | `TC-A03`                                     | IDOR ở `PATCH`                                                                                |
| Bỏ cùng điều kiện đó trong `DeleteAsync`                                | `TC-A03-delete`                              | IDOR ở `DELETE` — hai hàm khác nhau, một dòng không canh được cả hai                          |
| Đổi `Result.Forbidden()` thành `Result.NotFound()` ở tầng 3 của `PATCH` | `TC-A03`                                     | Trộn quy ước 3b — 403 và 404 không thay nhau được                                             |
| Bỏ kiểm tiền tố `posts/{actorId}/` trong `D5`                           | `TC-A03-media`                               | Đ-2.7 — **vẫn xanh nghĩa là** `Q-B2` **chưa xử lý**, quay lại Mục 1.3                         |
| Bỏ `[Authorize]` trên controller của `POST /posts`                      | `TC-A01-posts`                               | Endpoint mới quên khai tầng 1                                                                 |
| Bỏ nhánh BR-02 cho `private` trong `GET /posts/{id}`                    | `READ-01`                                    | Rò rỉ nội dung riêng tư                                                                       |
| Đổi `AlwaysStrangers` trả `true`                                        | `READ-01` **không** đỏ; test pin của `A6` đỏ | Đối chứng có chủ đích: matrix không phải lưới vạn năng, và đó là lý do `A6` có test pin riêng |




### Bước 3 — bốn test BR-01 integration (theo `Q-B3`)


| Mã        | Kịch bản                       | Khẳng định **bắt buộc** (không chỉ status code)                                           |
| --------- | ------------------------------ | ----------------------------------------------------------------------------------------- |
| `AC-02`   | `POST /posts` body rỗng, 0 ảnh | 400 **và** `errors` có key `body` (luật FE Mục 6: FE hiện lỗi theo key)                   |
| `AC-03`   | `POST /posts` 11 ảnh           | 400 **và** `errors` có key `mediaKeys`                                                    |
| `BR01-05` | Khai 1MB, object thật 12MB     | 400 **và** đếm `content.posts` **không tăng** — khẳng định này quan trọng hơn status code |
| `BR01-06` | Bài chỉ có ảnh, không có chữ   | 201 — canh `ck_posts_not_empty` không chặn nhầm, và canh thứ tự INSERT                    |


`BR01-05` dùng `FakeObjectStorage` của `C5` để dựng object 12MB (`fake.Put(key, 12L * 1024 * 1024, "image/jpeg")`)
— **không** gọi R2 thật. Lớp test này **sửa** dữ liệu nên dùng `CreateDatabaseAsync`, không dùng bản seed dùng
chung (luật chọn hàm, `B1`).

### Cạm bẫy đã biết

- **Push commit tiếp theo trước khi run đỏ chạy xong** → run bị hủy, mất link bằng chứng.
- **Thử đột biến rồi quên hoàn tác.** Kiểm `git status` sau **mỗi** dòng, không kiểm một lần ở cuối.
- **Đột biến làm đỏ *nhiều* dòng hơn dự kiến.** Không phải "càng tốt" — nghĩa là hai dòng đang canh chung một
thứ và một trong hai không có giá trị riêng. Ghi vào PR.
- **Đột biến làm đỏ *sai* dòng.** Dừng lại. Dòng dự kiến không canh thứ ta tưởng nó canh.
- **Chạy bảng đột biến trước khi 17 dòng xanh.** Đỏ sẵn thì không phân biệt được đỏ do đột biến hay do chưa xong.

---



## 10. B4 — Hai `ContractTests` mới + hai dòng trong csproj

**Mục tiêu.** Cổng `API contract (CI GATE)` canh **cả ba** module, hai chiều: code không lộ thứ hợp đồng chưa
ghi, và hợp đồng không có thứ code chưa hiện thực.

**Xong khi.** `--filter "Category=Contract"` chạy 6 test và xanh; đã thử cho đỏ một lần.

### Quyết định trước khi gõ: chép ba bản hay tách lớp cơ sở?

B.4 viết *"chép* `IdentityContractTests` *cho từng module (nó đã được viết theo tham số hóa được:* `BasePath`*,*
`ContractPath`*, tên nhóm)"*. Đọc file thật thì chính xác hơn là: **ba giá trị đó đã được cô lập thành hằng
số**, nhưng lớp là `sealed` và các hàm là `private static` — nó *tham số hóa được*, chưa *đã tham số hóa*.

**Đề xuất (khuyến nghị): tách** `ContractTestsBase` **abstract.** Toàn bộ logic so sánh (~150 dòng: `Operations`,
`StatusCodes`, `RequiredRequestFields`, `Resolve`, hai `[Fact]`) chuyển lên lớp cơ sở; mỗi module còn lại một
lớp con ba dòng:

```csharp
[Trait("Category", "Contract")]
public sealed class ContentContractTests(ApiFactory factory) : ContractTestsBase(factory), IClassFixture<ApiFactory>
{
    protected override string ContractFileName => "content-v1.yaml";
    protected override string SwaggerGroupName => ContentApiGroup.Name;
}
```

Lý do: chép ba bản nghĩa là **ba chỗ phải sửa** khi logic so sánh đổi (và nó *sẽ* đổi — GĐ5 thêm module thứ
tư, `CrossCuttingStatusCodes` sẽ phải nhận thêm mã). Hai bản lệch nhau âm thầm là cách cổng hợp đồng chết mà
không ai báo tang.

Ba điều **bắt buộc** khi tách lớp cơ sở:

1. `[Trait("Category","Contract")]` đặt trên **từng lớp con**, không chỉ trên lớp cơ sở. Bước CI lọc theo
  trait, và một trait không được phát hiện nghĩa là cổng chạy thiếu test mà vẫn xanh.
2. `IClassFixture<ApiFactory>` cũng khai ở lớp con (xUnit tạo fixture theo lớp cụ thể).
3. Giữ nguyên `CrossCuttingStatusCodes = [429, 500]` **và lý do** trong XML doc — 429 từ rate limiter, 500 từ
  `GlobalExceptionHandler`, không action nào khai `[ProducesResponseType]` cho chúng.



### Các bước

**Bước 1 — hai dòng** `Content Include` trong `tests/SocialApp.IntegrationTests/SocialApp.IntegrationTests.csproj`,
cùng `ItemGroup` với dòng của Identity:

```xml
<Content Include="..\..\src\backend\Modules\Profile\Presentation\profile-v1.yaml"
         Link="Contracts\profile-v1.yaml"
         CopyToOutputDirectory="PreserveNewest" />
<Content Include="..\..\src\backend\Modules\Content\Presentation\content-v1.yaml"
         Link="Contracts\content-v1.yaml"
         CopyToOutputDirectory="PreserveNewest" />
```

Comment đang có trong `ItemGroup` đó đã nói sẵn vì sao hợp đồng link vào output của **test** chứ không của
**Api** (*"hợp đồng không có việc gì trong image chạy production"*) và đã chừa chỗ: *"Thêm một dòng ở đây cho
mỗi module từ GĐ2"*. Không viết lại comment, chỉ thêm dòng.

**Bước 2 —** `ContractTestsBase` **+ hai lớp con** theo quyết định ở trên.

**Bước 3 — chạy đúng lệnh CI dùng**, không chạy `dotnet test` trần:

```bash
dotnet test tests/SocialApp.IntegrationTests/SocialApp.IntegrationTests.csproj \
  --filter "Category=Contract" -- RunConfiguration.TreatNoTestsAsError=true
```

**Bước 4 — thử cho đỏ một lần.** Thêm `[ProducesResponseType(StatusCodes.Status418ImATeapot)]` vào một action
của controller Content mà **không** sửa `content-v1.yaml` →
`Runtime_must_not_expose_anything_outside_the_contract` phải đỏ với thông điệp chỉ đúng operation. Hoàn tác,
kiểm `git status` sạch.

### Cạm bẫy đã biết

- **Đẩy hai dòng csproj lên trước khi file** `.yaml` **tồn tại** → MSBuild lỗi lúc copy, build đỏ cho cả solution
và mọi lane. `Content Include` với đường dẫn tường minh (không wildcard) **không** im lặng bỏ qua file
thiếu. Hai file yaml là sản phẩm của **cổng mở** (Ngày 6) — kiểm trước khi gõ.
- **Tên nhóm Swagger lệch một trong ba chỗ** (`[ApiExplorerSettings]` · `SwaggerDoc` ở `Program.cs` · tên file
yaml). Lệch thì `ReadRuntimeSwaggerAsync` nhận 404 và test đỏ với "không parse được JSON" — triệu chứng
không nói gì về nguyên nhân. Nghi thì `curl` thẳng `/swagger/content-v1/swagger.json`.
- **Đặt** `BasePath` **khác** `/api/v1` **cho module mới.** Cả ba module dùng chung base; hợp đồng ghi path tương đối,
Swagger runtime ghi tuyệt đối, và `Normalize` cắt base khỏi vế runtime.
- **Đưa** `description`**/**`example` **vào diện so sánh** vì "cho chặt". `IdentityContractTests` đã ghi lý do cố ý
không so: *"sửa một chữ mô tả mà đỏ test thì cả nhóm sẽ học cách phớt lờ nó"*.
- **Contract test chạm Postgres hoặc chạm R2.** `ApiFactory` cố ý trỏ vào `127.0.0.1:1` và cố ý **không** có
khóa R2 (`Q-C1`). Controller mới mà chạm DB/R2 lúc khởi động thì cổng hợp đồng đỏ — sửa controller, đừng
sửa `ApiFactory`.

---



## 11. B5 — Mở rộng cổng CI trong `ci.yml` và thử cho đỏ

**Mục tiêu.** Đóng lỗ xanh-giả mà rủi ro `R2-01` chỉ đích danh (cổng bundle). Lỗ thứ hai — cổng codegen của
Mục 10.4 điểm 4 — đã đóng ở `Q-B4` ngày 2026-09-19; Mục 11.1 giữ lại để giải thích nó đang chạy thế nào.

**Xong khi.** Hai bước trong `.github/workflows/ci.yml` đã sửa, mỗi bước đã được **thấy đỏ một lần** rồi khôi
phục, `git status` sạch trước và sau.

### 11.1 Cổng codegen — bỏ hẳn danh sách đường dẫn

Nền của mục này là `Q-B4` ở Mục 1.3: đừng vá chỗ gõ sai, **bỏ hẳn cái danh sách sinh ra chỗ gõ sai**.

Comment trong `ci.yml` đã ghi đúng một cái bẫy — dùng `git status --porcelain` chứ không
`git diff --exit-code`, vì `git diff` im lặng với file chưa commit. Cái bẫy thứ hai nằm ngay cạnh đó:
`git status --porcelain -- <đường dẫn không tồn tại>` **cũng** trả rỗng và **exit 0**. Gõ sai một ký tự là
cổng xanh vĩnh viễn cho file đó — đúng *"cổng CI mới xanh giả với 0 test"* ở bảng B.9.

**Phần (a) — đã làm.** Cổng không liệt kê file nữa; nó hỏi git một câu duy nhất:

```yaml
- name: API types khop hop dong (CI GATE)
  run: |
    pnpm gen:api
    # KHONG liet ke duong dan: `git status --porcelain -- <path khong ton tai>` tra RONG va exit 0
    # (da thu), nen MOI danh sach go tay deu co the xanh gia -- va con phai bao tri moi lan them
    # module. Hoi git "sau khi sinh lai, worktree co sach khong", khong hoi "ba file nay co doi khong".
    changed=$(git status --porcelain -- .)
    if [ -n "$changed" ]; then
      echo "::error::File sinh lech hop dong hoac chua duoc commit -- chay 'pnpm gen:api' roi commit"
      echo "$changed"
      git diff
      exit 1
    fi
```

Từ đây cổng **không cần sửa nữa** khi GĐ3–GĐ8 thêm module: file sinh mới mà chưa commit hiện thành `??`, file
sinh lệch hợp đồng hiện thành  `M`, cả hai đều đỏ.

Giữ nguyên **tên bước** `API types khop hop dong (CI GATE)` — tên bước là thứ người đọc log tìm, và đổi tên
làm mọi tham chiếu trong tài liệu GĐ1 chết.

**Phần (b) — đã làm.** Phần (a) đóng hàng 3 của bảng trong `Q-B4`
nhưng **không** đóng hàng 2: `gen:api` quên một module thì file sinh không bao giờ tồn tại, không bao giờ
lệch, và worktree vẫn sạch. Đóng nốt bằng cách cho `gen:api` **suy ra** hợp đồng từ glob — code thật ở
`[src/frontend/scripts/gen-api.mjs](../../src/frontend/scripts/gen-api.mjs)` (không chép lại vào đây: bản chép sớm muộn
lệch bản thật, đúng cái bệnh mục này đang chữa). Bốn điểm thiết kế cần biết khi đọc nó:

- **Đích suy từ tên nhóm Swagger** (tên file bỏ hậu tố `-v1`), không từ tên thư mục module. Tên nhóm đã buộc phải duy
nhất toàn app (`SwaggerDoc` trong `Program.cs`), nên không có ngoại lệ nào và không có lớp va chạm "một module hai
nhóm". Với mọi module đã lên kế hoạch, tên nhóm trùng tên module.
- `planJobs()` **là hàm thuần**, tách khỏi lời gọi `openapi-typescript`, có unit test ở `scripts/gen-api.test.ts` —
đường `lib/api/<nhóm>/` với **nhiều** hợp đồng được khẳng định mỗi lần `pnpm test`, không chỉ một lần thử tay.
- **Hai chỗ cố ý ném thay vì im lặng**, kiểm **trước** khi sinh file nào: glob không khớp gì · hai file cùng tên nhóm.
- Gọi bin bằng `process.execPath`, không `pnpm exec`: trên Windows `pnpm` là shim `.cmd`, `execFileSync` không shell sẽ
ENOENT.

`package.json` giờ còn **một** dòng — `"gen:api": "node scripts/gen-api.mjs"` — và **không** có
`gen:api:profile`/`gen:api:content`. Đây là chỗ **lệch** `[frontend-rules.md](../../.claude/rules/frontend-rules.md)`
**Mục 7** (*"Module mới (GĐ2+): thêm script* `gen:api:<module>`*"*), nên Mục 7 **đã được sửa trong cùng commit**,
cùng với `giai-doan-2.md` Mục 8.3 — không sửa lặng.

**Đã nghiệm thu (2026-09-19, Windows, Node 24.15.0, pnpm 10.33.1, openapi-typescript 7.13.0):**


| Thử                                                                                            | Kết quả                                                                                                                                                                                                             |
| ---------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `pnpm gen:api` sau khi dời Identity                                                            | `lib/api/identity/schema.d.ts` **y hệt** bản đã commit — `git status` chỉ thấy `R` (đổi tên), không `M`                                                                                                             |
| Happy path **hai** hợp đồng thật (`profile-v1.yaml` tạm trong `Modules/Profile/Presentation/`) | `lib/api/profile/schema.d.ts` được tạo (có `mkdir`), chứa `ProfileResponse`; prettier và ESLint **bỏ qua** nó nhờ glob `lib/api/**/schema.d.ts`; cổng thấy `?? lib/api/profile/` — đúng thứ phải đỏ khi quên commit |
| Unit test `planJobs` (`scripts/gen-api.test.ts`, 4 test)                                       | Ba module → ba đích đúng thứ tự; đường dẫn dấu gạch chéo ngược của Windows; rỗng → ném; `content-v1` cạnh `content-v2` → ném nêu cả hai nguồn                                                                       |
| Glob không khớp hợp đồng nào                                                                   | exit 1, thông điệp chỉ thẳng `scripts/gen-api.mjs`                                                                                                                                                                  |
| Hợp đồng đổi kiểu (thêm field vào `MeResponse`) mà quên `gen:api`                              | Cổng **đỏ**: `M lib/api/identity/schema.d.ts`                                                                                                                                                                       |
| Hợp đồng đổi chỗ **không** chạm kiểu (đổi `info.title`)                                        | Cổng **xanh** — đúng ý: `openapi-typescript` không đưa title vào file sinh, nên không có gì lệch để bắt                                                                                                             |
| `pnpm lint` · `typecheck` · `test`                                                             | Xanh cả ba; 25 file test, **245 test**                                                                                                                                                                              |


**Bốn điểm còn lại phải biết:**

- Phạm vi cổng là `git status --porcelain -- .` = `src/frontend` (theo `working-directory`). Thư mục `.` **không
gõ sai được** nên không tái sinh lỗ đường dẫn, và `gen-api.mjs` chỉ ghi vào `lib/api/` nên không mất gì so với soi
cả repo — trong khi soi cả repo thì một bước khác làm bẩn file ngoài FE cũng đỏ ở đây với thông điệp sai chỗ.
**Không** quay về liệt kê từng file.
- Cổng không tự bảo vệ được trước việc **chính nó** bị sửa thành no-op. Lưới cuối là định nghĩa cổng nằm
trong diff của PR — đó là việc của người review, không phải của CI.
- Đổi version `openapi-typescript` là **đổi file sinh ra**, nên cũng đỏ ở cổng này. Đúng ý (luật frontend
Mục 7), không phải lỗi.
- `gen:api` **tự** sinh cho cả ba module ngay khi hai file `.yaml` được commit ở cổng mở — `E1` không phải thêm script nào, và `B5` không phải chờ `E1` nữa (đây là thay đổi so với bảng phụ thuộc ở Mục 0.4).



### 11.2 Cổng bundle — thêm `R2__` và `X-Amz-Signature`

```yaml
hits=$(grep -rlE "mockServiceWorker|setupWorker|localhost:5259|ioredis|SESSION_ENCRYPTION_KEY|API_INTERNAL_URL|bff:session|R2__|X-Amz-Signature" .next/static || true)
```

Hai chuỗi mới bắt hai thứ khác nhau:

- `R2__` — tiền tố biến cấu hình R2. Lọt vào `.next/static` nghĩa là ai đó đặt tên biến có `NEXT_PUBLIC_`,
hoặc import một module server vào code trình duyệt. Đây là rủi ro `R2-01`.
- `X-Amz-Signature` — chữ ký của presigned URL. Trình duyệt **có** thấy chuỗi này lúc chạy (nó nằm trong
`uploadUrl` mà API trả về), nhưng nó **không được** nằm trong bundle: nằm trong bundle nghĩa là có chữ ký
hard-code, hoặc có code tự ký ở phía client — cả hai đều là thứ Đ-2.5 và Đ-2.8 cấm.

> Nếu cổng đỏ vì FE nhắc tên header một cách chính đáng (ví dụ log lỗi upload), **đừng nới cổng** — đổi code
> FE để không nhắc tên header trong bundle. Nới grep là bỏ luôn lưới.



### 11.3 Thử cho đỏ — ba lần


| Cổng                                      | Cách làm đỏ                                                                                                                                       | Khôi phục                           |
| ----------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------- |
| Codegen (lệch)                            | Sửa một chữ trong `content-v1.yaml` rồi push **không** chạy `pnpm gen:api`                                                                        | `git checkout -- …/content-v1.yaml` |
| Codegen (chưa commit)                     | Xóa `lib/api/content/schema.d.ts` khỏi index (`git rm --cached`) → phải đỏ với `??`, **không** xanh                                               | `git add` lại                       |
| Codegen (glob rỗng, nếu đã chốt phần (b)) | Đổi `PATTERN` trong `gen-api.mjs` thành đường dẫn không khớp gì → phải đỏ với "Không thấy hợp đồng nào", **không** sinh 0 file rồi báo thành công | Sửa lại `PATTERN`                   |
| Bundle                                    | Thêm tạm `const x = "R2__AccessKey"` vào một file dưới `features/` rồi `pnpm build`                                                               | Xóa dòng đó                         |


Sau mỗi lần: `git status` phải sạch. Dán bằng chứng ba lần đỏ vào PR.

### Cạm bẫy đã biết

- **Sửa** `ci.yml` **trên nhánh khác với nhánh đang push.** Với sự kiện `push`, GitHub dùng file workflow của
**commit được push** — sửa trên chính `loveart1210` là đủ (comment đầu `ci.yml` đã ghi). Với `pull_request`
thì khác; đừng suy từ cái này sang cái kia.
- `cancel-in-progress: true` — push tiếp trước khi run đỏ xong là mất bằng chứng.
- **Đường dẫn trong cổng là tương đối với** `src/frontend` (`defaults.run.working-directory`). Viết
`src/frontend/lib/api/…` là sai.
- **Thêm chuỗi grep có ký tự đặc biệt của regex.** `grep -rlE` là regex mở rộng; `R2__` và `X-Amz-Signature`
an toàn, nhưng chuỗi tiếp theo ai đó thêm có thể không. Kiểm bằng cách làm nó đỏ một lần.
- **Gộp** `B5` **vào commit của** `B4`**.** Hai cổng khác nhau, hai bằng chứng đỏ khác nhau.

---



## 12. Kế hoạch commit

Mười commit, tách theo mã việc (luật commit Mục 8), scope `gd2-c` cho khối C và `gd2-b` cho khối
B (luật commit Mục 3). Mỗi commit chạm code **phải** có dòng `Test:` và dòng `detect-changes:` (Mục 5.5, 5.6),
và footer **sạch bút ký** (Mục 6).


| #   | Tiêu đề                                                                                       | Gồm                                                                                                             |
| --- | --------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------- |
| 1   | `feat(gd2-c): C1 — bề mặt lưu trữ đối tượng năm thao tác, fail-fast ngoài Development`        | `AWSSDK.S3` + `IObjectStorage` + `R2Options` + `AddSharedKernelR2` + hai khẳng định `StartupConfigurationTests` |
| 2   | `feat(gd2-c): C2 — presign PUT ký kèm Content-Length, PUT thật từ trình duyệt lên bucket dev` | `R2ObjectStorage` + unit test bốn nhóm + **ảnh Network và ảnh bucket trong PR**                                 |
| 3   | `feat(gd2-c): C3 — đối chiếu HEAD với khai báo dạng hàm thuần, ba nhánh một mã 400`           | `MediaHeadPolicy` + `HeadAsync` + bốn unit test                                                                 |
| 4   | `test(gd2-c): C5 — FakeObjectStorage in-memory cho test chạy được trên CI không có khóa R2`   | Fake + đăng ký trong `ConfigureTestServices`                                                                    |
| 5   | `feat(gd2-c): C4 — worker dọn rác một instance, khóa Redis, mặc định tắt`                     | `MediaCleanupWorker` + công tắc + hai nhánh                                                                     |
| 6   | `test(gd2-b): B1 — harness seed cả ba module cho test chỉ đọc`                                | `SeededContentDatabaseAsync` + hai dòng gọi + **số đo thời gian trước/sau**                                     |
| 7   | `test(gd2-b): B2 — sáu dòng matrix cho ownership và BR-02, cố ý đỏ cho tới D7/D8`             | Sáu dòng + `TaoBaiCuaNguoiKhacAsync` + (nếu chốt `Q-B2`) sửa khung tối thiểu                                    |
| 8   | `test(gd2-b): B3 — bảng đột biến sáu dòng và bốn test BR-01 ở mức integration`                | Bảng đột biến + `AC-02`, `AC-03`, `BR01-05`, `BR01-06`                                                          |
| 9   | `test(gd2-b): B4 — cổng hợp đồng canh cả ba module, tách ContractTestsBase`                   | Hai dòng csproj + `ContractTestsBase` + hai lớp con                                                             |
| 10  | `ci(gd2-b): B5 — cổng bundle grep thêm R2__ và X-Amz-Signature`                               | Bước bundle trong `ci.yml`                                                                                      |


Cổng codegen đã đi trong commit riêng của `Q-B4` (2026-09-19, trước cổng mở) nên `B5` chỉ còn một commit. Tách
#7 và #8 là **bắt buộc**: commit #7 cố ý đỏ, và trộn nó với commit làm xanh thì mất luôn bằng chứng.

Commit #7 cố ý đỏ nhưng **không** phải loại "làm đỏ để chứng minh cổng chặn rồi revert" ở Mục 8 luật commit —
nó đỏ vì phần code chưa tới, và sẽ xanh lại ở `D7`/`D8` chứ không bị revert. Nói rõ điều đó trong thân bài,
đừng gắn nhãn `— DO CO CHU DICH, se revert`.

Mẫu thân bài cho commit #2:

```
R2ObjectStorage dựng trên AWSSDK.S3 (Đ-2.14), ForcePathStyle vì R2 yêu cầu path-style. Presigned PUT hạn 10
phút, Content-Type VÀ Content-Length nằm trong signed headers — không có vế thứ hai thì ký cho 2MB rồi client
PUT 400MB vẫn vào bucket (Đ-2.8 đoạn mở đầu). Presigned GET hạn 15 phút (Đ-2.9), hai số cố ý khác nhau.

ISS-02 đã đóng: một JPEG thật PUT thành công lên socialmedia-dev TỪ TRÌNH DUYỆT (ảnh Network + ảnh object trong
bucket ở mô tả PR). curl không nghiệm thu được việc này — nó không bao giờ thấy CORS (Mục 10.2 mức 3).

Không log uploadUrl ở bất kỳ mức nào (B.9 điểm 4).

Test: Unit 82 → 90 (+8 R2PresignTests: dạng key, allowlist, hạn, signed headers).
detect-changes: low, 0 luồng
```

---



## 13. Checklist nghiệm thu

Tick từng dòng, có bằng chứng. Dòng nào không áp dụng thì ghi lý do, **không xóa dòng**.

**Cấu hình và bí mật (**`C1`**)**

- [ ] `IObjectStorage` đúng **năm** phương thức; **không** kiểu nào của AWS SDK trên bề mặt
- [ ] `AWSSDK.S3` ghim **chính xác** version (không `^`, không dải)
- [ ] Ngoài Development thiếu bất kỳ key nào trong bốn key → app **từ chối khởi động**, thông báo nêu đúng tên biến
- [ ] `new ApiFactory()` khởi động được **không có** biến `R2__`* nào (nếu mất tính chất này, cổng hợp đồng đỏ)
- [ ] Khóa dev nằm trong `user-secrets`; `grep -n "R2__" deploy/.env` trên máy dev **không** có giá trị nào
- [ ] `DevEnvFile` **không** mở rộng cho `R2__`*
- [ ] `.env.example` vẫn chỉ có bốn dòng **trống**

**Presign và ISS-02 (**`C2`**)**

- [ ] `ForcePathStyle = true`
- [ ] Presigned `PUT` hạn **10 phút**; presigned `GET` hạn **15 phút** — hai số khác nhau
- [ ] `Content-Type` **và** `Content-Length` nằm trong `X-Amz-SignedHeaders` của URL `PUT` (có unit test khẳng định)
- [ ] Key đúng dạng `posts/{userId}/{uuid7}.{ext}` và `avatars/{userId}/{uuid7}.{ext}`; `ext` suy từ contentType, **không** từ tên file client
- [ ] Allowlist đúng ba loại; `image/gif` và `application/pdf` bị từ chối
- [ ] **Ảnh chụp tab Network** (`PUT` 200 từ `http://localhost:3000`) trong PR
- [ ] **Ảnh chụp object trong bucket** `-dev` (đúng size, đúng content type) trong PR
- [ ] `grep -rni "uploadUrl" src/backend | grep -i "log"` → 0 kết quả

**Kiểm lúc commit (**`C3`**)**

- [ ] `MediaHeadPolicy.Check` là hàm **thuần**, không async, không nhận `IObjectStorage`
- [ ] Ba nhánh hỏng, ba thông điệp khác nhau, **cùng** mã 400
- [ ] Bốn unit test (ba lỗi + một hợp lệ), không test nào chạm mạng
- [ ] `HeadAsync` trả `null` cho "không tồn tại"; ngoại lệ khác **để ném** (500, không phải 400)
- [ ] So `ContentType` **không** phân biệt hoa thường
- [ ] Có comment nói rõ HEAD đứng **trước** transaction ở `D5`

**Fake (**`C5`**)**

- [ ] Fake nằm trong project **test**, không có `#if DEBUG` nào trong code sản phẩm
- [ ] Có `Put(key, size, contentType)` để test dựng sẵn kết quả HEAD
- [ ] URL trả về dùng `fake.invalid`, **không** giống URL R2 thật
- [ ] `ConfigureTestServices` có `RemoveAll<IObjectStorage>()` trước khi đăng ký Fake
- [ ] `grep -rn "Skip" tests/` không có dòng nào mới

**Worker (**`C4`**)**

- [ ] `Media:Cleanup:Enabled` **mặc định tắt**; staging bật tường minh bằng `Media__Cleanup__Enabled=true`
- [ ] Không lấy được khóa → **bỏ lượt**, không chờ; Redis chết → **bỏ lượt**, không chạy
- [ ] `EX 3000` (50 phút) **nhỏ hơn** chu kỳ 1 giờ
- [ ] `When.NotExists` có mặt
- [ ] Ngưỡng mồ côi **24 giờ** (không phải 10 phút bằng hạn presign)
- [ ] Ngưỡng bài xóa mềm **7 ngày**
- [ ] `ListAsync` phân trang bằng continuation token, **giới hạn 1000 object/lượt**
- [ ] `IgnoreQueryFilters()` chỉ xuất hiện ở repository của worker — `grep -rn "IgnoreQueryFilters" src/backend` đúng **một** chỗ
- [ ] Log số object + số byte; **không** log key của người dùng, **không** log URL

**Harness (**`B1`**)**

- [ ] `SeededContentDatabaseAsync` migrate đủ **ba** module, thứ tự Identity → Profile → Content
- [ ] `SeededIdentityDatabaseAsync` **vẫn còn** và không đổi hành vi
- [ ] `AuthZMatrixTests` và `OwnershipTemplateTests` đổi **cùng lúc**, cùng `key = "authz"`
- [ ] Thời gian nhóm test Postgres: trước `… s` → sau `… s`, **có trong PR**; còn dưới ~3 phút

**Matrix (**`B2`**,** `B3`**)**

- [ ] `Category=AuthZ` chạy **19** dòng, xanh hết sau khi `D7`/`D8` xong
- [ ] `Ma_tran_khong_rong_va_ma_khong_trung` xanh
- [ ] Sáu dòng mới đều mang `AddedIn: "GĐ2"`
- [ ] `ArrangePath` tạo bài của B **qua API thật**, có tạo hồ sơ trước (Đ-2.4)
- [ ] `READ-01` kỳ vọng **404** (không phải 403)
- [ ] **Link CI run đỏ** (hoặc output local) có trong PR
- [ ] **Bảng đột biến ≥ 6 dòng** có trong PR, mỗi đột biến làm **đúng** dòng dự kiến đỏ
- [ ] Đột biến "bỏ kiểm tiền tố khóa" thật sự làm `TC-A03-media` đỏ (không thì `Q-B2` chưa xong)
- [ ] `git status` sạch sau khi thử đột biến

**BR-01 integration (**`B3`**)**

- [ ] `AC-02` khẳng định `errors.body`; `AC-03` khẳng định `errors.mediaKeys`
- [ ] `BR01-05` khẳng định **số bài không tăng**, không chỉ status 400
- [ ] `BR01-06` (bài chỉ có ảnh) trả 201
- [ ] Bốn test dùng `CreateDatabaseAsync` (chúng **sửa** dữ liệu) và `FakeObjectStorage`

**Cổng hợp đồng (**`B4`**)**

- [ ] Hai dòng `Content Include … Link="Contracts\…"` trong csproj
- [ ] `--filter "Category=Contract"` chạy **6** test, `TreatNoTestsAsError=true` không kêu
- [ ] `[Trait("Category","Contract")]` có trên **từng lớp con**
- [ ] Đã thử cho đỏ: thêm status code vào controller mà không sửa yaml → cổng đỏ
- [ ] Tên nhóm Swagger khớp **cả ba chỗ** cho cả hai module mới

**Cổng CI (**`B5`**)**

- [x] ~~Cổng codegen~~ — **xong 2026-09-19** ở `Q-B4`, không còn là việc của `B5`. Kiểm lại một lần khi hai
  ```
  hợp đồng GĐ2 được commit ở cổng mở: `pnpm gen:api` phải tự sinh `lib/api/profile/` và `lib/api/content/`
  mà **không ai sửa `package.json` hay `ci.yml`** — nếu phải sửa thì `Q-B4` đã hỏng ở đâu đó
  ```
- [ ] Cổng bundle grep thêm `R2__` và `X-Amz-Signature`
- [ ] Cả hai cổng **đã được thấy đỏ** (các lần thử ở Mục 11.3), bằng chứng trong PR
- [ ] `git status` sạch trước và sau mỗi lần thử

**Luật repo**

- [ ] Sáu quyết định `Q-C1`, `Q-C2`, `Q-B1`, `Q-B2`, `Q-B3`, `Q-B4` đã chốt với nhóm, và chỗ nào lệch
  ```
  B.4/B.5/Mục 6.3/`frontend-rules.md` Mục 7 đã **ghi ngược vào tài liệu gốc trong cùng commit**
  ```
- [x] `Q-B4` — **đã chốt và thi công xong 2026-09-19, trước cổng mở**, đúng lúc rẻ nhất (hai script
  ```
  per-module chưa kịp được viết nên không phải gỡ gì)
  ```
- [ ] **Không có giá trị secret nào trong diff, trong log, trong mô tả PR** — chỉ nêu tên key
- [ ] Không test nào, không log nào chứa token, `uploadUrl` hay presigned GET
- [ ] Mọi commit có dòng `Test:` và `detect-changes:`; footer sạch bút ký

---



## 14. Hai khối để lại gì


| Di sản                                                                    | Ai thừa hưởng ngay                            | Ai thừa hưởng về sau                                                       |
| ------------------------------------------------------------------------- | --------------------------------------------- | -------------------------------------------------------------------------- |
| `IObjectStorage` bề mặt năm thao tác                                      | `D3` (avatar), `D4` (presign), `D5` (HEAD)    | **GĐ5 media tin nhắn — dùng lại không sửa gì**, đúng mục tiêu khối C ở B.5 |
| `R2ObjectStorage` + cấu hình R2 đã chạy thật                              | `C3`, `C4`                                    | GĐ5, GĐ7 (2 instance)                                                      |
| **ISS-02 đóng** với bằng chứng trình duyệt                                | Cả nhóm — không còn phải dự phòng phương án B | GĐ5 không phải mở lại rủi ro này                                           |
| `MediaHeadPolicy` hàm thuần                                               | `D5`                                          | GĐ5 (media tin nhắn cũng cần lớp 2 của Đ-2.8)                              |
| `FakeObjectStorage`                                                       | `B3`, `D3`, `D4`, `D5`                        | Mọi test chạm lưu trữ từ GĐ3 trở đi                                        |
| Khuôn "worker nền + khóa Redis một instance"                              | `C4`                                          | GĐ6 (worker thông báo), GĐ7 (mọi việc nền khi có 2 instance)               |
| `SeededContentDatabaseAsync` — khuôn "seed nhiều module cho test chỉ đọc" | `B2`, `B3`, test đọc của khối D               | GĐ4–GĐ8: thêm module thứ tư là thêm ba dòng                                |
| Sáu dòng matrix + `TaoBaiCuaNguoiKhacAsync`                               | `D5`, `D7`, `D8` có lưới ngay khi viết        | GĐ3 (bình luận/reaction cũng có chủ), GĐ5, GĐ8                             |
| Khung matrix **đã biết id người gọi** (nếu chốt `Q-B2`)                   | `TC-A03-media`                                | Mọi dòng tương lai cần dựng dữ liệu *cho chính người gọi*                  |
| Bảng đột biến sáu dòng                                                    | Review PR khối D                              | Mẫu cho bảng đột biến GĐ3–GĐ8                                              |
| `ContractTestsBase` (nếu chốt tách lớp)                                   | `B4`                                          | Module thứ tư trở đi: ba dòng một lớp                                      |
| Cổng codegen biết bắt đường dẫn gõ sai + cổng bundle grep `R2__`          | `F4`                                          | GĐ3 trở đi, GĐ5 (media tin nhắn dùng lại R2)                               |


---



## 15. Ranh giới — cái gì **không** thuộc hai khối này


| Không thuộc B/C                                                                                      | Thuộc về                                       | Vì sao dễ nhầm                                                                           |
| ---------------------------------------------------------------------------------------------------- | ---------------------------------------------- | ---------------------------------------------------------------------------------------- |
| Hai bucket, CORS, hai API token, đặt khóa vào `user-secrets`/`deploy/.env`                           | **Mục 9.0**, chủ dự án, **trước** sáng Ngày 6  | Cũng là "việc R2", nhưng là thao tác dashboard, không phải code                          |
| `POST /media/uploads` — controller, DTO, validator, hai mức quyền theo `purpose`                     | `D4`                                           | `C2` sinh URL, `D4` mới là endpoint                                                      |
| Kiểm tiền tố `posts/{actorId}/` lúc commit                                                           | `D5` (Đ-2.7, trả **403**)                      | `C3` cũng kiểm lúc commit, nhưng là Đ-2.8 và trả **400**                                 |
| Transaction INSERT post + media                                                                      | `D5`                                           | `C3` chỉ nói "HEAD trước transaction", không viết transaction                            |
| Nới `img-src`/`connect-src` cho host R2                                                              | `E7`, ghi thành `Đ-E17` trong cùng commit      | Là hệ quả trực tiếp của `C2`                                                             |
| Upload bằng `XMLHttpRequest`, tiến trình từng ảnh, một ảnh lỗi không hủy cả lô                       | `E4`                                           | SEQ-01 bước 4                                                                            |
| Unit test BR-01 dạng hàm thuần (`PostContentPolicy`)                                                 | `A4`                                           | B.2 ghi "BR-01" trong nội dung khối B — nhưng đó là mức integration (`Q-B3`)             |
| `PROF-01`–`PROF-04`, `AC-01`, `AC-04`, `POST-06`–`POST-08`, `READ-02`–`READ-05`, `PAGE-01`–`PAGE-03` | Khối D, đi cùng endpoint sinh ra chúng         | Cũng nằm ở Mục 10.1                                                                      |
| Test pin `AlwaysStrangers`                                                                           | `A6`                                           | Bảng đột biến của `B3` có nhắc tới nó (dòng đối chứng)                                   |
| `ModuleBoundaryTests`, `PresentationBoundaryTests`                                                   | **Không ai** — Mục 10.4 điểm 5                 | Phải sửa chúng để code xanh nghĩa là **code đang phá ranh giới**, không phải test sai    |
| `scripts/gen-api.mjs`, `lib/api/identity/`                                                           | **Đã xong 2026-09-19** (`Q-B4`), trước cổng mở | Từng nằm trong kế hoạch của `E1`; giờ `E1` chỉ còn phần dùng type sinh ra, không sinh gì |
| Kiểm tay CORS ở cổng đóng trên staging, E2E Playwright                                               | `F2`, `F3`                                     | `C2` đã kiểm tay trên **dev**; `F3` là lần kiểm trên **staging**                         |
| Đóng băng hợp đồng `profile-v1` + `content-v1`                                                       | `F5`                                           | `B4` dựng cổng, `F5` mới là lúc chốt                                                     |


