import {
  Alert,
  Button,
  Card,
  Checkbox,
  Drawer,
  Input,
  Popconfirm,
  Select,
  Space,
  Spin,
  Table,
  Tag,
  message,
} from "antd";
import type { ColumnsType } from "antd/es/table";
import {
  CloudUploadOutlined,
  DeleteOutlined,
  EyeOutlined,
  LeftOutlined,
  LockOutlined,
  ReloadOutlined,
} from "@ant-design/icons";
import { useMemo, useRef, useState } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { useKnowledgeBases } from "../knowledge-base/useKnowledgeBases";
import {
  useDeleteDocument,
  useDocuments,
  useReindexDocument,
  useUploadDocument,
  useBatchDeleteDocuments,
  useDocumentContent,
} from "./useDocuments";
import type { DocumentDto } from "./documentApi";
import { DocumentPermissionModal } from "./DocumentPermissionModal";

const ALLOWED_MIME = new Set([
  "application/pdf",
  "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
  "application/msword",
  "application/vnd.openxmlformats-officedocument.presentationml.presentation",
  "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
  "application/vnd.ms-excel",
  "application/vnd.ms-outlook",
  "application/vnd.oasis.opendocument.text",
  "application/vnd.oasis.opendocument.presentation",
  "application/vnd.oasis.opendocument.spreadsheet",
  "text/markdown",
  "text/plain",
  "text/csv",
  "text/html",
  "message/rfc822",
  "image/png",
  "image/jpeg",
  "image/gif",
  "image/webp",
  "image/bmp",
  "image/tiff",
  "image/svg+xml",
]);

const MAX_FILE_SIZE_BYTES = 30 * 1024 * 1024;

const STATUS_CONFIG: Record<string, { color: string; label: string }> = {
  Pending: { color: "default", label: "等待中" },
  Processing: { color: "blue", label: "处理中" },
  Indexed: { color: "green", label: "已索引" },
  Failed: { color: "red", label: "失败" },
};

/// <summary>格式化文件大小为人类可读字符串。</summary>
function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}

