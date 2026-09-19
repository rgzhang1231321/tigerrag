import { Button, Input, Select, Table, Tag } from 'antd'
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

export function LogsPage() {
  const [keyword, setKeyword] = useState('')
  const [level, setLevel] = useState<string | undefined>(undefined)
  const [page, setPage] = useState(1)
  const pageSize = 20

  const { data, isPending } = useApiLogs({
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
      ellipsis: true,
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
      <h2 className="page-heading">日志管理</h2>
      <div className="users-toolbar">
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
