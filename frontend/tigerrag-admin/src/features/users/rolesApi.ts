import { http } from '../../app/http'

export interface RoleDto {
  name: string
  isSystem: boolean
}

/// <summary>列出全部角色（含系统保留集）；用于角色管理 UI 与权限矩阵列渲染。</summary>
export function listRolesAll(): Promise<RoleDto[]> {
  return http<RoleDto[]>('/api/roles/list', { method: 'POST' })
}

/// <summary>新建自定义角色；name 由服务端校验 PascalCase 与唯一性。</summary>
export function createRole(input: { name: string }): Promise<RoleDto> {
  return http<RoleDto>('/api/roles', { method: 'POST', body: input })
}

/// <summary>删除自定义角色；系统保留集由服务端拒绝。路径段做 URL 编码以兼容大小写角色名。</summary>
export function deleteRole(name: string): Promise<null> {
  return http<null>(`/api/roles/${encodeURIComponent(name)}/delete`, { method: 'POST' })
}