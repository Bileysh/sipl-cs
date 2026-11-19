#!/usr/bin/env bash
set -euo pipefail
awk -v v="## [$1]" 'index($0, v) == 1 { on = 1; next } on && /^## \[/ { exit } on' CHANGELOG.md
