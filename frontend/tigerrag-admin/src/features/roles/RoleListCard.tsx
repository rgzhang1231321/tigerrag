import {
  Button,
  Form,
  Input,
  Modal,
  Popconfirm,
  Space,
  Table,
  Tabs,
  Tag,
  Tooltip,
  Tree,
  message,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useMemo, useRef, useState } from 'react'
import { RoleEndpointMatrix, type RoleEndpointMatrixHandle } from '../role-endpoints/RoleEndpointMatrix'
import { buildMenuTree, type TreeMenuNode } from '../menu/MenuManagement'
import { useMenuConfigs, useUpdateMenuConfig } from '../menu/useMenuConfig'
import { useCreateRole, useDeleteRole, useRoles, useUpdateRole } from './useRoles'
import type { RoleDto } from './rolesApi'

interface RoleRow {
  id: string
  name: string
  userCount: number
  menuCount: number
  menuNames: string[]
}

interface RoleFormValues {
  name: string
  menuKeys: string[]
}

type ModalMode = 'closed' | 'create' | { edit: RoleDto }

export interface TreeNodeData {
  title: string
  key: string
  children?: TreeNodeData[]
}

/// <summary>把 MenuManagement 的扁平 TreeMenuNode 转换成 antd Tree 用的 {title,key,children} 形态。</summary>
function toAntdTree(nodes: TreeMenuNode[]): TreeNodeData[] {
  return nodes.map((n) => ({
    title: n.label,
    key: n.key,
    children: n.children.length > 0 ? toAntdTree(n.children) : undefined,
  }))
}

/// <summary>收集某节点下所有后代 key（递归）。用于父节点勾选时自动全选子节点。</summary>
export function collectDescendantKeys(nodes: TreeNodeData[], targetKey: string): string[] {
  const result: string[] = []
  const findAndCollect = (arr: TreeNodeData[]): boolean => {
    for (const n of arr) {
      if (n.key === targetKey) {
        collectAll(n)
        return true
      }
      if (n.children && findAndCollect(n.children)) return true
    }
    return false
  }
  const collectAll = (node: TreeNodeData) => {
    if (node.children) {
      for (const c of node.children) {
        result.push(c.key)
        collectAll(c)
      }
    }
  }
  findAndCollect(nodes)
  return result
}

