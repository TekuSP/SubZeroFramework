#!/usr/bin/env bash
set -euo pipefail

runtime_id="${1:-linux-x64}"
output_root="${2:-$(pwd)/artifacts}"
configuration="${3:-Release}"
# Product version to stamp. Leave empty to fall back to <Version> in Directory.Build.props.
# CI must pass the SAME value it stamps on the UI and the packages — this script is the ONLY place the
# service is ever built, so a mismatch ships one product at two versions.
version="${4:-}"

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
service_root="$(cd "${script_dir}/.." && pwd)"
output_dir="${output_root}/service-package/linux"

mkdir -p "${output_dir}"

# Append the version properties only when a value was supplied. A bare `-p:Version=` is an MSBuild
# GLOBAL property: it overrides Directory.Build.props and stamps an empty/invalid version, which is
# strictly worse than the honest fallback.
version_args=()
if [[ -n "${version}" ]]; then
  version_args=("-p:Version=${version}" "-p:InformationalVersion=${version}")
fi

# Optional: build into a separate obj/bin tree (set by build-release-linux.sh when running from WSL, so a
# Linux build never reuses Windows-generated obj/ — see that script for why). Unset in CI, where the
# checkout is Linux-only and the default in-repo obj/ is correct.
artifacts_args=()
if [[ -n "${ARTIFACTS_PATH:-}" ]]; then
  artifacts_args=("-p:ArtifactsPath=${ARTIFACTS_PATH}")
fi

dotnet publish "${service_root}/SubZeroFramework.Service.csproj" \
  -c "${configuration}" \
  -r "${runtime_id}" \
  --self-contained true \
  -o "${output_dir}" \
  "${artifacts_args[@]+"${artifacts_args[@]}"}" \
  "${version_args[@]+"${version_args[@]}"}" \
  /property:GenerateFullPaths=true \
  /v:minimal \
  /consoleloggerparameters:NoSummary

cp "${service_root}/subzeroframework.service" "${output_dir}/subzeroframework.service"
