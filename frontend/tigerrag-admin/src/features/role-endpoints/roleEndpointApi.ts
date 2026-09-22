import { http } from '../../app/http'

export interface EndpointGrantViewDto {
  endpointKey: string
  description: string
  httpMethod: string
  path: string
  granted: boolean
}

export interface MenuGroupDto {
  menuKey: string
  endpoints: EndpointGrantViewDto[]
}

export interface RoleEndpointMatrixDto {
  menus: MenuGroupDto[]
}

export interface ToggleEndpointRequest {
  endpointKey: string
  menuKey: string
  grant: boolean
}

/// <summary>单条目标授权变更：granted=true 落地为授权行；false 不落库（视为撤销目标）。</summary>
export interface BatchEndpointChange {
  menuKey: string
  endpointKey: string
  granted: boolean
}

export interface BatchGrantsRequest {
  endpoints: BatchEndpointChange[]
}

/// <summary>列出角色在全部已知 endpoint 上的授权矩阵（按菜单分组）。</summary>
export function listRoleGrants(role: string): Promise<RoleEndpointMatrixDto> {
  return http<RoleEndpointMatrixDto>(`/api/roles/${encodeURIComponent(role)}/grants`, { method: 'POST' })
}

/// <summary>授予某个菜单下的全部 endpoint。</summary>
export function grantMenu(role: string, menuKey: string): Promise<number> {
  return http<number>(`/api/roles/${encodeURIComponent(role)}/menus/${encodeURIComponent(menuKey)}/grant`, { method: 'POST' })
}

/// <summary>撤销某个菜单下的全部 endpoint。</summary>
export function revokeMenu(role: string, menuKey: string): Promise<number> {
  return http<number>(`/api/roles/${encodeURIComponent(role)}/menus/${encodeURIComponent(menuKey)}/revoke`, { method: 'POST' })
}

/// <summary>切换单个 endpoint 授权状态。</summary>
export function toggleEndpoint(role: string, input: ToggleEndpointRequest): Promise<null> {
  return http<null>(`/api/roles/${encodeURIComponent(role)}/endpoints/toggle`, { method: 'POST', body: input })
}

/// <summary>一次性应用角色在所有 endpoint 上的最终授权状态（替换式批量）。</summary>
export function applyBatchGrants(role: string, input: BatchGrantsRequest): Promise<number> {
  return http<number>(`/api/roles/${encodeURIComponent(role)}/grants/batch`, {
    method: 'POST',
    body: input,
  })
}