/// <summary>角色列表卡片：弹框方式新增/编辑（仅名称展示，菜单可见性在 tab 中配置），表格展示名称、引用数与关联菜单；所有角色平等可编辑/删除。</summary>
export function RoleListCard() {
  const { data: roles = [], isPending } = useRoles()
  const { data: menuItems = [] } = useMenuConfigs()
  const createMutation = useCreateRole()
  const updateMutation = useUpdateRole()
  const deleteMutation = useDeleteRole()
  const updateMenuMutation = useUpdateMenuConfig()
  const [modalMode, setModalMode] = useState<ModalMode>('closed')
  const [form] = Form.useForm<RoleFormValues>()
  const editing = typeof modalMode === 'object' ? modalMode.edit : null
  const endpointMatrixRef = useRef<RoleEndpointMatrixHandle>(null)

  function openCreate() {
    form.resetFields()
    setModalMode('create')
  }

  function openEdit(role: RoleDto) {
    setModalMode({ edit: role })
  }

  function closeModal() {
    setModalMode('closed')
    form.resetFields()
  }

  async function handleSubmit() {
    const trimmed = (form.getFieldValue('name') ?? '').trim()
    if (modalMode === 'create' && !trimmed) {
      message.warning('请填写角色名')
      return
    }
    const selectedMenuKeys = (form.getFieldValue('menuKeys') ?? []) as string[]

    try {
      if (modalMode === 'create') {
        await createMutation.mutateAsync({ name: trimmed })
        message.success(`角色 ${trimmed} 已创建`)
      } else if (typeof modalMode === 'object') {
        await syncMenuVisibility(modalMode.edit.name, modalMode.edit.name, selectedMenuKeys)
        if (endpointMatrixRef.current?.isDirty) {
          await endpointMatrixRef.current.submit()
        }
        message.success('角色已保存')
      }
      closeModal()
    } catch (error) {
      message.error(error instanceof Error ? error.message : '操作失败')
    }
  }

  /** 根据勾选结果同步每个菜单的 roles 字段：勾选 = 角色在 roles 中，未勾选 = 不在。仅修改状态发生变化的菜单。 */
  async function syncMenuVisibility(
    originalName: string,
    newName: string,
    selectedKeys: string[],
  ) {
    const roleName = newName || originalName
    const allRoleNames = roles.map((r) => r.name)
    const selectedSet = new Set(selectedKeys)
    const ops: Promise<unknown>[] = []

    for (const menu of menuItems) {
      const oldRoles = menu.roles
      const wasVisible = oldRoles.length === 0 || oldRoles.includes(originalName)
      const willVisible = selectedSet.has(menu.key)

      // 状态未变且角色名未变 → 跳过
      if (wasVisible === willVisible && originalName === newName) continue

      let newRoles: string[]
      if (willVisible) {
        if (oldRoles.length === 0) {
          // 全员可见保持全员可见
          newRoles = []
        } else {
          // 受限菜单：替换旧名或追加新名
          const withoutOld = oldRoles.filter((r) => r !== originalName)
          newRoles = withoutOld.includes(roleName) ? withoutOld : [...withoutOld, roleName]
        }
      } else {
        if (oldRoles.length === 0) {
          // 全员可见 → 排除该角色，写入其他全部角色
          newRoles = allRoleNames.filter((r) => r !== roleName)
        } else {
          // 受限菜单：移除角色
          newRoles = oldRoles.filter((r) => r !== originalName)
        }
      }

      ops.push(updateMenuMutation.mutateAsync({ id: menu.id, data: { roles: newRoles } }))
    }

    if (ops.length > 0) await Promise.all(ops)
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
    userCount: role.userCount,
    menuCount: role.menuCount,
    menuNames: role.menuNames ?? [],
  }))

  const columns: ColumnsType<RoleRow> = useMemo(() => [
    { title: '角色名', key: 'name', width: 200, render: (_value, row) => row.name },
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
      render: (_value, row) => (
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
      ),
    },
  ], [deleteMutation.isPending])

  const submitting = createMutation.isPending || updateMutation.isPending

  const initialValues = useMemo(() => {
    if (!editing) return undefined
    const visibleKeys = menuItems
      .filter((m) => m.roles.length === 0 || m.roles.includes(editing.name))
      .map((m) => m.key)
    return { menuKeys: visibleKeys }
  }, [editing, menuItems])

  // menuItems 未就绪时不渲染表单，避免 initialValues 为空导致 checkbox 初始态丢失。
  const menuTree = useMemo(() => buildMenuTree(menuItems), [menuItems])
  const dataReady = !editing || (menuItems.length > 0 && initialValues !== undefined)

  // 计算 Tree 的默认勾选 key（仅首次挂载时生效）。
  const treeDefaultCheckedKeys = useMemo(() => {
    if (!editing) return []
    return menuItems
      .filter((m) => m.roles.length === 0 || m.roles.includes(editing.name))
      .map((m) => m.key)
  }, [editing, menuItems])

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
        title={editing ? `编辑角色 ${editing.name}` : '新建角色'}
        open={modalMode !== 'closed'}
        onCancel={closeModal}
        onOk={handleSubmit}
        confirmLoading={submitting}
        okText={editing ? '保存' : '创建'}
        cancelText="取消"
        width={editing ? 720 : undefined}
        okButtonProps={{ 'data-testid': 'role-modal-ok' }}
        cancelButtonProps={{ 'data-testid': 'role-modal-cancel' }}
        destroyOnClose
      >
        {editing ? (
          <Tabs
            items={[
              {
                key: 'basic',
                label: '基本信息',
                children: dataReady ? (
                  <Form<RoleFormValues> form={form} layout="vertical" preserve={false} initialValues={initialValues}>
                    <Form.Item label="角色名">
                      <Tag color="blue" style={{ fontSize: 14, padding: '2px 12px' }}>
                        {editing.name}
                      </Tag>
                    </Form.Item>
                    <Form.Item label="可见菜单（已自动勾选当前角色可见的菜单）" name="menuKeys">
                      <div
                        style={{
                          maxHeight: 320,
                          overflow: 'auto',
                          padding: 8,
                          border: '1px solid #d9d9d9',
                          borderRadius: 6,
                        }}
                      >
                        <Tree
                          key={editing?.name ?? 'none'}
                          checkable
                          checkStrictly
                          defaultExpandAll
                          treeData={toAntdTree(menuTree)}
                          defaultCheckedKeys={treeDefaultCheckedKeys}
                          onCheck={(checked) => {
                            const list = Array.isArray(checked) ? checked : checked.checked
                            const checkedSet = new Set(list.map(String) as string[])
                            const prevKeys = (form.getFieldValue('menuKeys') ?? []).map(String)
                            const prevSet = new Set<string>(prevKeys)
                            const treeData = toAntdTree(menuTree)

                            const newChecked = new Set(checkedSet)
                            // 新增的 key：若是父节点则展开所有后代一并勾选
                            for (const key of checkedSet) {
                              if (!prevSet.has(key)) {
                                for (const desc of collectDescendantKeys(treeData, key)) {
                                  newChecked.add(desc)
                                }
                              }
                            }
                            // 移除的 key：若是父节点则展开所有后代一并取消
                            for (const key of prevSet) {
                              if (!checkedSet.has(key)) {
                                for (const desc of collectDescendantKeys(treeData, key)) {
                                  newChecked.delete(desc)
                                }
                              }
                            }

                            form.setFieldValue('menuKeys', Array.from(newChecked))
                          }}
                        />
                      </div>
                    </Form.Item>
                  </Form>
                ) : (
                  <div style={{ padding: '16px 0', color: 'rgba(0,0,0,0.45)' }}>加载中…</div>
                ),
              },
              {
                key: 'endpoints',
                label: '接口授权',
                children: <RoleEndpointMatrix ref={endpointMatrixRef} role={editing.name} />,
              },
            ]}
          />
        ) : (
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
        )}
      </Modal>
    </div>
  )
}
