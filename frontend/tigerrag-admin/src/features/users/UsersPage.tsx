import { Alert, Button, Checkbox, Form, Input, Modal, Popconfirm, Space, Steps, Switch, Table, Tag, message } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useMemo, useState } from 'react'
import { useAuthStore } from '../auth/authStore'
import type { UserDto } from './usersApi'
import {
  useAssignRoles,
  useCreateUserWithPassword,
  useDeleteUser,
  useResetPassword,
  useSetUserLockout,
  useUserRoles,
  useUsers,
} from './useUsers'

// 锁口判定：后端直接返回 isLocked，前端仅做展示与开关映射，不自行推导。
function isLocked(user: UserDto): boolean {
  return user.isLocked
}

export function UsersPage() {
  const { data: users = [], isPending } = useUsers()
  const { data: roles = [] } = useUserRoles()
  const [search, setSearch] = useState('')
  const [createOpen, setCreateOpen] = useState(false)
  const [assignTarget, setAssignTarget] = useState<UserDto | null>(null)
  const [resetTarget, setResetTarget] = useState<UserDto | null>(null)
  const currentUserId = useAuthStore((state) => state.user?.id)
  const deleteMutation = useDeleteUser()
  const lockoutMutation = useSetUserLockout()

  const filtered = useMemo(
    () => users.filter((user) => user.userName.toLowerCase().includes(search.toLowerCase())),
    [users, search],
  )

  const columns: ColumnsType<UserDto> = [
    {
      title: '用户名',
      dataIndex: 'userName',
      key: 'userName',
    },
    {
      title: '角色',
      dataIndex: 'roles',
      key: 'roles',
      render: (userRoles: string[]) => (
        <Space size={4} wrap>
          {userRoles.map((role) => (
            <Tag key={role}>{role}</Tag>
          ))}
        </Space>
      ),
    },
    {
      title: '状态',
      key: 'status',
      width: 120,
      render: (_value, user) => (
        <Switch
          size="small"
          checked={!isLocked(user)}
          checkedChildren="正常"
          unCheckedChildren="锁定"
          loading={lockoutMutation.isPending}
          disabled={user.id === currentUserId}
          onChange={(checked) =>
            lockoutMutation.mutate({ user, locked: !checked })
          }
        />
      ),
    },
    {
      title: '操作',
      key: 'actions',
      width: 280,
      render: (_value, user) => (
        <Space>
          <Button type="link" onClick={() => setAssignTarget(user)}>
            分配角色
          </Button>
          <Button type="link" onClick={() => setResetTarget(user)}>
            重置密码
          </Button>
          <Popconfirm
            title="删除用户"
            description={`确认删除用户 ${user.userName}？该操作不可撤销。`}
            okText="确认删除"
            cancelText="取消"
            okButtonProps={{ danger: true, loading: deleteMutation.isPending }}
            onConfirm={() => deleteMutation.mutate(user)}
            disabled={user.id === currentUserId}
          >
            <Button type="link" danger disabled={user.id === currentUserId}>
              删除
            </Button>
          </Popconfirm>
        </Space>
      ),
    },
  ]

  return (
    <main>
      <h2 className="page-heading">用户管理</h2>
      <div className="users-toolbar">
        <Input.Search
          allowClear
          placeholder="按用户名过滤"
          onChange={(event) => setSearch(event.target.value)}
          className="users-search"
        />
        <Button type="primary" onClick={() => setCreateOpen(true)}>
          新建用户
        </Button>
      </div>
      <Table<UserDto>
        rowKey="id"
        loading={isPending}
        dataSource={filtered}
        columns={columns}
        pagination={{ pageSize: 20, showSizeChanger: false }}
      />

      <CreateUserDialog
        open={createOpen}
        roles={roles}
        onCancel={() => setCreateOpen(false)}
        onCreated={() => setCreateOpen(false)}
      />
      <AssignRolesDialog
        target={assignTarget}
        roles={roles}
        isSelf={assignTarget !== null && assignTarget.id === currentUserId}
        currentUserId={currentUserId}
        onCancel={() => setAssignTarget(null)}
        onAssigned={() => setAssignTarget(null)}
      />
      <ResetPasswordDialog
        target={resetTarget}
        onCancel={() => setResetTarget(null)}
        onReset={() => setResetTarget(null)}
      />
    </main>
  )
}

