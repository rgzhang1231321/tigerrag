#!/usr/bin/env bash
# TigerRAG one-shot deployment script.
#
# All service data and configuration files are bind-mounted under a single
# root controlled by DATA_ROOT (runtime data) and CONFIG_ROOT (static
# config). They share one directory tree:
#
#   $DATA_ROOT/
#     postgres/                # postgres cluster root
#     redis/                   # redis append-only file
#     qdrant/                  # qdrant storage
#     minio/                   # minio data
#
#   $CONFIG_ROOT/
#     nginx.conf               # gateway (nginx) config
#
# Both default to $SCRIPT_DIR/{data,config}. On hosts where the working
# directory is on a filesystem that does not support POSIX permissions or
# proper file-type detection for bind mounts (e.g. WSL with the project
# mounted from /mnt/c, /mnt/d via DrvFS/9P), override either root in .env
# to point at a native ext4 path such as $HOME/tigerrag/{data,config}.
#
# Default behavior: skip infra services (postgres, redis, qdrant, minio)
# whose container already exists; always rebuild and restart app services
# (api, worker, admin, gateway). Use --force to rebuild everything.
#
# Usage:
#   ./deploy.sh            # idempotent deploy, skip existing infra
#   ./deploy.sh --force    # rebuild every service, including infra
#   ./deploy.sh --help

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
COMPOSE_FILE="$SCRIPT_DIR/docker-compose.yml"
PROJECT_NAME="tigerrag"

INFRA_SERVICES=(postgres redis qdrant minio)
APP_SERVICES=(api worker admin gateway)

usage() {
  sed -n '2,/^$/p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
}

# Returns 0 (true) when the given service should be skipped because its
# container already exists on this host. App services are never skipped.
# Docker Compose names containers "<project>-<service>-<replica>"; we check
# both the bare "<project>-<service>" and the suffixed form to handle
# existing stacks created before this script.
should_skip_service() {
  local service="$1"
  local is_infra=0
  local s
  for s in "${INFRA_SERVICES[@]}"; do
    if [ "$s" = "$service" ]; then
      is_infra=1
      break
    fi
  done
  if [ "$is_infra" -eq 0 ]; then
    return 1
  fi
  if docker container inspect "${PROJECT_NAME}-${service}" >/dev/null 2>&1 \
     || docker container inspect "${PROJECT_NAME}-${service}-1" >/dev/null 2>&1; then
    return 0
  fi
  return 1
}

# Load .env if present. Order: deploy/.env first, then project root .env so
# project-level settings override deploy-level ones. Exported so docker
# compose can see DATA_ROOT and credentials.
load_env() {
  local candidate
  for candidate in "$SCRIPT_DIR/.env" "$SCRIPT_DIR/../.env"; do
    if [ -f "$candidate" ]; then
      set -a
      # shellcheck disable=SC1090
      source "$candidate"
      set +a
    fi
  done
}

