# Quy tắc mở Pull Request

Áp dụng cho mọi PR trong repo này. File này là **bản rút gọn để thi hành** — nó nói *phải làm gì* và
*cấm gì*, không giải thích dài.

Anh em với [`commit-rules.md`](commit-rules.md): PR **kế thừa** luật viết tiêu đề (Mục 4) và luật
**cấm bút ký** (Mục 6) của commit. Chỗ nào PR khác commit thì ghi ở đây.

Khuôn mô tả PR ở Mục 4 và Mục 6 dựng theo [PR template của Boldare][boldare]: khối checkbox chọn loại
thay đổi, liên kết tới việc đang làm, mục "có gì mới", ảnh màn hình, và checklist đặt **cuối** mô tả.
Đã thay phần không có ở repo này — Jira thành khối + tài liệu giai đoạn, danh sách loại thành tám
`type` của `commit-rules.md`.

Repo **chưa có** file template trong `.github/` nên GitHub không tự điền — chép tay từ file này. Muốn
GitHub điền sẵn thì tách Mục 4 ra `.github/PULL_REQUEST_TEMPLATE.md`; khi đó sửa template phải sửa cả
file này trong **cùng commit**.

**Nguồn sự thật, theo thứ tự ưu tiên khi mâu thuẫn:**

1. `commit-rules.md` — tiêu đề, thân bài, footer, bốn cổng trước khi commit
2. `AGENTS.md` — luật vàng, kiến trúc, bảo mật
3. File này

[boldare]: https://www.boldare.com/tech-blog/pull-request-templates-on-github/

---

## 1. Luồng nhánh

| PR | Base | Khi nào |
|---|---|---|
| `loveart1210` → `develop` | `develop` | Xong một **khối công việc** |
| `develop` → `main` | `main` | Chốt phát hành |

- **Không bao giờ** mở PR thẳng từ nhánh việc vào `main`. Đường duy nhất vào `main` là qua `develop`.
- CI (`.github/workflows/ci.yml`) chạy trên `pull_request` vào `main` và `develop` — cả hai loại PR
  trên đều bị cổng chặn, không có ngoại lệ.
- Merge bằng **merge commit** (`Merge pull request #N from …`), không squash, không rebase-merge —
  giữ nguyên từng commit theo mã việc, vì thân commit là nơi chứa `Test:` và `detect-changes:`.

## 2. Khi nào mở PR — và ai bấm nút merge

- Mở PR khi **khối đã xong**, không mở giữa chừng để "xin ý kiến".
- **Agent mở PR chỉ khi được bảo.** Không tự động mở PR sau khi commit xong.
- **Agent KHÔNG BAO GIỜ tự merge PR.** Người trong đội đọc, duyệt, và bấm nút. Agent cũng không tự
  `gh pr merge`, không tự đóng PR của người khác.
