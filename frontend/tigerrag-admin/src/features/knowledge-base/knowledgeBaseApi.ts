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

/// <summary>列出当前用户可见的知识库；Admin 返回全部，非 Admin 返回 Owner ∪ ACL 授权的 KB。</summary>
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

/// <summary>级联删除知识库（含 KB ACL/文档/chunks/permissions/向量/MinIO 文件）。</summary>
export function deleteKb(id: string): Promise<null> {
  return http<null>(`/api/knowledge-bases/${encodeURIComponent(id)}/delete`, { method: 'POST' })
}

/// <summary>批量删除知识库。</summary>
export function batchDeleteKbs(ids: string[]): Promise<number> {
  return http<number>('/api/knowledge-bases/batch-delete', {
    method: 'POST',
    body: { ids },
  })
}

/// <summary>把 KB 下全部非 Processing 文档重置为 Pending 并重新入队。</summary>
export function reindexKb(id: string): Promise<null> {
  return http<null>(`/api/knowledge-bases/${encodeURIComponent(id)}/reindex`, { method: 'POST' })
}

/// <summary>KB 权限状态 DTO：与后端 KbPermissionsDto 一一对应。</summary>
export interface KbPermissionsDto {
  userIds: string[]
  roles: string[]
}

/// <summary>查询 KB 当前 ACL 状态；返回 null 表示加载失败（降级模式）。</summary>
export function getKbPermissions(kbId: string): Promise<KbPermissionsDto | null> {
  return http<KbPermissionsDto>(
    `/api/knowledge-bases/${encodeURIComponent(kbId)}/permissions`,
    { method: 'GET' },
  ).catch(() => null)
}

/// <summary>替换 KB 权限（整体替换语义）。</summary>
export function replaceKbPermissions(
  kbId: string,
  input: { userIds: string[]; roles: string[] },
): Promise<null> {
  return http<null>(`/api/knowledge-bases/${encodeURIComponent(kbId)}/permissions`, {
    method: 'POST',
    body: input,
  })
}
