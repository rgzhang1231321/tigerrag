import { Button, Card, Input, Popconfirm, Space, Table, Tag, message } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useState } from 'react'
import { useCreateRole, useDeleteRole, useRoles } from './useRoles'
import type { RoleDto } from './rolesApi'

interface RoleRow {
  key: string
  name: string
  isSystem: boolean
}

/// <summary>
/// 角色列表卡片：展示系统保留 + DB 自定义角色，提供新建 / 删除入口。
/// 删除自定义角色会触发后端事务，撤销持有该角色的全部用户会话。
/// </summary>
export function RoleListCard() {
  const { data: roles = [], isPending } = useRoles()
  const createMutation = useCreateRole()
  const deleteMutation = useDeleteRole()
  const [draftName, setDraftName] = useState('')

  async function handleCreate() {
    const trimmed = draftName.trim()
    if (!trimmed) {
      message.warning('请填写角色名')
      return
    }
    try {
      await createMutation.mutateAsync({ name: trimmed })
      message.success(`角色 ${trimmed} 已创建`)
      setDraftName('')
    } catch (error) {
      message.error(error instanceof Error ? error.message : '创建失败')
    }
  }

  async function handleDelete(name: string) {
    try {
      await deleteMutation.mutateAsync(name)
      message.success(`角色 ${name} 已删除`)
    } catch (error) {
      message.error(error instanceof Error ? error.message : '删除失败')
    }
  }

  const rows: RoleRow[] = roles.map((role: RoleDto) => ({
    key: role.name,
    name: role.name,
    isSystem: role.isSystem,
  }))

  const columns: ColumnsType<RoleRow> = [
    { title: '角色名', dataIndex: 'name', key: 'name', width: 200 },
    {
      title: '类型',
      key: 'kind',
      width: 100,
      render: (_value, row) =>
        row.isSystem ? <Tag color="blue">系统</Tag> : <Tag>自定义</Tag>,
    },
    {
      title: '操作',
      key: 'actions',
      width: 120,
      render: (_value, row) => {
        if (row.isSystem) {
          return (
            <Button type="link" size="small" disabled>
              删除
            </Button>
          )
        }
        return (
          <Popconfirm
            title="删除角色"
            description={`确认删除角色 ${row.name}？持有该角色的全部用户将被强制下线。`}
            okText="确认删除"
            cancelText="取消"
            okButtonProps={{ danger: true, loading: deleteMutation.isPending }}
            onConfirm={() => handleDelete(row.name)}
          >
            <Button type="link" size="small" danger>
              删除
            </Button>
          </Popconfirm>
        )
      },
    },
  ]

  return (
    <Card title="角色列表" className="role-list-card">
      <Space className="role-list-toolbar">
        <Input
          value={draftName}
          onChange={(event) => setDraftName(event.target.value)}
          placeholder="如 CustomRole"
          allowClear
          style={{ width: 200 }}
          onPressEnter={handleCreate}
        />
        <Button
          type="primary"
          loading={createMutation.isPending}
          onClick={handleCreate}
          aria-label="新建角色"
        >
          新建
        </Button>
      </Space>
      <Table<RoleRow>
        rowKey="key"
        loading={isPending}
        dataSource={rows}
        columns={columns}
        pagination={false}
      />
    </Card>
  )
}