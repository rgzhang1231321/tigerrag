import { queryKeys } from '../../app/http'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { deleteDoc, batchDeleteDocs, listDocs, reindexDoc, uploadDoc, getDocContent, getDocPermissions, replaceDocPermissions } from './documentApi'
import type { DocumentDto, DocumentListPage, DocumentPermissionsDto } from './documentApi'

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

/// <summary>批量删除文档；成功后让所有文档列表缓存失效。</summary>
export function useBatchDeleteDocuments() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (ids: string[]): Promise<number> => {
      return batchDeleteDocs(ids)
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['documents'] })
    },
  })
}

/// <summary>文档预览内容查询：仅在用户点击预览时启用。</summary>
export function useDocumentContent(id: string | null) {
  return useQuery({
    queryKey: ['document-content', id],
    queryFn: () => getDocContent(id!),
    enabled: !!id,
    retry: 1,
  })
}

/// <summary>查询文档权限状态；失败返回 null 触发降级模式。</summary>
export function useDocumentPermissions(documentId: string | null) {
  return useQuery<DocumentPermissionsDto | null>({
    queryKey: queryKeys.documentPermissions(documentId ?? ''),
    queryFn: () => getDocPermissions(documentId!),
    enabled: !!documentId,
    retry: 1,
  })
}

/// <summary>替换文档权限；成功后让该文档权限缓存失效。</summary>
export function useReplaceDocumentPermissions() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (input: {
      documentId: string
      userIds: string[]
      roles: string[]
    }): Promise<null> => {
      return replaceDocPermissions(input.documentId, {
        userIds: input.userIds,
        roles: input.roles,
      })
    },
    onSuccess: (_data, variables) => {
      void queryClient.invalidateQueries({
        queryKey: queryKeys.documentPermissions(variables.documentId),
      })
    },
  })
}