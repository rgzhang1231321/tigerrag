import type { AuthUser } from './authStore'

/// <summary>前端固定角色；与后端 SystemRoles 严格一致。</summary>
export type Role = 'Admin' | 'KbManager' | 'Editor' | 'Viewer' | 'Auditor'

/// <summary>所有固定角色，按字母序排列，供下拉与测试断言使用。</summary>
export const ALL_ROLES: readonly Role[] = [
  'Admin',
  'Auditor',
  'Editor',
  'KbManager',
  'Viewer',
]

/// <summary>判断用户是否持有某个角色；自定义角色以普通字符串传入，不再限定系统保留集。</summary>
export function hasRole(user: AuthUser | null, role: string): boolean {
  return user !== null && user.roles.includes(role)
}

/// <summary>判断用户是否持有任一所需角色；空数组返回 false。接受任意角色字符串，覆盖自定义角色。</summary>
export function hasAnyRole(user: AuthUser | null, roles: readonly string[]): boolean {
  if (user === null || roles.length === 0) {
    return false
  }
  return roles.some((role) => user.roles.includes(role))
}
