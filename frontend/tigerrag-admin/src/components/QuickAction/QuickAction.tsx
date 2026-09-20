import { Button, Card, Space } from 'antd'
import { DatabaseOutlined, FileAddOutlined, UserAddOutlined } from '@ant-design/icons'
import { useNavigate } from 'react-router-dom'

interface QuickActionItem {
  /// <summary>按钮文案。</summary>
  label: string
  /// <summary>antd 图标元素。</summary>
  icon: React.ReactNode
  /// <summary>点击后跳转的路由。</summary>
  to: string
}

const actions: QuickActionItem[] = [
  { label: '新建知识库', icon: <DatabaseOutlined />, to: '/knowledge-bases' },
  { label: '上传文档', icon: <FileAddOutlined />, to: '/documents' },
  { label: '新建用户', icon: <UserAddOutlined />, to: '/users' },
]

/// <summary>Dashboard 快捷入口：常用操作一键跳转。</summary>
export function QuickAction() {
  const navigate = useNavigate()
  return (
    <Card className="quick-action-card">
      <h3 className="quick-action-heading">快捷操作</h3>
      <Space size="middle">
        {actions.map((action) => (
          <Button
            key={action.to}
            type="primary"
            icon={action.icon}
            size="large"
            onClick={() => navigate(action.to)}
          >
            {action.label}
          </Button>
        ))}
      </Space>
    </Card>
  )
}
