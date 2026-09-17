import { Alert } from 'antd'

/// <summary>
/// 审计日志查看器占位：后端 /api/audit-logs 当前固定返回 50100。本页仅显示提示，不打 API。
/// 真实实现待 T08 任务落地后补齐。
/// </summary>
export function AuditPage() {
  return (
    <main>
      <h2 className="page-heading">审计日志</h2>
      <Alert
        type="info"
        showIcon
        message="审计服务尚未实现（T08 待办）"
        description="该模块将提供按时间范围、用户、操作类型检索系统审计日志的能力。后端接口与查询语法落地后将在此处开放。"
      />
    </main>
  )
}