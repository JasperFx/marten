#!/bin/zsh
#
# Wait for Weasel 9.37.x, upgrade Marten's pins to it, and then run the one check that the upgrade
# exists to make possible: whether marten#5529 is fixed.
#
# WHY THIS SCRIPT EXISTS
#
# marten#5529 -- five CoreTests fail with "Unable to attain the global lock in time to apply database
# changes" whenever an EARLIER CoreTests run has already migrated the database. Root cause is NOT in
# Marten: Weasel's DatabaseBase.ApplyAllConfiguredChangesToDatabaseAsync released the global advisory
# lock only on its SUCCESS paths, so when one test's migration threw, lock 4004 stayed held by an idle
# connection and every later host-starting test was locked out. Filed as weasel#659, which is closed
# in the Weasel repo but unpublished as of 2026-09-29 (nuget's newest is 9.36.0).
#
# There is also a candidate Marten-side change -- MartenActivator.TryAttainLock gives a store only
# ~400ms (a fixed 50/100/250ms ladder) to win the lock, which is plausibly too short when two stores
# in one host share a database and the winner runs a real migration. That is DELIBERATELY not applied
# here. It was measured against the stranded lock and did nothing useful (5 failures became 3, and the
# rest waited the full budget), so it can only be evaluated once the lock is actually released. Decide
# it from THIS script's output, not from before it.
#
# USAGE
#   scripts/upgrade-weasel-9-37.sh                 # wait up to 8h for the release, then upgrade+verify
#   WAIT_SECONDS=0 scripts/upgrade-weasel-9-37.sh  # fail immediately if 9.37.x is not published yet
#   WEASEL_VERSION=9.37.1 scripts/upgrade-weasel-9-37.sh   # pin an exact version instead of newest 9.37.x
#   SKIP_VERIFY=1 scripts/upgrade-weasel-9-37.sh   # bump the pins only
#
set -u

REPO=${REPO:-$(cd "$(dirname "$0")/.." && pwd)}
WAIT_SECONDS=${WAIT_SECONDS:-28800}
POLL_SECONDS=${POLL_SECONDS:-300}
WEASEL_VERSION=${WEASEL_VERSION:-}
SKIP_VERIFY=${SKIP_VERIFY:-}
PROPS="$REPO/Directory.Packages.props"

# Every Weasel package Marten pins. Poll ALL of them, never just one: nuget's per-package
# flatcontainer indexes update independently, so the first one to appear is not evidence the rest
# have. Marten has been bitten by exactly that lag on the JasperFx family before.
PACKAGES=(Weasel.EntityFrameworkCore Weasel.Postgresql Weasel.Storage)
# Not pinned by Marten (it arrives transitively) but published in lockstep, and a restore will fail
# without it, so it is part of "is the release actually out".
PACKAGES+=(Weasel.Core)

say_it() { command -v say >/dev/null 2>&1 && say -v Daniel "For Marten, $1" >/dev/null 2>&1; return 0; }

# Newest 9.37.x present for one package, or empty.
newest_937() {
  curl -fsS "https://api.nuget.org/v3-flatcontainer/${1:l}/index.json" 2>/dev/null \
    | python3 -c "
import json,sys
try: v=json.load(sys.stdin)['versions']
except Exception: sys.exit(0)
c=[x for x in v if x.startswith('9.37.') and '-' not in x]
print(c[-1] if c else '')
" 2>/dev/null
}

resolve_version() {
  # Returns the version all packages agree on, or empty if any is missing.
  local want="$1" agreed=""
  for p in "${PACKAGES[@]}"; do
    local found
    if [[ -n "$want" ]]; then
      curl -fsS "https://api.nuget.org/v3-flatcontainer/${p:l}/index.json" 2>/dev/null \
        | python3 -c "import json,sys; sys.exit(0 if '$want' in json.load(sys.stdin)['versions'] else 1)" 2>/dev/null \
        && found="$want" || found=""
    else
      found=$(newest_937 "$p")
    fi
    [[ -z "$found" ]] && { echo ""; return; }
    if [[ -z "$agreed" ]]; then agreed="$found"
    elif [[ "$agreed" != "$found" ]]; then echo ""; return; fi
  done
  echo "$agreed"
}

