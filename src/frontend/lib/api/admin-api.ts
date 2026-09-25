import { BFF_ROUTES } from "./bff-contract"
import { request } from "./http"
import type * as T from "./types"

// Kiểu payload lấy từ hợp đồng `admin-v1.yaml` (nhóm thứ hai của Identity, Đ-6.1): yaml đổi field là các dòng dưới đỏ compile.
// Mười một endpoint đi qua proxy chung `/bff/api/*` (Đ-E14) — không route BFF mới.
//
// Màn quản trị KHÔNG optimistic (Đ-6.21): mọi thao tác ghi trả trạng thái mới của server, màn vẽ theo đó. 409 có năm `type`
// (`last-admin`, `confirmation-required`, `system-role`, `role-in-use`, `role-code-taken`) — màn phân nhánh theo `type`.

type PageOpts = { cursor?: string | null; limit?: number }

const id = (value: string | number) => encodeURIComponent(String(value))
const users = `${BFF_ROUTES.api}/admin/users`
const roles = `${BFF_ROUTES.api}/admin/roles`

/** Lọc danh sách tài khoản — `q` là TIỀN TỐ email (citext); `status` chỉ `active` | `disabled` (Mục 8.2). */
export type AdminUserFilter = {
  q?: string
  status?: "active" | "disabled"
  roleCode?: string
}

export const adminApi = {
  /** Mới tạo trước. Hết dữ liệu KHI VÀ CHỈ KHI `nextCursor === null`. */
  listUsers: (
    filter: AdminUserFilter = {},
    opts: PageOpts = {},
    signal?: AbortSignal
  ) => {
    const params = new URLSearchParams()
    for (const [key, value] of Object.entries(filter))
      if (value) params.set(key, value)
    if (opts.cursor) params.set("cursor", opts.cursor)
    if (opts.limit !== undefined) params.set("limit", String(opts.limit))
    const qs = params.size > 0 ? `?${params.toString()}` : ""
    return request<T.AdminUserPage>(`${users}${qs}`, { signal })
  },

  getUser: (userId: string, signal?: AbortSignal) =>
    request<T.AdminUser>(`${users}/${id(userId)}`, { signal }),

  /** Tự khóa → 400 `errors.userId`; làm hệ thống còn 0 Admin → 409 `last-admin`. `revocation` nói thật về Redis (Đ-6.6). */
  lock: (userId: string, reason: string) =>
    request<T.AdminUserChange>(`${users}/${id(userId)}/lock`, {
      method: "POST",
      body: { reason } satisfies T.LockRequest,
    }),

  unlock: (userId: string) =>
    request<T.AdminUserChange>(`${users}/${id(userId)}/unlock`, {
      method: "POST",
    }),

  /** Chạm ADMIN cần thêm `role.manage` (L-D18) — thiếu → 403. `roleCode` lạ → 400 `errors.roleCode`. */
  assignRole: (userId: string, roleCode: string) =>
    request<T.AdminUserChange>(`${users}/${id(userId)}/role`, {
      method: "PUT",
      body: { roleCode } satisfies T.AssignRoleRequest,
    }),

  /** Mọi vai trò + quyền hiệu lực + số người mang. ADMIN: cả 18 mã, `editable: false`. */
  listRoles: (signal?: AbortSignal) =>
    request<T.RoleSummary[]>(roles, { signal }),

  createRole: (body: T.CreateRoleRequest) =>
    request<T.RoleSummary>(roles, { method: "POST", body }),

  /** CHỈ `displayName` — body có `code` là 400 (Đ-6.9), nên kiểu request không có chỗ cho nó. */
  renameRole: (roleId: number, displayName: string) =>
    request<T.RoleSummary>(`${roles}/${id(roleId)}`, {
      method: "PATCH",
      body: { displayName } satisfies T.RenameRoleRequest,
    }),

  /**
   * Thay CẢ tập quyền. USER/MODERATOR không `confirm` → 409 `confirmation-required` kèm `added`, `removed`, `affectedUsers`
   * — màn hiện đúng các số đó rồi gửi lại với `confirm: true`. ADMIN → 409 `system-role`.
   */
  setPermissions: (roleId: number, body: T.SetRolePermissionsRequest) =>
    request<T.RoleSummary>(`${roles}/${id(roleId)}/permissions`, {
      method: "PUT",
      body,
    }),

  /** 204. Vai trò hệ thống → 409 `system-role`; còn người mang → 409 `role-in-use`. */
  deleteRole: (roleId: number) =>
    request<void>(`${roles}/${id(roleId)}`, { method: "DELETE" }),

  /** 18 mã + mô tả + `assignable` (`role.manage` không trao được qua API — L-D19). */
  listPermissions: (signal?: AbortSignal) =>
    request<T.PermissionInfo[]>(`${BFF_ROUTES.api}/admin/permissions`, {
      signal,
    }),
}
