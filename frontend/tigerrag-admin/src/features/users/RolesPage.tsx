import { RolePermissionMatrix } from './RolePermissionMatrix'

/// <summary>角色管理页：只读展示角色 → 权限映射。</summary>
export function RolesPage() {
  return (
    <main>
      <h2 className="page-heading">角色管理</h2>
      <RolePermissionMatrix />
    </main>
  )
}
