import type { AuthUser } from './authStore'

/// <summary>角色字符串类型；所有角色平等，无编译期固定联合。后端 /api/roles/list 返回值动态决定可用角色名。</summary>
export type Role = string

/// <summary>固定角色集合占位（已无系统保留集）；下拉等场景改用 useRoles() 取运行时列表。</summary>
export const ALL_ROLES: readonly Role[] = []

/// <summary>判断用户是否持有某个角色；接受任意角色字符串，覆盖自定义角色。</summary>
export function hasRole(user: AuthUser | null, role: string): boolean {
  return user !== null && user.roles.includes(role)
}

/// <summary>判断用户是否持有任一所需角色；空数组返回 false。接受任意角色字符串。</summary>
export function hasAnyRole(user: AuthUser | null, roles: readonly string[]): boolean {
  if (user === null || roles.length === 0) {
    return false
  }
  return roles.some((role) => user.roles.includes(role))
}
