import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { queryKeys } from '../../app/http'
import { createRole, deleteRole, listRolesAll, updateRole } from './rolesApi'
import type { RoleDto } from './rolesApi'

/// <summary>列出全部角色（含系统保留 + DB 自定义），与 useCreateRole / useUpdateRole / useDeleteRole 共享缓存键。</summary>
export function useRoles() {
  return useQuery({
    queryKey: queryKeys.roles,
    queryFn: listRolesAll,
  })
}

/// <summary>新建自定义角色；成功后让角色列表缓存失效以触发重拉。</summary>
export function useCreateRole() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (input: { name: string }): Promise<RoleDto> => {
      return createRole(input)
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.roles })
    },
  })
}

/// <summary>重命名角色；成功后让角色列表缓存失效以触发重拉。</summary>
export function useUpdateRole() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (input: { originalName: string; name: string }): Promise<RoleDto> => {
      return updateRole(input.originalName, { name: input.name })
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.roles })
    },
  })
}

/// <summary>删除自定义角色；删除后让角色列表缓存失效以触发重拉。</summary>
export function useDeleteRole() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (name: string): Promise<null> => {
      return deleteRole(name)
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.roles })
    },
  })
}