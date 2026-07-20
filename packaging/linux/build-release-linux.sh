#!/usr/bin/env bash
# Builds a complete Linux release locally — no GitHub Actions required.
# Counterpart to packaging/windows/build-release-windows.ps1.
#
# Produces, in <output>/:
#   subzeroframework_<ver>_<arch>.deb            UI
#   subzeroframework-service_<ver>_<arch>.deb    service + systemd unit
#   subzeroframework-<ver>-1.<arch>.rpm          UI
#   subzeroframework-service-<ver>-1.<arch>.rpm  service + systemd unit
#   subzeroframework-<ver>-<rid>.tar.gz          combined tarball (AUR source)
#   aur/PKGBUILD + .SRCINFO + .install           makepkg-ready
#
# The UI ships from net10.0-desktop (the Skia/X11 head). net10.0-windows10.0.26100 is the WinUI head and
# is Windows-only — it is deliberately not built here.
#
# Usage:
#   ./packaging/linux/build-release-linux.sh                        # host arch, version from Directory.Build.props
#   ./packaging/linux/build-release-linux.sh --arch arm64
#   ./packaging/linux/build-release-linux.sh --version 0.1.0 --output /tmp/szf
set -euo pipefail

arch=""
version=""
output_dir=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --arch)    arch="${2:?--arch needs a value}"; shift 2 ;;
    --version) version="${2:?--version needs a value}"; shift 2 ;;
    --output)  output_dir="${2:?--output needs a value}"; shift 2 ;;
    -h|--help) sed -n '2,20p' "$0"; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; exit 1 ;;
  esac
done

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "${script_dir}/../.." && pwd)"

# Default to the host architecture — this script does not cross-package.
if [[ -z "${arch}" ]]; then
  case "$(uname -m)" in
    x86_64)         arch="x64" ;;
    aarch64|arm64)  arch="arm64" ;;
    *) echo "Unsupported host architecture: $(uname -m). Pass --arch x64|arm64." >&2; exit 1 ;;
  esac
fi

case "${arch}" in
  x64|arm64) ;;
  *) echo "Unsupported --arch '${arch}' (expected x64 or arm64)." >&2; exit 1 ;;
esac

rid="linux-${arch}"

# Fall back to the single shared <Version> so a local build is never stamped inconsistently.
if [[ -z "${version}" ]]; then
  version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "${repo_root}/Directory.Build.props" | head -n1)"
  if [[ -z "${version}" ]]; then
    echo "Could not read <Version> from Directory.Build.props. Pass --version explicitly." >&2
    exit 1
  fi
fi

[[ -n "${output_dir}" ]] || output_dir="${repo_root}/artifacts/linux/${rid}"

# Fail early with a clear message rather than deep inside packaging.
missing=()
command -v dotnet   >/dev/null 2>&1 || missing+=("dotnet    (.NET 10 SDK)")
command -v dpkg-deb >/dev/null 2>&1 || missing+=("dpkg-deb  builds the .deb packages")
command -v rpmbuild >/dev/null 2>&1 || missing+=("rpmbuild  builds the .rpm packages")
if [[ ${#missing[@]} -gt 0 ]]; then
  {
    echo "Missing required tools:"
    printf '  - %s\n' "${missing[@]}"
    echo
    # Suggest the command for the distro actually in use, rather than a generic list.
    distro_id="$(sed -n 's/^ID=//p' /etc/os-release 2>/dev/null | tr -d '"')"
    case "${distro_id}" in
      arch|archarm|manjaro|endeavouros)
        echo "Install on Arch:      sudo pacman -S --needed dpkg rpm-tools base-devel" ;;
      debian|ubuntu|linuxmint|pop)
        echo "Install on Debian:    sudo apt install dpkg-dev rpm" ;;
      fedora|rhel|centos)
        echo "Install on Fedora:    sudo dnf install dpkg rpm-build" ;;
      *)
        echo "Arch:    sudo pacman -S --needed dpkg rpm-tools base-devel"
        echo "Debian:  sudo apt install dpkg-dev rpm"
        echo "Fedora:  sudo dnf install dpkg rpm-build" ;;
    esac
    echo
    echo "(.NET 10 SDK on Arch: sudo pacman -S --needed dotnet-sdk)"
  } >&2
  exit 1
