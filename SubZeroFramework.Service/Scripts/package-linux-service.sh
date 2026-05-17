#!/usr/bin/env bash
set -euo pipefail

runtime_id="${1:-linux-x64}"
output_root="${2:-$(pwd)/artifacts}"
configuration="${3:-Release}"

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
service_root="$(cd "${script_dir}/.." && pwd)"
output_dir="${output_root}/service-package/linux"

mkdir -p "${output_dir}"

dotnet publish "${service_root}/SubZeroFramework.Service.csproj" \
  -c "${configuration}" \
  -r "${runtime_id}" \
  --self-contained true \
  -o "${output_dir}" \
  /property:GenerateFullPaths=true \
  /v:minimal \
  /consoleloggerparameters:NoSummary

cp "${service_root}/subzeroframework.service" "${output_dir}/subzeroframework.service"
