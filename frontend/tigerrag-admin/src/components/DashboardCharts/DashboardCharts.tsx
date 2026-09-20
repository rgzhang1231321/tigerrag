import { Card, Empty } from 'antd'
import ReactECharts from 'echarts-for-react'
import type { EChartsOption } from 'echarts'
import type { DashboardMetrics } from '../../features/statistics/statisticsApi'

interface DashboardChartsProps {
  /// <summary>Dashboard 聚合指标数据。</summary>
  data: DashboardMetrics
}

/// <summary>Dashboard 图表区：折线图展示近 7 天文档趋势，饼图展示知识库文档分布。</summary>
export function DashboardCharts({ data }: DashboardChartsProps) {
  const recentWeekDocuments = data.recentWeekDocuments ?? []
  const documentsByKb = data.documentsByKb ?? []
  const hasData = recentWeekDocuments.length > 0 || documentsByKb.length > 0

  if (!hasData) {
    return (
      <Card className="dashboard-charts-card">
        <h3 className="charts-heading">数据趋势</h3>
        <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="暂无趋势数据" />
      </Card>
    )
  }

  const lineOption: EChartsOption = {
    tooltip: { trigger: 'axis' as const },
    xAxis: {
      type: 'category' as const,
      data: recentWeekDocuments.map((d) => d.date),
    },
    yAxis: { type: 'value' as const },
    series: [{
      name: '文档上传',
      type: 'line' as const,
      smooth: true,
      data: recentWeekDocuments.map((d) => d.count),
      areaStyle: { opacity: 0.1 },
    }],
    grid: { left: 40, right: 16, top: 24, bottom: 24 },
  }

  const pieOption: EChartsOption = {
    tooltip: { trigger: 'item' as const },
    legend: { bottom: 0 },
    series: [{
      name: '文档分布',
      type: 'pie' as const,
      radius: ['40%', '70%'],
      data: documentsByKb.map((kb) => ({
        name: kb.knowledgeBaseName,
        value: kb.documentCount,
      })),
    }],
  }

  return (
    <Card className="dashboard-charts-card">
      <h3 className="charts-heading">数据趋势</h3>
      <div className="charts-grid">
        <ReactECharts option={lineOption} style={{ height: 260 }} notMerge />
        <ReactECharts option={pieOption} style={{ height: 260 }} notMerge />
      </div>
    </Card>
  )
}
