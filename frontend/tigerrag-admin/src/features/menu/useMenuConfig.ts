import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { queryKeys } from '../../app/http'
import {
  createMenuConfig,
  deleteMenuConfig,
  fetchMenuTree,
  fetchVisibleMenuTree,
  listMenuConfigs,
  updateMenuConfig,
} from './menuApi'

export function queryMenuKeys() {
  return ['menu-configs'] as const
}

export function queryMenuTreeKeys() {
  return ['menu-configs', 'tree'] as const
}

export function queryVisibleMenuTreeKeys(rolesKey: string) {
  return ['menu-configs', 'visible-tree', rolesKey] as const
}

/// <summary>列出全部菜单配置（管理页表格用）。</summary>
export function useMenuConfigs() {
  return useQuery({
    queryKey: queryMenuKeys(),
    queryFn: listMenuConfigs,
  })
}

/// <summary>前端导航用的菜单树（仅启用项）。多处复用，靠缓存避免每次切换菜单都打接口。</summary>
export function useMenuTree() {
  return useQuery({
    queryKey: queryMenuTreeKeys(),
    queryFn: fetchMenuTree,
    staleTime: 60_000,
  })
}

/// <summary>按当前用户角色过滤的可见菜单；cache key 含角色，登录切换/角色变更时重新拉取。</summary>
export function useVisibleMenuTree(rolesKey: string) {
  return useQuery({
    queryKey: queryVisibleMenuTreeKeys(rolesKey),
    queryFn: fetchVisibleMenuTree,
    staleTime: 60_000,
  })
}

/// <summary>新建菜单项。成功后让列表与两类导航树都失效。</summary>
export function useCreateMenuConfig() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: createMenuConfig,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryMenuKeys() })
      void queryClient.invalidateQueries({ queryKey: queryMenuTreeKeys() })
      void queryClient.invalidateQueries({ queryKey: ['menu-configs', 'visible-tree'] })
    },
  })
}

/// <summary>更新菜单项。成功后让列表、两类导航树、角色列表都失效（角色的 menuNames/menuCount 依赖菜单 roles 字段）。</summary>
export function useUpdateMenuConfig() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (input: { id: string; data: Parameters<typeof updateMenuConfig>[1] }) =>
      updateMenuConfig(input.id, input.data),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryMenuKeys() })
      void queryClient.invalidateQueries({ queryKey: queryMenuTreeKeys() })
      void queryClient.invalidateQueries({ queryKey: ['menu-configs', 'visible-tree'] })
      void queryClient.invalidateQueries({ queryKey: queryKeys.roles })
    },
  })
}

/// <summary>删除菜单项。成功后让列表与两类导航树都失效。</summary>
export function useDeleteMenuConfig() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: deleteMenuConfig,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryMenuKeys() })
      void queryClient.invalidateQueries({ queryKey: queryMenuTreeKeys() })
      void queryClient.invalidateQueries({ queryKey: ['menu-configs', 'visible-tree'] })
    },
  })
}