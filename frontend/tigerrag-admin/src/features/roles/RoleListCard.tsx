import {
  Button,
  Form,
  Input,
  Modal,
  Popconfirm,
  Space,
  Table,
  Tag,
  Tooltip,
  message,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useMemo, useState } from 'react'
import { useCreateRole, useDeleteRole, useRoles, useUpdateRole } from './useRoles'
import type { RoleDto } from './rolesApi'

interface RoleRow {
  id: string
  name: string
  isSystem: boolean
  userCount: number
  menuCount: number
  menuNames: string[]
}

interface RoleFormValues {
  name: string
}

type ModalMode = 'closed' | 'create' | { edit: RoleDto }

/// <summary>
/// 角色列表卡片：弹框方式新增/重命名，表格展示名称、类型、引用数与关联菜单；
/// Admin 受保护不可删/不可改名，其余角色被用户或菜单引用时拒绝删除。
/// </summary>
export function RoleListCard() {
  const { data: roles = [], isPending } = useRoles()
  const createMutation = useCreateRole()
  const updateMutation = useUpdateRole()
  const deleteMutation = useDeleteRole()
  const [modalMode, setModalMode] = useState<ModalMode>('closed')
  const [form] = Form.useForm<RoleFormValues>()

  function openCreate() {
    form.resetFields()
    setModalMode('create')
  }

  function openEdit(role: RoleDto) {
    form.setFieldsValue({ name: role.name })
    setModalMode({ edit: role })
  }

  function closeModal() {
    setModalMode('closed')
    form.resetFields()
  }

  async function handleSubmit() {
    const trimmed = (form.getFieldValue('name') ?? '').trim()
    if (!trimmed) {
      message.warning('请填写角色名')
      return
    }

    try {
      if (modalMode === 'create') {
        await createMutation.mutateAsync({ name: trimmed })
        message.success(`角色 ${trimmed} 已创建`)
      } else if (typeof modalMode === 'object') {
        await updateMutation.mutateAsync({
          originalName: modalMode.edit.name,
          name: trimmed,
        })
        message.success(`角色已重命名为 ${trimmed}`)
      }
      closeModal()
    } catch (error) {
      message.error(error instanceof Error ? error.message : '操作失败')
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

  const rows: RoleRow[] = roles.map((role) => ({
    id: role.name,
    name: role.name,
    isSystem: role.isSystem,
    userCount: role.userCount,
    menuCount: role.menuCount,
    menuNames: role.menuNames ?? [],
  }))

  const columns: ColumnsType<RoleRow> = useMemo(() => [
    { title: '角色名', key: 'name', width: 200, render: (_value, row) => row.name },
    {
      title: '类型',
      key: 'kind',
      width: 100,
      render: (_value, row) =>
        row.isSystem ? <Tag color="blue">系统</Tag> : <Tag>自定义</Tag>,
    },
    {
      title: '引用',
      key: 'references',
      width: 160,
      render: (_value, row) => (
        <Space direction="vertical" size={2}>
          <Tooltip title="持有该角色的用户数">
            <span>用户 {row.userCount}</span>
          </Tooltip>
          <Tooltip title="将该角色列入可见名单的菜单数">
            <span>菜单 {row.menuCount}</span>
          </Tooltip>
        </Space>
      ),
    },
    {
      title: '关联菜单',
      key: 'menuNames',
      render: (_value, row) =>
        row.menuNames.length === 0 ? (
          <span style={{ color: 'rgba(0, 0, 0, 0.45)' }}>—</span>
        ) : (
          <Space size={[4, 4]} wrap>
            {row.menuNames.map((label) => (
              <Tag key={label}>{label}</Tag>
            ))}
          </Space>
        ),
    },
    {
      title: '操作',
      key: 'actions',
      width: 160,
      render: (_value, row) => {
        if (row.isSystem) {
          return (
            <Space size={4}>
              <Tooltip title="系统角色受保护">
                <Button type="link" size="small" disabled>
                  编辑
                </Button>
              </Tooltip>
              <Tooltip title="系统角色受保护，不可删除">
                <Button type="link" size="small" danger disabled>
                  删除
                </Button>
              </Tooltip>
            </Space>
          )
        }
        return (
          <Space size={4}>
            <Button type="link" size="small" onClick={() => openEdit(row)}>
              编辑
            </Button>
            <Popconfirm
              title="删除角色"
              description={
                row.userCount > 0 || row.menuCount > 0
                  ? `该角色仍被 ${row.userCount} 个用户 / ${row.menuCount} 个菜单引用，无法删除。`
                  : `确认删除角色 ${row.name}？持有该角色的全部用户将被强制下线。`
              }
              okText="确认删除"
              cancelText="取消"
              okButtonProps={{ danger: true, loading: deleteMutation.isPending }}
              onConfirm={() => {
                if (row.userCount > 0 || row.menuCount > 0) {
                  message.warning(`角色 ${row.name} 被引用，无法删除`)
                  return
                }
                handleDelete(row.name)
              }}
            >
              <Button type="link" size="small" danger>
                删除
              </Button>
            </Popconfirm>
          </Space>
        )
      },
    },
  ], [deleteMutation.isPending])

  const editing = typeof modalMode === 'object' ? modalMode.edit : null
  const submitting = createMutation.isPending || updateMutation.isPending

  return (
    <div className="role-list">
      <Space className="role-list-toolbar" style={{ marginBottom: 20 }}>
        <Button
          type="primary"
          onClick={openCreate}
          aria-label="新建角色"
        >
          新建角色
        </Button>
      </Space>
      <Table<RoleRow>
        rowKey={(record) => record.id}
        loading={isPending}
        dataSource={rows}
        columns={columns}
        pagination={false}
      />
      <Modal
        title={editing ? `重命名角色 ${editing.name}` : '新建角色'}
        open={modalMode !== 'closed'}
        onCancel={closeModal}
        onOk={handleSubmit}
        confirmLoading={submitting}
        okText={editing ? '保存' : '创建'}
        cancelText="取消"
        okButtonProps={{ 'data-testid': 'role-modal-ok' }}
        cancelButtonProps={{ 'data-testid': 'role-modal-cancel' }}
        destroyOnClose
      >
        <Form<RoleFormValues> form={form} layout="vertical" preserve={false}>
          <Form.Item
            label="角色名"
            name="name"
            rules={[
              { required: true, message: '请填写角色名' },
              {
                pattern: /^[A-Z][A-Za-z0-9]{1,63}$/,
                message: '以大写字母开头，仅含字母数字，长度 2–64',
              },
            ]}
          >
            <Input placeholder="如 CustomRole" autoFocus allowClear />
          </Form.Item>
        </Form>
      </Modal>
    </div>
  )
}