import { http } from '../../app/http'

/// <summary>知识库 DTO：与后端 KnowledgeBaseDto 一一对应。</summary>
export interface KnowledgeBaseDto {
  id: string
  name: string
  description: string | null
  ownerId: string
  ownerName: string | null
  documentCount: number
  createdAt: string
}

/// <summary>列出当前用户可见的知识库；Admin 返回全部，非 Admin 仅返回 Owner 的 KB。</summary>
export function listKbs(): Promise<KnowledgeBaseDto[]> {
  return http<KnowledgeBaseDto[]>('/api/knowledge-bases/list', { method: 'POST' })
}

/// <summary>创建知识库。</summary>
export function createKb(input: { name: string; description?: string | null }): Promise<KnowledgeBaseDto> {
  return http<KnowledgeBaseDto>('/api/knowledge-bases', { method: 'POST', body: input })
}

/// <summary>按 Id 获取知识库详情。</summary>
export function getKb(id: string): Promise<KnowledgeBaseDto> {
  return http<KnowledgeBaseDto>(`/api/knowledge-bases/${encodeURIComponent(id)}`, { method: 'POST' })
}

/// <summary>更新知识库；后端 UpdateKnowledgeBaseRequest 用 Nullable&lt;T&gt; 区分"未提供"与"清空"。</summary>
export function updateKb(
  id: string,
  input: { name?: string | null; description?: string | null; newOwnerId?: string | null },
): Promise<KnowledgeBaseDto> {
  return http<KnowledgeBaseDto>(`/api/knowledge-bases/${encodeURIComponent(id)}/update`, {
    method: 'POST',
    body: input,
  })
}

/// <summary>级联删除知识库（含文档/chunks/permissions/向量/MinIO 文件）。</summary>
export function deleteKb(id: string): Promise<null> {
  return http<null>(`/api/knowledge-bases/${encodeURIComponent(id)}/delete`, { method: 'POST' })
}

/// <summary>把 KB 下全部非 Processing 文档重置为 Pending 并重新入队。</summary>
export function reindexKb(id: string): Promise<null> {
  return http<null>(`/api/knowledge-bases/${encodeURIComponent(id)}/reindex`, { method: 'POST' })
}