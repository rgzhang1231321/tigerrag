import { DownloadOutlined, ReloadOutlined } from '@ant-design/icons'
import { Button, Card, DatePicker, Empty, Segmented, Space, Spin, Table, Tag, message } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import dayjs from 'dayjs'
import { useMemo, useState } from 'react'
import ReactECharts from 'echarts-for-react'
import type { EChartsOption } from 'echarts'
import { useQuery } from '@tanstack/react-query'
import {
  exportReport,
  fetchReport,
  type ConversationsReportData,
  type DocumentsReportData,
  type ReportType,
  type SystemReportData,
  type UsersReportData,
} from '../statistics/statisticsApi'

interface DateRange {
  start: string
  end: string
}

const REPORT_TYPES: { label: string; value: ReportType }[] = [
  { label: '文档统计', value: 'documents' },
  { label: '用户活跃', value: 'users' },
  { label: '对话分析', value: 'conversations' },
  { label: '系统健康', value: 'system' },
]

const QUICK_RANGES: { label: string; days: number }[] = [
  { label: '近 7 天', days: 7 },
  { label: '近 30 天', days: 30 },
  { label: '近 90 天', days: 90 },
]

/// <summary>报表页面：4 类报表 + 日期筛选 + 导出。</summary>
export function ReportsPage() {
  const [reportType, setReportType] = useState<ReportType>('documents')
  const [dateRange, setDateRange] = useState<DateRange>(() => getDefaultRange(7))
  const [exporting, setExporting] = useState(false)

  const request = useMemo(() => ({ reportType, dateRange }), [reportType, dateRange])

  const { data, isPending, refetch } = useQuery({
    queryKey: ['statistics', 'report', request.reportType, request.dateRange.start, request.dateRange.end] as const,
    queryFn: () => fetchReport(request),
    staleTime: 5_000,
  })

  async function handleExport() {
    setExporting(true)
    try {
      const blob = await exportReport(request)
      const url = URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = url
      a.download = `report_${reportType}_${dayjs().format('YYYYMMDD')}.csv`
      a.click()
      URL.revokeObjectURL(url)
      message.success('导出成功')
    } catch {
      message.error('导出失败')
    } finally {
      setExporting(false)
    }
  }

  return (
    <main className="reports-page">
      <h2 className="page-heading">报表</h2>

      <Card className="reports-toolbar">
        <Space wrap>
          <Segmented
            options={REPORT_TYPES}
            value={reportType}
            onChange={(v) => setReportType(v as ReportType)}
          />
          <DatePicker.RangePicker
            value={[dayjs(dateRange.start), dayjs(dateRange.end)]}
            format="YYYY-MM-DD"
            onChange={(dates) => {
              if (dates?.[0] && dates?.[1]) {
                setDateRange({
                  start: dates[0].format('YYYY-MM-DDTHH:mm:ss[Z]'),
                  end: dates[1].endOf('day').format('YYYY-MM-DDTHH:mm:ss[Z]'),
                })
              }
            }}
          />
          {QUICK_RANGES.map((qr) => (
            <Button key={qr.days} size="small" onClick={() => setDateRange(getDefaultRange(qr.days))}>
              {qr.label}
            </Button>
          ))}
          <Button icon={<ReloadOutlined />} onClick={() => refetch()}>刷新</Button>
          <Button type="primary" icon={<DownloadOutlined />} loading={exporting} onClick={handleExport}>
            导出
          </Button>
        </Space>
      </Card>

      <Spin spinning={isPending}>
        {data?.data ? (
          <ReportContent reportType={reportType} data={data.data} />
        ) : (
          !isPending && <Empty description="暂无数据" />
        )}
      </Spin>
    </main>
  )
}

function ReportContent({ reportType, data }: { reportType: ReportType; data: unknown }) {
  switch (reportType) {
    case 'documents':
      return <DocumentsReport data={data as DocumentsReportData} />
    case 'users':
      return <UsersReport data={data as UsersReportData} />
    case 'conversations':
      return <ConversationsReport data={data as ConversationsReportData} />
    case 'system':
      return <SystemReport data={data as SystemReportData} />
  }
}

function DocumentsReport({ data }: { data: DocumentsReportData }) {
  const uploadTrend = data.uploadTrend ?? []
  const statusBreakdown = data.statusBreakdown ?? []
  const byKb = data.byKb ?? []
  const failures = data.failures ?? []
  const totalUploads = uploadTrend.reduce((sum, d) => sum + d.count, 0)

  const columns: ColumnsType<typeof failures[number]> = [
    { title: '文档标题', dataIndex: 'documentTitle', key: 'documentTitle' },
    { title: '失败原因', dataIndex: 'reason', key: 'reason', render: (r) => <Tag color="red">{r}</Tag> },
    { title: '失败时间', dataIndex: 'failedAt', key: 'failedAt', render: (t) => dayjs(t).format('YYYY-MM-DD HH:mm') },
  ]

  return (
    <div className="report-content">
      <div className="metric-grid">
        <MetricCard label="总上传数" value={totalUploads} />
        <MetricCard label="状态种类" value={statusBreakdown.length} />
        <MetricCard label="知识库数" value={byKb.length} />
        <MetricCard label="失败数" value={failures.length} />
      </div>

      <ReportCharts
        lineData={uploadTrend}
        pieData={byKb.map((kb) => ({ name: kb.knowledgeBaseName, value: kb.documentCount }))}
      />

      {failures.length > 0 && (
        <Card className="report-table-card">
          <h3 className="charts-heading">失败明细</h3>
          <Table columns={columns} dataSource={failures} rowKey="documentId" pagination={{ pageSize: 10 }} size="small" />
        </Card>
      )}
    </div>
  )
}

