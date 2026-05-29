#!/bin/bash
set -eo pipefail

# This script verifies SC-003: adding a new bank requires zero edits to:
# - existing bank code (e.g. BAC)
# - the core models
# - API endpoint, mapping, contracts, and error handling
#
# Usage:
#   ./verify-additive-only.sh [BASE_REF]
#
# Arguments:
#   BASE_REF: The base branch/commit to compare against (defaults to origin/main, falls back to main)

BASE_REF=${1:-origin/main}

# Check if BASE_REF exists in git, fallback to main if it does not
if ! git rev-parse --verify "$BASE_REF" >/dev/null 2>&1; then
  if git rev-parse --verify "main" >/dev/null 2>&1; then
    echo "Warning: Base ref '$BASE_REF' not found. Falling back to 'main'."
    BASE_REF="main"
  else
    echo "Error: Neither '$BASE_REF' nor 'main' branch could be resolved in Git."
    exit 1
  fi
fi

echo "Verifying no modifications to core or BAC files against '$BASE_REF'..."

DIFF_FILES=$(git diff --name-only "$BASE_REF" -- \
  'src/CardStatement.Core/Banks/Bac/' \
  'src/CardStatement.Core/Models/' \
  'src/CardStatement.Core/Reconciliation/' \
  'src/CardStatement.Api/Endpoints/' \
  'src/CardStatement.Api/Mapping/' \
  'src/CardStatement.Api/Contracts/ExtractedStatementResponse.cs' \
  'src/CardStatement.Api/Contracts/ErrorCodes.cs' \
  'src/CardStatement.Api/ErrorHandling/')

if [ -n "$DIFF_FILES" ]; then
  echo "ERROR: The following files were modified, violating SC-003 (no edits to core or BAC files allowed):"
  echo "$DIFF_FILES"
  exit 1
else
  echo "SUCCESS: No core or BAC files were modified."
  exit 0
fi
