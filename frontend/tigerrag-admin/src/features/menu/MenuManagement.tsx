import { Button, Form, Input, InputNumber, Modal, Select, Space, Switch, Table, Tag, message } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useMemo, useState } from 'react'
import {
  useCreateMenuConfig,
  useDeleteMenuConfig,
  useMenuConfigs,
  useUpdateMenuConfig,
} from './useMenuConfig'
import type { MenuConfigDto } from './menuApi'
import { AVAILABLE_ICONS } from './menuIcons'
import { listPermissions, permissionLabel } from '../auth/permissions'

interface MenuForm {
  key: string
  label: string
  icon: string | null
  permission: string | null
  parentId: string | null
  sortOrder: number
  isEnabled: boolean
}

const emptyForm: MenuForm = {
  key: '',
  label: '',
  icon: null,
  permission: null,
  parentId: null,
  sortOrder: 0,
  isEnabled: true,
}

/// <summary>菜单管理页：CRUD 菜单配置，支持层级、图标、权限、排序、启用/禁用。</summary>
export function MenuManagement() {
  const { data: items = [], isPending } = useMenuConfigs()
  const createMutation = useCreateMenuConfig()
  const updateMutation = useUpdateMenuConfig()
  const deleteMutation = useDeleteMenuConfig()
  const [editing, setEditing] = useState<MenuConfigDto | null>(null)
  const [formData, setFormData] = useState<MenuForm>(emptyForm)
  const [formOpen, setFormOpen] = useState(false)

  const permissions = useMemo(() => [...listPermissions({ id: 'fixture', userName: 'fixture', roles: ['Admin'] })], [])

  // 父项候选：排除自身，避免循环。
  const parentCandidates = useMemo(
    () => (editing ? items.filter((item) => item.id !== editing.id) : items),
    [items, editing],
  )

  function openCreate() {
    setEditing(null)
    setFormData(emptyForm)
    setFormOpen(true)
  }

  function openEdit(item: MenuConfigDto) {
    setEditing(item)
    setFormData({
      key: item.key,
      label: item.label,
      icon: item.icon,
      permission: item.permission,
      parentId: item.parentId,
      sortOrder: item.sortOrder,
      isEnabled: item.isEnabled,
    })
    setFormOpen(true)
  }

  function closeForm() {
    setFormOpen(false)
  }

  async function handleSubmit() {
    if (!formData.key.trim() || !formData.label.trim()) {
      message.warning('请填写 Key 和显示名称')
      return
    }
    const payload = {
      key: formData.key.trim(),
      label: formData.label.trim(),
      icon: formData.icon || null,
      permission: formData.permission || null,
      parentId: formData.parentId || null,
      sortOrder: formData.sortOrder,
      isEnabled: formData.isEnabled,
    }
    try {
      if (editing) {
        await updateMutation.mutateAsync({ id: editing.id, data: payload })
        message.success('菜单项已更新')
      } else {
        await createMutation.mutateAsync(payload)
        message.success('菜单项已创建')
      }
      closeForm()
    } catch (error) {
      message.error(error instanceof Error ? error.message : '保存失败')
    }
  }

  async function handleDelete(item: MenuConfigDto) {
    try {
      await deleteMutation.mutateAsync(item.id)
      message.success('菜单项已删除')
    } catch (error) {
      message.error(error instanceof Error ? error.message : '删除失败')
    }
  }

  const columns: ColumnsType<MenuConfigDto> = [
    { title: 'Key', dataIndex: 'key', key: 'key', width: 160 },
    { title: '显示名称', dataIndex: 'label', key: 'label', width: 140 },
    {
      title: '图标',
      dataIndex: 'icon',
      key: 'icon',
      width: 140,
      render: (icon: string | null) => (icon ? <Tag>{icon}</Tag> : '-'),
    },
    {
      title: '所需权限',
      dataIndex: 'permission',
      key: 'permission',
      width: 160,
      render: (permission: string | null) => (
        <Tag color="blue" title={permission ?? undefined}>
          {permissionLabel(permission)}
        </Tag>
      ),
    },
    {
      title: '排序',
      dataIndex: 'sortOrder',
      key: 'sortOrder',
      width: 80,
    },
    {
      title: '启用',
      dataIndex: 'isEnabled',
      key: 'isEnabled',
      width: 80,
      render: (isEnabled: boolean, item) => (
        <Switch
          size="small"
          checked={isEnabled}
          loading={updateMutation.isPending}
          onChange={(checked) =>
            updateMutation.mutate({ id: item.id, data: { isEnabled: checked } })
          }
        />
      ),
    },
    {
      title: '操作',
      key: 'actions',
      width: 160,
      render: (_value, item) => (
        <Space>
          <Button type="link" size="small" onClick={() => openEdit(item)}>
            编辑
          </Button>
          <Button type="link" size="small" danger onClick={() => handleDelete(item)}>
            删除
          </Button>
        </Space>
      ),
    },
  ]

  return (
    <main>
      <h2 className="page-heading">菜单管理</h2>
      <div className="users-toolbar">
        <Button type="primary" onClick={openCreate}>
          新建菜单项
        </Button>
      </div>
      <Table<MenuConfigDto>
        rowKey="id"
        loading={isPending}
        dataSource={items}
        columns={columns}
        pagination={{ pageSize: 20, showSizeChanger: false }}
      />

      <Modal
        title={editing ? '编辑菜单项' : '新建菜单项'}
        open={formOpen}
        onCancel={closeForm}
        destroyOnHidden
        footer={null}
        maskClosable={false}
      >
        <Form layout="vertical" className="users-form" initialValues={formData}>
          <Form.Item label="Key（路由路径）" required>
            <Input
              value={formData.key}
              onChange={(event) => setFormData((prev) => ({ ...prev, key: event.target.value }))}
              placeholder="/example"
            />
          </Form.Item>
          <Form.Item label="显示名称" required>
            <Input
              value={formData.label}
              onChange={(event) => setFormData((prev) => ({ ...prev, label: event.target.value }))}
            />
          </Form.Item>
          <Form.Item label="图标">
            <Select
              allowClear
              value={formData.icon}
              onChange={(value) => setFormData((prev) => ({ ...prev, icon: value || null }))}
              options={AVAILABLE_ICONS.map((icon) => ({ value: icon, label: icon }))}
            />
          </Form.Item>
          <Form.Item label="所需权限（空 = 所有人可见）">
            <Select
              allowClear
              value={formData.permission}
              onChange={(value) => setFormData((prev) => ({ ...prev, permission: value || null }))}
              options={permissions.map((permission) => ({
                value: permission,
                label: permissionLabel(permission),
              }))}
            />
          </Form.Item>
          <Form.Item label="父菜单（空 = 顶级）">
            <Select
              allowClear
              value={formData.parentId}
              onChange={(value) => setFormData((prev) => ({ ...prev, parentId: value || null }))}
              options={parentCandidates.map((item) => ({
                value: item.id,
                label: `${item.label} (${item.key})`,
              }))}
            />
          </Form.Item>
          <Form.Item label="排序号">
            <InputNumber
              min={0}
              value={formData.sortOrder}
              onChange={(value) => setFormData((prev) => ({ ...prev, sortOrder: value ?? 0 }))}
              style={{ width: '100%' }}
            />
          </Form.Item>
          <Form.Item label="启用">
            <Switch
              checked={formData.isEnabled}
              onChange={(checked) => setFormData((prev) => ({ ...prev, isEnabled: checked }))}
            />
          </Form.Item>
        </Form>
        <div className="users-form-actions">
          <Button onClick={closeForm} disabled={createMutation.isPending || updateMutation.isPending}>
            取消
          </Button>
          <Button
            type="primary"
            loading={createMutation.isPending || updateMutation.isPending}
            onClick={handleSubmit}
          >
            保存
          </Button>
        </div>
      </Modal>
    </main>
  )
}