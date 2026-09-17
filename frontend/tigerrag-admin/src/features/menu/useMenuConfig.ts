import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  createMenuConfig,
  deleteMenuConfig,
  fetchMenuTree,
  listMenuConfigs,
  updateMenuConfig,
} from './menuApi'

export function queryMenuKeys() {
  return ['menu-configs'] as const
}

export function queryMenuTreeKeys() {
  return ['menu-configs', 'tree'] as const
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

/// <summary>新建菜单项。成功后让列表与导航树都失效。</summary>
export function useCreateMenuConfig() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: createMenuConfig,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryMenuKeys() })
      void queryClient.invalidateQueries({ queryKey: queryMenuTreeKeys() })
    },
  })
}

/// <summary>更新菜单项。成功后让列表与导航树都失效。</summary>
export function useUpdateMenuConfig() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (input: { id: string; data: Parameters<typeof updateMenuConfig>[1] }) =>
      updateMenuConfig(input.id, input.data),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryMenuKeys() })
      void queryClient.invalidateQueries({ queryKey: queryMenuTreeKeys() })
    },
  })
}

/// <summary>删除菜单项。成功后让列表与导航树都失效。</summary>
export function useDeleteMenuConfig() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: deleteMenuConfig,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryMenuKeys() })
      void queryClient.invalidateQueries({ queryKey: queryMenuTreeKeys() })
    },
  })
}