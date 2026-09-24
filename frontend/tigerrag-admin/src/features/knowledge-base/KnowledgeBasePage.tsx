import {
  Alert,
  Button,
  Checkbox,
  Form,
  Input,
  Modal,
  Popconfirm,
  Select,
  Space,
  Table,
  message,
} from "antd";
import type { ColumnsType } from "antd/es/table";
import {
  DeleteOutlined,
  EditOutlined,
  EyeOutlined,
  LockOutlined,
  ReloadOutlined,
} from "@ant-design/icons";
import { useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";
import {
  useCreateKb,
  useDeleteKb,
  useKnowledgeBases,
  useReindexKb,
  useUpdateKb,
  useBatchDeleteKbs,
} from "./useKnowledgeBases";
import { useAuthStore } from "../auth/authStore";
import type { KnowledgeBaseDto } from "./knowledgeBaseApi";
import { KnowledgeBasePermissionModal } from "./KnowledgeBasePermissionModal";

type ModalMode = "closed" | "create" | { edit: KnowledgeBaseDto };

/// <summary>格式化拥有者显示：名称 + ID 前缀。</summary>
function renderOwner(kb: KnowledgeBaseDto) {
  return (
    <Space direction="vertical" size={0}>
      <span>{kb.ownerName ?? "—"}</span>
      <span style={{ color: "#999", fontSize: 12 }}>
        {kb.ownerId.slice(0, 8)}...
      </span>
    </Space>
  );
}

/// <summary>知识库管理页：列表 + 筛选 + 批量选择 + 创建/编辑弹窗 + 权限弹窗 + 删除确认。</summary>
export function KnowledgeBasePage() {
  const navigate = useNavigate();
  const currentUser = useAuthStore((state) => state.user);
  const { data: kbs = [], isPending, refetch } = useKnowledgeBases();
  const [search, setSearch] = useState("");
  const [statusFilter, setStatusFilter] = useState<
    "all" | "has-docs" | "no-docs"
  >("all");
  const [ownerFilter, setOwnerFilter] = useState<string>("all");
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
  const [mode, setMode] = useState<ModalMode>("closed");
  const [permissionKb, setPermissionKb] = useState<KnowledgeBaseDto | null>(
    null,
  );
  const createMutation = useCreateKb();
  const updateMutation = useUpdateKb();
  const deleteMutation = useDeleteKb();
  const reindexMutation = useReindexKb();
  const batchDeleteMutation = useBatchDeleteKbs();

  const owners = useMemo(() => {
    const unique = new Map<string, string>();
    for (const kb of kbs) {
      if (!unique.has(kb.ownerId))
        unique.set(kb.ownerId, kb.ownerName ?? kb.ownerId);
    }
    return Array.from(unique.entries()).map(([id, name]) => ({ id, name }));
  }, [kbs]);

  const filtered = useMemo(
    () =>
      kbs.filter((kb) => {
        const matchSearch = kb.name
          .toLowerCase()
          .includes(search.toLowerCase());
        const matchStatus =
          statusFilter === "all" ||
          (statusFilter === "has-docs" && kb.documentCount > 0) ||
          (statusFilter === "no-docs" && kb.documentCount === 0);
        const matchOwner = ownerFilter === "all" || kb.ownerId === ownerFilter;
        return matchSearch && matchStatus && matchOwner;
      }),
    [kbs, search, statusFilter, ownerFilter],
  );

  function toggleSelection(id: string, checked: boolean) {
    setSelectedIds((prev) => {
      const next = new Set(prev);
      if (checked) next.add(id);
      else next.delete(id);
      return next;
    });
  }

  const columns: ColumnsType<KnowledgeBaseDto> = [
    {
      title: "",
      dataIndex: "id",
      key: "select",
      width: 50,
      render: (_value, kb) => (
        <Checkbox
          checked={selectedIds.has(kb.id)}
          onChange={(event) => toggleSelection(kb.id, event.target.checked)}
        />
      ),
    },
    {
      title: "名称",
      dataIndex: "name",
      key: "name",
      render: (_value, kb) => (
        <Button
          type="link"
          style={{ padding: 0 }}
          onClick={() => navigate(`/documents?kbId=${kb.id}`)}
        >
          <Space>
            <span style={{ color: "var(--brand-secondary)" }}>{kb.name}</span>
          </Space>
        </Button>
      ),
    },
    {
      title: "描述",
      dataIndex: "description",
      key: "description",
      ellipsis: true,
      render: (value: string | null) => value ?? "—",
    },
    {
      title: "拥有者",
      key: "owner",
      width: 240,
      render: (_value, kb) => renderOwner(kb),
    },
    {
      title: "文档数",
      dataIndex: "documentCount",
      key: "documentCount",
      width: 90,
      align: "center",
    },
    {
      title: "创建时间",
      dataIndex: "createdAt",
      key: "createdAt",
      width: 170,
      render: (value: string) => new Date(value).toLocaleString("zh-CN"),
    },
    {
      title: "操作",
      key: "actions",
      width: 450,
      render: (_value, kb) => {
        const canManage =
          currentUser?.roles.includes("Admin") === true ||
          kb.ownerId === currentUser?.id;
        return (
          <Space>
            <Button
              type="link"
              style={{ padding: "0 4px" }}
              icon={<EyeOutlined />}
              onClick={() => navigate(`/documents?kbId=${kb.id}`)}
            >
              查看
            </Button>
            {canManage && (
              <>
                <Button
                  type="link"
                  style={{ padding: "0 4px" }}
                  icon={<ReloadOutlined />}
                  loading={
                    reindexMutation.isPending &&
                    reindexMutation.variables === kb.id
                  }
                  onClick={() =>
                    reindexMutation.mutate(kb.id, {
                      onSuccess: () =>
                        message.success(`已提交重新索引：${kb.name}`),
                    })
                  }
                >
                  重新索引
                </Button>
                <Button
                  type="link"
                  style={{ padding: "0 4px" }}
                  icon={<EditOutlined />}
                  onClick={() => setMode({ edit: kb })}
                >
                  编辑
                </Button>
                <Button
                  type="link"
                  style={{ padding: "0 4px" }}
                  icon={<LockOutlined />}
                  onClick={() => setPermissionKb(kb)}
                >
                  权限
                </Button>
                <Popconfirm
                  title="删除知识库"
                  description={
                    kb.documentCount > 0
                      ? `删除「${kb.name}」将同时删除 ${kb.documentCount} 个文档与对应向量，且不可恢复。确认删除？`
                      : `确认删除「${kb.name}」？该操作不可撤销。`
                  }
                  okText="确认删除"
                  cancelText="取消"
                  okButtonProps={{
                    danger: true,
                    loading: deleteMutation.isPending,
                  }}
                  onConfirm={() =>
                    deleteMutation.mutate(kb.id, {
                      onSuccess: () => message.success(`已删除：${kb.name}`),
                    })
                  }
                >
                  <Button
                    type="link"
                    danger
                    style={{ padding: "0 4px" }}
                    icon={<DeleteOutlined />}
                  >
                    删除
                  </Button>
                </Popconfirm>
              </>
            )}
          </Space>
        );
      },
    },
  ];

  return (
    <main>
      <div className="page-title-bar">
        <span className="page-title">知识库</span>
      </div>
      <div className="users-toolbar">
        <Input.Search
          style={{ margin: "5px 24px 24px 0px" }}
          allowClear
          placeholder="按名称过滤"
          onChange={(event) => setSearch(event.target.value)}
          className="users-search"
        />
        <Select
          value={statusFilter}
          onChange={setStatusFilter}
          style={{ margin: "5px 24px 24px 0px", minWidth: 140 }}
          options={[
            { value: "all", label: "全部状态" },
            { value: "has-docs", label: "有文档" },
            { value: "no-docs", label: "无文档" },
          ]}
        />
        <Select
          value={ownerFilter}
          onChange={setOwnerFilter}
          style={{ margin: "5px 24px 24px 0px", minWidth: 160 }}
          options={[
            { value: "all", label: "全部拥有者" },
            ...owners.map((o) => ({ value: o.id, label: o.name })),
          ]}
        />
        <Button onClick={() => void refetch()}>刷新</Button>
        <Button
          type="primary"
          style={{ margin: "5px 24px 24px 24px" }}
          onClick={() => setMode("create")}
        >
          新建知识库
        </Button>
        <div style={{ flex: 1 }} />
      </div>
      {selectedIds.size > 0 && (
        <div className="batch-action-bar">
          <Space>
            <span>
              已选择 <strong>{selectedIds.size}</strong> 项
            </span>
            <Popconfirm
              title="批量删除知识库"
              description={`确认删除选中的 ${selectedIds.size} 个知识库及其所有文档？该操作不可撤销。`}
              okText="确认删除"
              cancelText="取消"
              okButtonProps={{
                danger: true,
                loading: batchDeleteMutation.isPending,
              }}
              onConfirm={() => {
                const ids = Array.from(selectedIds);
                batchDeleteMutation.mutate(ids, {
                  onSuccess: (count) => {
                    message.success(`已删除 ${count} 个知识库`);
                    setSelectedIds(new Set());
                  },
                });
              }}
            >
              <Button size="small" danger>
                批量删除
              </Button>
            </Popconfirm>
          </Space>
        </div>
      )}
      <Table<KnowledgeBaseDto>
        rowKey="id"
        loading={isPending}
        dataSource={filtered}
        columns={columns}
        pagination={{ pageSize: 20, showSizeChanger: false }}
        locale={{
          emptyText: <EmptyKbState onCreate={() => setMode("create")} />,
        }}
      />
      {mode === "create" && (
        <KbFormDialog
          mode="create"
          onCancel={() => setMode("closed")}
          onSubmit={(values) =>
            createMutation.mutate(
              { name: values.name, description: values.description },
              {
                onSuccess: () => {
                  message.success("知识库已创建");
                  setMode("closed");
                },
              },
            )
          }
          submitting={createMutation.isPending}
        />
      )}
      {typeof mode === "object" && (
        <KbFormDialog
          mode="edit"
          initial={mode.edit}
          onCancel={() => setMode("closed")}
          onSubmit={(values) =>
            updateMutation.mutate(
              {
                id: mode.edit.id,
                name: values.name,
                description: values.description,
              },
              {
                onSuccess: () => {
                  message.success("知识库已更新");
                  setMode("closed");
                },
              },
            )
          }
          submitting={updateMutation.isPending}
        />
      )}
      {permissionKb !== null && (
        <KnowledgeBasePermissionModal
          kbId={permissionKb.id}
          kbName={permissionKb.name}
          open={true}
          onClose={() => setPermissionKb(null)}
        />
      )}
    </main>
  );
}

/// <summary>空状态：无知识库时显示引导创建。</summary>
function EmptyKbState({ onCreate }: { onCreate: () => void }) {
  return (
    <div className="empty-state">
      <div className="empty-state-icon">📚</div>
      <div className="empty-state-title">还没有知识库</div>
      <div className="empty-state-desc">创建第一个知识库，开始管理文档</div>
      <Button type="primary" onClick={onCreate}>
        新建知识库
      </Button>
    </div>
  );
}

interface KbFormValues {
  name: string;
  description?: string | null;
}

interface KbFormDialogProps {
  mode: "create" | "edit";
  initial?: KnowledgeBaseDto;
  onCancel: () => void;
  onSubmit: (values: KbFormValues) => void;
  submitting: boolean;
}

/// <summary>创建/编辑知识库弹窗。编辑模式下 Description 为空表示清空。</summary>
function KbFormDialog({
  mode,
  initial,
  onCancel,
  onSubmit,
  submitting,
}: KbFormDialogProps) {
  const [name, setName] = useState(initial?.name ?? "");
  const [description, setDescription] = useState(initial?.description ?? "");
  const [error, setError] = useState<string | null>(null);

  function handleSubmit() {
    setError(null);
    const trimmed = name.trim();
    if (trimmed.length === 0) {
      setError("请填写名称");
      return;
    }
    if (trimmed.length > 200) {
      setError("名称长度不能超过 200");
      return;
    }
    onSubmit({
      name: trimmed,
      description: description.trim() === "" ? null : description,
    });
  }

  return (
    <Modal
      title={
        mode === "create" ? "新建知识库" : `编辑知识库：${initial?.name ?? ""}`
      }
      open
      onCancel={onCancel}
      destroyOnHidden
      footer={null}
      maskClosable={false}
    >
      {error !== null && (
        <Alert type="error" showIcon className="users-alert" message={error} />
      )}
      <Form layout="vertical" className="users-form">
        <Form.Item label="名称" required>
          <Input
            value={name}
            onChange={(event) => setName(event.target.value)}
            maxLength={200}
          />
        </Form.Item>
        <Form.Item label={mode === "edit" ? "描述（留空表示清空）" : "描述"}>
          <Input.TextArea
            value={description}
            onChange={(event) => setDescription(event.target.value)}
            rows={3}
          />
        </Form.Item>
      </Form>
      <div className="users-form-actions">
        <Button onClick={onCancel} disabled={submitting}>
          取消
        </Button>
        <Button type="primary" loading={submitting} onClick={handleSubmit}>
          保存
        </Button>
      </div>
    </Modal>
  );
}
