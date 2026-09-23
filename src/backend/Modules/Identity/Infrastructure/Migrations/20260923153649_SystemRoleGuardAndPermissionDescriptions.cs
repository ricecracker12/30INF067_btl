using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SocialApp.Modules.Identity.Infrastructure.Migrations
{
    /// <summary>
    /// GĐ6 A3 — ba món nợ GĐ1 Mục 2 ở tầng dữ liệu (Đ-6.9):
    /// <list type="number">
    /// <item>Sequence <c>roles_role_id_seq</c> (từ 100) cho vai trò tự tạo — EF sinh từ <c>HasSequence</c>.</item>
    /// <item>Mô tả cho các mã quyền của DB ĐÃ seed trước migration này (staging, dev đang chạy). DB mới nhận mô tả từ seeder —
    /// migration chạy trước seeder nên ở DB mới câu UPDATE chạm 0 dòng (L-A2).</item>
    /// <item>Trigger <c>trg_roles_protect_system</c>: lớp chặn thứ ba cho ADMIN/USER/MODERATOR — chặn cả psql gõ tay.</item>
    /// </list>
    /// KHÔNG có <c>ADD COLUMN description</c> như B.4 A3 viết: cột đã có từ <c>InitialIdentity</c> (L-A1).
    ///
    /// Mọi chuỗi (mô tả, mã vai trò) viết THẲNG, không đọc <c>PermissionCodes</c>/<c>RoleCodes</c>: migration là ảnh chụp bất biến
    /// của lúc nó chạy trên staging — sửa hằng C# sau này không được lặng lẽ đổi nghĩa một migration đã chạy.
    /// </summary>
    public partial class SystemRoleGuardAndPermissionDescriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence<short>(
                name: "roles_role_id_seq",
                schema: "identity",
                startValue: 100L);

            // "AND description IS NULL": không đè mô tả ai đó đã sửa tay trên staging.
            migrationBuilder.Sql("""
                UPDATE identity.permissions SET description = 'Xem bài viết công khai' WHERE code = 'post.read.public' AND description IS NULL;
                UPDATE identity.permissions SET description = 'Xem bài viết chỉ dành cho bạn bè' WHERE code = 'post.read.friends' AND description IS NULL;
                UPDATE identity.permissions SET description = 'Đăng bài viết' WHERE code = 'post.create' AND description IS NULL;
                UPDATE identity.permissions SET description = 'Sửa bài viết của mình' WHERE code = 'post.update' AND description IS NULL;
                UPDATE identity.permissions SET description = 'Xóa bài viết của mình' WHERE code = 'post.delete' AND description IS NULL;
                UPDATE identity.permissions SET description = 'Ẩn và khôi phục nội dung vi phạm của người khác' WHERE code = 'post.hide' AND description IS NULL;
                UPDATE identity.permissions SET description = 'Bình luận' WHERE code = 'comment.create' AND description IS NULL;
                UPDATE identity.permissions SET description = 'Bày tỏ cảm xúc' WHERE code = 'reaction.set' AND description IS NULL;
                UPDATE identity.permissions SET description = 'Gửi lời mời kết bạn và theo dõi' WHERE code = 'friend.request' AND description IS NULL;
                UPDATE identity.permissions SET description = 'Chấp nhận lời mời kết bạn' WHERE code = 'friend.respond' AND description IS NULL;
                UPDATE identity.permissions SET description = 'Nhắn tin' WHERE code = 'message.send' AND description IS NULL;
                UPDATE identity.permissions SET description = 'Báo cáo nội dung hoặc tài khoản vi phạm' WHERE code = 'report.create' AND description IS NULL;
                UPDATE identity.permissions SET description = 'Xem hàng đợi và xử lý báo cáo vi phạm' WHERE code = 'report.resolve' AND description IS NULL;
                UPDATE identity.permissions SET description = 'Khóa tài khoản' WHERE code = 'user.lock' AND description IS NULL;
                UPDATE identity.permissions SET description = 'Mở khóa tài khoản' WHERE code = 'user.unlock' AND description IS NULL;
                UPDATE identity.permissions SET description = 'Gán vai trò cho tài khoản' WHERE code = 'role.assign' AND description IS NULL;
                UPDATE identity.permissions SET description = 'Xem nhật ký kiểm toán toàn hệ thống' WHERE code = 'audit.read' AND description IS NULL;
                UPDATE identity.permissions SET description = 'Tạo, đổi tên, sửa quyền và xóa vai trò' WHERE code = 'role.manage' AND description IS NULL;
                """);

            // Đ-6.9 lớp chặn 3 (hai lớp trên: API không nhận code, service trả 409). Ba vai trò hệ thống không đổi code, không
            // xóa được — kể cả psql. "UPDATE OF code" + so IS DISTINCT FROM: đổi display_name vẫn được, và UPDATE giữ nguyên code
            // không bị chặn oan. WHEN chỉ ba mã hệ thống: vai trò tự tạo đổi/xóa tự do (D5 lo phần còn người mang).
            // Ba mã gõ thẳng — RoleCodes.All ở thời điểm viết (xem summary).
            migrationBuilder.Sql("""
                CREATE FUNCTION identity.roles_protect_system() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' OR NEW.code IS DISTINCT FROM OLD.code THEN
                        RAISE EXCEPTION 'Vai trò hệ thống % không đổi mã, không xóa được', OLD.code USING ERRCODE = 'P0001';
                    END IF;
                    RETURN NEW;
                END $$;

                CREATE TRIGGER trg_roles_protect_system BEFORE UPDATE OF code OR DELETE ON identity.roles
                    FOR EACH ROW WHEN (OLD.code IN ('ADMIN', 'USER', 'MODERATOR'))
                    EXECUTE FUNCTION identity.roles_protect_system();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // KHÔNG xóa mô tả: đó là dữ liệu, không phải schema.
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_roles_protect_system ON identity.roles;
                DROP FUNCTION IF EXISTS identity.roles_protect_system();
                """);

            migrationBuilder.DropSequence(
                name: "roles_role_id_seq",
                schema: "identity");
        }
    }
}
