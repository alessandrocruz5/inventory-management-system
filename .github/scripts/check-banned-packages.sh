#!/usr/bin/env bash
# Fails when a .NET project (direct or transitive) references MediatR, AutoMapper, MassTransit or
# FluentAssertions 8+, or when a tracked npm lockfile contains PrimeNG.
# Rule: CLAUDE.md "Banned packages"; spec section 11 (CI) and section 13.
# Usage: check-banned-packages.sh [--dotnet-json FILE] [LOCKFILE ...]   (the arguments exist for fixtures)
set -euo pipefail

abs() {
  case "$1" in
    /*) printf '%s\n' "$1" ;;
    *) printf '%s/%s\n' "$PWD" "$1" ;;
  esac
}

dotnet_json=""
lockfiles=()
while [ $# -gt 0 ]; do
  case "$1" in
    --dotnet-json)
      [ $# -ge 2 ] || { echo "--dotnet-json needs a file" >&2; exit 2; }
      dotnet_json="$(abs "$2")"
      shift 2
      ;;
    *)
      lockfiles+=("$(abs "$1")")
      shift
      ;;
  esac
done

root="$(git rev-parse --show-toplevel)"
cd "$root"

tmp="$(mktemp)"
trap 'rm -f "$tmp"' EXIT

if [ -z "$dotnet_json" ]; then
  dotnet list InventorySystem.slnx package --include-transitive --format json > "$tmp"
  dotnet_json="$tmp"
fi

if [ ${#lockfiles[@]} -eq 0 ]; then
  while IFS= read -r f; do
    [ -n "$f" ] && lockfiles+=("$root/$f")
  done < <(git ls-files '*package-lock.json')
fi

unrestored="$(jq -r '.projects[] | select((.frameworks // []) | length == 0) | .path' "$dotnet_json")"
if [ -n "$unrestored" ]; then
  echo "Not restored (run dotnet restore or dotnet build first):" >&2
  echo "$unrestored" >&2
  exit 2
fi

offenders="$(jq -r '
  .projects[] | .path as $p | .frameworks[]
  | ((.topLevelPackages // []) | map({id, v: .resolvedVersion}))
    + ((.transitivePackages // []) | map({id, v: .resolvedVersion}))
  | .[]
  | select(
      (.id | test("^(MediatR|AutoMapper|MassTransit)($|\\.)"; "i"))
      or ((.id | ascii_downcase) == "fluentassertions"
          and ((.v | split("-")[0] | split(".")[0] | tonumber? // 0) >= 8))
    )
  | "BANNED  \($p)  \(.id)  \(.v)"
' "$dotnet_json" | sort -u)"

npm_offenders=""
for lock in ${lockfiles[@]+"${lockfiles[@]}"}; do
  if ! jq -e '.packages | type == "object"' "$lock" > /dev/null 2>&1; then
    echo "Lockfile has no packages object (lockfile v1?): $lock" >&2
    exit 2
  fi
  hits="$(jq -r --arg l "${lock#"$root"/}" '
    .packages | to_entries[]
    | select(.key | test("(^|/)node_modules/(primeng|@primeng/[^/]+)$"))
    | "BANNED  \($l)  \(.key | sub("^.*node_modules/"; ""))  \(.value.version // "?")"
  ' "$lock")"
  if [ -n "$hits" ]; then
    npm_offenders="${npm_offenders:+$npm_offenders$'\n'}$hits"
  fi
done

all="$(printf '%s\n%s\n' "$offenders" "$npm_offenders" | sed '/^$/d')"
if [ -n "$all" ]; then
  echo "$all"
  exit 1
fi

projects="$(jq '.projects | length' "$dotnet_json")"
echo "No banned packages ($projects .NET projects, ${#lockfiles[@]} lockfiles)."
