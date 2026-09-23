import {
  Alert,
  Button,
  Input,
  Popconfirm,
  Select,
  Space,
  Table,
  Tag,
  Upload,
  message,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import type { UploadProps } from 'antd'
import { useMemo, useState } from 'react'
import { useKnowledgeBases } from '../knowledge-base/useKnowledgeBases'
import { useDeleteDocument, useDocuments, useReindexDocument, useUploadDocument } from './useDocuments'
import type { DocumentDto } from './documentApi'

const ALLOWED_MIME = new Set([
  'application/pdf',
  'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
  'text/markdown',
  'text/plain',
])

const MAX_FILE_SIZE_BYTES = 30 * 1024 * 1024

const STATUS_COLORS: Record<string, string> = {
  Pending: 'default',
  Processing: 'blue',
  Indexed: 'green',
  Failed: 'red',
}

/// <summary>文档管理页：按 KB 过滤的列表 + 上传 + 删除 + 重新索引。</summary>
export function DocumentPage() {
  const { data: kbs = [], isPending: kbsLoading } = useKnowledgeBases()
  const [selectedKbId, setSelectedKbId] = useState<string | null>(null)
  const effectiveKbId = selectedKbId ?? kbs[0]?.id ?? null
  const { data: page, isPending: docsLoading } = useDocuments(effectiveKbId)

  const deleteMutation = useDeleteDocument()
  const reindexMutation = useReindexDocument()
  const uploadMutation = useUploadDocument()

  const documents = page?.items ?? []
  const [search, setSearch] = useState('')

  const filtered = useMemo(
    () => documents.filter((doc) => doc.fileName.toLowerCase().includes(search.toLowerCase())),
    [documents, search],
  )

  const columns: ColumnsType<DocumentDto> = [
    {
      title: '文件名',
      dataIndex: 'fileName',
      key: 'fileName',
      ellipsis: true,
    },
    {
      title: '状态',
      dataIndex: 'status',
      key: 'status',
      width: 110,
      render: (value: string) => <Tag color={STATUS_COLORS[value] ?? 'default'}>{value}</Tag>,
    },
    {
      title: '分块数',
      dataIndex: 'chunkCount',
      key: 'chunkCount',
      width: 80,
    },
    {
      title: '失败原因',
      dataIndex: 'failureReason',
      key: 'failureReason',
      ellipsis: true,
      render: (value: string | null) => value ?? '—',
    },
    {
      title: '更新时间',
      dataIndex: 'updatedAt',
      key: 'updatedAt',
      ellipsis: true,
      render: (value: string) => new Date(value).toLocaleString('zh-CN'),
    },
    {
      title: '操作',
      key: 'actions',
      width: 200,
      render: (_value, doc) => (
        <Space>
          <Button
            type="link"
            disabled={doc.status === 'Processing'}
            loading={reindexMutation.isPending && reindexMutation.variables === doc.id}
            onClick={() =>
              reindexMutation.mutate(doc.id, {
                onSuccess: () => message.success(`已提交重新索引：${doc.fileName}`),
              })
            }
          >
            重新索引
          </Button>
          <Popconfirm
            title="删除文档"
            description={`确认删除「${doc.fileName}」？该操作不可撤销。`}
            okText="确认删除"
            cancelText="取消"
            okButtonProps={{ danger: true, loading: deleteMutation.isPending }}
            onConfirm={() =>
              deleteMutation.mutate(doc.id, {
                onSuccess: () => message.success(`已删除：${doc.fileName}`),
              })
            }
            disabled={doc.status === 'Processing'}
          >
            <Button type="link" danger disabled={doc.status === 'Processing'}>
              删除
            </Button>
          </Popconfirm>
        </Space>
      ),
    },
  ]

  const uploadProps: UploadProps = {
    multiple: false,
    showUploadList: false,
    beforeUpload: (file) => {
      if (effectiveKbId === null) {
        message.error('请先选择知识库')
        return Upload.LIST_IGNORE
      }
      if (!ALLOWED_MIME.has(file.type)) {
        message.error(`不支持的 MIME 类型：${file.type || '未知'}`)
        return Upload.LIST_IGNORE
      }
      if (file.size > MAX_FILE_SIZE_BYTES) {
        message.error(`文件大小 ${file.size} 字节超过 30MB 上限`)
        return Upload.LIST_IGNORE
      }
      return true
    },
    customRequest: async ({ file, onSuccess, onError }) => {
      if (effectiveKbId === null || !(file instanceof File)) {
        onError?.(new Error('无效的上传请求'))
        return
      }
      try {
        await uploadMutation.mutateAsync({ kbId: effectiveKbId, file })
        message.success(`已上传：${file.name}`)
        onSuccess?.(file)
      } catch (mutationError) {
        message.error(mutationError instanceof Error ? mutationError.message : '上传失败')
        onError?.(mutationError instanceof Error ? mutationError : new Error('上传失败'))
      }
    },
  }

  return (
    <main>
      <div className="page-title-bar">
        <span className="page-title">文档管理</span>
      </div>
      <div className="users-toolbar">
        <Select
          placeholder="选择知识库"
          loading={kbsLoading}
          value={effectiveKbId}
          onChange={setSelectedKbId}
          style={{ width: 240 }}
          options={kbs.map((kb) => ({ value: kb.id, label: kb.name }))}
          disabled={kbs.length === 0}
        />
        <Input.Search
          allowClear
          placeholder="按文件名过滤"
          value={search}
          onChange={(event) => setSearch(event.target.value)}
          className="users-search"
          disabled={effectiveKbId === null}
        />
        <Upload {...uploadProps}>
          <Button type="primary" disabled={effectiveKbId === null}>
            上传文档
          </Button>
        </Upload>
      </div>
      {effectiveKbId === null && !kbsLoading && (
        <Alert
          showIcon
          className="users-alert"
          type="info"
          message="请先创建知识库，再上传文档。"
        />
      )}
      <Table<DocumentDto>
        rowKey="id"
        loading={docsLoading}
        dataSource={filtered}
        columns={columns}
        pagination={{ pageSize: 20, showSizeChanger: false }}
        locale={{ emptyText: effectiveKbId === null ? '请先选择知识库' : '暂无文档' }}
      />
    </main>
  )
}