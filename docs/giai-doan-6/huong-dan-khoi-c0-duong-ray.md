# Hướng dẫn thực hiện — Khối C0. Đường ray (GĐ6)

> Bản triển khai chi tiết của **B.3 Khối C0** trong [giai-doan-6.md](giai-doan-6.md). Tài liệu gốc trả lời *cái gì* và
> *vì sao*; tài liệu này trả lời *gõ vào file nào, theo thứ tự nào, và nhìn vào đâu để biết đã xong thật*.
>
> **Nguồn sự thật vẫn là** `giai-doan-6.md` — Đ-6.2 (event bus), Đ-6.4 (đường ray), Đ-6.17 (bảng loại thông báo + chữ ký
> record), Mục 9.4 (chỗ đụng nhau với A, B), Mục 10.1/10.3 (`EVT-*`) — và `AGENTS.md`. Chỗ nào tài liệu này lệch với hai
> file đó thì sửa ở đây, không sửa ngược. Muốn đổi một `Đ-6.*` thì đó là **quyết định mới**, có ngày tháng, ghi vào
> `giai-doan-6.md` trong cùng commit.
>
> Khối này **nhỏ nhưng đứng đầu đường găng**: nó là thứ duy nhất trong GĐ6 mà **người khác** chờ. A (GĐ3) và B (GĐ5) cần
> `IEventPublisher` + record event trên `develop` **trước khi** họ viết tới chỗ phát event — trễ là họ viết lớp "chỉ log"
> và GĐ6 phải sửa code của họ sau lưng họ (R6-02).

|                          |                                                                                                                                                                                                                                  |
| ------------------------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Người làm**            | Một người (không chia lane)                                                                                                                                                                                                      |
| **Thời lượng**           | **Bước 2** của Mục 9.3 — 0,5 ngày. Phải **merge vào `develop` trong ngày thứ nhất sau cổng mở** (B.10)                                                                                                                           |
| **Khối này cần trước**   | Cổng mở Mục 9.2 **bước 1–2**: sáu chữ ký record của Đ-6.17 đã chốt với A và B. **Không** cần ba file hợp đồng của bước 3 — C0 không có endpoint                                                                                  |
| **Khối này chặn**        | A phát `CommentCreated`/`ReactionSet`; B phát `MessageSent`; `D9`–`D10` (handler thông báo); `C2` (dùng `ModerationTargetType`); `B1` (harness `DrainEventsAsync`)                                                               |
| **Không thuộc khối này** | Handler thật nào (D10), bảng `notification.*` (A2), hub (C6), `IAuditTrail`/`IModerationTargets` (C1/C2), hợp đồng API, gói `prometheus-net` (GĐ7), CHECK `hidden` của `comments` (migration của A). Xem Mục 11 |

---

## 0. Danh sách công việc — mục tiêu và kết quả mong đợi

B.3 ghi C0 là **một** mã việc (một commit `feat(gd6-c): C0 — …`). Tài liệu này chia nó thành tám bước để làm và nghiệm thu
từng phần; mã `C0.x` chỉ dùng trong tài liệu này, **không** đi vào tiêu đề commit.

