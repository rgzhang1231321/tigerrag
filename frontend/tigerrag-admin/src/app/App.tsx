import {
  AuditOutlined,
  BookOutlined,
  DashboardOutlined,
  FileTextOutlined,
  LogoutOutlined,
  MenuOutlined,
  MessageOutlined,
  TeamOutlined,
} from '@ant-design/icons'
import { Button, Card, Dropdown, Layout, Menu, Spin, Tag } from 'antd'
import { lazy, Suspense, useEffect, useState } from 'react'
import { Link, Route, Routes, useLocation } from 'react-router-dom'
import { logout, refreshSession } from '../features/auth/authApi'
import { useAuthStore } from '../features/auth/authStore'

const LoginPage = lazy(() => import('../features/auth/LoginPage'))

const { Header, Content, Sider } = Layout

const navigation = [
  { key: '/', icon: <DashboardOutlined />, label: <Link to="/">概览</Link> },
  { key: '/knowledge-bases', icon: <BookOutlined />, label: <Link to="/knowledge-bases">知识库</Link> },
  { key: '/documents', icon: <FileTextOutlined />, label: <Link to="/documents">文档管理</Link> },
  { key: '/chat', icon: <MessageOutlined />, label: <Link to="/chat">问答工作台</Link> },
  { key: '/users', icon: <TeamOutlined />, label: <Link to="/users">用户管理</Link> },
  { key: '/audit', icon: <AuditOutlined />, label: <Link to="/audit">审计日志</Link> },
]

const pageTitles: Record<string, string> = {
  '/': '系统概览',
  '/knowledge-bases': '知识库',
  '/documents': '文档管理',
  '/chat': '问答工作台',
  '/users': '用户管理',
  '/audit': '审计日志',
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
  const title = pageTitles[location.pathname] ?? 'TigerRAG'

  return (
    <Layout className="app-shell">
      <Sider width={232} theme="light" className="app-sider">
        <div className="brand"><h1>TigerRAG</h1></div>
        <Menu mode="inline" selectedKeys={[location.pathname]} items={navigation} />
      </Sider>
      <Layout>
        <Header className="app-header">
          <div className="app-header-leading">
            <Dropdown menu={{ items: navigation }} trigger={['click']}>
              <Button className="mobile-menu-button" type="text" icon={<MenuOutlined />} aria-label="打开导航菜单" />
            </Dropdown>
            <span className="mobile-brand">TigerRAG</span>
            <span className="app-header-title">{title}</span>
          </div>
          <div className="account-actions">
            <Tag>{userName}</Tag>
            <Button type="text" icon={<LogoutOutlined />} onClick={onLogout} aria-label="退出登录" />
          </div>
        </Header>
        <Content className="app-content">
          <Routes>
            <Route path="/" element={<Dashboard />} />
            <Route path="/knowledge-bases" element={<ModulePage title="知识库" />} />
            <Route path="/documents" element={<ModulePage title="文档管理" />} />
            <Route path="/chat" element={<ModulePage title="问答工作台" />} />
            <Route path="/users" element={<ModulePage title="用户管理" />} />
            <Route path="/audit" element={<ModulePage title="审计日志" />} />
          </Routes>
        </Content>
      </Layout>
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