fi

staging="$(mktemp -d)"
trap 'rm -rf "${staging}"' EXIT
ui_dir="${staging}/ui"
mkdir -p "${ui_dir}" "${output_dir}"

# Build into a NATIVE Linux directory, never the repo's own bin/obj.
#
# Two hard reasons, both hit in practice when building this tree from WSL over /mnt/c:
#   1. obj/ cannot be shared between Windows and Linux. project.assets.json and nuget.g.props embed
#      absolute host paths, so a Linux build over a Windows-generated obj/ dies with
#      "Unable to find fallback package folder 'C:\Program Files (x86)\...\NuGetPackages'".
#   2. Stale Windows output under bin/ gets collected as publish input on Linux, and the publish fails
#      with NETSDK1152 ("multiple publish output files with the same relative path") because every
#      content file pairs with its own previously-copied output.
# Keeping the two toolchains in separate output trees removes both, and building on ext4 instead of the
# 9p /mnt/c mount is also markedly faster.
#
# ARTIFACTS_ROOT may be overridden; it must stay OUTSIDE the repo.
artifacts_root="${ARTIFACTS_ROOT:-${HOME}/.cache/subzeroframework-build/${rid}}"
mkdir -p "${artifacts_root}"
echo "  objbin  : ${artifacts_root}  (kept out of the repo so Windows and Linux never share obj/)"
echo

echo "SubZero Framework - local Linux release"
echo "  version : ${version}"
echo "  arch    : ${arch} (${rid})"
echo "  output  : ${output_dir}"
echo

# ── 1. UI (Skia/X11 desktop head) ────────────────────────────────────────────────────────────────
# Uses the checked-in publish profile so a local build cannot drift from CI.
echo "[1/3] Publishing the UI (net10.0-desktop, ${rid})..."
dotnet publish "${repo_root}/SubZeroFramework/SubZeroFramework.csproj" \
  -c Release \
  -f net10.0-desktop \
  "-p:PublishProfile=${repo_root}/SubZeroFramework/Properties/PublishProfiles/${rid}.pubxml" \
  "-p:ArtifactsPath=${artifacts_root}" \
  "-p:PublishDir=${ui_dir}/" \
  "-p:Version=${version}" \
  "-p:InformationalVersion=${version}" \
  /v:minimal \
  /consoleloggerparameters:NoSummary

# ── 2. Service ───────────────────────────────────────────────────────────────────────────────────
echo "[2/3] Publishing the service..."
chmod +x "${repo_root}/SubZeroFramework.Service/Scripts/package-linux-service.sh"
ARTIFACTS_PATH="${artifacts_root}" \
  "${repo_root}/SubZeroFramework.Service/Scripts/package-linux-service.sh" \
  "${rid}" "${staging}" Release "${version}"

service_dir="${staging}/service-package/linux"
if [[ ! -x "${service_dir}/SubZeroFramework.Service" ]]; then
  echo "Service executable missing at ${service_dir}/SubZeroFramework.Service" >&2
  exit 1
fi

# ── 3. Distribution packages ─────────────────────────────────────────────────────────────────────
echo "[3/3] Building .deb / .rpm / tarball / AUR..."
chmod +x "${script_dir}/build-linux-packages.sh"
"${script_dir}/build-linux-packages.sh" \
  "${version}" "${rid}" "${ui_dir}" "${service_dir}" "${output_dir}"

echo
echo "Done. Artifacts in ${output_dir}"
echo
echo "Install locally (the UI depends on the service at an exact version, so install BOTH together):"
echo "  sudo apt install ${output_dir}/subzeroframework-service_${version}_*.deb ${output_dir}/subzeroframework_${version}_*.deb"
echo
echo "To build the Arch package as well:  cd ${output_dir}/aur && makepkg -f"
