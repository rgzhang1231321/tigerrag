import type { ReactNode } from 'react'
import { useAuthStore } from '../features/auth/authStore'
import { hasAnyRole } from '../features/auth/permissions'
import { ForbiddenPage } from './ForbiddenPage'

interface RequireRoleProps {
  /// <summary>满足任一角色即可放行；支持自定义角色字符串。</summary>
  roles?: readonly string[]
  /// <summary>守卫通过时渲染的子树。</summary>
  children: ReactNode
}

/// <summary>
/// 路由级角色守卫：内存中无用户或角色不足时渲染 ForbiddenPage 阻止直接 URL 访问。
/// 与菜单门控组合实现"不可见 + 不可达"双重保护；不重新触发 fetch，不修改会话。
/// </summary>
export function RequireRole({ roles, children }: RequireRoleProps) {
  const user = useAuthStore((state) => state.user)

  if (user === null) {
    return <ForbiddenPage />
  }

  if (roles !== undefined && !hasAnyRole(user, roles)) {
    return <ForbiddenPage />
  }

  return <>{children}</>
}
