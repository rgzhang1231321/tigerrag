import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { queryKeys } from '../../app/http'
import {
  applyBatchGrants,
  grantMenu,
  listRoleGrants,
  revokeMenu,
  toggleEndpoint,
} from './roleEndpointApi'
import type { BatchGrantsRequest, RoleEndpointMatrixDto } from './roleEndpointApi'

/// <summary>列出角色的 endpoint 授权矩阵。</summary>
export function useRoleEndpoints(role: string | null) {
  return useQuery({
    queryKey: queryKeys.roleEndpoints(role ?? ''),
    queryFn: () => listRoleGrants(role!),
    enabled: role !== null,
  })
}

/// <summary>按菜单批量授权；成功后让矩阵缓存失效。</summary>
export function useGrantMenu(role: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (menuKey: string) => grantMenu(role, menuKey),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.roleEndpoints(role) })
    },
  })
}

/// <summary>按菜单批量撤销；成功后让矩阵缓存失效。</summary>
export function useRevokeMenu(role: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (menuKey: string) => revokeMenu(role, menuKey),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.roleEndpoints(role) })
    },
  })
}

/// <summary>切换单个 endpoint 授权状态；成功后让矩阵缓存失效。</summary>
export function useToggleEndpoint(role: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (input: { endpointKey: string; menuKey: string; grant: boolean }) =>
      toggleEndpoint(role, input),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.roleEndpoints(role) })
    },
  })
}

/// <summary>一次性提交矩阵变更；成功后让矩阵缓存失效。用于前端"保存"按钮。</summary>
export function useApplyBatch(role: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (input: BatchGrantsRequest) => applyBatchGrants(role, input),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.roleEndpoints(role) })
    },
  })
}

export type { BatchGrantsRequest, RoleEndpointMatrixDto }
