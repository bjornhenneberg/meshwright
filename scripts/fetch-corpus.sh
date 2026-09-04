#!/usr/bin/env bash
# Fetch the real-world test corpus described in tests/corpus/manifest.tsv into
# tests/corpus/files/ (gitignored). Idempotent: a file whose checksum already
# matches the manifest is left alone, so re-running costs nothing.
#
# The corpus is deliberately not committed - see the header of manifest.tsv.
set -uo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
manifest="$repo_root/tests/corpus/manifest.tsv"
dest="$repo_root/tests/corpus/files"

if [[ ! -f "$manifest" ]]; then
    echo "manifest not found: $manifest" >&2
    exit 1
fi

mkdir -p "$dest"

fetched=0 cached=0 failed=0

while IFS=$'\t' read -r name sha256 url _rest; do
    # Skip comments and blank lines.
    [[ -z "${name:-}" || "$name" == \#* ]] && continue

    target="$dest/$name"

    if [[ -f "$target" ]] && echo "$sha256  $target" | sha256sum --check --status 2>/dev/null; then
        cached=$((cached + 1))
        continue
    fi

    printf 'fetching %s ... ' "$name"
    if ! curl -sSfL --retry 3 --retry-delay 2 -o "$target.part" "$url"; then
        echo "FAILED (download)"
        rm -f "$target.part"
        failed=$((failed + 1))
        continue
    fi

    if ! echo "$sha256  $target.part" | sha256sum --check --status 2>/dev/null; then
        echo "FAILED (checksum mismatch - upstream changed, or a truncated download)"
        rm -f "$target.part"
        failed=$((failed + 1))
        continue
    fi

    mv "$target.part" "$target"
    echo "ok"
    fetched=$((fetched + 1))
done < "$manifest"

echo
echo "corpus: $fetched fetched, $cached already present, $failed failed -> $dest"
[[ $failed -eq 0 ]]
