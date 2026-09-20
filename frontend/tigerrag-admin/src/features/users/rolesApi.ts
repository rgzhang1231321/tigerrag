import { http } from '../../app/http'

export interface RoleDto {
  name: string
  isSystem: boolean
  userCount: number
  menuCount: number
  menuNames: string[]
}

/// <summary>列出全部角色（含系统保留 + DB 自定义）；含用户引用数、菜单引用数与菜单名称列表，供角色管理 UI 展示与删除闸提示。</summary>
export function listRolesAll(): Promise<RoleDto[]> {
  return http<RoleDto[]>('/api/roles/list', { method: 'POST' })
}

/// <summary>新建自定义角色；name 由服务端校验 PascalCase 与唯一性。</summary>
export function createRole(input: { name: string }): Promise<RoleDto> {
  return http<RoleDto>('/api/roles', { method: 'POST', body: input })
}

/// <summary>重命名角色；受角色管理事务保护（Admin 禁改名、新名不重、轮换受影响用户 stamp）。</summary>
export function updateRole(name: string, input: { name: string }): Promise<RoleDto> {
  return http<RoleDto>(`/api/roles/${encodeURIComponent(name)}/rename`, {
    method: 'POST',
    body: input,
  })
}

/// <summary>删除自定义角色；系统保留集由服务端拒绝。路径段做 URL 编码以兼容大小写角色名。</summary>
export function deleteRole(name: string): Promise<null> {
  return http<null>(`/api/roles/${encodeURIComponent(name)}/delete`, { method: 'POST' })
}