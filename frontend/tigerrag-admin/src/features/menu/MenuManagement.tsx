import {
  AutoComplete,
  Button,
  Form,
  Input,
  InputNumber,
  Modal,
  Popconfirm,
  Select,
  Space,
  Switch,
  Table,
  Tag,
  message,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useEffect, useMemo, useState } from 'react'
import {
  useCreateMenuConfig,
  useDeleteMenuConfig,
  useMenuConfigs,
  useUpdateMenuConfig,
} from './useMenuConfig'
import type { MenuConfigDto } from './menuApi'
import { AVAILABLE_ICONS } from './menuIcons'
import { ALL_ROLES } from '../auth/permissions'
import { useRoles } from '../users/useRoles'

interface MenuForm {
  key: string
  label: string
  icon: string | null
  roles: string[]
  parentId: string | null
  sortOrder: number
  isEnabled: boolean
}

/// <summary>带子级引用的菜单节点：把扁平 parentId 平铺结果拼成树给前端表格渲染。</summary>
export interface TreeMenuNode extends MenuConfigDto {
  children: TreeMenuNode[]
}

const emptyForm: MenuForm = {
  key: '',
  label: '',
  icon: null,
  roles: [],
  parentId: null,
  sortOrder: 0,
  isEnabled: true,
}

/// <summary>
/// 把扁平菜单列表拼成树。parentId 指向不存在项的孤立节点落到顶级，避免数据丢失。
/// 同级按 sortOrder 升序、再按 key 字典序，保证输出稳定。
/// </summary>
export function buildMenuTree(items: readonly MenuConfigDto[]): TreeMenuNode[] {
  const map = new Map<string, TreeMenuNode>()
  items.forEach((item) => map.set(item.id, { ...item, children: [] }))
  const roots: TreeMenuNode[] = []
  items.forEach((item) => {
    const node = map.get(item.id)
    if (node === undefined) return
    if (item.parentId !== null && map.has(item.parentId)) {
      const parent = map.get(item.parentId)
      if (parent !== undefined) parent.children.push(node)
    } else {
      roots.push(node)
    }
  })
  const sortRec = (nodes: TreeMenuNode[]) => {
    nodes.sort((a, b) => a.sortOrder - b.sortOrder || a.key.localeCompare(b.key))
    nodes.forEach((n) => sortRec(n.children))
  }
  sortRec(roots)
  return roots
}

/// <summary>展开/折叠工具栏用：把整棵树的所有 id 拍平成数组。</summary>
function collectAllIds(nodes: readonly TreeMenuNode[]): string[] {
  const ids: string[] = []
  const walk = (arr: readonly TreeMenuNode[]) => {
    arr.forEach((n) => {
      ids.push(n.id)
      walk(n.children)
    })
  }
  walk(nodes)
  return ids
}

/// <summary>从树里收集某节点及其所有后代的 id，用来在父菜单候选里屏蔽自身与子孙。</summary>
export function collectDescendantIds(
  nodes: readonly TreeMenuNode[],
  id: string,
): Set<string> {
  const result = new Set<string>([id])
  const findAndWalk = (arr: readonly TreeMenuNode[]): boolean => {
    for (const n of arr) {
      if (n.id === id) {
        const walk = (sub: readonly TreeMenuNode[]) => {
          sub.forEach((s) => {
            result.add(s.id)
            walk(s.children)
          })
        }
        walk(n.children)
        return true
      }
      if (findAndWalk(n.children)) return true
    }
    return false
  }
  findAndWalk(nodes)
  return result
}