| Mã       | Đầu việc                                                                                                              | Mục tiêu — việc này tồn tại để làm gì                                                                                                                                                                                  | Kết quả mong đợi — thứ kiểm chứng được                                                                                                                                                                                                                         |
| -------- | --------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **[0]**  | Chuẩn bị: nhánh, chữ ký đã chốt, impact analysis                                                                      | Đảm bảo PR đường ray **chỉ** chứa đường ray (không kéo theo commit đỏ có chủ đích của cổng mở) và sáu chữ ký record là thứ A, B **đã đồng ý**, không phải thứ GĐ6 tự đoán                                                 | `loveart1210` ngang `origin/develop`; tin nhắn trả lời của A và B về chữ ký được dán vào ghi chú cổng mở; kết quả `impact` của bốn symbol đã ghi (Mục 1.2)                                                                                                     |
| **C0.1** | Ba hợp đồng của bus + cách đăng ký handler                                                                            | Cho producer (A, B, SocialGraph) một bề mặt **một phương thức** để phát, và cho consumer (Notification, D10) **một** cách đăng ký handler — không ai phải biết bus hiện thực ra sao                                        | `IIntegrationEvent`, `IEventPublisher`, `IIntegrationEventHandler<TEvent>`, `EventHandlerRegistration`, `AddIntegrationEventHandler<TEvent, THandler>()` trong `SharedKernel/Events/`; build xanh                                                               |
| **C0.2** | Sáu record event + hai enum                                                                                           | Đóng băng **hình dạng** event mà ba giai đoạn song song cùng dựa vào — để A, B phát được từ ngày đầu mà GĐ6 đọc được ngay, không đổi chữ ký sau khi merge                                                                  | Bốn file `ContentEvents.cs`, `SocialGraphEvents.cs`, `MessagingEvents.cs`, `ModerationEvents.cs` đúng Đ-6.17; enum `ReactionTargetKind` (Events) và `ModerationTargetType` (Moderation); mọi record `sealed`, chỉ mang id/enum/số/cờ                             |
| **C0.3** | `InProcessEventBus` — `Channel` có giới hạn + `BackgroundService` + metric + `DrainAsync`                              | Hiện thực bốn luật của Đ-6.2: phát **không chờ** handler, handler lỗi **không** lan về request, hàng đợi đầy thì **rơi + đếm + cảnh báo có ngưỡng**, log **không** payload                                                  | Một lớp vừa là `IEventPublisher` vừa là `BackgroundService`; mỗi handler chạy trong **scope DI riêng**; counter `socialapp_events_published_total`, `…_dropped_total` trên `Meter("SocialApp.Events")`; `DrainAsync(timeout)` chờ tới khi không còn event dở dang |
| **C0.4** | `AddInProcessEventBus()` gọi trong `AddSharedKernel()`                                                                | Host có bus mà `Program.cs` **không thêm dòng nào** — giảm một chỗ đụng nhau với A, B (Mục 9.4 hàng `Program.cs`)                                                                                                        | Một instance duy nhất cho cả `IEventPublisher` lẫn `IHostedService`; `EVT-05` xanh; app khởi động, `/health/ready` xanh                                                                                                                                           |
| **C0.5** | Unit test của bus                                                                                                     | Chứng minh bốn luật bằng test chạy trên CI, không bằng đọc code — `EVT-01..03` của Mục 10.1 cộng hai ca canh chỗ dễ sai nhất (scope, một instance)                                                                        | `InProcessEventBusTests`: `EVT-01`, `EVT-02`, `EVT-03`, `EVT-04`, `EVT-05` xanh; **không** ca nào dùng `Task.Delay` để chờ                                                                                                                                       |
| **C0.6** | `SocialGraphEvents` gọi `Publish` + harness `DrainEventsAsync` + test đi từ API thật                                  | Bật event **thật** đầu tiên của hệ thống (Đ-6.4) và chứng minh nó đi từ `POST /friends/requests` tới handler **sau `COMMIT`**, **đúng người** — đổi chỗ hai id là thông báo gửi nhầm người mà không test nào khác bắt       | Thân hai phương thức gọi `Publish`, chữ ký lớp giữ nguyên; `EVT-06` xanh; `FRD-*` và `RelationshipServicePostCommitTests` vẫn xanh                                                                                                                              |
| **C0.7** | Test canh luật "event chỉ mang id và enum" *(đề xuất)*                                                                | Biến luật 3 của Đ-6.2 (event không mang dữ liệu người dùng — Đ-5.18) từ "tự rà trước PR" (B.10 #2) thành **cổng CI** — A, B thêm record sau này cũng bị canh                                                               | `EVT-07` (reflection) xanh; thử thêm `string Body` vào một record → đỏ, nêu đúng tên record + thuộc tính                                                                                                                                                        |
| **C0.8** | Docs + PR mỏng vào `develop` + tin nhắn cho A, B                                                                      | Biến code thành thứ **người khác dùng được**: nằm trên `develop`, có tài liệu nói cách dùng, và A, B biết đã tới                                                                                                         | `AGENTS.md` Mục 5 có một dòng về event bus; mục "Thực tế thi công" cuối file này điền; PR `loveart1210 → develop` đã merge (người trong đội bấm); A, B đã nhận tin kèm đoạn code mẫu                                                                             |

### Thứ tự thực thi

```
 [0] ─→ C0.1 ─┬→ C0.2 ─────────────┐
              └→ C0.3 ─→ C0.4 ─→ C0.5 ─→ C0.6 ─→ C0.7 ─→ C0.8
                                   ▲
            C0.2 phải xong trước C0.6 (SocialGraph phát record của C0.2)
```

- `C0.1` trước tất cả — `C0.2` và `C0.3` đều implement/nhận interface của nó.
- `C0.2` và `C0.3` **độc lập** nhau: record không biết bus, bus không biết record cụ thể. Unit test của bus (`C0.5`) dùng
  **record riêng của test**, không dùng sáu record thật — để test bus không đỏ khi A, B xin đổi một chữ ký.
- `C0.4 → C0.5`: ca `EVT-05` (một instance) cần hàm DI thật.
- `C0.6` **sau cùng trong phần code**: từ lúc `SocialGraphEvents` gọi `Publish`, mọi request kết bạn trong bộ integration đi
  qua bus — bus phải đã có test của riêng nó, để lỗi (nếu có) chỉ về đúng một chỗ.
- `C0.8` — PR chỉ mở khi `C0.1–C0.7` nằm trong **một** commit xanh (commit-rules Mục 8: một mã việc một commit).

**Mỗi mốc mở khóa việc gì:**

| Mốc                   | Mở khóa                                                                                                  |
| --------------------- | -------------------------------------------------------------------------------------------------------- |
| C0.1 + C0.2 trên `develop` | A, B rebase và gõ `publisher.Publish(new CommentCreated(…))` — compile được, chạy được (no-op có đếm)  |
| C0.3 + C0.4           | D10 chỉ việc viết handler + một dòng `AddIntegrationEventHandler` trong `AddNotificationModule`          |
| C0.6                  | D9–D10 có event **thật** (`friend_request`, `friend_accepted`) để test ngay, không chờ A, B              |
| Harness `DrainEventsAsync` | B1 không phải dựng lại; mọi test `EVT-*`/`NOTIF-*` sau này chờ bằng nó, không bằng `Task.Delay`       |

### Tám chỗ lệch B.3 — đề xuất, chốt khi thi công

Đọc kỹ B.3 và Đ-6.2 đối chiếu với code ngày 2026-09-23 thì thấy tám chỗ B.3 viết chưa đủ hoặc tự mâu thuẫn. Mỗi chỗ đã
chọn một hướng; chốt ở đâu thì ghi vào "Thực tế thi công" và sửa B.3/Đ-6.* trong cùng commit C0.

| #   | B.3 / Đ-6.* viết                                                                                   | Đề xuất                                                                                                                                                  | Vì sao                                                                                                                                                                                                                                                                                                                                                      |
| --- | --------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| L1  | B.3: "với mỗi **event** mở một scope DI". Đ-6.2: "mỗi **handler** một scope DI"                      | **Mỗi handler một scope** (theo Đ-6.2); sửa câu của B.3                                                                                                  | Hai handler cùng module (D10 sẽ có ba handler trong Notification) cùng một scope thì **chung một `DbContext`**: handler 1 ném sau khi đã `Add` entity, handler 2 gọi `SaveChangesAsync` là ghi luôn rác của handler 1. Lỗi này không có ở C0 (chưa có handler thật) nên phải chặn bằng thiết kế, không chờ gặp                                                   |
| L2  | B.3: tra handler bằng `IServiceProvider.GetServices<IIntegrationEventHandler<T>>()`                | Đăng ký qua `AddIntegrationEventHandler<TEvent, THandler>()`: handler vào DI như kiểu cụ thể (scoped) + một `EventHandlerRegistration` singleton mang delegate gọi | L1 cần biết **kiểu handler trước khi mở scope** — `GetServices` phải mở scope mới liệt kê được. Có danh sách ở singleton thì "không handler" biết được mà không mở scope nào, và gọi handler bằng delegate, không reflection                                                                                                                                    |
| L3  | Đ-6.17: `record ReactionSet(ReactionTargetType TargetType, …)`                                      | Kiểu enum ở SharedKernel đặt tên **`ReactionTargetKind`**; tên thuộc tính `TargetType` giữ nguyên                                                        | `SocialApp.Modules.Content.Domain.ReactionTargetType` **đã có** (`Content/Domain/ReactionTargetType.cs`). Service cảm xúc của A sẽ `using` cả `Content.Domain` lẫn `SharedKernel.Events` → `CS0104` tên mơ hồ ở đúng file A đang viết. SharedKernel không được dùng enum của Content (ADR-001), nên phải là enum riêng — tên khác là rẻ nhất                  |
| L4  | Đ-6.17: `ContentHidden(ModerationTargetType TargetType, …)` — không nói enum nằm đâu                | `ModerationTargetType { Post, Comment, User }` ở **`SharedKernel/Moderation/`** ngay từ C0                                                               | `C2` dựng `ModerationTarget` + `IModerationTargets` ở đúng namespace đó (Đ-6.3). Đặt enum ở `Events/` thì C2 phải dời nó — tức đổi `using` trong code A, B đã merge. Chỉ một enum, không phương thức nào nhận `DbTransaction`, nên không đụng test `WriteContracts_are_only_the_two_named`                                                                  |
| L5  | B.3: metric "nếu `prometheus-net` của GĐ7 đã có, chưa thì bộ đếm nội bộ + TODO trỏ GĐ7 C2"          | `System.Diagnostics.Metrics` (`IMeterFactory` → `Meter("SocialApp.Events")`) — có sẵn trong .NET 8, không thêm gói, không TODO                            | Kiểm 2026-09-23: repo **chưa** có `prometheus-net`. `Meter` là API đo chuẩn của .NET; GĐ7 C2 chỉ việc kiểm tên xuất ra ở `/metrics`. "Bộ đếm nội bộ" là một API tự chế mà GĐ7 phải gỡ                                                                                                                                                                    |
| L6  | Đ-6.2: "`DrainAsync()` (chỉ đăng ký trong test harness)"                                           | `DrainAsync(TimeSpan)` là phương thức **public của `InProcessEventBus`**; harness chỉ bọc nó thành `DrainEventsAsync()`                                   | Thứ `DrainAsync` cần (số event dở dang) phải nằm **trong** bus — không đăng ký riêng được. Code sản phẩm không gọi nó: `IEventPublisher` không có phương thức này, chỉ ai resolve kiểu cụ thể mới thấy                                                                                                                                                           |
| L7  | B.3: "log tên event + id (không payload)"                                                          | Log **tên kiểu event + số thứ tự phong bì + tên handler**; không id nào trong record                                                                      | Mọi record đều mang id người dùng (`ActorId`, `RequesterId`…) — GĐ4 đã chốt id người dùng là PII không vào log (`SocialGraphEvents.cs` hiện tại). Số thứ tự phong bì (bus tự đánh, `long` tăng dần) đủ để nối dòng log "rơi" với dòng log "handler lỗi"                                                                                                        |
| L8  | Mục 9.2: commit ba yaml (bước 3) **trước** C0 (bước 2 của 9.3)                                     | Làm C0 trên `loveart1210` **trước khi push** commit yaml của bước 3; yaml push sau khi PR đường ray đã merge                                              | Commit yaml cố ý đỏ cổng `API contract` (Mục 9.2 bước 3). Nằm dưới C0 trên cùng nhánh thì PR đường ray kéo theo nó → CI đỏ, không merge được. Làm vậy giữ đúng `pull-request-rules.md` Mục 1 (PR chỉ từ `loveart1210`), không cần nhánh tạm. Chi tiết ở Mục 2                                                                                                |

---

## 1. Trước khi gõ dòng đầu tiên

### 1.1 Bốn điều kiện cần

```bash
# 1. Postgres + Redis dev đang chạy — C0.6 chạy bộ integration SocialGraph
# 2. Docker daemon chạy được — IntegrationTests dùng Testcontainers
# 3. Solution build sạch từ điểm xuất phát
dotnet build SocialApp.sln
# 4. Nhánh ngang develop (Mục 2)
git fetch origin && git rev-list --left-right --count origin/develop...HEAD
```

Ghi lại **số test trước** (`dotnet test` từng project: Unit, Integration, Architecture) — thân commit cần dòng
`Test: Unit X → Y, …`. Nhớ: máy dev có **một đỏ nền** ở test khởi động R2 (user-secrets có khóa R2), CI vẫn xanh — đừng
tính nó là lỗi của C0.

### 1.2 Impact analysis trước khi sửa symbol có sẵn

C0 chỉ **sửa** ba symbol có sẵn; mọi thứ còn lại là file mới. Chạy lại ngay trước khi sửa (index có thể đã cũ):

```bash
node .gitnexus/run.cjs impact "SocialGraphEvents" --direction upstream --repo .
node .gitnexus/run.cjs impact "FriendRequestSent" --direction upstream --repo .
node .gitnexus/run.cjs impact "FriendRequestAccepted" --direction upstream --repo .
node .gitnexus/run.cjs impact "AddSharedKernel" --direction upstream --repo .
```

Kết quả chạy ngày 2026-09-23:

| Symbol              | Risk        | Ghi chú                                                                                                                                                                                                                                                  |
| ------------------- | ----------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `SocialGraphEvents` | **LOW**     | Index chỉ thấy **một** người gọi: `RelationshipServicePostCommitTests` (dựng `new SocialGraphEvents(NullLogger…)`). Người gọi thật — `RelationshipService` (tiêm qua constructor, gọi ở [RelationshipService.cs:85](../../src/backend/Modules/SocialGraph/Application/Relationships/RelationshipService.cs#L85) và [:117](../../src/backend/Modules/SocialGraph/Application/Relationships/RelationshipService.cs#L117)) — index không ghi cạnh. Đổi constructor làm **đỏ compile** test đó; sửa trong cùng commit |
| `AddSharedKernel`   | **UNKNOWN** | Index không thấy người gọi. Tìm bằng text: **đúng một** chỗ — [Program.cs:47](../../src/backend/SocialApp.Api/Program.cs#L47). Mọi host test (`ApiFactory`, `ModulesApiFactory`, `AuthZApiFactory`) đi qua `Program` nên cũng nhận bus — đó là ý muốn, không phải rủi ro |

Không cái nào HIGH/CRITICAL. `UNKNOWN` **không** được đọc là "an toàn" — đã xác nhận bằng text search ở trên.

### 1.3 Sáu luật áp thẳng vào khối C0

1. **Phát sau `COMMIT`** — không bao giờ trong khối `await using var tx`. SocialGraph không mở transaction tường minh nào
   (mỗi câu ghi tự commit), nên hai chỗ gọi hiện tại đã đúng; luật này là để **dặn A, B** (Mục 10).
2. **`Publish` là `void`, không `async`.** Có `Task` là có người `await` — và `await` một thứ chờ handler là phá luật 2 của
   Đ-6.2.
3. **Record chỉ mang id, enum, số, cờ** — không `string` nội dung, không tên hiển thị. Ngoại lệ **duy nhất**:
   `ContentHidden.ReasonCode` (mã lý do trong tập cố định của CHECK, không phải chữ người dùng gõ).
4. **Không bao giờ log cả record.** `record` tự sinh `ToString()` in **mọi** thuộc tính — `logger.LogError(ex, "{Event}", e)`
   là đổ `ActorId`, `RecipientId` vào log.
5. **SharedKernel không tham chiếu module nào** (ADR-001) — kể cả enum của Content (L3).
6. **Thêm một cổng thì thử cho đỏ một lần** (`frontend-rules.md` Mục 9, áp chung cho backend theo nếp repo) — `git status`
   sạch trước và sau.

---

## 2. [0] — Chuẩn bị

**Mục tiêu:** PR đường ray chứa đúng đường ray (+ tài liệu kế hoạch GĐ6 mà A, B cần đọc), và chữ ký record là thứ đã
thống nhất.

**Kết quả mong đợi:** nhánh sạch ngang `develop`; ghi chú cổng mở có câu trả lời của A, B; bảng impact Mục 1.2 đã chạy lại.

### Các bước

1. **Đưa `loveart1210` ngang `develop`.** Ngày 2026-09-23: `origin/develop` hơn `HEAD` hai commit merge (PR #21, #22), `HEAD`
   không hơn gì. `git merge --ff-only origin/develop` (hoặc `git pull`) — không có gì để xung đột.
2. **Chốt chữ ký với A và B** (Mục 9.2 bước 2). Tối thiểu ba câu hỏi:
   - A: `CommentCreated`, `ReactionSet` như Đ-6.17 có đủ không; A có tên `ReactionTargetKind` (L3) được không.
   - B: `MessageSent(ConversationId, MessageId, SenderId, RecipientId, Seq)` khớp `IMessagingEvents` của B không.
   - B: có cần `IAccountStatusReader` sớm (lọc tài khoản bị khóa khỏi danh sách hội thoại — Đ-6.4) không. **Có** → kéo `C5`
     vào PR này (xem Mục 11); **không** → C5 ở chỗ cũ.
3. **Thứ tự commit trên nhánh (L8).** Ba file yaml của Mục 9.2 bước 3 **chưa push** cho tới khi PR đường ray merge. Nếu đã
   commit yaml ở máy rồi thì đặt nó **sau** commit C0 (`git rebase` không tương tác không đổi thứ tự được — cách đơn giản
   nhất là chưa commit yaml; đã commit thì `git reset --soft` về trước nó, commit C0, rồi commit lại yaml). Tài liệu kế
   hoạch (`docs/giai-doan-6/*.md`, `ke-hoach-trien-khai.md`) **nên** đi cùng PR — A, B cần đọc Đ-6.17 trên `develop`.
4. **Chạy impact** (Mục 1.2).

### Cạm bẫy đã biết

- **PR mở rồi mới push yaml** → yaml nhảy vào PR đang mở, CI đỏ. Trong lúc PR đường ray còn mở: **không push gì** lên
  `loveart1210` ngoài sửa của chính PR đó.
- Chốt chữ ký qua lời nói rồi quên ghi. Đổi chữ ký sau khi merge = báo cả hai + chỉ-thêm tham số có mặc định (Mục 9.4). Ghi
  câu trả lời của A, B vào ghi chú cổng mở để lúc đó có cái mà dẫn.

---

## 3. C0.1 — Ba hợp đồng của bus + cách đăng ký handler

**Mục tiêu:** một bề mặt nhỏ nhất đủ cho producer và consumer, không lộ hiện thực.

**Kết quả mong đợi:** năm kiểu trong namespace `SocialApp.SharedKernel.Events`, build xanh, chưa ai dùng.

### Các bước

Thư mục mới `src/backend/SocialApp.SharedKernel/Events/`:

```csharp
// IIntegrationEvent.cs — đánh dấu. Mọi event là sealed record bất biến, chỉ mang id + enum (Đ-6.2 luật 3).
public interface IIntegrationEvent;

// IEventPublisher.cs
public interface IEventPublisher
{
    /// Gọi SAU COMMIT. Không chờ handler, không ném khi hàng đợi đầy (rơi + đếm). void có chủ đích — xem luật 2 Mục 1.3.
    void Publish(IIntegrationEvent integrationEvent);
}

// IIntegrationEventHandler.cs
public interface IIntegrationEventHandler<in TEvent> where TEvent : IIntegrationEvent
{
    Task HandleAsync(TEvent integrationEvent, CancellationToken ct);
}

// EventHandlerRegistration.cs + EventBusServiceCollectionExtensions.cs (L2)
public sealed record EventHandlerRegistration(
    Type EventType, Type HandlerType, Func<object, IIntegrationEvent, CancellationToken, Task> Invoke);

public static IServiceCollection AddIntegrationEventHandler<TEvent, THandler>(this IServiceCollection services)
    where TEvent : class, IIntegrationEvent
    where THandler : class, IIntegrationEventHandler<TEvent>
{
    services.TryAddScoped<THandler>();
    services.AddSingleton(new EventHandlerRegistration(typeof(TEvent), typeof(THandler),
        static (handler, e, ct) => ((THandler)handler).HandleAsync((TEvent)e, ct)));
    return services;
}
```

Comment đầu `IIntegrationEvent.cs` ghi bốn luật của Đ-6.2 một dòng mỗi luật + trỏ `giai-doan-6.md` Đ-6.2 — đây là chỗ A, B
sẽ mở ra đọc đầu tiên.

### Cạm bẫy đã biết

- **`Task PublishAsync`** "cho hợp thời" — xem luật 2. Nếu sau này thật cần async (outbox), đó là quyết định mới.
- **Đăng ký handler thẳng bằng `AddScoped<IIntegrationEventHandler<X>, H>()`** — compile được, **không bao giờ chạy** (bus đọc
  `EventHandlerRegistration`, không đọc interface). Doc comment của interface phải nói "đăng ký bằng
  `AddIntegrationEventHandler`". Test tự động bắt lỗi này để lại cho D10 (Mục 12).

---

## 4. C0.2 — Sáu record event + hai enum

**Mục tiêu:** đóng băng hình dạng event cho ba giai đoạn.

**Kết quả mong đợi:** sáu record đúng Đ-6.17 (trừ tên enum theo L3), mỗi cái `sealed record … : IIntegrationEvent`.

### Các bước

```
SharedKernel/Events/
  ContentEvents.cs      CommentCreated, ReactionSet, enum ReactionTargetKind { Post, Comment }
  SocialGraphEvents.cs  FriendRequestSent(RequesterId, AddresseeId), FriendRequestAccepted(RequesterId, AccepterId)
  MessagingEvents.cs    MessageSent(ConversationId, MessageId, SenderId, RecipientId, long Seq)
  ModerationEvents.cs   ContentHidden(ModerationTargetType TargetType, TargetId, Guid? PostId, AuthorId, string ReasonCode)
SharedKernel/Moderation/
  ModerationTargetType.cs   enum { Post, Comment, User }        ← L4
```

- Mỗi record một `/// <summary>` ghi: **ai phát** (module + giai đoạn), **lúc nào** (sau `COMMIT` của thao tác gì), **ai
  tiêu thụ** (loại thông báo của Đ-6.17). Người đọc là A, B — họ không đọc `giai-doan-6.md` từ đầu tới cuối.
- `CommentCreated.MentionedUserIds` là `IReadOnlyList<Guid>`; ghi rõ "rỗng tới khi làm `tag`" (B.10 thứ tự cắt 1).
- `ReactionSet.IsNew` ghi rõ: `false` khi **đổi loại** cảm xúc — handler không tạo thông báo cho trường hợp đó.
- Tên file `SocialGraphEvents.cs` trùng tên **lớp** `SocialGraphEvents` của module SocialGraph — khác namespace, không xung
  đột; file ở SharedKernel không khai lớp nào tên đó, chỉ chứa record.

### Cạm bẫy đã biết

- **Chép `ReactionTargetType` từ Đ-6.17 nguyên văn** → `CS0104` ở code của A (L3).
- **`record class` không `sealed`** → ai đó kế thừa `CommentCreated` để "thêm trường", và bus dispatch theo **kiểu chính
  xác** (`e.GetType()`) nên handler của kiểu cha không nhận. `sealed` chặn từ gốc.
- **`string ReasonCode` bị bắt chước**: người sau thấy một `string` trong record và thêm `string Preview`. Comment trên
  `ReasonCode` nói rõ vì sao nó là ngoại lệ, và `EVT-07` (C0.7) canh.

---

## 5. C0.3 — `InProcessEventBus`

**Mục tiêu:** bốn luật của Đ-6.2 thành code.

**Kết quả mong đợi:** `SharedKernel/Events/InProcessEventBus.cs` + `EventBusOptions.cs` + `EventBusMetrics.cs`.

### Các bước

**Khung lớp** — một lớp giữ cả hai vai, để chắc chắn chỉ có **một** `Channel`:

```csharp
public sealed class InProcessEventBus : BackgroundService, IEventPublisher
{
    private readonly Channel<Envelope> _channel;
    private readonly ILookup<Type, EventHandlerRegistration> _handlers;
    private long _sequence;
    private long _pending;   // đã nhận mà chưa xử lý xong / chưa rơi — DrainAsync đọc

    public InProcessEventBus(IServiceScopeFactory scopes, IEnumerable<EventHandlerRegistration> registrations,
        IOptions<EventBusOptions> options, IMeterFactory meters, FailOpenLogThrottle throttle,
        ILogger<InProcessEventBus> logger)
    {
        // Đăng ký trùng (cùng event + cùng handler) → ném lúc dựng: handler chạy hai lần là thông báo nhân đôi.
        _handlers = registrations.ToLookup(r => r.EventType);
        _channel = Channel.CreateBounded<Envelope>(
            new BoundedChannelOptions(options.Value.Capacity)
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true,
            },
            OnDropped);   // ← BẮT BUỘC, xem cạm bẫy 1
        …
    }

    public void Publish(IIntegrationEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);
        Interlocked.Increment(ref _pending);
        _published.Add(1, new KeyValuePair<string, object?>("event", e.GetType().Name));
        _channel.Writer.TryWrite(new Envelope(e, Interlocked.Increment(ref _sequence)));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var envelope in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            try { await DispatchAsync(envelope, stoppingToken); }
            finally { Interlocked.Decrement(ref _pending); }
        }
    }
}
```

**Dispatch** — mỗi handler một scope (L1), bắt lỗi **từng** handler:

```csharp
foreach (var registration in _handlers[envelope.Event.GetType()])
{
    await using var scope = _scopes.CreateAsyncScope();
    try
    {
        var handler = scope.ServiceProvider.GetRequiredService(registration.HandlerType);
        await registration.Invoke(handler, envelope.Event, stoppingToken);
    }
    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Handler {Handler} lỗi khi xử lý {EventType} #{Sequence}",
            registration.HandlerType.Name, envelope.Event.GetType().Name, envelope.Sequence);   // L7: KHÔNG có e
    }
}
```

Không có handler nào → vòng `foreach` rỗng, không log (Information cho mỗi event là ngập log lúc có tải). Counter
`published` đã đếm lúc `Publish` — đủ cho "no-op có đếm" của Đ-6.4.

**Rơi event** — `OnDropped(Envelope e)`: `Interlocked.Decrement(ref _pending)`; `_dropped.Add(1, tag event)`;
`throttle.ShouldLog("event-bus-full", out var suppressed)` đúng thì `LogWarning("Hàng đợi event đầy ({Capacity}), rơi
{EventType} #{Sequence}; {Suppressed} lần rơi trước đó không ghi log")`. Dùng lại `FailOpenLogThrottle` — cùng khuôn GĐ4,
một dòng mỗi 30 giây.

**Metric** (L5) — `EventBusMetrics`: `meters.Create("SocialApp.Events")`, hai `Counter<long>`:
`socialapp_events_published_total`, `socialapp_events_dropped_total`, tag `event` = tên kiểu. Tên để **y như** B.12/Đ-6.2;
GĐ7 C2 kiểm tên thật xuất ra ở `/metrics` (adapter có thể thêm hậu tố) và sửa ở đúng một chỗ này nếu cần.

**`DrainAsync(TimeSpan timeout)`** (L6): vòng hỏi `Volatile.Read(ref _pending) == 0` mỗi 10 ms; quá hạn thì ném
`TimeoutException` **nêu số event còn dở** — test đỏ vì bus kẹt phải nói được là bus kẹt, không phải "assert sai". Vòng hỏi
nằm **trong** bus là chấp nhận được: luật "không `Task.Delay`" là để test không **đoán** thời gian; ở đây điều kiện dừng là
trạng thái thật, 10 ms chỉ là nhịp hỏi.

**`EventBusOptions`**: `Capacity = 10_000`. **Không** bind từ cấu hình — không thêm key `.env` nào; test đặt nhỏ bằng
`services.Configure<EventBusOptions>(o => o.Capacity = 2)`.

### Cạm bẫy đã biết

1. **`DropWrite` + `TryWrite` luôn trả `true`.** Với `BoundedChannelFullMode.DropWrite`, khi đầy thì phần tử đang ghi **bị bỏ
   và `TryWrite` vẫn trả `true`** — kiểm giá trị trả về để đếm rơi là **không bao giờ** đếm được. Chỉ callback `itemDropped`
   của `Channel.CreateBounded(options, itemDropped)` biết. Quên nó → `EVT-03` đỏ (đúng lý do) — hoặc tệ hơn, ai đó "sửa"
   test cho xanh.
2. **Bắt `Exception` bao ngoài vòng `await foreach`** thay vì quanh từng handler → một handler lỗi làm `ExecuteAsync` kết
   thúc, `BackgroundService` chết im lặng (.NET 8 mặc định **dừng host** khi `BackgroundService` ném — `BackgroundServiceExceptionBehavior.StopHost`).
   Một thông báo lỗi làm sập api.
3. **Mở scope ngoài vòng handler** (một scope cho cả event) — L1.
4. **`_pending` giảm trước khi handler xong** (giảm ngay sau khi đọc khỏi channel) → `DrainAsync` trả về khi handler còn
   chạy. Giảm trong `finally` **sau** `DispatchAsync`. Handler giả đồng bộ **không** lộ được lỗi này (đo khi thi công: 0/20
   lượt unit, 0/5 lượt `EVT-06` đỏ) — `EVT-03` khẳng định tất định "`DrainAsync` chưa xong trong lúc handler bị chặn".
5. **Log `envelope.Event`** hoặc dùng `{@Event}` — luật 4 Mục 1.3. `EVT-02` khẳng định log không chứa id.

---

## 6. C0.4 — Đăng ký DI trong `AddSharedKernel()`

**Mục tiêu:** host có bus mà `Program.cs` không đổi dòng nào.

**Kết quả mong đợi:** `AddInProcessEventBus()` public (test trần gọi được), `AddSharedKernel()` gọi nó; app khởi động được.

### Các bước

```csharp
public static IServiceCollection AddInProcessEventBus(this IServiceCollection services)
{
    services.AddMetrics();                          // IMeterFactory — idempotent; host .NET 8 thường đã có
    services.TryAddSingleton(TimeProvider.System);  // cùng nếp TryAdd với AddSharedKernelRedis: một đồng hồ, một throttle
    services.TryAddSingleton<FailOpenLogThrottle>();
    services.AddOptions<EventBusOptions>();
    services.TryAddSingleton<InProcessEventBus>();
    services.TryAddSingleton<IEventPublisher>(sp => sp.GetRequiredService<InProcessEventBus>());
    services.AddHostedService(sp => sp.GetRequiredService<InProcessEventBus>());
    return services;
}
```

Gọi ở cuối `AddSharedKernel()` trong [SharedKernelExtensions.cs](../../src/backend/SocialApp.SharedKernel/DependencyInjection/SharedKernelExtensions.cs),
thêm một câu vào doc comment của lớp: "và event bus trong tiến trình (Đ-6.2)".

### Cạm bẫy đã biết

- **`services.AddHostedService<InProcessEventBus>()`** — dòng trông tự nhiên nhất và **sai**: nó dựng **instance thứ hai**.
  Producer ghi vào channel của instance A, `BackgroundService` đọc channel của instance B → không handler nào chạy, không lỗi
  nào, metric `published` vẫn tăng. `EVT-05` sinh ra để bắt đúng dòng này.
- `--migrate` đi qua `AddSharedKernel` nhưng không `RunAsync` host → `BackgroundService` không chạy. Đúng ý: migrate không
  phát event.
- **Không** đặt bus trong `AddSharedKernelRedis` "vì cùng dùng throttle" — bus không cần Redis.

---

## 7. C0.5 — Unit test của bus

**Mục tiêu:** bốn luật chạy trên CI.

**Kết quả mong đợi:** `tests/SocialApp.UnitTests/SharedKernel/InProcessEventBusTests.cs`, năm ca xanh, đã thử cho đỏ.

### Các bước

Dựng trần: `new ServiceCollection().AddLogging().AddInProcessEventBus()` + handler giả bằng `AddIntegrationEventHandler`;
resolve `InProcessEventBus`, `await bus.StartAsync(ct)`, cuối ca `StopAsync`. Record của test khai **trong file test**
(`sealed record ProbeEvent(Guid Id) : IIntegrationEvent`) — không dùng sáu record thật. Log bắt bằng một `ILoggerProvider`
ghi vào danh sách (test trần không có Serilog, `CapturingLogSink` là của integration). Metric bắt bằng `MeterListener` lọc
`instrument.Meter.Scope == meterFactory` của **chính** container ca đó — `Meter` từ `IMeterFactory` không lẫn giữa các ca chạy
song song.

| Id       | Kịch bản                                                                                                             | Kỳ vọng                                                                                                                                                    |
| -------- | -------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `EVT-01` | `Publish` khi chưa có handler nào                                                                                     | Không ném; `DrainAsync` trả về; `published` = 1, `dropped` = 0                                                                                             |
| `EVT-02` | Handler ném `InvalidOperationException`                                                                               | `Publish` không ném; `DrainAsync` trả về; đúng một log Error có tên handler + `ProbeEvent`; **chuỗi log và mọi property không chứa `Id.ToString()`**; bus vẫn xử lý event kế tiếp |
| `EVT-03` | `Capacity = 2`; handler chặn trên một `TaskCompletionSource`; phát e1, chờ handler **đã vào** e1, rồi phát e2…e5       | `dropped` = 2 (e4, e5); đúng **một** log Warning (ngưỡng); mở chặn → handler nhận e1, e2, e3 **theo thứ tự**                                                |
| `EVT-04` | Hai handler cùng một event, cùng resolve một dịch vụ scoped; handler 1 ném                                            | Handler 2 **vẫn chạy**; hai handler thấy **hai** instance khác nhau của dịch vụ scoped (L1)                                                                |
| `EVT-05` | Dựng bằng `AddInProcessEventBus()`                                                                                    | `IEventPublisher`, `InProcessEventBus` và phần tử `IHostedService` là **cùng một object** (`Assert.Same`)                                                  |

`EVT-01..03` có sẵn ở Mục 10.1; `EVT-04`, `EVT-05` là **đề xuất thêm** — ghi vào Mục 10.3 trong cùng commit.

### Cạm bẫy đã biết

- **`EVT-03` phát e1…e5 liền một mạch** → không biết consumer đã lấy e1 khỏi channel chưa, nên số rơi là 2 hoặc 3 tùy lượt —
  đỏ ngẫu nhiên. Handler giả phải báo "đã vào" (một `TaskCompletionSource` thứ hai) và test chờ nó **trước** khi phát e2.
  Cùng bài học với stub `IntersectionObserver` của GĐ4 E4: chờ **trạng thái**, không chờ thời gian.
- Khẳng định `EVT-02` "log không có payload" bằng `Assert.DoesNotContain(id, message)` trên **message đã render** chưa đủ —
  Serilog/`ILogger` còn giữ **property**. Khẳng định trên cả hai.

---

## 8. C0.6 — `SocialGraphEvents` gọi `Publish` + harness + test từ API thật

**Mục tiêu:** event thật đầu tiên; chứng minh nó đi đúng đường, đúng lúc, đúng người.

**Kết quả mong đợi:** hai thân hàm đổi; chữ ký lớp và hai chỗ gọi trong `RelationshipService` **không đổi**; `EVT-06` xanh;
`FRD-*` không đổi khẳng định nào mà vẫn xanh.

### Các bước

1. [SocialGraphEvents.cs](../../src/backend/Modules/SocialGraph/Application/SocialGraphEvents.cs): constructor nhận
   `IEventPublisher` thay `ILogger`; thân hàm là một dòng `publisher.Publish(new FriendRequestSent(requesterId, addresseeId))`.
   Đổi tên tham số `a, b` → `requesterId, addresseeId` / `requesterId, accepterId` — ánh xạ hiện tại
   (`FriendRequestAccepted(userId, actorId)` ở dòng 117: `userId` là người **gửi** lời mời, `actorId` là người **chấp nhận**)
   phải đọc được từ tên, không phải từ comment. Sửa doc comment lớp: bỏ "GĐ4 chỉ log; GĐ6 thay thân hàm", ghi "phát qua
   `IEventPublisher` (Đ-6.4)".
2. `SocialGraphModuleExtensions`: `AddSingleton<SocialGraphEvents>()` giữ nguyên (bus là singleton); sửa comment "chỉ log".
3. [RelationshipServicePostCommitTests.cs](../../tests/SocialApp.UnitTests/SocialGraph/RelationshipServicePostCommitTests.cs):
   thay `new SocialGraphEvents(NullLogger…)` bằng `new SocialGraphEvents(new RecordingPublisher())` — một publisher giả ghi
   danh sách. Không đổi khẳng định nào của sáu ca.
4. **Harness** `tests/SocialApp.IntegrationTests/Harness/EventBusHarness.cs`: extension
   `DrainEventsAsync(this WebApplicationFactory<Program> app)` gọi `InProcessEventBus.DrainAsync(TimeSpan.FromSeconds(5))`.
   Đây là phần "đăng ký `DrainAsync` của event bus" của **B1** — làm sớm ở đây vì `EVT-06` cần; B1 ghi "đã có từ C0".
5. **`EVT-06`** — `tests/SocialApp.IntegrationTests/SocialGraph/SocialGraphEventsTests.cs`: `factory.UseTestServices(s =>
   …AddIntegrationEventHandler<FriendRequestSent, Recorder>()…)` ở `InitializeAsync` (hook mới của `ModulesApiFactory`, cùng nếp
   `UseRedis`); Recorder ghi vào một singleton **của host**, test đọc lại qua `factory.Services`. Gửi lời mời A → B qua API thật, `DrainEventsAsync`, khẳng định **đúng một**
   `FriendRequestSent(RequesterId = A, AddresseeId = B)`; B chấp nhận → **đúng một** `FriendRequestAccepted(RequesterId = A,
   AccepterId = B)`. Thêm nhánh âm: gửi lần hai (409) → **không** có event thứ hai.

| Id       | Kịch bản                                                                           | Kỳ vọng                                                                                     |
| -------- | ---------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------- |
| `EVT-06` | A gửi lời mời B → B chấp nhận → A gửi lại (409) — qua `ModulesApiFactory`, Postgres thật | Đúng hai event, đúng thứ tự, **đúng vai** từng id; request 409 không phát gì              |

### Cạm bẫy đã biết

- **Đổi chỗ hai id** khi viết `new FriendRequestAccepted(…)` — compile được (cùng `Guid`), `FRD-*` vẫn xanh (không ai đọc
  event), và ở D10 thông báo "đã chấp nhận lời mời" gửi cho **chính người vừa bấm chấp nhận**. `EVT-06` là lưới duy nhất.
- **`WithWebHostBuilder` không dùng được**: nó trả `WebApplicationFactory<Program>`, còn `ModulesTestClient` đòi đúng kiểu
  `ModulesApiFactory`. Cũng không cần: `IClassFixture<ModulesApiFactory>` dựng **một factory cho mỗi lớp test**, nên handler
  đăng ký qua `UseTestServices` không lọt sang lớp khác. *(Bản đầu của tài liệu này ghi ngược lại — sai, sửa khi thi công.)*
- **Giữ bộ ghi ở field của lớp test** (`private readonly Recorded _recorded = new()`) rồi `AddSingleton(_recorded)` → xUnit dựng
  lại lớp test cho **mỗi ca** nhưng host dựng **một lần cho cả lớp**: từ ca thứ hai, test đọc một bộ ghi mà handler không bao
  giờ ghi vào. Đăng ký `AddSingleton<Recorded>()` và đọc qua `factory.Services`.
- Khẳng định `EVT-06` **trước** `DrainEventsAsync` → đỏ ngẫu nhiên. Mọi khẳng định về event đứng sau nó.

---

## 9. C0.7 — Test canh "event chỉ mang id và enum" *(đề xuất)*

**Mục tiêu:** luật 3 của Đ-6.2 thành cổng CI — B.10 #2 hiện là việc "tự rà trước PR", và A, B sẽ thêm record mà GĐ6 không
review.

**Kết quả mong đợi:** `tests/SocialApp.UnitTests/SharedKernel/IntegrationEventShapeTests.cs`, một ca `EVT-07` xanh.

### Các bước

Reflection trên assembly SharedKernel: mọi kiểu non-abstract implement `IIntegrationEvent` phải (a) `sealed`, (b) mọi thuộc
tính public có kiểu thuộc tập cho phép — `Guid`, `Guid?`, enum, `long`, `int`, `bool`, `DateTimeOffset`,
`IReadOnlyList<Guid>` — (c) ngoại lệ đúng **một** cặp `(ContentHidden, ReasonCode)` khai tường minh trong test kèm lý do. Thông
điệp đỏ nêu `<Record>.<Thuộc tính>: <Kiểu>` để người sửa biết chỗ.

**Thử cho đỏ:** thêm tạm `string Body` vào `CommentCreated` → đỏ, đúng tên; khôi phục, `git status` sạch.

### Cạm bẫy đã biết

- Quét **cả** các assembly module (vì A có thể khai event trong Content) — không cần: Đ-6.2 bắt mọi event nằm ở
  `SharedKernel/Events/`, và bus dispatch theo kiểu nên event khai ở module khác vẫn chạy được. Muốn chặn cả chỗ đó thì là
  một luật ArchUnitNET riêng — **không** làm ở C0, ghi vào Mục 12.

---

## 10. C0.8 — Docs, PR mỏng, tin nhắn cho A và B

**Mục tiêu:** code thành thứ người khác dùng được.

**Kết quả mong đợi:** docs sống cùng code trong **cùng commit**; PR mở theo `pull-request-rules.md`; A, B đã nhận tin.

### Các bước

1. **Docs trong commit C0** (commit-rules Mục 7.2):
   - `AGENTS.md` Mục 5, dòng SharedKernel: thêm "event bus trong tiến trình (`SharedKernel/Events/`, Đ-6.2) — phát sau
     `COMMIT` bằng `IEventPublisher`, đăng ký handler bằng `AddIntegrationEventHandler`".
   - `giai-doan-6.md`: sửa B.3 theo L1, L2, L6, L7 đã chốt; Đ-6.17 đổi `ReactionTargetType` → `ReactionTargetKind` (L3) và
     ghi nơi đặt `ModerationTargetType` (L4) — **có ngày**, theo luật "trạng thái các quyết định" đầu tài liệu; Mục 10.1/10.3
     thêm `EVT-04..07`; B.10 #2 ghi "record: có `EVT-07` canh".
   - Mục "Thực tế thi công" cuối file này.
2. **`detect-changes`** (`node .gitnexus/run.cjs detect-changes --scope all --repo .`) — không `partial`/`truncated`. Dự kiến
   thấy `SocialGraphEvents` + `AddSharedKernel`; mức `medium` trở lên thì ghi lý do vào thân commit.
3. **Commit** (một mã việc, một commit — commit-rules Mục 8):

   ```
   feat(gd6-c): C0 — event bus trong tiến trình, sáu record event, SocialGraph phát thật

   Đường ray của Đ-6.4: A (GĐ3) và B (GĐ5) phát event thẳng qua IEventPublisher thay vì lớp "chỉ log".
   - SharedKernel/Events: IEventPublisher, IIntegrationEventHandler<T>, InProcessEventBus (Channel 10.000, DropWrite +
     itemDropped, mỗi handler một scope DI), metric published/dropped trên Meter "SocialApp.Events", DrainAsync cho test
   - Sáu record của Đ-6.17; SocialGraphEvents đổi thân hàm sang Publish, chữ ký lớp giữ nguyên
   Lệch Đ-6.17 (…chốt ngày …): enum ReactionTargetKind thay ReactionTargetType — trùng tên enum của Content.Domain.
   Lệch B.3: …

   Test: Unit … → …, Integration … → … (+EVT-06), Architecture … → …. Thử cho đỏ N đột biến đều bị bắt.
   detect-changes: …
   ```

   **Không** dòng đồng tác giả/ghi công nào ở cuối (commit-rules Mục 6).
4. **PR** `loveart1210 → develop` — chỉ khi được bảo mở; **không** tự merge (pull-request-rules Mục 2):
   - Tiêu đề: `GĐ6: khối C0 — đường ray event trong tiến trình cho GĐ3, GĐ5, GĐ6`
   - `## Loại PR`: `feat`. `## Trước khi merge`: "Không có migration EF mới. Không thêm key `.env`. Không thao tác VPS."
   - `## Có gì`: ba gạch — bus, record, SocialGraph phát thật. Nói rõ PR có kèm tài liệu kế hoạch GĐ6 (A, B cần đọc
     Đ-6.17).
   - `## Bằng chứng`: số test tuyệt đối + bảng đột biến (Mục 13). `## Ảnh màn hình`: N/A — không chạm frontend.
   - Mô tả kết thúc ở checklist, không dòng ghi công.
5. **Tin nhắn cho A và B** — gửi **sau khi merge**, không phải lúc mở PR:

   > Đường ray event đã lên `develop` (PR #…). Rebase rồi phát sau `COMMIT` — **sau** `await tx.CommitAsync()`, không trong
   > khối `await using var tx`:
   > ```csharp
   > publisher.Publish(new CommentCreated(commentId, postId, postAuthorId, parentId, parentAuthorId, actorId, []));
   > ```
   > `IEventPublisher` tiêm qua constructor (singleton). `Publish` không chờ, không ném — không cần try/catch. Record chỉ
   > mang id/enum; chữ ký ở `SharedKernel/Events/`, muốn đổi thì báo trước (chỉ-thêm tham số có mặc định).
   > **A:** enum ở SharedKernel tên `ReactionTargetKind` (tránh trùng `Content.Domain.ReactionTargetType`), ánh xạ một
   > dòng `switch`; `hidden` vẫn phải có trong `ck_comments_status` từ migration đầu (Đ-6.14).
   > **B:** hiện thực `IMessagingEvents` của bạn chỉ cần gọi `Publish(new MessageSent(…))`.

---

## 11. Ranh giới — cái gì **không** thuộc khối C0

| Việc                                                        | Thuộc                         | Ghi chú                                                                                                                                                         |
| ----------------------------------------------------------- | ----------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Handler thông báo nào, kể cả cho `friend_*`                 | **D10**                       | C0 chỉ có handler giả trong test                                                                                                                              |
| Bảng `notification.*`, module Notification có code          | **A2**                        | C0 không migration nào                                                                                                                                        |
| `ModerationTarget`, `IModerationTargets`, `IAuditTrail`     | **C1, C2**                    | C0 chỉ đặt enum `ModerationTargetType` (L4)                                                                                                                   |
| `IAccountStatusReader`                                      | **C5** — *trừ khi B cần sớm*  | B trả lời "cần" ở [0] → kéo **nguyên C5** (interface + hiện thực Identity + `SRCH-05` chưa có thì một test đọc trực tiếp) vào PR này và ghi vào "Thực tế thi công" |
| `hidden` trong `ck_comments_status`                         | **A** (migration đầu của GĐ3) | Chỉ là **thỏa thuận** nhắc trong tin nhắn — C0 không có code nào về nó                                                                                         |
| Gói `prometheus-net`, `/metrics`, dashboard                 | **GĐ7** C1–C4                 | C0 dùng `Meter` có sẵn (L5)                                                                                                                                   |
| Outbox, retry handler, timeout từng handler                 | Không làm                     | Đ-6.2 đã loại outbox; timeout xem Mục 12                                                                                                                      |
| Sửa `SharedKernel/Realtime/` của B                          | Không bao giờ                 | Mục 9.4                                                                                                                                                       |

---

## 12. Khối C0 để lại gì

| Cho ai                  | Để lại                                                                                                                              | Còn nợ                                                                                                                                                                                                                                                 |
| ----------------------- | ----------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| **A (GĐ3)**             | `IEventPublisher`, `CommentCreated`, `ReactionSet`, `ReactionTargetKind`                                                             | —                                                                                                                                                                                                                                                      |
| **B (GĐ5)**             | `IEventPublisher`, `MessageSent`                                                                                                     | —                                                                                                                                                                                                                                                      |
| **D10**                 | `AddIntegrationEventHandler`, event `friend_*` thật, `DrainEventsAsync`                                                              | Test canh "mọi `IIntegrationEventHandler<>` trong assembly module đều đăng ký qua `AddIntegrationEventHandler`" (cạm bẫy C0.1) — viết cùng handler đầu tiên                                                                                          |
| **B1**                  | `EventBusHarness.DrainEventsAsync`                                                                                                   | —                                                                                                                                                                                                                                                      |
| **GĐ7 C2**              | Counter `socialapp_events_published_total`, `…_dropped_total` trên `Meter("SocialApp.Events")`                                        | Kiểm tên thật ở `/metrics` sau khi thêm `prometheus-net`; cảnh báo `dropped > 0` · *2026-09-24: `Meter` không lên `/metrics`, counter đã chuyển về `BusinessMetrics` — xem "Thực tế thi công"* |
| **GĐ8 (k6)**            | Metric `dropped` để đọc dưới tải (R6-10)                                                                                             | **Một handler chậm chặn cả hàng đợi** (một consumer, tuần tự). Nếu k6 thấy `dropped > 0` mà handler chậm là nguyên nhân: thêm timeout từng handler (`CancellationTokenSource.CreateLinkedTokenSource` + `CancelAfter`) trước khi nghĩ tới tăng dung lượng |

---

## 13. Checklist nghiệm thu khối C0

**Code**

- [ ] `SharedKernel/Events/` có đủ ba hợp đồng, `EventHandlerRegistration`, `AddIntegrationEventHandler`, bus, options, metric
- [ ] Sáu record `sealed`, đúng Đ-6.17 (trừ L3); `ModerationTargetType` ở `SharedKernel/Moderation/`
- [ ] `Publish` là `void`; không `await` nào trên đường phát
- [ ] `Channel.CreateBounded(options, itemDropped)` — có callback rơi
- [ ] Mỗi handler một scope, try/catch quanh **từng** handler
- [ ] Không log nào đưa record (hay thuộc tính của nó) vào message/property
- [ ] `AddHostedService(sp => sp.GetRequiredService<InProcessEventBus>())` — không phải `AddHostedService<InProcessEventBus>()`
- [ ] `SocialGraphEvents` gọi `Publish`; tham số đặt tên theo vai; `RelationshipService` không đổi dòng nào
- [ ] SharedKernel không tham chiếu module nào; `ModuleBoundaryTests` xanh

**Test**

- [ ] `EVT-01..05` (unit), `EVT-06` (integration), `EVT-07` (unit) xanh
- [ ] `FRD-*`, `RelationshipServicePostCommitTests` xanh, không đổi khẳng định
- [ ] Không ca nào chờ bằng `Task.Delay`
- [ ] Thử cho đỏ — mỗi đột biến dưới đây bị **đúng** ca tương ứng bắt, `git status` sạch sau khi khôi phục:

| Đột biến                                                                  | Ca đỏ    |
| ------------------------------------------------------------------------- | -------- |
| Bỏ callback `itemDropped`                                                 | `EVT-03` |
| Bỏ try/catch quanh handler (để lỗi lan ra `ExecuteAsync`)                 | `EVT-02` |
| Log `{Event}` với cả record                                               | `EVT-02` |
| Một scope cho cả event thay vì mỗi handler                                | `EVT-04` |
| `AddHostedService<InProcessEventBus>()`                                   | `EVT-05` |
| Đổi chỗ hai id trong `new FriendRequestAccepted(…)`                       | `EVT-06` |
| Giảm `_pending` trước khi dispatch                                        | `EVT-03` (khẳng định "`DrainAsync` chưa xong khi handler còn chạy") |
| Thêm `string Body` vào `CommentCreated`                                   | `EVT-07` |

**Quy trình**

- [ ] `detect-changes` chạy, không `partial`/`truncated`, kết quả vào thân commit
- [ ] `AGENTS.md`, `giai-doan-6.md` (B.3, Đ-6.17, Mục 10) sửa **trong cùng commit**
- [ ] Commit không có dòng ghi công; PR không có dòng ghi công
- [ ] PR chỉ chứa C0 + tài liệu kế hoạch — **không** có commit yaml cố ý đỏ (L8)
- [ ] App chạy local: `/health/ready` xanh; gửi một lời mời qua UI → không lỗi (chưa có handler, không thấy gì thêm — đúng)
- [ ] Đã merge vào `develop` (người trong đội bấm) **trong ngày thứ nhất sau cổng mở**
- [ ] A, B đã nhận tin nhắn Mục 10 bước 5

---

## Thực tế thi công

Thi công 2026-09-23 trên `loveart1210` (fast-forward lên `origin/develop` `9f296a8` trước khi sửa — cây không đổi, chỉ thêm hai
commit merge của PR #21, #22).

**Chỗ lệch L1–L8:** cả tám chốt **đúng như đề xuất**; B.3, Đ-6.2, Đ-6.17 và Mục 10.1 của `giai-doan-6.md` đã sửa theo, mỗi chỗ có
ghi "sửa 2026-09-23 khi thi công C0".

**[0] — bỏ bước chốt với A, B (quyết định của người thi công, 2026-09-23):** không chờ và không hỏi người GĐ3, người GĐ5.
Chữ ký sáu record lấy đúng Đ-6.17 (trừ tên enum theo L3) làm bản chốt; ai cần khác thì đổi theo luật chỉ-thêm của Mục 9.4.
`IAccountStatusReader` **không** kéo vào C0 — C5 giữ chỗ cũ. Bước 5 của C0.8 (tin nhắn cho A, B) không làm.

**Lệch so với chính tài liệu này (đã sửa ở trên):**
- **`EventBusMetrics` là lớp thường, không `static`.** Bus tự dựng nó từ `IMeterFactory` — mỗi container một Meter, test không
  đếm lẫn nhau.
- **C0.6 bước 5:** không dùng được `WithWebHostBuilder` (`ModulesTestClient` đòi đúng kiểu `ModulesApiFactory`). Thêm hook
  `ModulesApiFactory.UseTestServices(Action<IServiceCollection>)`, cùng nếp `UseRedis`. Cạm bẫy "factory chung của lớp" trong bản
  đầu là sai — `IClassFixture` dựng một factory cho mỗi lớp test.
- **Đột biến "giảm `_pending` trước khi dispatch" không bị bắt** trong lượt đầu (0/20 unit, 0/5 `EVT-06` — handler giả đều đồng
  bộ). Thêm vào `EVT-03` khẳng định tất định "`DrainAsync` chưa xong khi handler còn chạy" → bị bắt.
- Thêm hai ca ngoài bảng: `Dang_ky_trung_mot_handler_cho_mot_event_thi_nem_luc_dung` (bus ném lúc dựng khi đăng ký trùng) và
  `Sau_record_cua_D6_17_deu_co_mat` (đổi tên/xóa một record đã chốt với A, B → đỏ; thêm record mới không đỏ).
- `RelationshipServicePostCommitTests` dùng `DiscardingPublisher` (không ghi gì) thay cho `RecordingPublisher` — ca đó canh
  cache nguồn, event đã có `EVT-06`.

**Test:** Unit 295 → 303 (+6 `InProcessEventBusTests`, +2 `IntegrationEventShapeTests`), Integration 483 → 484 (+`EVT-06`),
Architecture 16 → 16. Integration còn **một đỏ nền trên máy dev**, có từ trước C0:
`StartupConfigurationTests.Development_boots_without_r2_config_and_first_use_names_the_variables` (user-secrets có khóa R2; CI
xanh). 100/100 test `SocialGraph` xanh, không đổi khẳng định nào.

**Thử cho đỏ — 8/8 đột biến đều bị bắt**, file khôi phục nguyên byte sau mỗi lượt:

| Đột biến                                      | Ca đỏ thực tế            |
| --------------------------------------------- | ------------------------ |
| Bỏ callback `itemDropped`                     | `EVT-03`                 |
| Bỏ try/catch quanh handler                    | `EVT-02`, `EVT-04`       |
| Log cả record                                 | `EVT-02`                 |
| Một scope cho cả event                        | `EVT-04`                 |
| `AddHostedService<InProcessEventBus>()`       | `EVT-05`                 |
| Đổi chỗ hai id `FriendRequestAccepted`        | `EVT-06`                 |
| Giảm `_pending` trước khi dispatch            | `EVT-03`                 |
| Thêm `string Body` vào `CommentCreated`       | `EVT-07`                 |

**detect-changes:** medium, 4 luồng — `FriendRequestSent/Accepted → Envelope/Published`: đây là thay đổi có chủ đích (hai phương
thức của `SocialGraphEvents` giờ phát qua bus).

**Chưa kiểm:** chạy app local rồi gọi `/health/ready` bằng tay. Host thật (`Program`) đã được bộ integration dựng và gọi hàng
trăm lần, kể cả `EVT-06` đi qua bus thật.

**PR / CI run:** chưa mở.

### Sửa 2026-09-24 — metric chuyển sang prometheus-net (L5 sai trên thực tế)

L5 chọn `System.Diagnostics.Metrics` vì tin rằng "GĐ7 C2 chỉ việc kiểm tên xuất ra ở `/metrics`". GĐ7 C2 đã kiểm, và kết quả là
**không xuất ra**: họ thử bốn cách không được, nên khai counter bằng prometheus-net ở `BusinessMetrics` (`huong-dan-khoi-c-quan-sat.md`
Mục 2). Hai counter của bus không có trong danh sách của họ. Đo lại trước khối D: publish một event, drain xong, `/metrics` chỉ có
`prometheus_net_eventcounteradapter_*`, không có dòng nào của event bus dưới bất kỳ tên nào.

- Hai counter chuyển vào `BusinessMetrics` (`EventPublished(Type)`, `EventDropped(Type)`). `Initialize()` tạo sẵn chuỗi cho mọi
  kiểu `IIntegrationEvent` của SharedKernel bằng phản chiếu, nên thêm record mới là tự có chuỗi.
- Gỡ `EventBusMetrics.cs`, tham số `IMeterFactory` của bus và `services.AddMetrics()` trong `AddInProcessEventBus`.
- Cạm bẫy "`EventBusMetrics` là lớp thường, không `static`" ở trên không còn áp dụng. Counter prometheus-net là của cả process, nên
  `InProcessEventBusTests` đọc bản export của registry (đúng chữ `/metrics` in ra) và khẳng định trên **phần tăng** kể từ đầu ca.
  Nhãn `ProbeEvent` chỉ lớp đó phát, và các ca trong một lớp chạy tuần tự.
- Hai ca mới ở `MetricsEndpointTests`: `Chi_so_event_bus_co_mat_tu_luc_khoi_dong_cho_moi_kieu_event` (có canh gác "phản chiếu tìm
  đúng 6 kiểu") và `Event_da_phat_duoc_dem_tren_metrics`. **Cả hai đỏ với bản cũ** trước khi sửa.

**Test:** Unit 336 → 336, Integration 544 → 546 (+2 `MetricsEndpointTests`), Architecture 23 → 23 + 1 Skip cũ (gỡ ở D2). Còn đỏ
nền R2 trên máy dev.

**Thử cho đỏ — 3/3 đột biến bị bắt**, build 0 lỗi ở mọi lượt, file khôi phục nguyên byte:

| Đột biến                                             | Ca đỏ thực tế                                                        |
| ---------------------------------------------------- | -------------------------------------------------------------------- |
| `Initialize()` không tạo chuỗi `published` cho event | `Chi_so_event_bus_co_mat_tu_luc_khoi_dong_cho_moi_kieu_event`       |
| `Publish` không đếm                                  | `EVT-01`, `Event_da_phat_duoc_dem_tren_metrics`                      |
| `OnDropped` không đếm                                | `EVT-03`                                                             |