echo "== Waiting for Weasel ${WEASEL_VERSION:-9.37.x} on nuget.org (up to ${WAIT_SECONDS}s) =="
deadline=$(( $(date +%s) + WAIT_SECONDS ))
VERSION=""
while true; do
  VERSION=$(resolve_version "$WEASEL_VERSION")
  [[ -n "$VERSION" ]] && break
  now=$(date +%s)
  if (( now >= deadline )); then
    echo "NOT PUBLISHED: no single 9.37.x is available for all of ${PACKAGES[*]}."
    for p in "${PACKAGES[@]}"; do echo "   $p -> ${$(newest_937 $p):-<none>}"; done
    exit 1
  fi
  echo "   $(date '+%H:%M:%S') not yet; sleeping ${POLL_SECONDS}s"
  sleep "$POLL_SECONDS"
done

echo "== Weasel $VERSION is live for every pinned package =="
say_it "Weasel $VERSION is out. Upgrading."

# ---- Bump the pins -------------------------------------------------------------------------------
python3 - "$PROPS" "$VERSION" <<'PY'
import re, sys
path, version = sys.argv[1], sys.argv[2]
s = open(path).read()
changed = []
for pkg in ("Weasel.EntityFrameworkCore", "Weasel.Postgresql", "Weasel.Storage"):
    pat = re.compile(r'(<PackageVersion Include="%s" Version=")[^"]+(" />)' % re.escape(pkg))
    s, n = pat.subn(lambda m: m.group(1) + version + m.group(2), s)
    if n != 1:
        sys.exit("expected exactly one pin for %s, found %d" % (pkg, n))
    changed.append(pkg)
open(path, "w").write(s)
print("bumped: " + ", ".join(changed) + " -> " + version)
PY
[[ $? -ne 0 ]] && exit 1

cd "$REPO" || exit 1
echo "== Restore + build =="
./build.sh compile > /tmp/weasel937-compile.log 2>&1
code=$?
echo "compile exit=$code"
if (( code != 0 )); then
  echo "BUILD FAILED -- pins bumped but not verified. Tail:"
  tail -25 /tmp/weasel937-compile.log
  exit 1
fi

# Confirm the restored assembly really is 9.37.x rather than a cached 9.36. A green build says
# nothing about which Weasel got restored.
restored=$(ls -d "$HOME"/.nuget/packages/weasel.postgresql/9.37.* 2>/dev/null | tail -1)
echo "restored Weasel.Postgresql package dir: ${restored:-<NONE FOUND>}"
[[ -z "$restored" ]] && { echo "Weasel 9.37 was not actually restored."; exit 1; }

[[ -n "$SKIP_VERIFY" ]] && { echo "SKIP_VERIFY set; stopping after the bump."; exit 0; }

# ---- The check this upgrade exists for ------------------------------------------------------------
#
# marten#5529's signature is that CoreTests passes on a VIRGIN database and fails on one an earlier
# CoreTests run already migrated. So run it TWICE against the SAME database. Before weasel#659 the
# second run fails ~5 tests with "Unable to attain the global lock in time"; if it now passes, the
# stranded lock is gone and #5529 can be closed without any Marten change.
echo "== marten#5529 check: CoreTests twice against ONE database =="
PG=marten-weasel937-postgresql-1
docker compose -p marten-weasel937 -f "$REPO/docker-compose.yml" down > /dev/null 2>&1
docker compose -p marten-weasel937 -f "$REPO/docker-compose.yml" up -d > /dev/null 2>&1
for i in $(seq 1 90); do docker exec "$PG" pg_isready -U postgres >/dev/null 2>&1 && break; sleep 2; done

export TZ=UTC
export DISABLE_TEST_PARALLELIZATION=true
for pass in 1 2; do
  ( cd src/CoreTests && dotnet run --no-build -f net9.0 > "/tmp/weasel937-core-$pass.log" 2>&1 )
  echo "  pass $pass :: $(grep -E '^  total:|^  failed:' "/tmp/weasel937-core-$pass.log" | tr '\n' ' ')"
  grep -E "^failed " "/tmp/weasel937-core-$pass.log" | sed 's/^/        /' | head -8
done

echo
echo "INTERPRETATION"
echo "  pass 2 clean            -> weasel#659 fixed marten#5529. Close it; do NOT apply the"
echo "                             TryAttainLock timeout change, which was never shown to be needed."
echo "  pass 2 still ~5 failures-> the strand survives, or there is a second cause. Re-check which"
echo "                             connection holds advisory lock 4004 (state=idle) before changing"
echo "                             anything in Marten."
say_it "Weasel $VERSION upgrade verified. Check the 5529 result."