# Resolve MOUNT_ROOT from DATA_ROOT (relative paths are taken relative to
# SCRIPT_DIR so the value matches what docker-compose sees). The result is
# normalized to a canonical absolute path (no leading "./" or trailing "/").
resolve_mount_root() {
  local raw="${DATA_ROOT:-$SCRIPT_DIR/data}"
  case "$raw" in
    /*|~*)
      MOUNT_ROOT="$raw"
      ;;
    *)
      MOUNT_ROOT="$SCRIPT_DIR/$raw"
      ;;
  esac
  if [ -d "$MOUNT_ROOT" ]; then
    MOUNT_ROOT="$(cd "$MOUNT_ROOT" && pwd)"
  else
    # Strip a trailing "./" if present without requiring the dir to exist.
    MOUNT_ROOT="${MOUNT_ROOT%/./}"
    MOUNT_ROOT="${MOUNT_ROOT%/}"
  fi
}

# Resolve CONFIG_ROOT similarly; defaults to $SCRIPT_DIR/config.
resolve_config_root() {
  local raw="${CONFIG_ROOT:-$SCRIPT_DIR/config}"
  case "$raw" in
    /*|~*)
      CONFIG_ROOT="$raw"
      ;;
    *)
      CONFIG_ROOT="$SCRIPT_DIR/$raw"
      ;;
  esac
  if [ -d "$CONFIG_ROOT" ]; then
    CONFIG_ROOT="$(cd "$CONFIG_ROOT" && pwd)"
  else
    CONFIG_ROOT="${CONFIG_ROOT%/./}"
    CONFIG_ROOT="${CONFIG_ROOT%/}"
  fi
}

# Per-service data directory resolver. Kept in sync with docker-compose.yml
# bind mounts.
mount_path() {
  echo "$MOUNT_ROOT/$1"
}

# Ensure every infra data directory and the config root exist before
# docker compose runs, so bind mounts don't surprise us with auto-created
# empty directories owned by root. Safe to call repeatedly. When called
# without arguments it uses the script defaults; arguments allow tests to
# redirect the roots into a sandbox.
prepare_mount_paths() {
  local mount_root_arg="${1:-}"
  local config_root_arg="${2:-}"
  if [ -z "$mount_root_arg" ]; then mount_root_arg="$MOUNT_ROOT"; fi
  if [ -z "$config_root_arg" ]; then config_root_arg="$CONFIG_ROOT"; fi
  mkdir -p "$config_root_arg"
  local svc
  for svc in "${INFRA_SERVICES[@]}"; do
    mkdir -p "$mount_root_arg/$svc"
  done
  # Postgres 18+'s entrypoint runs mkdir as the container's root before
  # dropping to the postgres user. The bind-mount source therefore has to be
  # writable by the container root (UID 0). We chmod 0777 only on an empty
  # directory so we never touch an existing cluster.
  if [ -d "$mount_root_arg/postgres" ] && [ -z "$(ls -A "$mount_root_arg/postgres" 2>/dev/null)" ]; then
    chmod 0777 "$mount_root_arg/postgres" 2>/dev/null || true
  fi
}

run() {
  echo "+ $*"
  "$@"
}

deploy() {
  local force="${1:-false}"

  load_env
  resolve_mount_root
  resolve_config_root

  if [ ! -f "$COMPOSE_FILE" ]; then
    echo "ERROR: compose file not found: $COMPOSE_FILE" >&2
    exit 1
  fi

  if ! command -v docker >/dev/null 2>&1; then
    echo "ERROR: docker CLI not found in PATH" >&2
    exit 1
  fi

  prepare_mount_paths

  # Run docker compose from SCRIPT_DIR so that bind-mount paths in the
  # compose file resolve relative to deploy/, not the caller's cwd.
  cd "$SCRIPT_DIR"

  local skipped=()
  local started=()

  for service in "${INFRA_SERVICES[@]}"; do
    if [ "$force" = "true" ]; then
      echo "infra: $service -- force rebuild"
      run docker compose -p "$PROJECT_NAME" -f "$COMPOSE_FILE" up -d --build "$service"
      started+=("$service")
    elif should_skip_service "$service"; then
      echo "infra: $service -- already deployed, skipping"
      run docker compose -p "$PROJECT_NAME" -f "$COMPOSE_FILE" start "$service" >/dev/null 2>&1 || true
      skipped+=("$service")
    else
      echo "infra: $service -- starting"
      run docker compose -p "$PROJECT_NAME" -f "$COMPOSE_FILE" up -d "$service"
      started+=("$service")
    fi
  done

  run docker compose -p "$PROJECT_NAME" -f "$COMPOSE_FILE" up -d --build "${APP_SERVICES[@]}"

  echo
  echo "skipped infra: ${skipped[*]:-(none)}"
  echo "started:       ${started[*]:-(none)} + ${APP_SERVICES[*]}"
  echo "data root:     $MOUNT_ROOT"
  echo "config root:   $CONFIG_ROOT"
}

main() {
  local force=false
  while [ $# -gt 0 ]; do
    case "$1" in
      --force|-f)
        force=true
        shift
        ;;
      --help|-h)
        usage
        exit 0
        ;;
      --source-only)
        return 0
        ;;
      *)
        echo "ERROR: unknown argument: $1" >&2
        usage
        exit 2
        ;;
    esac
  done

  deploy "$force"
}

main "$@"
