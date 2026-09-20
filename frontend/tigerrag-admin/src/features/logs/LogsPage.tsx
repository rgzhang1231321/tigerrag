import { Button, Input, Select, Table, Tag, message } from 'antd'
import { CopyOutlined } from '@ant-design/icons'
import type { ColumnsType } from 'antd/es/table'
import { useState } from 'react'
import { useApiLogs } from './useLogs'
import type { ApiLogEntry } from './logsApi'

const LEVEL_COLORS: Record<string, string> = {
  Error: 'red',
  Warning: 'orange',
  Information: 'blue',
  Critical: 'magenta',
}

/// 复制文本到剪贴板并弹成功提示。
function copyText(value: string) {
  navigator.clipboard?.writeText(value)
    .then(() => message.success('复制成功'))
    .catch(() => message.error('复制失败'))
}

export function LogsPage() {
  const [requestId, setRequestId] = useState('')
  const [keyword, setKeyword] = useState('')
  const [level, setLevel] = useState<string | undefined>('Error')
  const [page, setPage] = useState(1)
  const pageSize = 10

  const { data, isPending } = useApiLogs({
    requestId: requestId || null,
    keyword: keyword || null,
    level: level || null,
    page,
    pageSize,
  })

  const entries = data?.entries ?? []
  const total = data?.total ?? 0

  const columns: ColumnsType<ApiLogEntry> = [
    {
      title: '时间',
      dataIndex: 'timestamp',
      key: 'timestamp',
      width: 180,
      render: (value: string) => new Date(value).toLocaleString('zh-CN'),
    },
    {
      title: '级别',
      dataIndex: 'level',
      key: 'level',
      width: 100,
      render: (value: string) => <Tag color={LEVEL_COLORS[value] ?? 'default'}>{value}</Tag>,
    },
    {
      title: 'RequestId',
      dataIndex: 'requestId',
      key: 'requestId',
      width: 120,
    },
    {
      title: '路径',
      dataIndex: 'requestPath',
      key: 'requestPath',
      width: 200,
    },
    {
      title: '消息',
      dataIndex: 'message',
      key: 'message',
      width: 300,
      ellipsis: true,
      render: (value: string) => (
        <span style={{ display: 'flex', alignItems: 'center', gap: 4 }}>
          <span style={{ flex: 1, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
            {value}
          </span>
          <CopyOutlined
            style={{ cursor: 'pointer', color: '#1677ff', flexShrink: 0 }}
            onClick={() => copyText(value)}
            title="复制"
          />
        </span>
      ),
    },
    {
      title: '异常',
      dataIndex: 'exception',
      key: 'exception',
      width: 300,
      ellipsis: true,
      render: (value: string | null) =>
        value ? (
          <span style={{ display: 'flex', alignItems: 'center', gap: 4 }}>
            <span style={{ flex: 1, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', color: '#cf1322' }}>
              {value}
            </span>
            <CopyOutlined
              style={{ cursor: 'pointer', color: '#1677ff', flexShrink: 0 }}
              onClick={() => copyText(value)}
              title="复制"
            />
          </span>
        ) : null,
    },
    {
      title: '耗时(ms)',
      dataIndex: 'elapsedMs',
      key: 'elapsedMs',
      width: 100,
    },
  ]

  return (
    <main>
      <div className="page-title-bar">
        <span className="page-title">日志管理</span>
      </div>
      <div className="users-toolbar">
        <Input.Search
          allowClear
          placeholder="按 RequestId 过滤"
          value={requestId}
          onChange={(event) => setRequestId(event.target.value)}
          className="users-search"
        />
        <Input.Search
          allowClear
          placeholder="按关键词过滤"
          value={keyword}
          onChange={(event) => setKeyword(event.target.value)}
          className="users-search"
        />
        <Select
          allowClear
          placeholder="按级别过滤"
          value={level}
          onChange={(value) => setLevel(value)}
          style={{ width: 140 }}
          options={[
            { value: 'Error', label: 'Error' },
            { value: 'Warning', label: 'Warning' },
            { value: 'Information', label: 'Information' },
            { value: 'Critical', label: 'Critical' },
          ]}
        />
        <Button type="primary" onClick={() => setPage(1)}>
          查询
        </Button>
      </div>
      <Table<ApiLogEntry>
        rowKey="id"
        loading={isPending}
        dataSource={entries}
        columns={columns}
        pagination={{
          current: page,
          pageSize,
          total,
          showSizeChanger: false,
          onChange: (next) => setPage(next),
        }}
        locale={{ emptyText: '暂无日志' }}
      />
    </main>
  )
}
