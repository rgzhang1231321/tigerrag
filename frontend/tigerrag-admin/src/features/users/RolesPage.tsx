import { RoleListCard } from './RoleListCard'

/// <summary>角色管理页：角色 CRUD 卡片。</summary>
export function RolesPage() {
  return (
    <main>
      <div className="page-title-bar">
        <span className="page-title">角色管理</span>
      </div>
      <RoleListCard />
    </main>
  )
}
