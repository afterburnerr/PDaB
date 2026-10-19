#!/usr/bin/env bash
set -euo pipefail
pdab_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
cd -- "$pdab_dir"
exec "$pdab_dir/PDaB.x86_64" "$@"
