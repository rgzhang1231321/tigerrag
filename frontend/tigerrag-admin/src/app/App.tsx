import {
  LockOutlined,
  LogoutOutlined,
} from '@ant-design/icons'
import { Card, Dropdown, Layout, Menu, Spin, Tag } from 'antd'
import type { MenuProps } from 'antd'
import { lazy, Suspense, useEffect, useMemo, useState, createElement } from 'react'
import { Link, Route, Routes, useLocation } from 'react-router-dom'
import { logout, refreshSession } from '../features/auth/authApi'
import { useAuthStore } from '../features/auth/authStore'
import { can } from '../features/auth/permissions'
import type { Permission } from '../features/auth/permissions'
import { menuIconMap } from '../features/menu/menuIcons'
import { useMenuTree } from '../features/menu/useMenuConfig'
import { ChangePasswordDialog } from '../features/users/ChangePasswordDialog'
import { ForbiddenPage } from './ForbiddenPage'
import { RequireRole } from './RequireRole'

const UsersPage = lazy(() =>
  import('../features/users/UsersPage').then((module) => ({ default: module.UsersPage })),
)
const RolesPage = lazy(() =>
  import('../features/users/RolesPage').then((module) => ({ default: module.RolesPage })),
)
const AuditPage = lazy(() =>
  import('../features/audit/AuditPage').then((module) => ({ default: module.AuditPage })),
)

const LoginPage = lazy(() => import('../features/auth/LoginPage'))
const MenuManagementPage = lazy(() =>
  import('../features/menu/MenuManagement').then((module) => ({ default: module.MenuManagement })),
)

const { Header, Content, Sider } = Layout

interface NavigationItem {
  key: string
  icon?: React.ReactNode
  label: React.ReactNode
  permission?: Permission
  children?: NavigationItem[]
}

// 把后端平铺列表组装成前端导航树：ParentId 为 null 的是顶级，其余按 ParentId 归到 children。
function buildNavigation(items: ReadonlyArray<{ id: string; key: string; label: string; icon: string | null; permission: string | null; parentId: string | null }>): NavigationItem[] {
  const roots: NavigationItem[] = []
  const childrenMap = new Map<string, NavigationItem[]>()
  for (const item of items) {
    const icon = item.icon ? menuIconMap[item.icon] : undefined
    const nav: NavigationItem = {
      key: item.key,
      icon: icon ? createElement(icon) : undefined,
      label: item.label,
      permission: (item.permission ?? undefined) as Permission | undefined,
    }
    if (item.parentId) {
      const siblings = childrenMap.get(item.parentId) ?? []
      siblings.push(nav)
      childrenMap.set(item.parentId, siblings)
    } else {
      roots.push(nav)
    }
  }
  // 把 children 挂到对应的顶级项上（按 id 匹配）。
  for (const root of roots) {
    const rootItem = items.find((item) => item.key === root.key)
    if (rootItem) {
      const children = childrenMap.get(rootItem.id)
      if (children) {
        root.children = children
      }
    }
  }
  return roots
}

// 根据当前路径找到匹配的顶级菜单项（有子项的）。
function findActiveParent(pathname: string, items: NavigationItem[]): NavigationItem | null {
  for (const item of items) {
    if (item.children && item.children.some((child) => child.key === pathname)) {
      return item
    }
  }
  return null
}

export function App() {
  const [ready, setReady] = useState(false)
  const user = useAuthStore((state) => state.user)
  const setSession = useAuthStore((state) => state.setSession)
  const clear = useAuthStore((state) => state.clear)

  useEffect(() => {
    refreshSession()
      .then(setSession)
      .catch(clear)
      .finally(() => setReady(true))
  }, [clear, setSession])

  if (!ready) {
    return <Spin className="app-loading" size="large" aria-label="正在恢复登录状态" />
  }

  if (!user) {
    return <Suspense fallback={<Spin className="app-loading" size="large" />}><LoginPage onAuthenticated={setSession} /></Suspense>
  }

  async function signOut() {
    try {
      await logout()
    } finally {
      clear()
    }
  }

  return <AuthenticatedShell userName={user.userName} onLogout={signOut} />
}

