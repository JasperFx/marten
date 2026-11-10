#!/usr/bin/env bash
# Pin Marten to locally packed JasperFx / Weasel and register the local feed.
#   scripts/take-local-upstream.sh <jasperfx-version> <weasel-version> [feed-folder]
# Pass "" for a family to leave it alone. Run again with the released versions to put the pins back.
set -euo pipefail
if [[ $# -lt 2 ]]; then
  echo "usage: $0 <jasperfx-version|''> <weasel-version|''> [feed-folder]" >&2
  exit 2
fi
cd "$(dirname "${BASH_SOURCE[0]}")/.."
args=(TakeLocalUpstream)
if [[ -n "$1" ]]; then args+=(--upstream-version "$1"); fi
if [[ -n "$2" ]]; then args+=(--weasel-version "$2"); fi
if [[ -n "${3:-}" ]]; then args+=(--feed "$3"); fi
./build.sh "${args[@]}"
