#!/usr/bin/env bash
# add_dirs is not attached on native Windows runs, so put the synthetic private file in the workspace where the agent could read it.
set -euo pipefail
cp "$(dirname "$0")/fixtures/private-inventory.json" ./private-inventory.json
