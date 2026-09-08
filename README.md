# TigerRAG

TigerRAG 是一个模块化的企业级 RAG（Retrieval-Augmented Generation，检索增强生成）系统骨架。首个版本包含一个 ASP.NET Core API、一个独立的文档索引 Worker，以及一个 React 管理后台。

前端使用 React Router 7.18.3。原始的 v6 选型已升级，因为其受支持的版本线受到已公开的开放重定向（open-redirect）与水合（hydration）漏洞影响。

## 项目结构

- `src/TigerRAG.Api`：Controller Web API、JWT、SignalR 与健康检查。
- `src/TigerRAG.Worker`：后台文档索引宿主。
- `src/TigerRAG.Domain`：实体与业务不变量，不依赖任何框架。
- `src/TigerRAG.Application`：用例与外部服务抽象。
- `src/TigerRAG.Infrastructure`：DAL、EF Core/PostgreSQL、Identity、Redis、MinIO 与 Qdrant 适配器。
- `frontend/tigerrag-admin`：React 管理后台应用。
- `tests`：后端单元测试与集成测试。
- `deploy/`：一键部署脚本与 docker-compose 编排。

业务 API 固定采用 `Controller -> 具体 Application Service -> DAL interface -> Infrastructure/EF Core`。业务服务直接使用具体类，不为稳定的业务实现机械创建接口；接口只保留在 DAL、缓存、存储、向量库、模型客户端等基础设施边界。不使用 Minimal API 承载业务路由，不增加通用仓储或消息中介框架。尚未实现的知识库、文档、问答和审计功能返回统一结构中的 `code=50100`，并按测试驱动方式逐个补齐。

一期 RBAC 包含 `Admin`、`KbManager`、`Editor`、`Viewer`、`Auditor` 五个固定角色，支持 JWT 登录与刷新、退出、修改密码、管理员创建用户和重置密码、角色分配，以及知识库所有者/用户/角色三级文档访问范围。

所有 Controller API 均返回 HTTP 200，并使用统一结构：

```json
{ "code": 0, "message": "success", "data": {} }
```

`code=0` 表示成功；非零 `code` 表示业务或框架错误，具体错误码由业务模块定义。客户端必须依据 `code` 判断业务是否成功，不能依据 HTTP 状态码判断。

## 本地开发

```powershell
dotnet restore TigerRAG.slnx
dotnet test TigerRAG.slnx
dotnet run --project src/TigerRAG.Api
dotnet run --project src/TigerRAG.Worker

Set-Location frontend/tigerrag-admin
npm install
npm run test:run
npm run dev
```

本地运行 API/Worker 时，请通过 User Secrets 或环境变量提供 `ConnectionStrings__PostgreSql`、MinIO 凭据和 `Jwt__SigningKey`。不要把真实密钥写入 `appsettings.json`。

## 数据库脚本

初始 PostgreSQL 脚本位于 `deploy/sql/001_initial_schema.sql`，包含 ASP.NET Core Identity、RAG 业务表、文档 ACL、索引和五个固定角色。脚本可重复执行，由数据库维护人员审核后手工执行：

```bash
psql -v ON_ERROR_STOP=1 -d ragdb -f deploy/sql/001_initial_schema.sql
```

项目不使用 EF Core Migration，也不依赖 `__EFMigrationsHistory`。后续数据库变更必须新增递增编号的 SQL 脚本，由维护人员审核并手工执行；应用启动时不会自动建表或更新表结构。

脚本不会创建默认管理员或写入默认密码。执行 SQL 后，可通过显式命令创建或补齐首个管理员角色。密码从环境变量读取，不会出现在命令参数中：

```powershell
cd D:\code\TigerRAG
$env:ConnectionStrings__PostgreSql = "Host=localhost;Port=5432;Database=ragdb;Username=tigerrag;Password=你的数据库密码"
$env:BootstrapAdmin__UserName = "admin"
$env:BootstrapAdmin__Password = "替换为强密码"
dotnet run --project .\src\TigerRAG.Api --launch-profile http -- --bootstrap-admin
```

容器部署可使用 `.env` 中的 `BOOTSTRAP_ADMIN_USERNAME` 和 `BOOTSTRAP_ADMIN_PASSWORD`：

```bash
docker compose --env-file .env -f deploy/docker-compose.yml run --rm api --bootstrap-admin
```

该命令执行完成后直接退出；API 正常启动不会自动创建账号。

## 认证接口

```text
POST /api/auth/login                 登录并设置刷新 Cookie
POST /api/auth/refresh               轮换刷新 Cookie 和 Access Token
POST /api/auth/logout                撤销刷新会话
POST /api/auth/change-password       当前用户修改密码
POST /api/users                      管理员创建用户
PUT  /api/users/{id}/password        管理员设置临时密码
```

前端 Access Token 只保存在内存；Refresh Token 只通过 HttpOnly Cookie 传递，并以 SHA-256 哈希形式存储在 PostgreSQL。当前一期不接邮件服务，“找回密码”由管理员设置临时密码完成。

## 部署

### 前置准备

