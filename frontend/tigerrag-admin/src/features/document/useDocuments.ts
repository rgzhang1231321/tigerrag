import { queryKeys } from '../../app/http'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { deleteDoc, listDocs, reindexDoc, uploadDoc } from './documentApi'
import type { DocumentDto, DocumentListPage } from './documentApi'

/// <summary>列出指定 KB 下当前用户可见的文档；按 ACL 过滤。</summary>
export function useDocuments(kbId: string | null) {
  return useQuery<DocumentListPage>({
    queryKey: queryKeys.documents(kbId),
    queryFn: () => listDocs(kbId ?? ''),
    enabled: kbId !== null && kbId !== '',
  })
}

/// <summary>上传文档；成功后让该 KB 的文档列表缓存失效以触发重拉。</summary>
export function useUploadDocument() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (input: { kbId: string; file: File }): Promise<DocumentDto> => {
      return uploadDoc(input.kbId, input.file)
    },
    onSuccess: (_data, variables) => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.documents(variables.kbId) })
    },
  })
}

/// <summary>删除文档；成功后让该文档所在 KB 的列表缓存失效。</summary>
export function useDeleteDocument() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (id: string): Promise<null> => {
      return deleteDoc(id)
    },
    onSuccess: () => {
      // 失效所有 KB 的文档列表，因为不知道该文档属于哪个 KB。
      void queryClient.invalidateQueries({ queryKey: ['documents'] })
    },
  })
}

/// <summary>单篇重索引；不影响列表缓存（队列后台跑）。</summary>
export function useReindexDocument() {
  return useMutation({
    mutationFn: async (id: string): Promise<null> => {
      return reindexDoc(id)
    },
  })
}