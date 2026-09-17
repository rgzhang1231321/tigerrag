import type { AuthUser } from './authStore'

/// <summary>前端固定角色；与后端 SystemRoles 严格一致。</summary>
export type Role = 'Admin' | 'KbManager' | 'Editor' | 'Viewer' | 'Auditor'

/// <summary>前端权限代码；与后端 SystemPermissions 严格一致。</summary>
export type Permission =
  | 'users.manage'
  | 'knowledge-bases.manage'
  | 'documents.manage'
  | 'chat.use'
  | 'audit.read'

/// <summary>角色 → 权限集合的固定映射。任一调整必须同步后端 RolePermissionMap.cs 并改 permissions.test.ts 里的快照断言。</summary>
const PERMISSIONS_BY_ROLE: Readonly<Record<Role, ReadonlySet<Permission>>> = {
  Admin: new Set<Permission>([
    'users.manage',
    'knowledge-bases.manage',
    'documents.manage',
    'chat.use',
    'audit.read',
  ]),
  KbManager: new Set<Permission>([
    'knowledge-bases.manage',
    'documents.manage',
    'chat.use',
  ]),
  Editor: new Set<Permission>(['documents.manage', 'chat.use']),
  Viewer: new Set<Permission>(['chat.use']),
  Auditor: new Set<Permission>(['audit.read']),
}

/// <summary>权限码 → 中文显示标签。展示给管理员看时统一用中文，原始码仅作为 value 与日志保留。</summary>
export const PERMISSION_LABELS: Readonly<Record<Permission, string>> = {
  'users.manage': '用户管理',
  'knowledge-bases.manage': '知识库管理',
  'documents.manage': '文档管理',
  'chat.use': '问答使用',
  'audit.read': '审计日志',
}

/// <summary>把后端返回的权限码翻译成中文标签；未知码或 null 原样返回，保留调试信息。</summary>
export function permissionLabel(code: string | null | undefined): string {
  if (code === null || code === undefined) {
    return '所有人'
  }
  return PERMISSION_LABELS[code as Permission] ?? code
}

/// <summary>所有固定角色，按字母序排列，供下拉与测试断言使用。</summary>
export const ALL_ROLES: readonly Role[] = [
  'Admin',
  'Auditor',
  'Editor',
  'KbManager',
  'Viewer',
]

/// <summary>判断用户是否持有某个固定角色。未知角色字符串视为不持有。</summary>
export function hasRole(user: AuthUser | null, role: Role): boolean {
  return user !== null && user.roles.includes(role)
}

/// <summary>判断用户是否持有任一所需角色；空数组返回 false。</summary>
export function hasAnyRole(user: AuthUser | null, roles: readonly Role[]): boolean {
  if (user === null || roles.length === 0) {
    return false
  }
  return roles.some((role) => user.roles.includes(role))
}

/// <summary>聚合用户所有角色对应的权限集合，用于权限门控判断。</summary>
export function listPermissions(user: AuthUser | null): ReadonlySet<Permission> {
  if (user === null) {
    return new Set()
  }
  const merged = new Set<Permission>()
  for (const role of user.roles) {
    const permissions = PERMISSIONS_BY_ROLE[role as Role]
    if (permissions !== undefined) {
      for (const permission of permissions) {
        merged.add(permission)
      }
    }
  }
  return merged
}

/// <summary>判断用户是否拥有某项权限。null 用户一律拒绝。</summary>
export function can(user: AuthUser | null, permission: Permission): boolean {
  return listPermissions(user).has(permission)
}