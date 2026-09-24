import {
  Alert,
  Button,
  Card,
  Checkbox,
  Drawer,
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
import {
  CloudUploadOutlined,
  DeleteOutlined,
  EyeOutlined,
  LeftOutlined,
  LockOutlined,
  ReloadOutlined,
} from '@ant-design/icons'
import { useMemo, useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
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

const STATUS_CONFIG: Record<string, { color: string; label: string }> = {
  Pending: { color: 'default', label: '等待中' },
  Processing: { color: 'blue', label: '处理中' },
  Indexed: { color: 'green', label: '已索引' },
  Failed: { color: 'red', label: '失败' },
}

/// <summary>格式化文件大小为人类可读字符串。</summary>
function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`
}

/// <summary>文档管理页：KB 详情头 + 指标卡 + 筛选 + 批量操作 + 上传 + 预览。</summary>
export function DocumentPage() {
  const navigate = useNavigate()
  const [searchParams, setSearchParams] = useSearchParams()
  const { data: kbs = [], isPending: kbsLoading } = useKnowledgeBases()

  const selectedKbId = searchParams.get('kbId') ?? null
  const effectiveKbId = selectedKbId ?? kbs[0]?.id ?? null

  function selectKb(id: string) {
    setSearchParams({ kbId: id })
  }

  const currentKb = useMemo(
    () => kbs.find((kb) => kb.id === effectiveKbId),
    [kbs, effectiveKbId],
  )

  const { data: page, isPending: docsLoading, refetch } = useDocuments(effectiveKbId)
  const deleteMutation = useDeleteDocument()
  const reindexMutation = useReindexDocument()
  const uploadMutation = useUploadDocument()

  const documents = page?.items ?? []
  const [search, setSearch] = useState('')
  const [statusFilter, setStatusFilter] = useState<string>('all')
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set())
  const [previewDoc, setPreviewDoc] = useState<DocumentDto | null>(null)

  const filtered = useMemo(
    () => documents.filter((doc) => {
      const matchSearch = doc.fileName.toLowerCase().includes(search.toLowerCase())
      const matchStatus = statusFilter === 'all' || doc.status === statusFilter
      return matchSearch && matchStatus
    }),
    [documents, search, statusFilter],
  )

  const stats = useMemo(() => {
    const total = documents.length
    const indexed = documents.filter((d) => d.status === 'Indexed').length
    const processing = documents.filter((d) => d.status === 'Processing').length
    const failed = documents.filter((d) => d.status === 'Failed').length
    return { total, indexed, processing, failed }
  }, [documents])

  function toggleSelection(id: string, checked: boolean) {
    setSelectedIds((prev) => {
      const next = new Set(prev)
      if (checked) next.add(id)
      else next.delete(id)
      return next
    })
  }

  const columns: ColumnsType<DocumentDto> = [
    {
      title: '',
      dataIndex: 'id',
      key: 'select',
      width: 50,
      render: (_value, doc) => (
        <Checkbox
          checked={selectedIds.has(doc.id)}
          onChange={(event) => toggleSelection(doc.id, event.target.checked)}
        />
      ),
    },
    {
      title: '文件名',
      dataIndex: 'fileName',
      key: 'fileName',
      ellipsis: true,
      render: (_value, doc) => (
        <Button
          type="link"
          style={{ padding: 0 }}
          onClick={() => setPreviewDoc(doc)}
        >
          <Space>
            <span style={{ color: 'var(--text-secondary)' }}>📄</span>
            <span style={{ color: 'var(--brand-secondary)' }}>{doc.fileName}</span>
          </Space>
        </Button>
      ),
    },
    {
      title: '状态',
      dataIndex: 'status',
      key: 'status',
      width: 110,
      render: (value: string) => {
        const config = STATUS_CONFIG[value] ?? { color: 'default', label: value }
        return <Tag color={config.color}>{config.label}</Tag>
      },
    },
    {
      title: '分块数',
      dataIndex: 'chunkCount',
      key: 'chunkCount',
      width: 80,
      align: 'center',
    },
    {
      title: '大小',
      dataIndex: 'fileSize',
      key: 'fileSize',
      width: 100,
      render: (value: number) => formatFileSize(value ?? 0),
    },
    {
      title: '失败原因',
      dataIndex: 'failureReason',
      key: 'failureReason',
      ellipsis: true,
      render: (value: string | null) => (
        <span style={{ color: value ? 'var(--error)' : 'var(--text-secondary)' }}>
          {value ?? '—'}
        </span>
      ),
    },
    {
      title: '更新时间',
      dataIndex: 'updatedAt',
      key: 'updatedAt',
      width: 170,
      render: (value: string) => new Date(value).toLocaleString('zh-CN'),
    },
    {
      title: '操作',
      key: 'actions',
      width: 320,
      render: (_value, doc) => (
        <Space>
          <Button type="link" style={{ padding: '0 4px' }} icon={<EyeOutlined />} onClick={() => setPreviewDoc(doc)}>
            预览
          </Button>
          <Button
            type="link"
            style={{ padding: '0 4px' }}
            icon={<ReloadOutlined />}
            disabled={doc.status === 'Processing'}
            loading={reindexMutation.isPending && reindexMutation.variables === doc.id}
            onClick={() =>
              reindexMutation.mutate(doc.id, {
                onSuccess: () => message.success(`已提交重新索引：${doc.fileName}`),
              })
            }
          >
            重索引
          </Button>
          <Button type="link" style={{ padding: '0 4px' }} icon={<LockOutlined />} onClick={() => message.info('权限管理未实现')}>
            权限
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
            <Button type="link" danger style={{ padding: '0 4px' }} icon={<DeleteOutlined />} disabled={doc.status === 'Processing'}>
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
      <Button
        type="link"
        style={{ padding: 0, marginBottom: 12 }}
        icon={<LeftOutlined />}
        onClick={() => navigate('/knowledge-bases')}
      >
        返回知识库列表
      </Button>
      {currentKb !== undefined && (
        <Card className="detail-header-card" bordered>
          <div className="detail-header-icon">📚</div>
          <div className="detail-header-info">
            <h2 className="detail-header-title">{currentKb.name}</h2>
            <div className="detail-header-meta">
              <span>{currentKb.description ?? '—'}</span>
              <span>👤 拥有者：{currentKb.ownerName ?? '—'}</span>
              <span>🕐 创建时间：{new Date(currentKb.createdAt).toLocaleString('zh-CN')}</span>
            </div>
          </div>
        </Card>
      )}
      <div className="metric-grid" style={{marginBottom: "24px"}}>
        <MetricCard title="总文档" value={stats.total} icon="📄" />
        <MetricCard title="已索引" value={stats.indexed} icon="✅" color="#52c41a" />
        <MetricCard title="处理中" value={stats.processing} icon="⏳" color="#1677ff" />
        <MetricCard title="失败" value={stats.failed} icon="❌" color="#ff4d4f" />
      </div>
      <Upload.Dragger
        {...uploadProps}
        disabled={effectiveKbId === null}
        className="upload-dropzone"
        style={{marginBottom: "24px"}}
      >
        <div className="upload-dropzone-inner">
          <CloudUploadOutlined className="upload-dropzone-icon" />
          <div className="upload-dropzone-text">拖拽文件到此处，或点击上传</div>
          <div className="upload-dropzone-hint">支持 PDF / Word / Markdown / TXT，单文件不超过 30MB</div>
        </div>
      </Upload.Dragger>
      <div className="users-toolbar">
        <Select
          placeholder="选择知识库"
          loading={kbsLoading}
          value={effectiveKbId}
          onChange={selectKb}
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
        <Select
          value={statusFilter}
          onChange={setStatusFilter}
          style={{ minWidth: 140 }}
          disabled={effectiveKbId === null}
          options={[
            { value: 'all', label: '全部状态' },
            { value: 'Indexed', label: '已索引' },
            { value: 'Processing', label: '处理中' },
            { value: 'Pending', label: '等待中' },
            { value: 'Failed', label: '失败' },
          ]}
        />
        <Button onClick={() => void refetch()} disabled={effectiveKbId === null}>
          刷新
        </Button>
      </div>
      {selectedIds.size > 0 && (
        <div className="batch-action-bar">
          <Space>
            <span>已选择 <strong>{selectedIds.size}</strong> 项</span>
            <Button size="small" onClick={() => message.info('批量重索引未实现')}>
              批量重索引
            </Button>
            <Button size="small" danger onClick={() => message.info('批量删除未实现')}>
              批量删除
            </Button>
          </Space>
        </div>
      )}
      {effectiveKbId === null && !kbsLoading && (
        <Alert showIcon className="users-alert" type="info" message="请先创建知识库，再上传文档。" />
      )}
      <Table<DocumentDto>
        rowKey="id"
        loading={docsLoading}
        dataSource={filtered}
        columns={columns}
        pagination={{ pageSize: 20, showSizeChanger: false }}
        locale={{ emptyText: <EmptyDocState effectiveKbId={effectiveKbId} /> }}
      />
      <Drawer
        title={`文档预览 — ${previewDoc?.fileName ?? ''}`}
        placement="right"
        width={720}
        onClose={() => setPreviewDoc(null)}
        open={previewDoc !== null}
      >
        {previewDoc !== null && (
          <div>
            <div className="preview-meta">
              <div><span className="preview-meta-label">文件名：</span>{previewDoc.fileName}</div>
              <div><span className="preview-meta-label">文件大小：</span>{formatFileSize(previewDoc.fileSize ?? 0)}</div>
              <div><span className="preview-meta-label">上传时间：</span>{new Date(previewDoc.createdAt).toLocaleString('zh-CN')}</div>
              <div><span className="preview-meta-label">状态：</span>{STATUS_CONFIG[previewDoc.status]?.label ?? previewDoc.status}（{previewDoc.chunkCount} 个分块）</div>
            </div>
            <div className="preview-content-placeholder">
              <Alert type="info" message="文档内容预览功能暂未实现" />
            </div>
          </div>
        )}
      </Drawer>
    </main>
  )
}

/// <summary>指标卡组件。</summary>
function MetricCard({ title, value, icon, color }: { title: string; value: number; icon: string; color?: string }) {
  return (
    <Card className="metric-card" bordered>
      <div className="metric-card-label">
        <span className="metric-card-icon" style={color ? { color } : undefined}>{icon}</span>
        {title}
      </div>
      <div className="metric-card-value" style={color ? { color } : undefined}>{value}</div>
    </Card>
  )
}

/// <summary>空状态：根据是否选中 KB 显示不同引导。</summary>
function EmptyDocState({ effectiveKbId }: { effectiveKbId: string | null }) {
  if (effectiveKbId === null) {
    return (
      <div className="empty-state">
        <div className="empty-state-icon">📂</div>
        <div className="empty-state-title">请先选择知识库</div>
        <div className="empty-state-desc">从上方下拉框选择一个知识库以查看其文档</div>
      </div>
    )
  }
  return (
    <div className="empty-state">
      <div className="empty-state-icon">📄</div>
      <div className="empty-state-title">知识库中还没有文档</div>
      <div className="empty-state-desc">上传 PDF、Word、Markdown 或 TXT 文件</div>
    </div>
  )
}
