#!/bin/bash
set -euo pipefail

rev="${DOGWOOD_PINNED_REV}"
cloneDir="r5r3-work/dogwood-pinned-source"

if [ -d "$cloneDir" ]; then rm -rf "$cloneDir"; fi
mkdir -p "$cloneDir"
git clone https://github.com/dogwood-policy/dogwood.git "$cloneDir"
if [ $? -ne 0 ]; then echo "Failed to clone Dogwood repo"; exit 1; fi

git -C "$cloneDir" checkout "$rev"
if [ $? -ne 0 ]; then echo "Failed to checkout Dogwood revision $rev"; exit 1; fi

actual=$(git -C "$cloneDir" rev-parse HEAD | tr -d '\n')
if [ "$actual" != "$rev" ]; then echo "Dogwood revision mismatch: expected $rev, got $actual"; exit 1; fi

echo "Dogwood cloned and checked out at $rev"
echo "path=$cloneDir" >> $GITHUB_OUTPUT