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

# Checksum computation, portably. GNU coreutils gives us sha256sum; stock macOS
# ships shasum instead and has no sha256sum at all; openssl is the last-resort
# fallback. Earlier versions of this script piped into `sha256sum --check` /
# `shasum -a 256 --check --status` and compared only the exit status - on the
# macos-latest CI runner that reported a checksum mismatch for every single
# file (including ones independently verified byte-for-byte against the
# manifest), for a reason that was never pinned down. Rather than depend on
# --check/--status flag behaviour being identical across GNU coreutils, BSD,
# and Perl shasum implementations, compute the digest ourselves and compare
# the hex strings directly in shell - simpler, and it tells us exactly what
# went wrong if it ever fails again.
#
# Kept POSIX/bash-3.2-safe throughout (macOS ships bash 3.2 as /bin/bash): no
# mapfile, no associative arrays, no ${var^^}.
if command -v sha256sum >/dev/null 2>&1; then
    compute_sha256() { sha256sum -- "$1" 2>/dev/null | awk '{print $1}'; }
    checksum_tool="sha256sum"
elif command -v shasum >/dev/null 2>&1; then
    compute_sha256() { shasum -a 256 -- "$1" 2>/dev/null | awk '{print $1}'; }
    checksum_tool="shasum -a 256"
elif command -v openssl >/dev/null 2>&1; then
    compute_sha256() { openssl dgst -sha256 -- "$1" 2>/dev/null | awk '{print $NF}'; }
    checksum_tool="openssl dgst -sha256"
else
    echo "error: none of sha256sum, shasum, or openssl found; cannot verify corpus checksums" >&2
    exit 1
fi

# verify_sha256 <expected> <file>: computes the file's digest with whichever
# tool was selected above, compares it against the expected hex string (case-
# insensitively), and on a definite result echoes which case it was to stderr
# so a failure is diagnosable from the log alone rather than lumped into one
# generic "checksum mismatch" message.
verify_sha256() {
    expected="$1"
    file="$2"
    actual="$(compute_sha256 "$file")"
    if [[ -z "$actual" ]]; then
        echo "  (could not compute a sha256 digest for $file using $checksum_tool)" >&2
        return 1
    fi
    expected_lc="$(printf '%s' "$expected" | tr '[:upper:]' '[:lower:]')"
    actual_lc="$(printf '%s' "$actual" | tr '[:upper:]' '[:lower:]')"
    if [[ "$actual_lc" != "$expected_lc" ]]; then
        echo "  (digest differs for $file: expected $expected_lc, got $actual_lc)" >&2
        return 1
    fi
    return 0
}

fetched=0 cached=0 failed=0

while IFS=$'\t' read -r name sha256 url _rest; do
    # Skip comments and blank lines.
    [[ -z "${name:-}" || "$name" == \#* ]] && continue

    target="$dest/$name"

    if [[ -f "$target" ]] && verify_sha256 "$sha256" "$target" 2>/dev/null; then
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

    if ! verify_sha256 "$sha256" "$target.part"; then
        echo "FAILED (checksum verification failed - see reason above; upstream may have changed, or the download was truncated)"
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
