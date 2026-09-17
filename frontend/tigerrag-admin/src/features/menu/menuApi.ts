import { http } from '../../app/http'

export interface MenuConfigDto {
  id: string
  key: string
  label: string
  icon: string | null
  permission: string | null
  parentId: string | null
  sortOrder: number
  isEnabled: boolean
}

/// <summary>取菜单树（导航用）。</summary>
export async function fetchMenuTree(): Promise<MenuConfigDto[]> {
  return http<MenuConfigDto[]>('/api/menu-configs/tree', { method: 'POST' })
}

/// <summary>取菜单平铺列表（管理页用）。</summary>
export async function listMenuConfigs(): Promise<MenuConfigDto[]> {
  return http<MenuConfigDto[]>('/api/menu-configs/list', { method: 'POST' })
}

/// <summary>新建菜单项。</summary>
export async function createMenuConfig(input: {
  key: string
  label: string
  icon: string | null
  permission: string | null
  parentId: string | null
  sortOrder: number
  isEnabled: boolean
}): Promise<MenuConfigDto> {
  return http<MenuConfigDto>('/api/menu-configs', { method: 'POST', body: input })
}

/// <summary>更新菜单项。</summary>
export async function updateMenuConfig(
  id: string,
  input: Partial<{
    key: string
    label: string
    icon: string | null
    permission: string | null
    parentId: string | null
    sortOrder: number
    isEnabled: boolean
  }>,
): Promise<MenuConfigDto> {
  return http<MenuConfigDto>(`/api/menu-configs/${id}`, { method: 'POST', body: input })
}

/// <summary>删除菜单项。</summary>
export async function deleteMenuConfig(id: string): Promise<null> {
  return http<null>(`/api/menu-configs/${id}/delete`, { method: 'POST' })
}