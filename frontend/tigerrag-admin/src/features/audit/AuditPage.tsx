import { DatePicker, Input, Select, Space, Table, Tag } from "antd";
import type { ColumnsType } from "antd/es/table";
import dayjs from "dayjs";
import { useState } from "react";
import { useAuditLogs } from "./useAuditLogs";
import type { AuditEntry } from "./auditApi";

const ACTION_COLORS: Record<string, string> = {
  "user.roles.assign": "blue",
  "user.create": "green",
  "user.password.initial": "green",
  "user.password.reset": "orange",
  "user.delete": "red",
  "user.lockout": "orange",
  "role.create": "green",
  "role.delete": "red",
  "role.rename": "orange",
  "menu.create": "green",
  "menu.update": "blue",
  "menu.delete": "red",
  "menu.references.update": "blue",
  "document.permissions.replace": "blue",
  "kb.create": "green",
  "kb.update": "blue",
  "kb.delete": "red",
  "kb.reindex": "orange",
  "document.create": "green",
  "document.delete": "red",
  "document.reindex": "orange",
  "document.index.start": "blue",
  "document.index.success": "green",
  "document.index.failed": "red",
  "auth.login": "cyan",
  "auth.logout": "default",
  "auth.password.change": "orange",
};

const ACTION_LABELS: Record<string, string> = {
  "user.roles.assign": "分配角色",
  "user.create": "创建用户",
  "user.password.initial": "设置初始密码",
  "user.password.reset": "重置密码",
  "user.delete": "删除用户",
  "user.lockout": "锁定/解锁",
  "role.create": "创建角色",
  "role.delete": "删除角色",
  "role.rename": "重命名角色",
  "menu.create": "创建菜单",
  "menu.update": "更新菜单",
  "menu.delete": "删除菜单",
  "menu.references.update": "菜单引用更新",
  "document.permissions.replace": "修改文档权限",
  "kb.create": "创建知识库",
  "kb.update": "更新知识库",
  "kb.delete": "删除知识库",
  "kb.reindex": "重新索引知识库",
  "document.create": "上传文档",
  "document.delete": "删除文档",
  "document.reindex": "重新索引文档",
  "document.index.start": "开始索引",
  "document.index.success": "索引成功",
  "document.index.failed": "索引失败",
  "auth.login": "登录",
  "auth.logout": "退出登录",
  "auth.password.change": "修改密码",
};

/// <summary>审计日志查看器：按时间范围、操作人、操作类型、关键词检索系统审计记录。</summary>
export function AuditPage() {
  const [from, setFrom] = useState<string | null>(null);
  const [to, setTo] = useState<string | null>(null);
  const [actorId, setActorId] = useState("");
  const [action, setAction] = useState<string | undefined>(undefined);
  const [keyword, setKeyword] = useState("");
  const [page, setPage] = useState(1);
  const pageSize = 20;

  const { data, isPending } = useAuditLogs({
    from: from || null,
    to: to || null,
    actorId: actorId || null,
    action: action || null,
    keyword: keyword || null,
    page,
    pageSize,
  });

  const entries = data?.entries ?? [];
  const total = data?.total ?? 0;

  const columns: ColumnsType<AuditEntry> = [
    {
      title: "时间",
      dataIndex: "createdAt",
      key: "createdAt",
      ellipsis: true,
      render: (value: string) => new Date(value).toLocaleString("zh-CN"),
    },
    {
      title: "操作人",
      key: "actor",
      ellipsis: true,
      render: (_value, entry) => (
        <Space direction="vertical" size={0}>
          <span>{entry.actorName}</span>
          <span style={{ color: "#999", fontSize: 12 }}>
            {entry.actorId.slice(0, 8)}...
          </span>
        </Space>
      ),
    },
    {
      title: "操作类型",
      dataIndex: "action",
      key: "action",
      ellipsis: true,
      render: (value: string) => (
        <Tag color={ACTION_COLORS[value] ?? "default"}>
          {ACTION_LABELS[value] ?? value}
        </Tag>
      ),
    },
    {
      title: "目标",
      key: "target",
      ellipsis: true,
      render: (_value, entry) => (
        <Space direction="vertical" size={0}>
          <span style={{ color: "#666" }}>{entry.targetType}</span>
          <span style={{ fontSize: 12 }}>{entry.targetId}</span>
        </Space>
      ),
    },
    {
      title: "摘要",
      dataIndex: "summary",
      key: "summary",
      ellipsis: true,
    },
  ];

  function handleQuery() {
    setPage(1);
  }

  return (
    <main>
      <div className="page-title-bar">
        <span className="page-title">审计日志</span>
      </div>
      <div className="users-toolbar">
        <DatePicker.RangePicker
          showTime
          style={{ marginRight: 24 }}
          placeholder={["开始时间", "结束时间"]}
          onChange={(dates) => {
            setFrom(dates?.[0] ? dayjs(dates[0]).toISOString() : null);
            setTo(dates?.[1] ? dayjs(dates[1]).toISOString() : null);
          }}
        />
        <Input
          allowClear
          style={{ marginRight: 24, width: 180 }}
          placeholder="操作人 ID"
          value={actorId}
          onChange={(event) => setActorId(event.target.value)}
        />
        <Select
          allowClear
          placeholder="操作类型"
          value={action}
          onChange={setAction}
          style={{ marginRight: 24, width: 160 }}
          options={Object.entries(ACTION_LABELS).map(([value, label]) => ({
            value,
            label,
          }))}
        />
        <Input.Search
          allowClear
          placeholder="关键词"
          value={keyword}
          onChange={(event) => setKeyword(event.target.value)}
          onSearch={handleQuery}
          enterButton="查询"
          style={{ marginRight: 24, width: 200 }}
        />
      </div>
      <Table<AuditEntry>
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
        locale={{ emptyText: "暂无审计记录" }}
      />
    </main>
  );
}
