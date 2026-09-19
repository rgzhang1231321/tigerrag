import { Breadcrumb as AntdBreadcrumb } from 'antd'
import { Link, useLocation } from 'react-router-dom'

/// <summary>路由标签静态映射；路径不在表里时只显示首页，避免根据 URL 推断出错误模块。</summary>
const ROUTE_LABELS: Readonly<Record<string, string>> = {
  '/': '首页',
  '/users': '用户管理',
  '/roles': '角色管理',
  '/menu-configs': '菜单管理',
  '/reports': '报表',
  '/audit': '审计日志',
  '/knowledge-bases': '知识库',
  '/documents': '文档管理',
  '/chat': '问答工作台',
}

/// <summary>顶部面包屑：根据当前路由显示 首页 > 模块，路径未注册时仅显示首页。</summary>
export function Breadcrumb() {
  const location = useLocation()
  const segment = ROUTE_LABELS[location.pathname] ?? null

  if (segment === null || location.pathname === '/') {
    return <AntdBreadcrumb items={[{ title: '首页' }]} />
  }

  return (
    <AntdBreadcrumb
      items={[
        { title: <Link to="/">首页</Link> },
        { title: segment },
      ]}
    />
  )
}
