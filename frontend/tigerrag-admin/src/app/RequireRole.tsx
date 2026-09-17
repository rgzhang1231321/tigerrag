import type { ReactNode } from 'react'
import { useAuthStore } from '../features/auth/authStore'
import { can, hasAnyRole } from '../features/auth/permissions'
import type { Permission, Role } from '../features/auth/permissions'
import { ForbiddenPage } from './ForbiddenPage'

interface RequireRoleProps {
  /// <summary>满足任一角色即可放行；与 permission 二选一，至少传一项。</summary>
  roles?: readonly Role[]
  /// <summary>满足指定权限码即可放行；与 roles 二选一，至少传一项。</summary>
  permission?: Permission
  /// <summary>守卫通过时渲染的子树。</summary>
  children: ReactNode
}

/// <summary>
/// 路由级权限守卫：内存中无用户或权限不足时渲染 ForbiddenPage 阻止直接 URL 访问。
/// 与菜单门控组合实现"不可见 + 不可达"双重保护；不重新触发 fetch，不修改会话。
/// </summary>
export function RequireRole({ roles, permission, children }: RequireRoleProps) {
  const user = useAuthStore((state) => state.user)

  if (user === null) {
    return <ForbiddenPage />
  }

  const passesRole = roles !== undefined && hasAnyRole(user, roles)
  const passesPermission = permission !== undefined && can(user, permission)
  if (!passesRole && !passesPermission) {
    return <ForbiddenPage />
  }

  return <>{children}</>
}