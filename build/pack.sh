#!/usr/bin/env bash
# Restore, build, test, and pack Cachemix exactly as CI does.
#
# Usage: ./build/pack.sh [Configuration]
#   Configuration defaults to Release.
#   Output directory can be overridden with the ARTIFACTS_DIR env var.

set -euo pipefail

CONFIGURATION="${1:-Release}"
ARTIFACTS="${ARTIFACTS_DIR:-artifacts}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

cd "$ROOT"

echo "==> Restoring"
dotnet restore Cachemix.slnx

echo "==> Building ($CONFIGURATION)"
dotnet build Cachemix.slnx --configuration "$CONFIGURATION" --no-restore

echo "==> Testing"
dotnet test Cachemix.slnx --configuration "$CONFIGURATION" --no-build

echo "==> Packing -> $ARTIFACTS"
dotnet pack Cachemix.slnx --configuration "$CONFIGURATION" --no-build --output "$ARTIFACTS"

echo "==> Done. Packages written to: $ARTIFACTS"
