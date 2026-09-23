import {
  Alert,
  Button,
  Form,
  Input,
  Modal,
  Popconfirm,
  Space,
  Table,
  message,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useMemo, useState } from 'react'
import { useCreateKb, useDeleteKb, useKnowledgeBases, useReindexKb, useUpdateKb } from './useKnowledgeBases'
import type { KnowledgeBaseDto } from './knowledgeBaseApi'

type ModalMode = 'closed' | 'create' | { edit: KnowledgeBaseDto }

/// <summary>知识库管理页：列表 + 创建/编辑弹窗 + 删除确认 + 重新索引按钮。</summary>
export function KnowledgeBasePage() {
  const { data: kbs = [], isPending } = useKnowledgeBases()
  const [search, setSearch] = useState('')
  const [mode, setMode] = useState<ModalMode>('closed')
  const createMutation = useCreateKb()
  const updateMutation = useUpdateKb()
  const deleteMutation = useDeleteKb()
  const reindexMutation = useReindexKb()

  const filtered = useMemo(
    () => kbs.filter((kb) => kb.name.toLowerCase().includes(search.toLowerCase())),
    [kbs, search],
  )

  const columns: ColumnsType<KnowledgeBaseDto> = [
    {
      title: '名称',
      dataIndex: 'name',
      key: 'name',
    },
    {
      title: '描述',
      dataIndex: 'description',
      key: 'description',
      ellipsis: true,
      render: (value: string | null) => value ?? '—',
    },
    {
      title: '拥有者',
      key: 'owner',
      render: (_value, kb) => (
        <Space direction="vertical" size={0}>
          <span>{kb.ownerName ?? '—'}</span>
          <span style={{ color: '#999', fontSize: 12 }}>{kb.ownerId.slice(0, 8)}...</span>
        </Space>
      ),
    },
    {
      title: '文档数',
      dataIndex: 'documentCount',
      key: 'documentCount',
      width: 90,
    },
    {
      title: '创建时间',
      dataIndex: 'createdAt',
      key: 'createdAt',
      ellipsis: true,
      render: (value: string) => new Date(value).toLocaleString('zh-CN'),
    },
    {
      title: '操作',
      key: 'actions',
      width: 240,
      render: (_value, kb) => (
        <Space>
          <Button type="link" onClick={() => setMode({ edit: kb })}>
            编辑
          </Button>
          <Button
            type="link"
            loading={reindexMutation.isPending && reindexMutation.variables === kb.id}
            onClick={() =>
              reindexMutation.mutate(kb.id, {
                onSuccess: () => message.success(`已提交重新索引：${kb.name}`),
              })
            }
          >
            重新索引
          </Button>
          <Popconfirm
            title="删除知识库"
            description={
              kb.documentCount > 0
                ? `删除「${kb.name}」将同时删除 ${kb.documentCount} 个文档与对应向量，且不可恢复。确认删除？`
                : `确认删除「${kb.name}」？该操作不可撤销。`
            }
            okText="确认删除"
            cancelText="取消"
            okButtonProps={{ danger: true, loading: deleteMutation.isPending }}
            onConfirm={() =>
              deleteMutation.mutate(kb.id, {
                onSuccess: () => message.success(`已删除：${kb.name}`),
              })
            }
          >
            <Button type="link" danger>
              删除
            </Button>
          </Popconfirm>
        </Space>
      ),
    },
  ]

  return (
    <main>
      <div className="page-title-bar">
        <span className="page-title">知识库</span>
      </div>
      <div className="users-toolbar">
        <Input.Search
          allowClear
          placeholder="按名称过滤"
          onChange={(event) => setSearch(event.target.value)}
          className="users-search"
        />
        <Button type="primary" onClick={() => setMode('create')}>
          新建知识库
        </Button>
      </div>
      <Table<KnowledgeBaseDto>
        rowKey="id"
        loading={isPending}
        dataSource={filtered}
        columns={columns}
        pagination={{ pageSize: 20, showSizeChanger: false }}
        locale={{ emptyText: '暂无知识库' }}
      />
      {mode === 'create' && (
        <KbFormDialog
          mode="create"
          onCancel={() => setMode('closed')}
          onSubmit={(values) =>
            createMutation.mutate(
              { name: values.name, description: values.description },
              {
                onSuccess: () => {
                  message.success('知识库已创建')
                  setMode('closed')
                },
              },
            )
          }
          submitting={createMutation.isPending}
        />
      )}
      {typeof mode === 'object' && (
        <KbFormDialog
          mode="edit"
          initial={mode.edit}
          onCancel={() => setMode('closed')}
          onSubmit={(values) =>
            updateMutation.mutate(
              {
                id: mode.edit.id,
                name: values.name,
                description: values.description,
              },
              {
                onSuccess: () => {
                  message.success('知识库已更新')
                  setMode('closed')
                },
              },
            )
          }
          submitting={updateMutation.isPending}
        />
      )}
    </main>
  )
}

interface KbFormValues {
  name: string
  description?: string | null
}

interface KbFormDialogProps {
  mode: 'create' | 'edit'
  initial?: KnowledgeBaseDto
  onCancel: () => void
  onSubmit: (values: KbFormValues) => void
  submitting: boolean
}

/// <summary>创建/编辑知识库弹窗。编辑模式下 Description 为空表示清空。</summary>
function KbFormDialog({ mode, initial, onCancel, onSubmit, submitting }: KbFormDialogProps) {
  const [name, setName] = useState(initial?.name ?? '')
  const [description, setDescription] = useState(initial?.description ?? '')
  const [error, setError] = useState<string | null>(null)

  function handleSubmit() {
    setError(null)
    const trimmed = name.trim()
    if (trimmed.length === 0) {
      setError('请填写名称')
      return
    }
    if (trimmed.length > 200) {
      setError('名称长度不能超过 200')
      return
    }
    onSubmit({
      name: trimmed,
      description: description.trim() === '' ? null : description,
    })
  }

  return (
    <Modal
      title={mode === 'create' ? '新建知识库' : `编辑知识库：${initial?.name ?? ''}`}
      open
      onCancel={onCancel}
      destroyOnHidden
      footer={null}
      maskClosable={false}
    >
      {error !== null && <Alert type="error" showIcon className="users-alert" message={error} />}
      <Form layout="vertical" className="users-form">
        <Form.Item label="名称" required>
          <Input value={name} onChange={(event) => setName(event.target.value)} maxLength={200} />
        </Form.Item>
        <Form.Item
          label={mode === 'edit' ? '描述（留空表示清空）' : '描述'}
        >
          <Input.TextArea
            value={description}
            onChange={(event) => setDescription(event.target.value)}
            rows={3}
          />
        </Form.Item>
      </Form>
      <div className="users-form-actions">
        <Button onClick={onCancel} disabled={submitting}>
          取消
        </Button>
        <Button type="primary" loading={submitting} onClick={handleSubmit}>
          保存
        </Button>
      </div>
    </Modal>
  )
}