interface CreateUserDialogProps {
  open: boolean
  roles: string[]
  onCancel: () => void
  onCreated: () => void
}

// 多步向导：基本信息 → 设置初始密码 → 完成。失败保留输入、不跳步。
function CreateUserDialog({ open, roles, onCancel, onCreated }: CreateUserDialogProps) {
  const [step, setStep] = useState(0)
  const [userName, setUserName] = useState('')
  const [selectedRoles, setSelectedRoles] = useState<string[]>(['Viewer'])
  const [password, setPassword] = useState('')
  const [confirm, setConfirm] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)
  const mutation = useCreateUserWithPassword()

  function handleCancel() {
    if (submitting) return
    onCancel()
  }

  async function handleSubmit() {
    setError(null)
    if (!userName.trim()) {
      setError('请填写用户名')
      return
    }
    if (selectedRoles.length === 0) {
      setError('请至少选择一个角色')
      return
    }
    if (password.length < 8) {
      setError('密码至少 8 位')
      return
    }
    if (password !== confirm) {
      setError('两次输入的密码不一致')
      return
    }
    setSubmitting(true)
    try {
      await mutation.mutateAsync({ userName: userName.trim(), roles: selectedRoles, password })
      message.success('用户创建成功')
      onCreated()
    } catch (mutationError) {
      setError(mutationError instanceof Error ? mutationError.message : '创建失败')
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <Modal
      title="新建用户"
      open={open}
      onCancel={handleCancel}
      destroyOnHidden
      footer={null}
      maskClosable={false}
    >
      <Steps
        current={step}
        items={[{ title: '基本信息' }, { title: '设置初始密码' }, { title: '完成' }]}
        className="users-step"
      />
      {error !== null && <Alert type="error" message={error} showIcon className="users-alert" />}
      {step === 0 && (
        <Form layout="vertical" className="users-form">
          <Form.Item label="用户名" required>
            <Input value={userName} onChange={(event) => setUserName(event.target.value)} />
          </Form.Item>
          <Form.Item label="角色">
            <Checkbox.Group
              value={selectedRoles}
              onChange={(values) => setSelectedRoles(values as string[])}
            >
              <Space wrap>
                {roles.map((role) => (
                  <Checkbox key={role} value={role}>
                    {role}
                  </Checkbox>
                ))}
              </Space>
            </Checkbox.Group>
          </Form.Item>
          <div className="users-form-actions">
            <Button onClick={handleCancel} disabled={submitting}>
              取消
            </Button>
            <Button type="primary" onClick={() => setStep(1)} disabled={!userName.trim()}>
              下一步
            </Button>
          </div>
        </Form>
      )}
      {step === 1 && (
        <Form layout="vertical" className="users-form">
          <Form.Item label="密码" required>
            <Input.Password
              value={password}
              onChange={(event) => setPassword(event.target.value)}
            />
          </Form.Item>
          <Form.Item label="确认密码" required>
            <Input.Password
              value={confirm}
              onChange={(event) => setConfirm(event.target.value)}
            />
          </Form.Item>
          <div className="users-form-actions">
            <Button onClick={() => setStep(0)} disabled={submitting}>
              上一步
            </Button>
            <Button type="primary" loading={submitting} onClick={handleSubmit}>
              提交
            </Button>
          </div>
        </Form>
      )}
      {step === 2 && (
        <div className="users-form">
          <Alert type="success" showIcon message={`用户 ${userName} 已创建并设置初始密码`} />
          <div className="users-form-actions">
            <Button type="primary" onClick={onCreated}>
              完成
            </Button>
          </div>
        </div>
      )}
    </Modal>
  )
}

interface AssignRolesDialogProps {
  target: UserDto | null
  roles: string[]
  isSelf: boolean
  currentUserId: string | undefined
  onCancel: () => void
  onAssigned: () => void
}