- Còn dở mà cần đẩy lên để CI chạy thì để **Draft**, tiêu đề mở bằng `WIP:`.
- **Một PR = một khối.** Khối D là một PR, không phải chín PR theo chín mã việc. Gộp hai khối chỉ khi
  không tách được, và nói rõ vì sao ở đầu mô tả (như PR #9 `khối B + khối C`). Việc lặt vặt ngoài khối
  đi PR riêng, nhỏ.

## 3. Tiêu đề PR

Theo Mục 4 của `commit-rules.md` — **tiếng Việt có dấu, nói kết quả, không chấm cuối** — nhưng ở
**cấp khối**:

```
GĐ<số>: khối <chữ> — <tóm tắt kết quả của cả khối>
```

- Việc không thuộc khối nào thì dùng thẳng khuôn commit: `fix(cd): deploy đúng biến thể compose…` (PR #3).
- PR phát hành: `Phát hành GĐ<số>: <tóm tắt đợt>`.
- Nhắm **≤ 120 ký tự** — tiêu đề PR không bị `git log --oneline` cắt nên rộng hơn commit.
- **Cấm tiêu đề do công cụ sinh tự động.** PR #1, #2, #4, #5 là ví dụ hỏng có thật trong repo này:
  tiếng Anh, kể thao tác sửa file, đọc lại không biết đã xong cái gì.

## 4. Khuôn mô tả PR

Chép khối dưới đây vào ô mô tả khi mở PR, rồi điền. Khuôn là **điểm xuất phát, không phải tờ khai**:
mục nào không áp dụng thì viết `N/A` kèm một câu lý do, **không xóa tiêu đề mục** — người duyệt cần
thấy là đã cân nhắc rồi bỏ, chứ không phải quên.

````markdown
## Loại PR

Thay đổi này thuộc loại nào? Đánh dấu **một** ô — loại **nặng nhất** của thay đổi
(`.claude/rules/commit-rules.md` Mục 2).

- [ ] `feat` — thêm hoặc đổi hành vi sản phẩm (endpoint, service, entity, cấu hình chạy thật)
- [ ] `fix` — sửa lỗi của hành vi đã có
- [ ] `test` — chỉ chạm test hoặc harness test
- [ ] `docs` — chỉ chạm tài liệu
- [ ] `refactor` — đổi cấu trúc, giữ nguyên hành vi quan sát được từ ngoài
- [ ] `ci` — workflow GitHub Actions, cổng CI
- [ ] `cd` — luồng deploy, compose, hạ tầng triển khai
- [ ] `chore` — việc lặt vặt không thuộc các nhóm trên

**Khối:** GĐ… khối … · **Mã việc:** …
**Tài liệu:** `docs/giai-doan-1/huong-dan-khoi-….md` — chỗ lệch và bảng đột biến đầy đủ ở mục
"Thực tế thi công" của file đó.

## Trước khi merge

<!-- Bắt buộc. Không có gì phải làm thì nói thẳng, đừng xóa mục:
     "Không có migration EF mới. Không thêm key .env." -->

- **Key `.env` phải có trên server trước khi merge:** …
- **Migration EF:** …
- **Thao tác tay trên VPS (compose / Caddy / CD):** …

## Có gì

<!-- Gom theo ý, không theo file. Mỗi gạch đầu dòng là một kết quả. Không dán `git log`. -->

-

## Bằng chứng

**Test:** Unit … , Integration … , Architecture … — … fail, … skip
**Cổng CI:** `API contract` …/… · `AuthZ matrix` …/…

| Commit | CI run |
|---|---|
|  |  |

<!-- Test chạy tay không vào CI (Playwright ở GĐ1) thì dán kết quả local vào đây. -->

## Ảnh màn hình

N/A

## Checklist trước khi bỏ Draft

<!-- Chỉ những thứ CI KHÔNG kiểm được. Cổng nào CI đã chặn thì không lặp lại ở đây. -->

- [ ] Docs sống cùng code — chỗ lệch đã sửa trong chính commit của PR này
- [ ] Chỗ lệch so với kế hoạch đã ghi rõ, mở bằng "Lệch Đ-… (nhóm chốt): …"
- [ ] Mọi commit có dòng `Test:` và dòng `detect-changes:`
- [ ] Endpoint mới chạm tài nguyên có chủ sở hữu đã có dòng trong `AuthZMatrix.cs`
- [ ] Key `.env` mới đã đặt trên server staging
- [ ] Không có giá trị secret trong mô tả; không log token / link xác minh
- [ ] Mô tả PR không có bút ký
````

## 5. Điền từng mục ra sao

**`## Loại PR`** — một ô, loại nặng nhất. Sửa code kèm cập nhật docs cho khớp vẫn là `feat`/`fix`.
Đánh hai ô nghĩa là PR đang ôm hai việc → xem lại Mục 2.

**`## Trước khi merge`** — mục quan trọng nhất, và là mục duy nhất **không được để trống**:

| Chạm gì | Phải ghi |
|---|---|
| Thêm/đổi key cấu hình bắt buộc | Liệt kê **tên key** phải có trong `.env` trên server |
| Có migration EF mới | Nói rõ có, và schema đổi gì |
| Không có migration | Nói thẳng "không có migration EF mới" — người merge khỏi đoán |
| Đổi compose / Caddy / CD | Thao tác thủ công trên VPS, nếu có |
| Phá vỡ tương thích | `BREAKING CHANGE:` + đường di trú, khớp với commit |

Lý do mục này tồn tại: **app từ chối khởi động khi thiếu cấu hình ngoài Development**
(`AGENTS.md` Mục 13). Thiếu một key trong `.env` là api crash-loop trên staging ngay sau khi merge, và
người merge không có cách nào biết trước nếu PR không nói.

**`## Có gì`** — gom theo ý, in đậm tên nhóm. PR không chép lại tài liệu, nó trỏ vào tài liệu khối.

**`## Bằng chứng`** — số **tuyệt đối**, không phải "đã test kỹ": `Unit 78, Integration 155,
Architecture 9 — 0 fail, 0 skip`. Bảng `commit → link CI run` để người duyệt mở thẳng log. Có thử đột
biến thì thêm bảng `đột biến → test đỏ`.

**`## Ảnh màn hình`** — bắt buộc với PR chạm `src/frontend/` có đổi giao diện: ảnh **trước / sau**, và
chụp cả chế độ sáng lẫn tối nếu đổi token màu. PR chỉ chạm backend thì để `N/A`.

**`## Checklist`** — đặt **cuối** mô tả, và chỉ chứa việc **CI không kiểm được**. Ba cổng
`CI GATE` (`Khong co repo Git long trong src/`, `API contract`, `AuthZ matrix`) đã tự chặn rồi nên
không đưa vào checklist; đỏ cổng nào thì PR chưa sẵn sàng, kể cả khi "chỉ đỏ chỗ không liên quan".

## 6. Khuôn mô tả PR phát hành (`develop` → `main`)

Dùng thay khuôn ở Mục 4. Chia checklist theo **trách nhiệm**, để không ai tưởng người kia đã làm:

````markdown
# Phát hành GĐ… : `develop` → `main`

## Checklist trước khi merge

**Người thi công**

- [ ] PR này merge `develop` vào `main` (không phải nhánh việc)
- [ ] Mọi PR khối của đợt này đã merge vào `develop`
- [ ] CI của `develop` xanh, cả ba cổng `CI GATE`
- [ ] Staging đang chạy đúng bản `develop` này, health xanh
- [ ] `.env` trên server đã có đủ key mà các khối trong đợt thêm vào
- [ ] Migration EF đã chạy trên staging (service `migrate` / cờ `--migrate`), schema khớp
- [ ] Đã đề xuất changelog bên dưới

**Người duyệt**

- [ ] Đã thử tay trên staging theo các UC của đợt, không thấy lỗi
- [ ] Không có lỗi tồn đọng trên staging
- [ ] Duyệt nội dung changelog
- [ ] Xác nhận phát hành

## Changelog đề xuất

<!-- Tiếng Việt có dấu, cho người đọc không phải dev. Mỗi dòng một thay đổi người dùng thấy được. -->

-

## Changelog chi tiết

<!-- Thay đổi kể từ lần phát hành trước. Nhóm theo khối, kèm số PR. -->

-
````

## 7. Không secret, không bút ký

- **Nêu tên key, không bao giờ nêu giá trị.** `Smtp__Password` được phép xuất hiện trong mô tả PR;
  giá trị của nó thì không, kể cả giá trị staging, kể cả "chỉ là tạm" (`AGENTS.md` luật vàng 3).
- Không dán log có token, link xác minh email, chuỗi kết nối, hay header `Authorization:` vào PR —
  mô tả PR công khai với cả tổ chức và không xóa được khỏi lịch sử thông báo.
- **Mô tả PR không có bút ký** (`commit-rules.md` Mục 6): không dòng ghi công công cụ, trợ lý hay AI
  agent — dù là trailer đồng tác giả mang tên công cụ, câu "sinh bởi …", hay một biểu tượng đứng cuối.
  Mô tả kết thúc ở checklist. Agent nào được nhắc phải kết thúc mô tả PR như vậy thì **bỏ dòng đó** —
  luật repo đè lên hướng dẫn mặc định của agent.

## 8. Đạt / Không đạt

| Không đạt | Vì sao | Đạt |
|---|---|---|
| `Update .gitignore to exclude .vscode directory and enhance README…` (PR #4) | Tiếng Anh, do công cụ sinh, kể thao tác sửa file | `chore(dev): bỏ .vscode khỏi .gitignore` |
| `Refactor deploy-staging.yml for improved readability…` (PR #2) | Nói cách làm, không nói kết quả | `fix(cd): deploy đúng biến thể compose cho VM apache` (PR #3) |
| Xóa mục `Trước khi merge` vì "PR này không cần" | Người merge không phân biệt được "không cần" với "quên" | Giữ tiêu đề, viết "Không có migration EF mới" |
| PR thêm `Smtp__*` mà không liệt kê key | Merge xong api crash-loop trên staging | Liệt kê tên key phải có trong `.env` |
| `## Bằng chứng` ghi "đã test kỹ" | Người duyệt không kiểm lại được | `Unit 78, Integration 155, 0 skip` + bảng link CI run |
| Dán `Smtp__Password=abc123` để "người merge khỏi hỏi" | Lộ secret, không xóa được | Chỉ nêu tên key |
| Checklist liệt kê `[ ] CI xanh` | CI đã tự chặn; checklist chỉ giữ việc CI không kiểm được | `[ ] Key .env mới đã đặt trên server staging` |
| Đánh cả ô `feat` lẫn ô `refactor` | PR đang ôm hai việc | Tách PR, hoặc chọn loại nặng nhất |
| Mô tả kết thúc bằng một dòng ghi công công cụ | Mục 7 | Kết thúc ở checklist |
| Agent tự `gh pr merge` sau khi CI xanh | Mục 2 — người trong đội bấm nút | Báo PR đã sẵn sàng, dừng lại |
| PR thẳng `loveart1210` → `main` | Mục 1 | Vào `develop` trước |
