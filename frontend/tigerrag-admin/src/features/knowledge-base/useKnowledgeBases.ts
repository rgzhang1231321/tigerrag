import { queryKeys } from '../../app/http'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { createKb, deleteKb, batchDeleteKbs, listKbs, reindexKb, updateKb } from './knowledgeBaseApi'
import type { KnowledgeBaseDto } from './knowledgeBaseApi'

/// <summary>列出当前用户可见的知识库。</summary>
export function useKnowledgeBases() {
  return useQuery({
    queryKey: queryKeys.knowledgeBases,
    queryFn: listKbs,
  })
}

/// <summary>新建知识库；成功后让知识库列表缓存失效以触发重拉。</summary>
export function useCreateKb() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (input: { name: string; description?: string | null }): Promise<KnowledgeBaseDto> => {
      return createKb(input)
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.knowledgeBases })
    },
  })
}

/// <summary>更新知识库；成功后让知识库列表缓存失效以触发重拉。</summary>
export function useUpdateKb() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (input: {
      id: string
      name?: string | null
      description?: string | null
      newOwnerId?: string | null
    }): Promise<KnowledgeBaseDto> => {
      return updateKb(input.id, {
        name: input.name,
        description: input.description,
        newOwnerId: input.newOwnerId,
      })
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.knowledgeBases })
    },
  })
}

/// <summary>删除知识库；成功后让知识库列表缓存失效以触发重拉。</summary>
export function useDeleteKb() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (id: string): Promise<null> => {
      return deleteKb(id)
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.knowledgeBases })
    },
  })
}

/// <summary>重新索引知识库；不影响知识库列表缓存（队列后台跑），仅触发统计重拉。</summary>
export function useReindexKb() {
  return useMutation({
    mutationFn: async (id: string): Promise<null> => {
      return reindexKb(id)
    },
  })
}

/// <summary>批量删除知识库；成功后让知识库列表缓存失效。</summary>
export function useBatchDeleteKbs() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (ids: string[]): Promise<number> => {
      return batchDeleteKbs(ids)
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.knowledgeBases })
    },
  })
}