/// <summary>文档管理页：KB 详情头 + 指标卡 + 筛选 + 批量操作 + 上传 + 预览。</summary>
export function DocumentPage() {
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();
  const { data: kbs = [], isPending: kbsLoading } = useKnowledgeBases();

  const selectedKbId = searchParams.get("kbId") ?? null;
  const effectiveKbId = selectedKbId ?? kbs[0]?.id ?? null;

  function selectKb(id: string) {
    setSearchParams({ kbId: id });
  }

  const currentKb = useMemo(
    () => kbs.find((kb) => kb.id === effectiveKbId),
    [kbs, effectiveKbId],
  );

  const {
    data: page,
    isPending: docsLoading,
    refetch,
  } = useDocuments(effectiveKbId);
  const deleteMutation = useDeleteDocument();
  const reindexMutation = useReindexDocument();
  const uploadMutation = useUploadDocument();
  const batchDeleteMutation = useBatchDeleteDocuments();

  const documents = page?.items ?? [];
  const [search, setSearch] = useState("");
  const [statusFilter, setStatusFilter] = useState<string>("all");
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
  const [previewDoc, setPreviewDoc] = useState<DocumentDto | null>(null);
  const [permissionDoc, setPermissionDoc] = useState<DocumentDto | null>(null);
  const contentQuery = useDocumentContent(previewDoc?.id ?? null);
  const [dragOver, setDragOver] = useState(false);
  const fileInputRef = useRef<HTMLInputElement>(null);

  const filtered = useMemo(
    () =>
      documents.filter((doc) => {
        const matchSearch = doc.fileName
          .toLowerCase()
          .includes(search.toLowerCase());
        const matchStatus =
          statusFilter === "all" || doc.status === statusFilter;
        return matchSearch && matchStatus;
      }),
    [documents, search, statusFilter],
  );

  const stats = useMemo(() => {
    const total = documents.length;
    const indexed = documents.filter((d) => d.status === "Indexed").length;
    const processing = documents.filter(
      (d) => d.status === "Processing",
    ).length;
    const failed = documents.filter((d) => d.status === "Failed").length;
    return { total, indexed, processing, failed };
  }, [documents]);

  function toggleSelection(id: string, checked: boolean) {
    setSelectedIds((prev) => {
      const next = new Set(prev);
      if (checked) next.add(id);
      else next.delete(id);
      return next;
    });
  }

  const columns: ColumnsType<DocumentDto> = [
    {
      title: "",
      dataIndex: "id",
      key: "select",
      width: 50,
      render: (_value, doc) => (
        <Checkbox
          checked={selectedIds.has(doc.id)}
          onChange={(event) => toggleSelection(doc.id, event.target.checked)}
        />
      ),
    },
    {
      title: "文件名",
      dataIndex: "fileName",
      key: "fileName",
      ellipsis: true,
      render: (_value, doc) => (
        <Button
          type="link"
          style={{ padding: 0 }}
          onClick={() => setPreviewDoc(doc)}
        >
          <Space>
            <span style={{ color: "var(--text-secondary)" }}>📄</span>
            <span style={{ color: "var(--brand-secondary)" }}>
              {doc.fileName}
            </span>
          </Space>
        </Button>
      ),
    },
    {
      title: "状态",
      dataIndex: "status",
      key: "status",
      width: 110,
      render: (value: string) => {
        const config = STATUS_CONFIG[value] ?? {
          color: "default",
          label: value,
        };
        return <Tag color={config.color}>{config.label}</Tag>;
      },
    },
    {
      title: "分块数",
      dataIndex: "chunkCount",
      key: "chunkCount",
      width: 80,
      align: "center",
    },
    {
      title: "大小",
      dataIndex: "fileSize",
      key: "fileSize",
      width: 100,
      render: (value: number) => formatFileSize(value ?? 0),
    },
    {
      title: "失败原因",
      dataIndex: "failureReason",
      key: "failureReason",
      ellipsis: true,
      render: (value: string | null) => (
        <span
          style={{ color: value ? "var(--error)" : "var(--text-secondary)" }}
        >
          {value ?? "—"}
        </span>
      ),
    },
    {
      title: "更新时间",
      dataIndex: "updatedAt",
      key: "updatedAt",
      width: 170,
      render: (value: string) => new Date(value).toLocaleString("zh-CN"),
    },
    {
      title: "操作",
      key: "actions",
      width: 320,
      render: (_value, doc) => (
        <Space>
          <Button
            type="link"
            style={{ padding: "0 4px" }}
            icon={<EyeOutlined />}
            onClick={() => setPreviewDoc(doc)}
          >
            预览
          </Button>
          <Button
            type="link"
            style={{ padding: "0 4px" }}
            icon={<ReloadOutlined />}
            disabled={doc.status === "Processing"}
            loading={
              reindexMutation.isPending && reindexMutation.variables === doc.id
            }
            onClick={() =>
              reindexMutation.mutate(doc.id, {
                onSuccess: () =>
                  message.success(`已提交重新索引：${doc.fileName}`),
              })
            }
          >
            重索引
          </Button>
          <Button
            type="link"
            style={{ padding: "0 4px" }}
            icon={<LockOutlined />}
            onClick={() => setPermissionDoc(doc)}
          >
            权限
          </Button>
          <Popconfirm
            title="删除文档"
            description={`确认删除「${doc.fileName}」？该操作不可撤销。`}
            okText="确认删除"
            cancelText="取消"
            okButtonProps={{ danger: true, loading: deleteMutation.isPending }}
            onConfirm={() =>
              deleteMutation.mutate(doc.id, {
                onSuccess: () => message.success(`已删除：${doc.fileName}`),
              })
            }
            disabled={doc.status === "Processing"}
          >
            <Button
              type="link"
              danger
              style={{ padding: "0 4px" }}
              icon={<DeleteOutlined />}
              disabled={doc.status === "Processing"}
            >
              删除
            </Button>
          </Popconfirm>
        </Space>
      ),
    },
  ];

  /// <summary>处理单文件上传：校验后调用 mutation；失败抛异常由调用方处理。</summary>
  async function handleFileUpload(file: File): Promise<void> {
    if (effectiveKbId === null) {
      throw new Error("请先选择知识库");
    }
    if (!ALLOWED_MIME.has(file.type)) {
      throw new Error(`不支持的 MIME 类型：${file.type || "未知"}`);
    }
    if (file.size > MAX_FILE_SIZE_BYTES) {
      throw new Error("文件大小超过 30MB 上限");
    }
    await uploadMutation.mutateAsync({ kbId: effectiveKbId, file });
  }

  /// <summary>处理拖拽文件：校验后批量上传。</summary>
  function handleDrop(event: React.DragEvent) {
    event.preventDefault();
    event.stopPropagation();
    setDragOver(false);
    if (effectiveKbId === null) {
      message.error("请先选择知识库");
      return;
    }
    const files = Array.from(event.dataTransfer.files);
    if (files.length > 0) {
      void handleMultipleFilesUpload(files);
    }
  }

  /// <summary>批量上传多个文件：逐个校验并上传，汇总结果提示。</summary>
  async function handleMultipleFilesUpload(files: File[]) {
    let successCount = 0;
    const failMessages: string[] = [];
    for (const file of files) {
      try {
        await handleFileUpload(file);
        successCount++;
      } catch (error) {
        const reason = error instanceof Error ? error.message : "上传失败";
        failMessages.push(`${file.name}：${reason}`);
      }
    }
    if (successCount > 0) {
      message.success(`已上传 ${successCount} 个文件`);
    }
    for (const msg of failMessages) {
      message.error(msg);
    }
  }

  /// <summary>处理拖拽进入页面。</summary>
  function handleDragOver(event: React.DragEvent) {
    event.preventDefault();
    event.stopPropagation();
    if (effectiveKbId !== null && !dragOver) {
      setDragOver(true);
    }
  }

  /// <summary>处理拖拽离开页面。</summary>
  function handleDragLeave(event: React.DragEvent) {
    event.preventDefault();
    event.stopPropagation();
    // 仅当离开整个页面区域时才隐藏浮层
    const rect = (event.currentTarget as HTMLElement).getBoundingClientRect();
    const x = event.clientX;
    const y = event.clientY;
    if (x < rect.left || x > rect.right || y < rect.top || y > rect.bottom) {
      setDragOver(false);
    }
  }

  /// <summary>处理点击上传按钮。</summary>
  function triggerFileSelect() {
    fileInputRef.current?.click();
  }

  /// <summary>处理文件选择变更：支持多选。</summary>
  function handleFileSelect(event: React.ChangeEvent<HTMLInputElement>) {
    const files = Array.from(event.target.files ?? []);
    if (files.length > 0) {
      void handleMultipleFilesUpload(files);
    }
    event.target.value = "";
  }

  return (
    <main
      onDragOver={handleDragOver}
      onDragLeave={handleDragLeave}
      onDrop={handleDrop}
    >
      <input
        ref={fileInputRef}
        type="file"
        accept=".pdf,.docx,.doc,.pptx,.xlsx,.xls,.odt,.ods,.odp,.eml,.msg,.md,.txt,.csv,.html,.htm,.png,.jpg,.jpeg,.gif,.webp,.bmp,.tiff,.tiff,.svg"
        multiple
        style={{ display: "none" }}
        onChange={handleFileSelect}
      />
      <div className="page-title-bar">
        <span className="page-title">文档管理</span>
      </div>
      <Button
        type="link"
        style={{ padding: 0, marginBottom: 12 }}
        icon={<LeftOutlined />}
        onClick={() => navigate("/knowledge-bases")}
      >
        返回知识库列表
      </Button>
      {currentKb !== undefined && (
        <Card className="detail-header-card" bordered>
          <div className="detail-header-icon">📚</div>
          <div className="detail-header-info">
            <h2 className="detail-header-title">{currentKb.name}</h2>
            <div className="detail-header-meta">
              <span>{currentKb.description ?? "—"}</span>
              <span>👤 拥有者：{currentKb.ownerName ?? "—"}</span>
              <span>
                🕐 创建时间：
                {new Date(currentKb.createdAt).toLocaleString("zh-CN")}
              </span>
            </div>
          </div>
        </Card>
      )}
      <div className="metric-grid" style={{ marginBottom: "24px" }}>
        <MetricCard title="总文档" value={stats.total} icon="📄" />
        <MetricCard
          title="已索引"
          value={stats.indexed}
          icon="✅"
          color="#52c41a"
        />
        <MetricCard
          title="处理中"
          value={stats.processing}
          icon="⏳"
          color="#1677ff"
        />
        <MetricCard
          title="失败"
          value={stats.failed}
          icon="❌"
          color="#ff4d4f"
        />
      </div>
      <div style={{ margin: "24px 0" }} className="users-toolbar">
        <Select
          placeholder="选择知识库"
          loading={kbsLoading}
          value={effectiveKbId}
          onChange={selectKb}
          style={{ width: 240 }}
          options={kbs.map((kb) => ({ value: kb.id, label: kb.name }))}
          disabled={kbs.length === 0}
        />
        <Input.Search
          style={{ marginLeft: "24px" }}
          allowClear
          placeholder="按文件名过滤"
          value={search}
          onChange={(event) => setSearch(event.target.value)}
          className="users-search"
          disabled={effectiveKbId === null}
        />
        <Select
          value={statusFilter}
          onChange={setStatusFilter}
          style={{ minWidth: 140, marginLeft: "24px" }}
          disabled={effectiveKbId === null}
          options={[
            { value: "all", label: "全部状态" },
            { value: "Indexed", label: "已索引" },
            { value: "Processing", label: "处理中" },
            { value: "Pending", label: "等待中" },
            { value: "Failed", label: "失败" },
          ]}
        />
        <Button
          style={{ marginLeft: "24px" }}
          onClick={() => void refetch()}
          disabled={effectiveKbId === null}
        >
          刷新
        </Button>
        <Button
          style={{ float: "right" }}
          type="primary"
          icon={<CloudUploadOutlined />}
          disabled={effectiveKbId === null}
          onClick={triggerFileSelect}
        >
          上传文档
        </Button>
        <div style={{ flex: 1 }} />
      </div>
      {selectedIds.size > 0 && (
        <div className="batch-action-bar">
          <Space>
            <span>
              已选择 <strong>{selectedIds.size}</strong> 项
            </span>
            <Button
              size="small"
              onClick={() => message.info("批量重索引未实现")}
            >
              批量重索引
            </Button>
            <Popconfirm
              title="批量删除文档"
              description={`确认删除选中的 ${selectedIds.size} 个文档？该操作不可撤销。`}
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
                    message.success(`已删除 ${count} 个文档`);
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
      {effectiveKbId === null && !kbsLoading && (
        <Alert
          showIcon
          className="users-alert"
          type="info"
          message="请先创建知识库，再上传文档。"
        />
      )}
      <Table<DocumentDto>
        rowKey="id"
        loading={docsLoading && effectiveKbId !== null}
        dataSource={filtered}
        columns={columns}
        pagination={{ pageSize: 20, showSizeChanger: false }}
        locale={{
          emptyText: (
            <EmptyDocState effectiveKbId={effectiveKbId} onDrop={handleDrop} />
          ),
        }}
      />
      <Drawer
        title={`文档预览 — ${previewDoc?.fileName ?? ""}`}
        placement="right"
        width={720}
        onClose={() => setPreviewDoc(null)}
        open={previewDoc !== null}
      >
        {previewDoc !== null && (
          <div>
            <div className="preview-meta">
              <div>
                <span className="preview-meta-label">文件名：</span>
                {previewDoc.fileName}
              </div>
              <div>
                <span className="preview-meta-label">文件大小：</span>
                {formatFileSize(previewDoc.fileSize ?? 0)}
              </div>
              <div>
                <span className="preview-meta-label">上传时间：</span>
                {new Date(previewDoc.createdAt).toLocaleString("zh-CN")}
              </div>
              <div>
                <span className="preview-meta-label">状态：</span>
                {STATUS_CONFIG[previewDoc.status]?.label ?? previewDoc.status}（
                {previewDoc.chunkCount} 个分块）
              </div>
            </div>
            {contentQuery.isLoading && (
              <Spin style={{ display: "block", marginTop: 40 }} />
            )}
            {contentQuery.isError && (
              <Alert
                type="error"
                showIcon
                style={{ marginTop: 16 }}
                message={(contentQuery.error as Error)?.message ?? "加载失败"}
              />
            )}
            {contentQuery.data && (
              <div style={{ marginTop: 16 }}>
                {contentQuery.data.truncated && (
                  <Alert
                    type="warning"
                    showIcon
                    style={{ marginBottom: 12 }}
                    message={`文件过大，仅显示前 ${contentQuery.data.maxPreviewBytes ? (contentQuery.data.maxPreviewBytes / 1024 / 1024).toFixed(0) : "1"} MB 内容`}
                  />
                )}
                <pre className="preview-text-content">
                  {contentQuery.data.content}
                </pre>
              </div>
            )}
          </div>
        )}
      </Drawer>
      <DocumentPermissionModal
        documentId={permissionDoc?.id ?? ''}
        fileName={permissionDoc?.fileName ?? ''}
        open={permissionDoc !== null}
        onClose={() => setPermissionDoc(null)}
      />
      {dragOver && (
        <div
          className="drag-overlay"
          onDragOver={handleDragOver}
          onDragLeave={handleDragLeave}
          onDrop={handleDrop}
        >
          <div className="drag-overlay-inner">
            <CloudUploadOutlined className="drag-overlay-icon" />
            <div className="drag-overlay-text">释放文件以上传</div>
            <div className="drag-overlay-hint">
              支持 PDF / Word / Markdown / TXT，单文件不超过 30MB
            </div>
          </div>
        </div>
      )}
    </main>
  );
}

/// <summary>指标卡组件。</summary>
function MetricCard({
  title,
  value,
  icon,
  color,
}: {
  title: string;
  value: number;
  icon: string;
  color?: string;
}) {
  return (
    <Card className="metric-card" bordered>
      <div className="metric-card-label">
        <span
          className="metric-card-icon"
          style={color ? { color } : undefined}
        >
          {icon}
        </span>
        {title}
      </div>
      <div className="metric-card-value" style={color ? { color } : undefined}>
        {value}
      </div>
    </Card>
  );
}

/// <summary>空状态：未选中 KB 显示引导；已选中显示拖拽上传区。</summary>
function EmptyDocState({
  effectiveKbId,
  onDrop,
}: {
  effectiveKbId: string | null;
  onDrop: (event: React.DragEvent) => void;
}) {
  if (effectiveKbId === null) {
    return (
      <div className="empty-state">
        <div className="empty-state-icon">📂</div>
        <div className="empty-state-title">请先选择知识库</div>
        <div className="empty-state-desc">
          从上方下拉框选择一个知识库以查看其文档
        </div>
      </div>
    );
  }
  return (
    <div
      className="upload-dropzone upload-dropzone--empty"
      onDragOver={(e) => e.preventDefault()}
      onDrop={onDrop}
    >
      <div className="upload-dropzone-inner">
        <CloudUploadOutlined className="upload-dropzone-icon" />
        <div className="upload-dropzone-text">拖拽文件到此处上传</div>
        <div className="upload-dropzone-hint">
          支持 PDF / Word / Markdown / TXT，单文件不超过 30MB
        </div>
      </div>
    </div>
  );
}
