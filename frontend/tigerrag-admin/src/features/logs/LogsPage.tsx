import { Tabs } from 'antd'
import { AccessLogsPanel } from './AccessLogsPanel'
import { ErrorLogsPanel } from './ErrorLogsPanel'

/// <summary>日志管理页：错误日志（api_log 消息日志）与访问日志（api_access_log 每请求记录）双 Tab。</summary>
export function LogsPage() {
  return (
    <main>
      <div className="page-title-bar">
        <span className="page-title">日志管理</span>
      </div>
      <Tabs
        defaultActiveKey="error"
        items={[
          {
            key: 'error',
            label: '错误日志',
            children: <ErrorLogsPanel />,
          },
          {
            key: 'access',
            label: '访问日志',
            children: <AccessLogsPanel />,
          },
        ]}
      />
    </main>
  )
}
