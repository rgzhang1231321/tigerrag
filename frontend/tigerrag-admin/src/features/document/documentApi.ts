import { http } from '../../app/http'

/// <summary>文档 DTO：与后端 DocumentDto 一一对应。</summary>
export interface DocumentDto {
  id: string
  kbId: string
  fileName: string
  status: 'Pending' | 'Processing' | 'Indexed' | 'Failed' | string
  chunkCount: number
  failureReason: string | null
  createdBy: string
  createdAt: string
  updatedAt: string
}

/// <summary>文档分页响应：与后端 DocumentListPage 一一对应。</summary>
export interface DocumentListPage {
  items: DocumentDto[]
  page: number
  pageSize: number
  total: number
}

/// <summary>列出指定 KB 下当前用户可见的文档（按 ACL 过滤）。</summary>
export function listDocs(kbId: string): Promise<DocumentListPage> {
  return http<DocumentListPage>('/api/documents/list', {
    method: 'POST',
    body: { kbId, page: 1, pageSize: 100 },
  })
}

/// <summary>按 Id 获取文档详情；非 Admin 必须在 ACL 可见集合内。</summary>
export function getDoc(id: string): Promise<DocumentDto> {
  return http<DocumentDto>(`/api/documents/${encodeURIComponent(id)}`, { method: 'POST' })
}

/// <summary>上传文档到指定 KB（multipart/form-data：字段 kbId、file）。</summary>
export function uploadDoc(kbId: string, file: File): Promise<DocumentDto> {
  const form = new FormData()
  form.append('kbId', kbId)
  form.append('file', file)
  return http<DocumentDto>('/api/documents/upload', {
    method: 'POST',
    body: form,
    retryOnAuth: false,
  })
}

/// <summary>删除文档；级联清理 chunks/permissions/向量/MinIO。</summary>
export function deleteDoc(id: string): Promise<null> {
  return http<null>(`/api/documents/${encodeURIComponent(id)}/delete`, { method: 'POST' })
}

/// <summary>单篇重索引：Processing 时拒绝；否则 Status 重置为 Pending 并入队。</summary>
export function reindexDoc(id: string): Promise<null> {
  return http<null>(`/api/documents/${encodeURIComponent(id)}/reindex`, { method: 'POST' })
}