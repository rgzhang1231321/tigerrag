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
import { logout } from '../features/auth/authApi'
import { useAuthStore, restoreSessionFromCache } from '../features/auth/authStore'
import { refreshSessionShared } from './http'
import { menuIconMap } from '../features/menu/menuIcons'
import { useVisibleMenuTree } from '../features/menu/useMenuConfig'
import { useDashboardMetrics } from '../features/statistics/useStatistics'
import { MetricCard } from '../components/MetricCard/MetricCard'
import { QuickAction } from '../components/QuickAction/QuickAction'
import { DashboardCharts } from '../components/DashboardCharts/DashboardCharts'
import { ChangePasswordDialog } from '../features/users/ChangePasswordDialog'
import { ForbiddenPage } from './ForbiddenPage'

const UsersPage = lazy(() =>
  import('../features/users/UsersPage').then((module) => ({ default: module.UsersPage })),
)
const RolesPage = lazy(() =>
  import('../features/roles/RolesPage').then((module) => ({ default: module.RolesPage })),
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
  children?: NavigationItem[]
}

// 把后端平铺列表组装成前端导航树：ParentId 为 null 的是顶级，其余按 ParentId 归到 children。
function buildNavigation(items: ReadonlyArray<{ id: string; key: string; label: string; icon: string | null; parentId: string | null }>): NavigationItem[] {
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

  // 页面加载时先从 sessionStorage 恢复会话，避免 F5 后因 cookie 未发送导致闪跳登录页。
  // sessionStorage 在 F5 刷新后保留（同标签页），关闭标签页后清除。
  useEffect(() => {
    restoreSessionFromCache()
  }, [])

  // 用 refresh cookie 换取新 accessToken。失败时清空登录态，触发 if (!user) → LoginPage：
  // refresh 失败意味着 token 已被撤/过期/cookie 缺失，继续保留 user 会让业务请求持续 401。
  useEffect(() => {
    refreshSessionShared()
      .then((session) => {
        if (session !== null) {
          setSession(session)
        } else {
          useAuthStore.getState().clear()
        }
      })
      .catch(() => useAuthStore.getState().clear())
      .finally(() => setReady(true))
  }, [setSession])

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
  const rolesKey = user?.roles.slice().sort().join('|') ?? 'none'
  const { data: menuItems = [] } = useVisibleMenuTree(rolesKey)

  // 把后端平铺列表组装成前端导航树：ParentId 为 null 的是顶级，其余按 ParentId 归到 children。
  const navigation = useMemo(() => buildNavigation(menuItems), [menuItems])

  // 当前路径匹配到的含子项的顶级菜单 → 左侧边栏展示其子项。
  const activeParent = findActiveParent(location.pathname, navigation)
  const sidebarItems = activeParent?.children

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
        <nav className="top-nav">{renderTopNav(navigation)}</nav>
        <div className="account-actions">
          <Dropdown menu={{ items: userMenuItems }} trigger={['click']}>
            <Tag className="user-tag">{userName?.charAt(0) ?? '?'}</Tag>
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
          <Routes>
            <Route path="/" element={<Dashboard />} />
            <Route path="/knowledge-bases" element={<ModulePage title="知识库" />} />
            <Route path="/documents" element={<ModulePage title="文档管理" />} />
            <Route path="/chat" element={<ModulePage title="问答工作台" />} />
            <Route
              path="/users"
              element={
                <Suspense fallback={<Spin className="app-loading" />}>
                  <UsersPage />
                </Suspense>
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
                <Suspense fallback={<Spin className="app-loading" />}>
                  <MenuManagementPage />
                </Suspense>
              }
            />
            <Route
              path="/reports"
              element={
                <Suspense fallback={<Spin className="app-loading" />}>
                  <ReportsPage />
                </Suspense>
              }
            />
            <Route
              path="/logs"
              element={
                <Suspense fallback={<Spin className="app-loading" />}>
                  <LogsPage />
                </Suspense>
              }
            />
            <Route
              path="/audit"
              element={
                <Suspense fallback={<Spin className="app-loading" />}>
                  <AuditPage />
                </Suspense>
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
      {data && <DashboardCharts data={data} />}
      <QuickAction />
    </main>
  )
}

function ModulePage({ title }: { title: string }) {
  return <main className="module-placeholder"><h2 className="page-heading">{title}</h2></main>
}
