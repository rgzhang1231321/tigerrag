import { Button, Result } from 'antd'
import { useNavigate } from 'react-router-dom'

/// <summary>无权限时显示的静态页面：提示 403 与回退入口，不暴露业务数据。</summary>
export function ForbiddenPage() {
  const navigate = useNavigate()
  return (
    <main className="forbidden-page">
      <Result
        status="403"
        title="403"
        subTitle="当前账号没有访问该页面的权限。请联系管理员调整角色分配。"
        extra={
          <Button type="primary" onClick={() => navigate('/')}>
            返回系统概览
          </Button>
        }
      />
    </main>
  )
}