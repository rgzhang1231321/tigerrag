#!/usr/bin/env bash
# Unit tests for deploy.sh skip-if-running logic and unified mount paths.
# Source deploy.sh to load pure functions, then assert their behavior.
set -euo pipefail

TESTS_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEPLOY_SH="$TESTS_DIR/deploy.sh"

if [ ! -f "$DEPLOY_SH" ]; then
  echo "FAIL: $DEPLOY_SH does not exist yet"
  exit 1
fi

# shellcheck disable=SC1090
source "$DEPLOY_SH" --source-only
load_env >/dev/null 2>&1 || true
resolve_mount_root
resolve_config_root

assert_eq() {
  local expected="$1" actual="$2" label="$3"
  if [ "$expected" != "$actual" ]; then
    echo "FAIL: $label — expected '$expected', got '$actual'"
    exit 1
  fi
  echo "PASS: $label"
}

# Test 1: INFRA_SERVICES list is defined and non-empty.
if [ "${#INFRA_SERVICES[@]}" -lt 1 ]; then
  echo "FAIL: INFRA_SERVICES must list at least one infra service"
  exit 1
fi
echo "PASS: INFRA_SERVICES contains ${#INFRA_SERVICES[@]} entries"

# Test 2: known infra services must be present.
for svc in postgres redis qdrant minio; do
  found=0
  for s in "${INFRA_SERVICES[@]}"; do
    if [ "$s" = "$svc" ]; then found=1; break; fi
  done
  if [ "$found" -ne 1 ]; then
    echo "FAIL: $svc missing from INFRA_SERVICES"
    exit 1
  fi
done
echo "PASS: postgres/redis/qdrant/minio all classified as infra"

# Test 3: known app services must NOT be in INFRA_SERVICES.
for svc in api worker admin gateway; do
  for s in "${INFRA_SERVICES[@]}"; do
    if [ "$s" = "$svc" ]; then
      echo "FAIL: $svc should not be classified as infra"
      exit 1
    fi
  done
done
echo "PASS: api/worker/admin/gateway correctly excluded from infra"

# Test 4: should_skip_service returns 0 (true) for infra when container exists.
docker() {
  case "$1 $2" in
    "container inspect"*)
      return 0
      ;;
    *)
      return 1
      ;;
  esac
}
for svc in postgres redis qdrant minio; do
  if ! should_skip_service "$svc"; then
    echo "FAIL: should_skip_service $svc should return true when container exists"
    exit 1
  fi
done
echo "PASS: should_skip_service returns true for existing infra container"

# Test 5: should_skip_service returns 1 (false) for infra when container missing.
docker() {
  case "$1 $2" in
    "container inspect"*)
      return 1
      ;;
    *)
      return 1
      ;;
  esac
}
for svc in postgres redis qdrant minio; do
  if should_skip_service "$svc"; then
    echo "FAIL: should_skip_service $svc should return false when container missing"
    exit 1
  fi
done
echo "PASS: should_skip_service returns false for missing infra container"

# Test 6: app services are never skipped even when their container exists.
docker() {
  case "$1 $2" in
    "container inspect"*)
      return 0
      ;;
    *)
      return 1
      ;;
  esac
}
for svc in api worker admin gateway; do
  if should_skip_service "$svc"; then
    echo "FAIL: should_skip_service $svc should never skip app services"
    exit 1
  fi
done
echo "PASS: app services are never skipped"

# Test 7: MOUNT_ROOT is defined (default under SCRIPT_DIR, overridable to a
# native fs path via DATA_ROOT).
if [ -z "${MOUNT_ROOT:-}" ]; then
  echo "FAIL: MOUNT_ROOT must be defined"
  exit 1
fi
echo "PASS: MOUNT_ROOT is defined ($MOUNT_ROOT)"

# Test 8: each infra service has a known data subdirectory under MOUNT_ROOT.
for svc in postgres redis qdrant minio; do
  expected="$MOUNT_ROOT/$svc"
  actual="$(mount_path "$svc")"
  if [ "$expected" != "$actual" ]; then
    echo "FAIL: mount_path $svc expected '$expected', got '$actual'"
    exit 1
  fi
done
echo "PASS: mount_path maps every infra service under MOUNT_ROOT"

# Test 9: CONFIG_ROOT is defined (default under SCRIPT_DIR, overridable to a
# native fs path via CONFIG_ROOT).
if [ -z "${CONFIG_ROOT:-}" ]; then
  echo "FAIL: CONFIG_ROOT must be defined"
  exit 1
fi
echo "PASS: CONFIG_ROOT is defined ($CONFIG_ROOT)"

