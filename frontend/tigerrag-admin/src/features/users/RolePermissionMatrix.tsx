import { Card, Table, Tag } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { ALL_ROLES, listPermissions, permissionLabel, type Permission, type Role } from '../auth/permissions'

interface RolePermissionRow {
  key: string
  role: Role
  permissions: Permission[]
}

const columns: ColumnsType<RolePermissionRow> = [
  {
    title: '角色',
    dataIndex: 'role',
    key: 'role',
    width: 160,
    render: (role: Role) => <Tag color="blue">{role}</Tag>,
  },
  {
    title: '权限',
    dataIndex: 'permissions',
    key: 'permissions',
    render: (permissions: Permission[]) => (
      <div className="role-permission-tags">
        {permissions.map((permission) => (
          <Tag key={permission} title={permission}>{permissionLabel(permission)}</Tag>
        ))}
      </div>
    ),
  },
]

/// <summary>
/// 角色 → 权限映射表：一期为固定映射，只读展示。映射与后端 RolePermissionMap.cs 严格一致。
/// </summary>
export function RolePermissionMatrix() {
  const rows: RolePermissionRow[] = ALL_ROLES.map((role) => ({
    key: role,
    role,
    permissions: [...listPermissions({ id: 'fixture', userName: 'fixture', roles: [role] })],
  }))

  return (
    <Card title="角色权限映射" className="role-permission-matrix">
      <Table<RolePermissionRow>
        rowKey="key"
        dataSource={rows}
        columns={columns}
        pagination={false}
      />
    </Card>
  )
}