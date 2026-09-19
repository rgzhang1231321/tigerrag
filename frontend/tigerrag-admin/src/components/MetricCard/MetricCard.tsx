import { Card, Skeleton } from 'antd'
import type { ReactNode } from 'react'

interface MetricCardProps {
  /// <summary>指标名（中文短词）；显示在卡片顶部。</summary>
  label: string
  /// <summary>当前数值；加载态时忽略该字段。</summary>
  value: number | string
  /// <summary>为 true 时用骨架占位，避免数值闪烁。</summary>
  loading?: boolean
  /// <summary>可选的图标，渲染在 label 左侧。</summary>
  icon?: ReactNode
  /// <summary>可选的下文（如"较昨日 +12%"），显示在 value 下方。</summary>
  footer?: ReactNode
}

/// <summary>Dashboard 指标卡：统一风格呈现单个关键指标。</summary>
export function MetricCard({ label, value, loading = false, icon, footer }: MetricCardProps) {
  return (
    <Card className="metric-card" bordered>
      <div className="metric-row">
        {icon !== undefined && <span className="metric-icon">{icon}</span>}
        <span className="metric-label">{label}</span>
      </div>
      {loading ? (
        <Skeleton.Input active style={{ width: 80, marginTop: 8 }} />
      ) : (
        <div className="metric-value">{value}</div>
      )}
      {footer !== undefined && !loading && <div className="metric-footer">{footer}</div>}
    </Card>
  )
}
