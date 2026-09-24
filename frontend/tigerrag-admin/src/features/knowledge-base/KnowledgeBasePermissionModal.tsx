import { Alert, Button, Checkbox, Input, Modal, Spin, Tag } from 'antd'
import { useEffect, useMemo, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useKbPermissions, useReplaceKbPermissions } from './useKnowledgeBases'
import { listUsers } from '../users/usersApi'
import { listRolesAll } from '../roles/rolesApi'
import type { UserDto } from '../users/usersApi'
import type { RoleDto } from '../roles/rolesApi'
import { message } from 'antd'

/// <summary>知识库权限管理 Modal 属性。</summary>
interface KnowledgeBasePermissionModalProps {
  /// <summary>被授权知识库 Id。</summary>
  kbId: string
  /// <summary>被授权知识库名称，用于标题显示。</summary>
  kbName: string
  /// <summary>Modal 是否可见。</summary>
  open: boolean
  /// <summary>关闭回调（取消或保存成功后触发）。</summary>
  onClose: () => void
}

/// <summary>
/// 知识库权限管理 Modal：双面板（用户/角色）+ 搜索 + checkbox + 已授权摘要。
/// 整体替换语义：保存时提交全部选中项，后端覆盖已有 ACL。
/// 降级保护：GET 当前权限失败时显示警告，checkbox 全部未勾选，用户仍可操作。
/// </summary>
export function KnowledgeBasePermissionModal({ kbId, kbName, open, onClose }: KnowledgeBasePermissionModalProps) {
  // 查询当前权限状态（降级保护）
  const {
    data: currentPermissions,
    isPending: permissionsLoading,
    isError: permissionsError,
  } = useKbPermissions(open ? kbId : null)

  // 查询用户列表和角色列表
  const { data: users = [] } = useQuery<UserDto[]>({
    queryKey: ['users'],
    queryFn: listUsers,
    enabled: open,
  })
  const { data: roles = [] } = useQuery<RoleDto[]>({
    queryKey: ['roles'],
    queryFn: listRolesAll,
    enabled: open,
  })

  // 本地选择状态
  const [selectedUserIds, setSelectedUserIds] = useState<Set<string>>(new Set())
  const [selectedRoleNames, setSelectedRoleNames] = useState<Set<string>>(new Set())
  const [userSearch, setUserSearch] = useState('')
  const [roleSearch, setRoleSearch] = useState('')

  // Modal 打开时初始化已选状态
  useEffect(() => {
    if (open && currentPermissions) {
      setSelectedUserIds(new Set(currentPermissions.userIds))
      setSelectedRoleNames(new Set(currentPermissions.roles))
    } else if (open && !currentPermissions && !permissionsLoading) {
      // 降级模式：GET 失败，全部未勾选
      setSelectedUserIds(new Set())
      setSelectedRoleNames(new Set())
    }
  }, [open, currentPermissions, permissionsLoading])

  // 搜索过滤
  const filteredUsers = useMemo(() => {
    const keyword = userSearch.toLowerCase()
    return users.filter(
      (u) =>
        u.userName.toLowerCase().includes(keyword) ||
        u.id.toLowerCase().includes(keyword),
    )
  }, [users, userSearch])

  const filteredRoles = useMemo(() => {
    const keyword = roleSearch.toLowerCase()
    return roles.filter((r) => r.name.toLowerCase().includes(keyword))
  }, [roles, roleSearch])

  // 替换权限 mutation
  const replaceMutation = useReplaceKbPermissions()

  /// <summary>切换用户选中状态；checked 未传时取反。</summary>
  function toggleUser(id: string, checked?: boolean) {
    setSelectedUserIds((prev) => {
      const next = new Set(prev)
      const isChecked = checked ?? !next.has(id)
      if (isChecked) next.add(id)
      else next.delete(id)
      return next
    })
  }

  /// <summary>切换角色选中状态；checked 未传时取反。</summary>
  function toggleRole(name: string, checked?: boolean) {
    setSelectedRoleNames((prev) => {
      const next = new Set(prev)
      const isChecked = checked ?? !next.has(name)
      if (isChecked) next.add(name)
      else next.delete(name)
      return next
    })
  }

  /// <summary>保存权限：整体替换语义，成功后关闭 Modal。</summary>
  function handleSave() {
    replaceMutation.mutate(
      {
        kbId,
        userIds: Array.from(selectedUserIds),
        roles: Array.from(selectedRoleNames),
      },
      {
        onSuccess: () => {
          message.success('权限已更新')
          onClose()
        },
      },
    )
  }

  return (
    <Modal
      title={`知识库权限管理 — ${kbName}`}
      open={open}
      onCancel={onClose}
      width={720}
      destroyOnHidden
      footer={null}
      maskClosable={false}
    >
      {permissionsLoading && <Spin style={{ display: 'block', margin: '40px auto' }} />}

      {permissionsError && (
        <Alert
          type="warning"
          showIcon
          style={{ marginBottom: 16 }}
          message="无法加载当前权限状态，保存将覆盖现有权限"
        />
      )}

      {!permissionsLoading && (
        <>
          {/* 双面板：用户授权 + 角色授权 */}
          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 24 }}>
            {/* 左面板：按用户授权 */}
            <div style={{ border: '1px solid var(--border-color)', borderRadius: 6, padding: 16 }}>
              <div style={{ fontWeight: 600, marginBottom: 12 }}>按用户授权</div>
              <Input.Search
                placeholder="搜索用户..."
                value={userSearch}
                onChange={(e) => setUserSearch(e.target.value)}
                style={{ marginBottom: 12 }}
              />
              <div style={{ maxHeight: 240, overflowY: 'auto', border: '1px solid var(--border-color)', borderRadius: 4 }}>
                {filteredUsers.map((user) => (
                  <div
                    key={user.id}
                    style={{ display: 'flex', alignItems: 'center', gap: 10, padding: '8px 16px', cursor: 'pointer' }}
                    onClick={() => toggleUser(user.id)}
                  >
                    <Checkbox
                      checked={selectedUserIds.has(user.id)}
                      onChange={(e) => toggleUser(user.id, e.target.checked)}
                    />
                    <div style={{ flex: 1 }}>
                      <div style={{ fontWeight: 500 }}>{user.userName}</div>
                      <div style={{ fontSize: 12, color: 'var(--text-secondary)' }}>{user.id}</div>
                    </div>
                  </div>
                ))}
              </div>
            </div>

            {/* 右面板：按角色授权 */}
            <div style={{ border: '1px solid var(--border-color)', borderRadius: 6, padding: 16 }}>
              <div style={{ fontWeight: 600, marginBottom: 12 }}>按角色授权</div>
              <Input.Search
                placeholder="搜索角色..."
                value={roleSearch}
                onChange={(e) => setRoleSearch(e.target.value)}
                style={{ marginBottom: 12 }}
              />
              <div style={{ maxHeight: 240, overflowY: 'auto', border: '1px solid var(--border-color)', borderRadius: 4 }}>
                {filteredRoles.map((role) => (
                  <div
                    key={role.name}
                    style={{ display: 'flex', alignItems: 'center', gap: 10, padding: '8px 16px', cursor: 'pointer' }}
                    onClick={() => toggleRole(role.name)}
                  >
                    <Checkbox
                      checked={selectedRoleNames.has(role.name)}
                      onChange={(e) => toggleRole(role.name, e.target.checked)}
                    />
                    <div style={{ flex: 1 }}>
                      <div style={{ fontWeight: 500 }}>{role.name}</div>
                    </div>
                  </div>
                ))}
              </div>
            </div>
          </div>

          <hr style={{ border: 'none', height: 1, background: 'var(--border-color)', margin: '16px 0' }} />

          {/* 已授权摘要 */}
          <div>
            <div style={{ marginBottom: 8, fontWeight: 500 }}>已授权：</div>
            <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>
              {Array.from(selectedUserIds).map((id) => {
                const user = users.find((u) => u.id === id)
                return user ? <Tag key={id} color="success">{user.userName}</Tag> : null
              })}
              {Array.from(selectedRoleNames).map((name) => (
                <Tag key={name} color="blue">{name}</Tag>
              ))}
            </div>
          </div>

          {/* 底部按钮 */}
          <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8, padding: '12px 0', borderTop: '1px solid var(--border-color)', marginTop: 16 }}>
            <Button onClick={onClose}>取消</Button>
            <Button
              type="primary"
              loading={replaceMutation.isPending}
              onClick={handleSave}
            >
              保存权限
            </Button>
          </div>
        </>
      )}
    </Modal>
  )
}
