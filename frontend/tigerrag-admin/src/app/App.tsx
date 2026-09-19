import {
  DatabaseOutlined,
  FileTextOutlined,
  LockOutlined,
  LogoutOutlined,
  MessageOutlined,
  TeamOutlined,
} from '@ant-design/icons'
import { Dropdown, Layout, Menu, Spin, Tag } from 'antd'
import type { MenuProps } from 'antd'
import { lazy, Suspense, useEffect, useMemo, useState, createElement } from 'react'
import { Link, Route, Routes, useLocation } from 'react-router-dom'
import { logout, refreshSession } from '../features/auth/authApi'
import { useAuthStore } from '../features/auth/authStore'
import type { AuthUser } from '../features/auth/authStore'
import { hasRole } from '../features/auth/permissions'
import { Breadcrumb } from '../features/layout/Breadcrumb'
import { menuIconMap } from '../features/menu/menuIcons'
import { useMenuTree } from '../features/menu/useMenuConfig'
import { useDashboardMetrics } from '../features/statistics/useStatistics'
import { MetricCard } from '../components/MetricCard/MetricCard'
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
const ReportsPage = lazy(() =>
  import('../features/reports/ReportsPage').then((module) => ({ default: module.ReportsPage })),
)
const LogsPage = lazy(() =>
  import('../features/logs/LogsPage').then((module) => ({ default: module.LogsPage })),
)

const { Header, Content, Sider } = Layout

interface NavigationItem {
  key: string
  icon?: React.ReactNode
  label: React.ReactNode
  roles?: readonly string[]
  children?: NavigationItem[]
}

// 把后端平铺列表组装成前端导航树：ParentId 为 null 的是顶级，其余按 ParentId 归到 children。
function buildNavigation(items: ReadonlyArray<{ id: string; key: string; label: string; icon: string | null; roles: string[]; parentId: string | null }>): NavigationItem[] {
  const roots: NavigationItem[] = []
  const childrenMap = new Map<string, NavigationItem[]>()
  for (const item of items) {
    const IconComponent = item.icon ? menuIconMap[item.icon] : undefined
    const nav: NavigationItem = {
      key: item.key,
      // 已知图标按映射渲染；未知图标降级显示文本 Tag，让管理员看到原值以便发现配置错误。
      icon: IconComponent
        ? createElement(IconComponent)
        : item.icon
          ? <Tag style={{ margin: 0 }}>{item.icon}</Tag>
          : undefined,
      label: item.label,
      roles: item.roles,
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

// 菜单可见性：空数组/NULL = 所有人可见；Admin 始终可见（bypass）；其余按角色名单过滤。
function isMenuVisible(user: AuthUser | null, item: NavigationItem): boolean {
  if (user === null) return false
  if (item.roles === undefined || item.roles.length === 0) return true
  if (hasRole(user, 'Admin')) return true
  return item.roles.some((role) => hasRole(user, role))
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
  const [siderCollapsed, setSiderCollapsed] = useState(false)
  const { data: menuItems = [] } = useMenuTree()

  // 把后端平铺列表组装成前端导航树：ParentId 为 null 的是顶级，其余按 ParentId 归到 children。
  const navigation = useMemo(() => buildNavigation(menuItems), [menuItems])

  const visibleNavigation = navigation.filter((item) => isMenuVisible(user, item))

  // 当前路径匹配到的含子项的顶级菜单 → 左侧边栏展示其子项。
  const activeParent = findActiveParent(location.pathname, visibleNavigation)
  const sidebarItems = activeParent?.children?.filter((child) => isMenuVisible(user, child))

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
          <Sider
            width={200}
            collapsedWidth={64}
            collapsed={siderCollapsed}
            onCollapse={setSiderCollapsed}
            theme="light"
            className="app-sider"
            trigger={null}
          >
            <div className="sider-collapse-bar">
              <button
                type="button"
                className="sider-collapse-trigger"
                aria-label={siderCollapsed ? '展开侧边栏' : '折叠侧边栏'}
                onClick={() => setSiderCollapsed(!siderCollapsed)}
              >
                {siderCollapsed ? '»' : '«'}
              </button>
            </div>
            <Menu
              mode="inline"
              selectedKeys={[location.pathname]}
              inlineCollapsed={siderCollapsed}
              items={sidebarItems.map((item) => ({
                key: item.key,
                icon: item.icon,
                label: <Link to={item.key}>{item.label}</Link>,
              }))}
            />
          </Sider>
        )}
        <Content className="app-content">
          <div className="app-breadcrumb"><Breadcrumb /></div>
          <Routes>
            <Route path="/" element={<Dashboard />} />
            <Route path="/knowledge-bases" element={<ModulePage title="知识库" />} />
            <Route path="/documents" element={<ModulePage title="文档管理" />} />
            <Route path="/chat" element={<ModulePage title="问答工作台" />} />
            <Route
              path="/users"
              element={
                <RequireRole roles={['Admin']}>
                  <Suspense fallback={<Spin className="app-loading" />}>
                    <UsersPage />
                  </Suspense>
                </RequireRole>
              }
            />
            <Route
              path="/roles"
              element={
                <RequireRole roles={['Admin']}>
                  <Suspense fallback={<Spin className="app-loading" />}>
                    <RolesPage />
                  </Suspense>
                </RequireRole>
              }
            />
            <Route
              path="/menu-configs"
              element={
                <RequireRole roles={['Admin']}>
                  <Suspense fallback={<Spin className="app-loading" />}>
                    <MenuManagementPage />
                  </Suspense>
                </RequireRole>
              }
            />
            <Route
              path="/reports"
              element={
                <RequireRole roles={['Admin', 'Auditor']}>
                  <Suspense fallback={<Spin className="app-loading" />}>
                    <ReportsPage />
                  </Suspense>
                </RequireRole>
              }
            />
            <Route
              path="/logs"
              element={
                <RequireRole roles={['Admin']}>
                  <Suspense fallback={<Spin className="app-loading" />}>
                    <LogsPage />
                  </Suspense>
                </RequireRole>
              }
            />
            <Route
              path="/audit"
              element={
                <RequireRole roles={['Admin', 'Auditor']}>
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
  const { data, isPending } = useDashboardMetrics()

  return (
    <main>
      <h2 className="page-heading">系统概览</h2>
      <div className="metric-grid">
        <MetricCard
          label="知识库"
          value={data?.knowledgeBaseCount ?? 0}
          loading={isPending}
          icon={<DatabaseOutlined />}
        />
        <MetricCard
          label="文档总数"
          value={data?.documentCount ?? 0}
          loading={isPending}
          icon={<FileTextOutlined />}
          footer={
            data !== undefined && data.documentCount > 0
              ? `已索引 ${data.indexedDocumentCount} · 处理中 ${data.processingDocumentCount}`
              : undefined
          }
        />
        <MetricCard
          label="用户总数"
          value={data?.userCount ?? 0}
          loading={isPending}
          icon={<TeamOutlined />}
        />
        <MetricCard
          label="问答消息数"
          value={data?.messageCount ?? 0}
          loading={isPending}
          icon={<MessageOutlined />}
          footer={data !== undefined ? `${data.conversationCount} 个会话` : undefined}
        />
      </div>
    </main>
  )
}

function ModulePage({ title }: { title: string }) {
  return <main className="module-placeholder"><h2 className="page-heading">{title}</h2></main>
}