function UsersReport({ data }: { data: UsersReportData }) {
  const newUserTrend = data.newUserTrend ?? []
  const activeUserTrend = data.activeUserTrend ?? []
  const roleDistribution = data.roleDistribution ?? []
  const totalNew = newUserTrend.reduce((sum, d) => sum + d.count, 0)
  const totalActive = activeUserTrend.reduce((sum, d) => sum + d.count, 0)

  return (
    <div className="report-content">
      <div className="metric-grid">
        <MetricCard label="新增用户" value={totalNew} />
        <MetricCard label="活跃用户" value={totalActive} />
        <MetricCard label="角色种类" value={roleDistribution.length} />
      </div>

      <ReportCharts
        lineData={newUserTrend}
        pieData={roleDistribution.map((r) => ({ name: r.role, value: r.count }))}
      />
    </div>
  )
}

function ConversationsReport({ data }: { data: ConversationsReportData }) {
  const conversationTrend = data.conversationTrend ?? []
  const messageTrend = data.messageTrend ?? []
  const tokenTrend = data.tokenTrend ?? []
  const totalConvs = conversationTrend.reduce((sum, d) => sum + d.count, 0)
  const totalMsgs = messageTrend.reduce((sum, d) => sum + d.count, 0)

  return (
    <div className="report-content">
      <div className="metric-grid">
        <MetricCard label="总会话数" value={totalConvs} />
        <MetricCard label="总消息数" value={totalMsgs} />
        <MetricCard label="平均消息/会话" value={(data.avgMessagesPerConversation ?? 0).toFixed(1)} />
        <MetricCard label="Token 消耗" value={tokenTrend.reduce((sum, d) => sum + d.count, 0)} />
      </div>

      <ReportCharts lineData={messageTrend} pieData={[]} />
    </div>
  )
}

function SystemReport({ data }: { data: SystemReportData }) {
  const apiCallTrend = data.apiCallTrend ?? []
  return (
    <div className="report-content">
      <div className="metric-grid">
        <MetricCard label="索引成功率" value={`${((data.indexingSuccessRate ?? 0) * 100).toFixed(1)}%`} />
        <MetricCard label="失败率" value={`${((data.failureRate ?? 0) * 100).toFixed(1)}%`} />
        <MetricCard label="平均处理耗时" value={`${(data.avgProcessTimeSeconds ?? 0).toFixed(1)}s`} />
        <MetricCard label="API 调用" value={apiCallTrend.reduce((sum, d) => sum + d.count, 0)} />
      </div>

      <ReportCharts lineData={apiCallTrend} pieData={[]} />
    </div>
  )
}

function MetricCard({ label, value }: { label: string; value: number | string }) {
  return (
    <Card className="report-metric-card">
      <div className="metric-label">{label}</div>
      <div className="metric-value">{value}</div>
    </Card>
  )
}

function ReportCharts({
  lineData,
  pieData,
}: {
  lineData: { date: string; count: number }[]
  pieData: { name: string; value: number }[]
}) {
  if (lineData.length === 0 && pieData.length === 0) {
    return (
      <Card className="dashboard-charts-card">
        <h3 className="charts-heading">数据趋势</h3>
        <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="暂无趋势数据" />
      </Card>
    )
  }

  const lineOption: EChartsOption = {
    tooltip: { trigger: 'axis' as const },
    xAxis: { type: 'category' as const, data: lineData.map((d) => d.date) },
    yAxis: { type: 'value' as const },
    series: [{
      name: '数量',
      type: 'line' as const,
      smooth: true,
      data: lineData.map((d) => d.count),
      areaStyle: { opacity: 0.1 },
    }],
    grid: { left: 40, right: 16, top: 24, bottom: 24 },
  }

  const charts: React.ReactNode[] = [
    <ReactECharts key="line" option={lineOption} style={{ height: 260 }} notMerge />,
  ]

  if (pieData.length > 0) {
    const pieOption: EChartsOption = {
      tooltip: { trigger: 'item' as const },
      legend: { bottom: 0 },
      series: [{ name: '分布', type: 'pie' as const, radius: ['40%', '70%'], data: pieData }],
    }
    charts.push(<ReactECharts key="pie" option={pieOption} style={{ height: 260 }} notMerge />)
  }

  return (
    <Card className="dashboard-charts-card">
      <h3 className="charts-heading">数据趋势</h3>
      <div className="charts-grid">{charts}</div>
    </Card>
  )
}

function getDefaultRange(days: number): DateRange {
  const end = dayjs().endOf('day')
  const start = end.subtract(days - 1, 'day').startOf('day')
  return { start: start.format('YYYY-MM-DDTHH:mm:ss[Z]'), end: end.format('YYYY-MM-DDTHH:mm:ss[Z]') }
}