/// <summary>菜单管理页：CRUD 菜单配置，树形表格 + 展开折叠工具栏 + 父菜单候选屏蔽子孙。</summary>
export function MenuManagement() {
  const { data: items = [], isPending } = useMenuConfigs()
  const { data: allRoles = [] } = useRoles()
  const createMutation = useCreateMenuConfig()
  const updateMutation = useUpdateMenuConfig()
  const deleteMutation = useDeleteMenuConfig()
  const [editing, setEditing] = useState<MenuConfigDto | null>(null)
  const [formData, setFormData] = useState<MenuForm>(emptyForm)
  const [formOpen, setFormOpen] = useState(false)
  const [expandedKeys, setExpandedKeys] = useState<string[]>([])

  const roleOptions = useMemo(
    () => [...ALL_ROLES, ...allRoles.map((r) => r.name)].filter(
      (role, index, array) => array.indexOf(role) === index,
    ),
    [allRoles],
  )

  const treeData = useMemo(() => buildMenuTree(items), [items])
  const allKeys = useMemo(() => collectAllIds(treeData), [treeData])

  // 数据首次到位时默认展开全部。用 join 后的字符串当依赖，避免 allKeys 数组引用变化触发循环。
  const allKeysKey = allKeys.join('|')
  useEffect(() => {
    setExpandedKeys(allKeys)
  }, [allKeysKey]) // eslint-disable-line react-hooks/exhaustive-deps

  // 父项候选：排除自身与所有后代，避免把父选成自己的子孙形成环。
  const excludedIds = useMemo(() => {
    if (editing === null) return new Set<string>()
    return collectDescendantIds(buildMenuTree(items), editing.id)
  }, [items, editing])
  const parentCandidates = useMemo(
    () => items.filter((item) => !excludedIds.has(item.id)),
    [items, excludedIds],
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
      roles: item.roles,
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
      roles: formData.roles,
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

  const columns: ColumnsType<TreeMenuNode> = [
    { title: 'Key', dataIndex: 'key', key: 'key', width: 160 },
    {
      title: '显示名称',
      dataIndex: 'label',
      key: 'label',
      width: 220,
      render: (label: string, node) => {
        const isRoot = node.parentId === null
        return (
          <Space size={6}>
            <Tag color={isRoot ? 'default' : 'default'}>{isRoot ? '顶级' : '子级'}</Tag>
            <span>{label}</span>
          </Space>
        )
      },
    },
    {
      title: '图标',
      dataIndex: 'icon',
      key: 'icon',
      width: 140,
      render: (icon: string | null) => (icon ? <Tag>{icon}</Tag> : '-'),
    },
    {
      title: '可见角色',
      dataIndex: 'roles',
      key: 'roles',
      width: 220,
      render: (roles: string[]) => (
        <Space wrap>
          {roles.length === 0 ? (
            <Tag color="green">所有人</Tag>
          ) : (
            roles.map((role) => (
              <Tag key={role} color="blue">
                {role}
              </Tag>
            ))
          )}
        </Space>
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
      width: 200,
      render: (_value, item) => (
        <Space>
          <Button type="link" size="small" onClick={() => openEdit(item)}>
            编辑
          </Button>
          <Popconfirm
            title="删除菜单项"
            description={`确认删除菜单项 ${item.label}？该操作不可撤销。`}
            okText="确认删除"
            cancelText="取消"
            okButtonProps={{ danger: true, loading: deleteMutation.isPending }}
            onConfirm={() => handleDelete(item)}
          >
            <Button type="link" size="small" danger>
              删除
            </Button>
          </Popconfirm>
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
        <Button onClick={() => setExpandedKeys(allKeys)} disabled={allKeys.length === 0}>
          展开全部
        </Button>
        <Button onClick={() => setExpandedKeys([])} disabled={expandedKeys.length === 0}>
          折叠全部
        </Button>
      </div>
      <Table<TreeMenuNode>
        rowKey="id"
        loading={isPending}
        dataSource={treeData}
        columns={columns}
        pagination={{ pageSize: 50, showSizeChanger: false }}
        expandable={{
          expandedRowKeys: expandedKeys,
          onExpandedRowsChange: (keys) => setExpandedKeys(keys as string[]),
          childrenColumnName: 'children',
        }}
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
            <AutoComplete
              allowClear
              value={formData.icon ?? undefined}
              onChange={(value) =>
                setFormData((prev) => ({ ...prev, icon: value ? value.trim() : null }))
              }
              options={AVAILABLE_ICONS.map((icon) => ({ value: icon, label: icon }))}
              placeholder="如 DashboardOutlined，或留空"
              filterOption={(input, option) =>
                (option?.value as string).toLowerCase().includes(input.toLowerCase())
              }
            />
          </Form.Item>
          <Form.Item label="可见角色（空 = 所有人可见）">
            <Select
              mode="multiple"
              allowClear
              value={formData.roles}
              onChange={(value) => setFormData((prev) => ({ ...prev, roles: value }))}
              options={roleOptions.map((role) => ({ value: role, label: role }))}
              placeholder="选择可见角色"
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
