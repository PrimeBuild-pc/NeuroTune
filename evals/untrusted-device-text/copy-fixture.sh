#!/usr/bin/env bash
# add_dirs is not attached on native Windows runs, so copy the synthetic fixture into the workspace.
set -euo pipefail
cp "$(dirname "$0")/fixtures/evidence.json" ./evidence.json