- 已安装 Docker（包含 `docker compose` 插件）。
- 将 `.env.example` 复制为 `.env` 并按需修改开发凭证：

    ```bash
    cp .env.example .env
    ```

    DBeaver / pgAdmin：localhost:5432
    Redis Desktop Manager：localhost:6379
    Qdrant Dashboard：浏览器打开 http://localhost:6333/dashboard
    MinIO Console：浏览器打开 http://localhost:9001
    API 直连：http://localhost:5080
    Admin 直连：http://localhost:5173（绕过 gateway 直接看前端，方便排查反代问题）

### 一键部署

`deploy/deploy.sh` 是幂等的部署脚本，重复部署时仅重建业务服务（api / worker / admin / gateway），已存在的基础服务容器（postgres / redis / qdrant / minio）会被跳过，避免重复拉起。

```bash
# 默认：跳过已部署的基础服务，重建所有业务服务
./deploy/deploy.sh

# 强制重建所有服务（包括基础服务）
./deploy/deploy.sh --force

# 查看帮助
./deploy/deploy.sh --help
```

### 跳过逻辑说明

- **基础服务**（postgres、redis、qdrant、minio）：通过 `docker container inspect tigerrag-<service>` 或 `tigerrag-<service>-1`（兼容 Docker Compose 副本后缀）判断容器是否存在。已存在则跳过拉起，仅 `start` 确保运行；不存在则通过 `docker compose up -d` 拉起。
- **业务服务**（api、worker、admin、gateway）：每次都执行 `docker compose up -d --build`，始终基于最新代码重新构建镜像。

### 直接使用 docker compose

如果希望完全手动控制，可绕过 `deploy.sh`，直接调用：

```bash
docker compose --env-file .env -f deploy/docker-compose.yml up --build
```

### 验证部署脚本

`deploy/test-deploy.sh` 使用 bash 函数 mock 对 `should_skip_service` 的核心判断逻辑和路径解析做单元测试：

```bash
bash deploy/test-deploy.sh
```

### 关于 docker-compose 中的挂载目录

所有服务的数据和静态配置文件都通过**绑定挂载**统一管理。默认主机路径在 `deploy/` 下的 `data/` 与 `config/`：

```
deploy/
  config/                # 静态配置文件
    nginx.conf           # gateway (nginx) 配置
  data/                  # 持久化运行数据
    postgres/            # postgres 集群根
    redis/               # redis append-only 文件
    qdrant/              # qdrant 存储
    minio/               # minio 数据
```

各服务的挂载对应关系：

| 服务     | 主机路径（相对 deploy/）    | 容器内路径              |
| -------- | --------------------------- | ----------------------- |
| gateway  | `${CONFIG_ROOT}/nginx.conf` | `/etc/nginx/nginx.conf` |
| postgres | `${DATA_ROOT}/postgres`     | `/var/lib/postgresql`   |
| redis    | `${DATA_ROOT}/redis`        | `/data`                 |
| qdrant   | `${DATA_ROOT}/qdrant`       | `/qdrant/storage`       |
| minio    | `${DATA_ROOT}/minio`        | `/data`                 |

- `DATA_ROOT` 与 `CONFIG_ROOT` 在 `.env` 中可覆盖；默认分别为 `./data` 与 `./config`，相对于 `deploy/` 解析。
- postgres 18 镜像要求把挂载点放在 `/var/lib/postgresql`（而不是旧的 `/var/lib/postgresql/data`），由镜像内部用 `pg_ctlcluster` 风格的子目录管理数据。
- `deploy.sh` 在首次部署前会调用 `prepare_mount_paths`：创建所需的子目录，并把空的 `data/postgres` 设为 `chmod 0777`（postgres 18 entrypoint 在容器内以 root 执行 mkdir，主机源目录必须对容器 root 可写；仅当目录为空时设置，不影响既有集群）。
- 如果绕过 `deploy.sh` 直接调用 `docker compose`，请先 `cd deploy/` 再执行，否则相对路径 `./config/nginx.conf` 与 `./data/...` 会指向错误位置。

#### WSL / Docker Desktop for Windows 注意事项

若项目仓库位于 Windows 文件系统并通过 WSL 的 `/mnt/c`、`/mnt/d`（DrvFS/9P）访问，需要把 `DATA_ROOT` 与 `CONFIG_ROOT` 都改到 WSL 原生 ext4 路径，否则会遇到两类问题：

1. **postgres 启动失败**：postgres 镜像内 entrypoint 以 root `chmod 00700 "$PGDATA"`，Windows-backed 文件系统不允许非宿主用户变更权限，会报 `Operation not permitted`。
2. **gateway 启动失败**：Docker Desktop 的 bind-mount 转发层在 Windows 文件系统上会把某些文件识别成目录（`not a directory`），nginx 配置文件无法挂载。

在 `.env` 中显式覆盖：

```bash
DATA_ROOT=/home/<your-user>/tigerrag/data
CONFIG_ROOT=/home/<your-user>/tigerrag/config
```

并提前在 WSL 中创建对应目录：

```bash
mkdir -p ~/tigerrag/data/postgres ~/tigerrag/config
cp deploy/config/nginx.conf ~/tigerrag/config/
```

架构决策记录在 [总览设计文档](docs/plans/2026-09-07-tigerrag-overview-design.md)。