# Test 10: prepare_mount_paths creates the required directories inside a sandbox.
SANDBOX="$(mktemp -d)"
trap 'rm -rf "$SANDBOX"' EXIT
PREP_ROOT="$SANDBOX/data"
PREP_CONFIG="$SANDBOX/config"
prepare_mount_paths "$PREP_ROOT" "$PREP_CONFIG"
for sub in postgres redis qdrant minio; do
  if [ ! -d "$PREP_ROOT/$sub" ]; then
    echo "FAIL: prepare_mount_paths did not create $PREP_ROOT/$sub"
    exit 1
  fi
done
if [ ! -d "$PREP_CONFIG" ]; then
  echo "FAIL: prepare_mount_paths did not create $PREP_CONFIG"
  exit 1
fi
echo "PASS: prepare_mount_paths creates data/config subdirectories"

# Test 11: prepare_mount_paths is idempotent (running twice does not fail).
prepare_mount_paths "$PREP_ROOT" "$PREP_CONFIG"
echo "PASS: prepare_mount_paths is idempotent"

# Test 12: DATA_ROOT override resolves relative paths against SCRIPT_DIR.
(
  DATA_ROOT="./data"
  resolve_mount_root
  assert_eq "$SCRIPT_DIR/data" "$MOUNT_ROOT" "DATA_ROOT relative path resolves against SCRIPT_DIR"
)

# Test 13: DATA_ROOT absolute path is preserved.
(
  DATA_ROOT="/tmp/some/abs/path"
  resolve_mount_root
  assert_eq "/tmp/some/abs/path" "$MOUNT_ROOT" "DATA_ROOT absolute path preserved"
)

# Test 14: DATA_ROOT unset falls back to $SCRIPT_DIR/data.
(
  unset DATA_ROOT
  resolve_mount_root
  assert_eq "$SCRIPT_DIR/data" "$MOUNT_ROOT" "DATA_ROOT unset defaults to SCRIPT_DIR/data"
)

# Test 15: CONFIG_ROOT relative path resolves against SCRIPT_DIR.
(
  CONFIG_ROOT="./config"
  resolve_config_root
  assert_eq "$SCRIPT_DIR/config" "$CONFIG_ROOT" "CONFIG_ROOT relative path resolves against SCRIPT_DIR"
)

# Test 16: CONFIG_ROOT absolute path is preserved.
(
  CONFIG_ROOT="/tmp/some/abs/config"
  resolve_config_root
  assert_eq "/tmp/some/abs/config" "$CONFIG_ROOT" "CONFIG_ROOT absolute path preserved"
)

# Test 17: CONFIG_ROOT unset falls back to $SCRIPT_DIR/config.
(
  unset CONFIG_ROOT
  resolve_config_root
  assert_eq "$SCRIPT_DIR/config" "$CONFIG_ROOT" "CONFIG_ROOT unset defaults to SCRIPT_DIR/config"
)

# Test 18: the API proxy must preserve the /api prefix expected by controllers.
NGINX_CONF="$SCRIPT_DIR/config/nginx.conf"
if ! grep -Eq 'proxy_pass[[:space:]]+http://api_upstream;' "$NGINX_CONF"; then
  echo "FAIL: nginx API proxy must preserve the /api path prefix"
  exit 1
fi
echo "PASS: nginx API proxy preserves the /api path prefix"

# Test 19: database schema is maintained as standalone SQL, without EF migrations.
SCHEMA_SQL="$SCRIPT_DIR/sql/001_initial_schema.sql"
MIGRATIONS_DIR="$SCRIPT_DIR/../src/TigerRAG.Infrastructure/Persistence/Migrations"
TOOLS_MANIFEST="$SCRIPT_DIR/../dotnet-tools.json"
if grep -q '__EFMigrationsHistory' "$SCHEMA_SQL"; then
  echo "FAIL: schema SQL must not depend on EF migration history"
  exit 1
fi
if [ -d "$MIGRATIONS_DIR" ] && find "$MIGRATIONS_DIR" -type f -name '*.cs' -print -quit | grep -q .; then
  echo "FAIL: EF migration source files must not be present"
  exit 1
fi
if [ -f "$TOOLS_MANIFEST" ] && grep -q 'dotnet-ef' "$TOOLS_MANIFEST"; then
  echo "FAIL: dotnet-ef must not be configured as a local tool"
  exit 1
fi
if find "$SCRIPT_DIR/../src" -type f -name '*.cs' \
    -not -path '*/bin/*' -not -path '*/obj/*' \
    -exec grep -l 'IDesignTimeDbContextFactory' {} + | grep -q .; then
  echo "FAIL: EF design-time DbContext factories must not be present"
  exit 1
fi
if ! grep -q 'CREATE TABLE IF NOT EXISTS "RefreshTokens"' "$SCHEMA_SQL"; then
  echo "FAIL: standalone schema SQL must create RefreshTokens idempotently"
  exit 1
fi
echo "PASS: database schema is managed without EF migrations"

echo
echo "All tests passed."