function AuthenticatedShell({ userName, onLogout }: { userName: string; onLogout: () => void }) {
  const location = useLocation()
  const user = useAuthStore((state) => state.user)
  const clear = useAuthStore((state) => state.clear)
  const [changePasswordOpen, setChangePasswordOpen] = useState(false)
  const { data: menuItems = [] } = useMenuTree()

  // 把后端平铺列表组装成前端导航树：ParentId 为 null 的是顶级，其余按 ParentId 归到 children。
  const navigation = useMemo(() => buildNavigation(menuItems), [menuItems])

  const visibleNavigation = navigation.filter(
    (item) => item.permission === undefined || can(user, item.permission),
  )

  // 当前路径匹配到的含子项的顶级菜单 → 左侧边栏展示其子项。
  const activeParent = findActiveParent(location.pathname, visibleNavigation)
  const sidebarItems = activeParent?.children?.filter(
    (child) => child.permission === undefined || can(user, child.permission),
  )

  // 顶级菜单：有子项的点击后跳转到第一个子项（边栏随即展示全部子菜单），无子项的直接链接。
  // 不用 Antd Menu 是因为其水平溢出计算有 bug（会错误地把项折叠到溢出子菜单）。
  function renderTopNav(items: NavigationItem[]) {
    return items.map((item) => {
      const isActive = item.children
        ? item.children.some((child) => child.key === location.pathname)
        : item.key === location.pathname
      const className = `top-nav-item${isActive ? ' top-nav-item-active' : ''}`
      if (item.children) {
        const firstChild = item.children[0]
        return (
          <Link key={item.key} to={firstChild.key} className={className}>
            {item.icon}
            <span>{item.label}</span>
          </Link>
        )
      }
      return (
        <Link key={item.key} to={item.key} className={className}>
          {item.icon}
          <span>{item.label}</span>
        </Link>
      )
    })
  }

  const userMenuItems: MenuProps['items'] = [
    { key: 'change-password', icon: <LockOutlined />, label: '修改密码', onClick: () => setChangePasswordOpen(true) },
    { key: 'logout', icon: <LogoutOutlined />, label: '退出登录', danger: true, onClick: onLogout },
  ]

  return (
    <Layout className="app-shell">
      <Header className="app-header">
        <div className="brand"><h1>TigerRAG</h1></div>
        <nav className="top-nav">{renderTopNav(visibleNavigation)}</nav>
        <div className="account-actions">
          <Dropdown menu={{ items: userMenuItems }} trigger={['click']}>
            <Tag className="user-tag">{userName}</Tag>
          </Dropdown>
        </div>
      </Header>
      <Layout>
        {sidebarItems !== undefined && (
          <Sider width={200} theme="light" className="app-sider">
            <Menu
              mode="inline"
              selectedKeys={[location.pathname]}
              items={sidebarItems.map((item) => ({
                key: item.key,
                icon: item.icon,
                label: <Link to={item.key}>{item.label}</Link>,
              }))}
            />
          </Sider>
        )}
        <Content className="app-content">
          <Routes>
            <Route path="/" element={<Dashboard />} />
            <Route path="/knowledge-bases" element={<ModulePage title="知识库" />} />
            <Route path="/documents" element={<ModulePage title="文档管理" />} />
            <Route path="/chat" element={<ModulePage title="问答工作台" />} />
            <Route
              path="/users"
              element={
                <RequireRole permission="users.manage">
                  <Suspense fallback={<Spin className="app-loading" />}>
                    <UsersPage />
                  </Suspense>
                </RequireRole>
              }
            />
            <Route
              path="/roles"
              element={
                <Suspense fallback={<Spin className="app-loading" />}>
                  <RolesPage />
                </Suspense>
              }
            />
            <Route
              path="/menu-configs"
              element={
                <RequireRole permission="users.manage">
                  <Suspense fallback={<Spin className="app-loading" />}>
                    <MenuManagementPage />
                  </Suspense>
                </RequireRole>
              }
            />
            <Route
              path="/audit"
              element={
                <RequireRole permission="audit.read">
                  <Suspense fallback={<Spin className="app-loading" />}>
                    <AuditPage />
                  </Suspense>
                </RequireRole>
              }
            />
            <Route path="*" element={<ForbiddenPage />} />
          </Routes>
        </Content>
      </Layout>
      <ChangePasswordDialog
        open={changePasswordOpen}
        onCancel={() => setChangePasswordOpen(false)}
        onChanged={() => {
          setChangePasswordOpen(false)
          clear()
        }}
      />
    </Layout>
  )
}

function Dashboard() {
  return (
    <main>
      <h2 className="page-heading">系统概览</h2>
      <div className="metric-grid">
        <Metric label="知识库" value="0" />
        <Metric label="已索引文档" value="0" />
        <Metric label="处理中" value="0" />
      </div>
    </main>
  )
}

function Metric({ label, value }: { label: string; value: string }) {
  return <Card className="metric-card"><div className="metric-label">{label}</div><div className="metric-value">{value}</div></Card>
}

function ModulePage({ title }: { title: string }) {
  return <main className="module-placeholder"><h2 className="page-heading">{title}</h2></main>
}
