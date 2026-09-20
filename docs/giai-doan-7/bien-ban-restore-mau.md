# Biên bản diễn tập khôi phục dữ liệu

> Chép file này thành `bien-ban-restore-YYYY-MM-DD.md` rồi điền. Đây là **sản phẩm được chấm** (NFR-REL-02),
> không phải ghi chú nội bộ — mọi ô đều phải có số thật, không có "≈" hay "khoảng".

- Ngày giờ thực hiện (giờ VN):
- Người thực hiện / người chứng kiến:
- Môi trường đích: stack `socialapp-restore` trên VM staging (127.0.0.1:15432) — **không** phải DB đang chạy
- Kịch bản giả định: *(ví dụ: "mất toàn bộ VM lúc 09:30, chỉ còn bản sao trên R2")*

## 1. Bản sao được dùng

| Khoản | Giá trị |
|---|---|
| Tên bản sao (`backups/base/<tên>`) | |
| Thời điểm tạo (từ tên, UTC) | |
| `base.tar.gz` — kích thước · sha256 (từ `backup.log`) | |
| Khoảng WAL có trong `backups/wal/` (file đầu … file cuối) | |
| Nguồn lấy về | ☐ đã có trên VM ☐ `rclone copy` từ R2 (thời gian tải: … ) |
| `recovery_target_time` (nếu PITR) | |

## 2. Mốc thời gian

| Bước | Bắt đầu | Kết thúc | Thời lượng | Ghi chú |
|---|---|---|---|---|
| Tải bản sao về (nếu từ R2) | | | | |
| `restore.sh` — nạp base vào volume trống | | | | |
| Tua WAL tới mốc + promote | | | | số file WAL đã tua: |
| Chạy `--migrate` kiểm tra | | | | phải in "không có gì để áp dụng" |
| Đối chiếu dữ liệu (Mục 3) | | | | |
| **Tổng — RTO thực đo** | | | **…** | **cam kết ≤ 2 giờ** |

## 3. Đối chiếu dữ liệu

Chạy `deploy/dem-ban-ghi.sql` trên DB gốc **trước khi** bắt đầu, và trên DB khôi phục sau khi promote.

| Bảng | DB gốc (lúc …) | Sau khôi phục | Khớp |
|---|---|---|---|
| identity.users | | | ☐ |
| identity.refresh_tokens | | | ☐ |
| profile.profiles | | | ☐ |
| content.posts | | | ☐ |
| content.media_attachments | | | ☐ |
| *(thêm mọi bảng còn lại mà script in ra)* | | | ☐ |

- Bản ghi **mới nhất** khôi phục được (`max(created_at)` của bảng bận nhất): …
- Thời điểm sự cố giả định: …
- **RPO thực đo** (khoảng cách giữa hai mốc trên): … · **cam kết ≤ 15 phút**
- Lệch (nếu có) và lý do: …

## 4. Sự cố gặp phải trong lúc diễn tập

Ghi cả những thứ đã tự khắc phục được. Đây là phần có giá trị nhất của biên bản — diễn tập không gặp sự cố
nào thường có nghĩa là diễn tập chưa đủ thật.

| # | Chuyện gì xảy ra | Nguyên nhân | Đã sửa thế nào | Đã sửa vào runbook/script chưa |
|---|---|---|---|---|
| 1 | | | | ☐ |

## 5. Kết luận

- [ ] Đạt RPO ≤ 15 phút (số thực đo: … )
- [ ] Đạt RTO ≤ 2 giờ (số thực đo: … )
- [ ] Số bản ghi mọi bảng khớp
- [ ] `--migrate` trên DB khôi phục là no-op
- [ ] `runbook-khoi-phuc.md` đã cập nhật theo những gì học được
- [ ] Stack `socialapp-restore` đã `down -v` sau diễn tập

Việc phải làm sau diễn tập:

Chữ ký người thực hiện: ………………  Người chứng kiến: ………………