// 单步：勾选角色提交。若是当前用户且新角色不含 Admin，提示自保护警告。
function AssignRolesDialog({
  target,
  roles,
  isSelf,
  currentUserId,
  onCancel,
  onAssigned,
}: AssignRolesDialogProps) {
  const initialRoles = useMemo(() => (target === null ? [] : [...target.roles]), [target])
  const [selected, setSelected] = useState<string[]>(initialRoles)
  // 每次切换 target 时重置 selected：target 变化是唯一触发点。
  const [lastTargetId, setLastTargetId] = useState<string | null>(target?.id ?? null)
  if (target !== null && target.id !== lastTargetId) {
    setLastTargetId(target.id)
    setSelected([...target.roles])
  }
  const [error, setError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)
  const mutation = useAssignRoles(currentUserId)

  if (target === null) {
    return <Modal open={false} footer={null} onCancel={onCancel} />
  }
  const currentTarget = target

  const willLoseAdmin = isSelf && !selected.includes('Admin')

  async function handleSubmit() {
    setError(null)
    if (selected.length === 0) {
      setError('请至少选择一个角色')
      return
    }
    setSubmitting(true)
    try {
      await mutation.mutateAsync({ user: currentTarget, roles: selected })
      message.success('角色已更新')
      onAssigned()
    } catch (mutationError) {
      setError(mutationError instanceof Error ? mutationError.message : '更新失败')
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <Modal
      title={`分配角色：${currentTarget.userName}`}
      open={currentTarget !== null}
      onCancel={onCancel}
      destroyOnHidden
      footer={null}
      maskClosable={false}
    >
      {willLoseAdmin && (
        <Alert
          type="warning"
          showIcon
          className="users-alert"
          message="提交后您将失去用户管理权限，无法再访问此页面"
        />
      )}
      {error !== null && <Alert type="error" showIcon className="users-alert" message={error} />}
      <Checkbox.Group
        value={selected}
        onChange={(values) => setSelected(values as string[])}
        className="users-form"
      >
        <Space wrap>
          {roles.map((role) => (
            <Checkbox key={role} value={role}>
              {role}
            </Checkbox>
          ))}
        </Space>
      </Checkbox.Group>
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

interface ResetPasswordDialogProps {
  target: UserDto | null
  onCancel: () => void
  onReset: () => void
}

// 单步：密码 + 确认。salt 在 useResetPassword mutation 中按需取。
function ResetPasswordDialog({ target, onCancel, onReset }: ResetPasswordDialogProps) {
  const [password, setPassword] = useState('')
  const [confirm, setConfirm] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)
  const mutation = useResetPassword()

  if (target === null) {
    return <Modal open={false} footer={null} onCancel={onCancel} />
  }
  const currentTarget = target

  async function handleSubmit() {
    setError(null)
    if (password.length < 8) {
      setError('密码至少 8 位')
      return
    }
    if (password !== confirm) {
      setError('两次输入的密码不一致')
      return
    }
    setSubmitting(true)
    try {
      await mutation.mutateAsync({ user: currentTarget, password })
      message.success(`已重置 ${currentTarget.userName} 的密码`)
      onReset()
    } catch (mutationError) {
      setError(mutationError instanceof Error ? mutationError.message : '重置失败')
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <Modal
      title={`重置密码：${currentTarget.userName}`}
      open={currentTarget !== null}
      onCancel={onCancel}
      destroyOnHidden
      footer={null}
      maskClosable={false}
    >
      {error !== null && <Alert type="error" showIcon className="users-alert" message={error} />}
      <Form layout="vertical" className="users-form">
        <Form.Item label="新密码" required>
          <Input.Password
            value={password}
            onChange={(event) => setPassword(event.target.value)}
          />
        </Form.Item>
        <Form.Item label="确认密码" required>
          <Input.Password
            value={confirm}
            onChange={(event) => setConfirm(event.target.value)}
          />
        </Form.Item>
      </Form>
      <div className="users-form-actions">
        <Button onClick={onCancel} disabled={submitting}>
          取消
        </Button>
        <Button type="primary" loading={submitting} onClick={handleSubmit}>
          重置
        </Button>
      </div>
    </Modal>
  )
}