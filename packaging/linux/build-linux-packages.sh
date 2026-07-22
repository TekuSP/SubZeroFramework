#!/usr/bin/env bash
# Builds every Linux distribution artifact from the already-published UI and service outputs:
#   - subzeroframework_<ver>_<deb-arch>.deb           (UI + .desktop + icon)
#   - subzeroframework-service_<ver>_<deb-arch>.deb   (service + systemd unit, enabled on install)
#   - subzeroframework-<ver>-1.<rpm-arch>.rpm         (UI)
#   - subzeroframework-service-<ver>-1.<rpm-arch>.rpm (service + systemd unit)
#   - subzeroframework-<ver>-<rid>.tar.gz             (combined tarball; the AUR PKGBUILD source)
#   - aur/PKGBUILD + aur/.SRCINFO + aur/*.install     (Arch/AUR format; makepkg-ready with the local tarball)
#
# Install layout (packages): /usr/lib/subzeroframework/{ui,service}, /usr/bin/subzeroframework symlink,
# unit at /usr/lib/systemd/system/subzeroframework.service. This differs from the app's own
# --service-management install (/usr/local/...) on purpose: package-managed installs are owned by the
# package manager, and the in-app install flow should detect and defer to them (see docs/ReleasePlan.md).
#
# Usage: build-linux-packages.sh <version> <rid> <ui-publish-dir> <service-publish-dir> <output-dir>
#   <rid> is linux-x64 or linux-arm64. Runs natively on the matching runner (no cross-packaging).
set -euo pipefail

version="${1:?version required (e.g. 0.1.123)}"
rid="${2:?rid required (linux-x64|linux-arm64)}"
ui_publish_dir="${3:?ui publish dir required}"
service_publish_dir="${4:?service publish dir required}"
output_dir="${5:?output dir required}"

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "${script_dir}/../.." && pwd)"

case "${rid}" in
  linux-x64)   deb_arch="amd64"; pkg_arch="x86_64" ;;
  linux-arm64) deb_arch="arm64"; pkg_arch="aarch64" ;;
  *) echo "Unsupported rid: ${rid}" >&2; exit 1 ;;
esac

maintainer="TekuSP <richard.torhan@gmail.com>"
homepage="https://github.com/TekuSP/SubZeroFramework"
ui_summary="Fan control and telemetry UI for Framework computers"
service_summary="Background fan-control and telemetry service for Framework computers"

work_dir="$(mktemp -d)"
trap 'rm -rf "${work_dir}"' EXIT
mkdir -p "${output_dir}" "${output_dir}/aur"

# ----------------------------------------------------------------------------------------------------
# Shared payload staging
# ----------------------------------------------------------------------------------------------------
desktop_source="${script_dir}/subzeroframework.desktop"

# Desktop icon. NOTE: SubZeroFramework/Assets/Icons/icon.svg is NOT the app icon — it is only the
# BACKGROUND layer of it (a bare <rect fill="#5b5b5c">, zero paths). Uno.Resizetizer composes it with
# icon_foreground.svg at build time. Shipping icon.svg directly gave Linux users a plain grey square,
# which reads as "no icon" on a dark desktop (reported on Framework 13 / Hyprland).
# The composed results are already in the UI publish output as iconLogo.targetsize-<N>.png, at exactly
# the pixel sizes the hicolor theme wants — so install those instead. There is no composed SVG to ship;
# fixed-size PNGs are the normal and correct way to populate an icon theme.
icon_sizes=(16 24 32 48 256)
icon_publish_dir="${ui_publish_dir}/Assets/Icons"

install_desktop_icons() { # $1 = destination root
  local installed=0 size src
  for size in "${icon_sizes[@]}"; do
    src="${icon_publish_dir}/iconLogo.targetsize-${size}.png"
    if [[ -f "${src}" ]]; then
      install -Dm644 "${src}" "$1/usr/share/icons/hicolor/${size}x${size}/apps/subzeroframework.png"
      installed=$((installed + 1))
    fi
  done

  # Fail loudly rather than silently shipping an iconless desktop entry again.
  if [[ "${installed}" -eq 0 ]]; then
    echo "No composed icons found under ${icon_publish_dir} (expected iconLogo.targetsize-<size>.png)." >&2
    exit 1
  fi
}

# Package-path variant of the repo unit (the repo copy targets the in-app /usr/local install flow).
packaged_unit="${work_dir}/subzeroframework.service"
sed \
  -e 's|ExecStart=/usr/local/bin/SubZeroFramework.Service|ExecStart=/usr/lib/subzeroframework/service/SubZeroFramework.Service|' \
  -e 's|WorkingDirectory=/usr/local/lib/subzeroframework|WorkingDirectory=/usr/lib/subzeroframework/service|' \
  "${repo_root}/SubZeroFramework.Service/subzeroframework.service" > "${packaged_unit}"

stage_ui_payload() { # $1 = destination root
  mkdir -p "$1/usr/lib/subzeroframework/ui" "$1/usr/bin" "$1/usr/share/applications"
  # The UI publish dir doubles as the artifact root; keep the service bundle out of the UI payload.
  (cd "${ui_publish_dir}" && find . -path ./service-package -prune -o -type f -print | while read -r f; do
      mkdir -p "$1/usr/lib/subzeroframework/ui/$(dirname "${f}")"
      cp "${f}" "$1/usr/lib/subzeroframework/ui/${f}"
    done)
  chmod +x "$1/usr/lib/subzeroframework/ui/SubZeroFramework"
  ln -sf /usr/lib/subzeroframework/ui/SubZeroFramework "$1/usr/bin/subzeroframework"
  cp "${desktop_source}" "$1/usr/share/applications/subzeroframework.desktop"
  install_desktop_icons "$1"
}

stage_service_payload() { # $1 = destination root
  mkdir -p "$1/usr/lib/subzeroframework/service" "$1/usr/lib/systemd/system"
  (cd "${service_publish_dir}" && find . -type f ! -name 'subzeroframework.service' -print | while read -r f; do
      mkdir -p "$1/usr/lib/subzeroframework/service/$(dirname "${f}")"
      cp "${f}" "$1/usr/lib/subzeroframework/service/${f}"
    done)
  chmod +x "$1/usr/lib/subzeroframework/service/SubZeroFramework.Service"
  cp "${packaged_unit}" "$1/usr/lib/systemd/system/subzeroframework.service"
}

# ----------------------------------------------------------------------------------------------------
# .deb (dpkg-deb, native on the ubuntu runners)
# ----------------------------------------------------------------------------------------------------
build_deb() { # $1 = package name, $2 = summary, $3 = stage function, $4 = with_service_scripts
  local name="$1" summary="$2" stager="$3" with_service="$4"
  local root="${work_dir}/deb-${name}"
  mkdir -p "${root}/DEBIAN"
  "${stager}" "${root}"

  local installed_size
  installed_size="$(du -ks "${root}" --exclude=DEBIAN | cut -f1)"

  # Dependencies. The .NET publish is SELF-CONTAINED, so there is deliberately no dotnet-runtime
  # dependency — but "self-contained" only covers managed code. The native libraries below are
  # genuinely required:
  #   libudev1     FrameworkDotnet's libframework_lib_ffi.so links it directly (DT_NEEDED) for EC
  #                access. This — not sd_notify, which is managed — is why the service needs systemd.
  #   libicu       InvariantGlobalization is deliberately not set (quantities format through UnitsNet),
  #                and a self-contained publish does not bundle ICU. Alternatives are ORed so one
  #                package works across Debian/Ubuntu releases that ship different sonames.
  #   X11/GL/fontconfig  dlopen'd by the Uno Skia X11 head. fontconfig is declared on every arch even
  #                where it is lazily loaded: an undeclared dlopen dependency fails at first text
  #                render, which is far worse than failing at install.
  # libvulkan and dbus are Recommends, not Depends — Skia falls back to GL without Vulkan, and the
  # notification service degrades to log-only without a session bus.
  local depends recommends
  if [[ "${with_service}" == "yes" ]]; then
    depends="libc6, libgcc-s1, libudev1, libicu76 | libicu74 | libicu72 | libicu71 | libicu70 | libicu67"
    # Deliberately NOT depending on xrandr (x11-xserver-utils). Hardware.Info's display/GPU enumeration
    # shells out to it, but this service is a root systemd unit with no DISPLAY/WAYLAND_DISPLAY, so xrandr
    # answers "Can't open display" even when installed — verified. Those calls are now skipped on Linux
    # (see FrameworkDataProvider), so the package would be dead weight on every install.
    recommends=""
  else
    # The UI is a pure gRPC client with no local hardware fallback, so it is useless without the
    # service. Pinned to the exact version: both are built from one CI run against one gRPC contract,
    # so version skew is protocol skew.
    depends="subzeroframework-service (= ${version}), libc6, libgcc-s1, libfontconfig1, libx11-6, libxext6, libxfixes3, libxi6, libxrandr2, libgl1, libicu76 | libicu74 | libicu72 | libicu71 | libicu70 | libicu67"
    recommends="libvulkan1, fonts-dejavu-core, dbus-x11"
  fi

  cat > "${root}/DEBIAN/control" <<EOF
Package: ${name}
Version: ${version}
Architecture: ${deb_arch}
Maintainer: ${maintainer}
Installed-Size: ${installed_size}
Section: utils
Priority: optional
Homepage: ${homepage}
Depends: ${depends}
EOF

  if [[ -n "${recommends}" ]]; then
    echo "Recommends: ${recommends}" >> "${root}/DEBIAN/control"
  fi

  cat >> "${root}/DEBIAN/control" <<EOF
Description: ${summary}
 Self-contained .NET build: no .NET runtime is required, only the native libraries listed above.
EOF

  if [[ "${with_service}" == "yes" ]]; then
    cat > "${root}/DEBIAN/postinst" <<'EOF'
#!/bin/sh
set -e
if [ -d /run/systemd/system ]; then
    systemctl daemon-reload || true
    systemctl enable --now subzeroframework.service || true
fi
EOF
    # $1 is "remove" on an actual removal and "upgrade" when a newer version is being installed over
    # this one. Without the guard, every UPGRADE disabled the unit and silently discarded a user's
    # deliberate "disabled" choice — and postinst then re-enabled it. The RPM %preun below already
    # gets this right via [ $1 -eq 0 ]; this makes the two formats agree.
    cat > "${root}/DEBIAN/prerm" <<'EOF'
#!/bin/sh
set -e
if [ "$1" = "remove" ] || [ "$1" = "purge" ]; then
    if [ -d /run/systemd/system ]; then
        systemctl disable --now subzeroframework.service || true
    fi
fi
EOF
    cat > "${root}/DEBIAN/postrm" <<'EOF'
#!/bin/sh
set -e
if [ -d /run/systemd/system ]; then
    systemctl daemon-reload || true
fi
EOF
    chmod 755 "${root}/DEBIAN/postinst" "${root}/DEBIAN/prerm" "${root}/DEBIAN/postrm"
  fi

  dpkg-deb --build --root-owner-group "${root}" "${output_dir}/${name}_${version}_${deb_arch}.deb"
}

build_deb "subzeroframework" "${ui_summary}" stage_ui_payload no
build_deb "subzeroframework-service" "${service_summary}" stage_service_payload yes

# ----------------------------------------------------------------------------------------------------
# .rpm (rpmbuild repacking the prebuilt payload; native target on each runner)
# ----------------------------------------------------------------------------------------------------
build_rpm() { # $1 = package name, $2 = summary, $3 = stage function, $4 = with_service_scripts
  local name="$1" summary="$2" stager="$3" with_service="$4"
  local rpm_top="${work_dir}/rpm-${name}"
  local payload="${work_dir}/rpmroot-${name}"
  mkdir -p "${rpm_top}"/{SPECS,RPMS,BUILD,BUILDROOT,SOURCES,SRPMS} "${payload}"
  "${stager}" "${payload}"

  # Only what the automatic ELF scanner cannot infer. systemd-libs provides libudev on Fedora/RHEL;
  # the scanner does find it via DT_NEEDED, but naming it documents the requirement explicitly.
  local rpm_requires
  if [[ "${with_service}" == "yes" ]]; then
    rpm_requires="Requires: systemd-libs"
  else
    # Same exact-version pin as the .deb: the UI cannot function without its service.
    rpm_requires=$(cat <<EOF
Requires: ${name}-service = ${version}-1
Requires: fontconfig
Requires: libX11
Requires: libXext
Requires: libXfixes
Requires: libXi
Requires: libXrandr
Requires: libglvnd-glx
Recommends: vulkan-loader
Recommends: dbus
EOF
)
  fi

  local scriptlets=""
  if [[ "${with_service}" == "yes" ]]; then
    scriptlets=$(cat <<'EOF'
%post
if [ -d /run/systemd/system ]; then
    systemctl daemon-reload || true
    systemctl enable --now subzeroframework.service || true
fi

%preun
if [ $1 -eq 0 ] && [ -d /run/systemd/system ]; then
    systemctl disable --now subzeroframework.service || true
fi

%postun
if [ -d /run/systemd/system ]; then
    systemctl daemon-reload || true
fi
EOF
)
  fi

  cat > "${rpm_top}/SPECS/${name}.spec" <<EOF
%define __strip /bin/true
%define debug_package %{nil}
%global __os_install_post %{nil}
%global _build_id_links none

Name: ${name}
Version: ${version}
Release: 1
Summary: ${summary}
License: MIT
URL: ${homepage}
# AutoReqProv is deliberately LEFT ON. It was previously "no", which suppressed rpm's ELF scanner —
# the scanner finds libudev (linked by FrameworkDotnet's EC FFI), fontconfig, libicu and the X11 stack
# automatically and correctly, and keeps doing so as dependencies change. A hand-maintained list would
# only drift. Requires below cover what the scanner CANNOT see: the cross-package relationship, and
# libraries reached via dlopen rather than DT_NEEDED.
${rpm_requires}

%description
${summary}. Self-contained .NET build: no .NET runtime is required, only native system libraries.

%install
cp -a ${payload}/. %{buildroot}/

%files
/usr/*

${scriptlets}
EOF

  rpmbuild --define "_topdir ${rpm_top}" -bb "${rpm_top}/SPECS/${name}.spec"
  find "${rpm_top}/RPMS" -name '*.rpm' -exec cp {} "${output_dir}/" \;
}

build_rpm "subzeroframework" "${ui_summary}" stage_ui_payload no
build_rpm "subzeroframework-service" "${service_summary}" stage_service_payload yes

# ----------------------------------------------------------------------------------------------------
# Combined tarball — release artifact and the AUR PKGBUILD source
# ----------------------------------------------------------------------------------------------------
tar_name="subzeroframework-${version}-${rid}.tar.gz"
tar_root="${work_dir}/tar/subzeroframework-${version}"
mkdir -p "${tar_root}/ui" "${tar_root}/service"
(cd "${ui_publish_dir}" && find . -path ./service-package -prune -o -type f -print | while read -r f; do
    mkdir -p "${tar_root}/ui/$(dirname "${f}")"
    cp "${f}" "${tar_root}/ui/${f}"
  done)
(cd "${service_publish_dir}" && find . -type f ! -name 'subzeroframework.service' -print | while read -r f; do
    mkdir -p "${tar_root}/service/$(dirname "${f}")"
    cp "${f}" "${tar_root}/service/${f}"
  done)
cp "${packaged_unit}" "${tar_root}/subzeroframework.service"
cp "${desktop_source}" "${tar_root}/subzeroframework.desktop"
# Composed icons at the top of the tarball, so the PKGBUILD installs the same artwork the deb/rpm do
# (icon.svg alone is just the grey background layer — see install_desktop_icons above).
for size in "${icon_sizes[@]}"; do
  [[ -f "${icon_publish_dir}/iconLogo.targetsize-${size}.png" ]] \
    && cp "${icon_publish_dir}/iconLogo.targetsize-${size}.png" "${tar_root}/subzeroframework-${size}.png"
done
tar -C "${work_dir}/tar" -czf "${output_dir}/${tar_name}" "subzeroframework-${version}"

# ----------------------------------------------------------------------------------------------------
# Arch/AUR format: PKGBUILD + .SRCINFO + .install, makepkg-ready against the local tarball.
# Each CI leg emits a single-arch PKGBUILD; a real AUR submission would merge both arches and point
# source= at the GitHub release URL instead of a local file.
# ----------------------------------------------------------------------------------------------------
tar_sha256="$(sha256sum "${output_dir}/${tar_name}" | cut -d' ' -f1)"
cp "${output_dir}/${tar_name}" "${output_dir}/aur/"

cat > "${output_dir}/aur/subzeroframework.install" <<'EOF'
post_install() {
    echo "==> Start the SubZero background service with:"
    echo "      systemctl enable --now subzeroframework.service"
}

post_upgrade() {
    systemctl daemon-reload || true
}
EOF

cat > "${output_dir}/aur/PKGBUILD" <<EOF
# Maintainer: ${maintainer}
pkgname=subzeroframework-bin
pkgver=${version}
pkgrel=1
pkgdesc="${ui_summary} (UI + background service)"
arch=('${pkg_arch}')
url="${homepage}"
license=('MIT')
# Arch ships UI + service in one package, so this is the union of both dependency sets.
# systemd-libs provides libudev, which FrameworkDotnet's EC FFI links directly — that, not sd_notify
# (which is managed), is the real systemd dependency. No dotnet runtime: the publish is self-contained.
# No glibc version bound: CI builds on Ubuntu runners whose glibc is OLDER than Arch's, so the
# binaries are more portable than the build host, not less.
depends=('glibc' 'gcc-libs' 'systemd-libs' 'fontconfig' 'icu' 'libx11' 'libxext' 'libxfixes' 'libxi' 'libxrandr' 'libglvnd')
optdepends=('vulkan-icd-loader: GPU-accelerated rendering')
options=('!strip' 'staticlibs')
install=subzeroframework.install
source=("${tar_name}")
sha256sums=('${tar_sha256}')

package() {
    cd "subzeroframework-\${pkgver}"

    install -dm755 "\${pkgdir}/usr/lib/subzeroframework"
    cp -a ui "\${pkgdir}/usr/lib/subzeroframework/ui"
    cp -a service "\${pkgdir}/usr/lib/subzeroframework/service"
    chmod 755 "\${pkgdir}/usr/lib/subzeroframework/ui/SubZeroFramework" \\
              "\${pkgdir}/usr/lib/subzeroframework/service/SubZeroFramework.Service"

    install -dm755 "\${pkgdir}/usr/bin"
    ln -s /usr/lib/subzeroframework/ui/SubZeroFramework "\${pkgdir}/usr/bin/subzeroframework"

    install -Dm644 subzeroframework.service "\${pkgdir}/usr/lib/systemd/system/subzeroframework.service"
    install -Dm644 subzeroframework.desktop "\${pkgdir}/usr/share/applications/subzeroframework.desktop"
    for _size in ${icon_sizes[*]}; do
        [ -f "subzeroframework-\${_size}.png" ] && install -Dm644 "subzeroframework-\${_size}.png" \\
            "\${pkgdir}/usr/share/icons/hicolor/\${_size}x\${_size}/apps/subzeroframework.png"
    done
}
EOF

cat > "${output_dir}/aur/.SRCINFO" <<EOF
pkgbase = subzeroframework-bin
	pkgdesc = ${ui_summary} (UI + background service)
	pkgver = ${version}
	pkgrel = 1
	url = ${homepage}
	install = subzeroframework.install
	arch = ${pkg_arch}
	license = MIT
	depends = glibc
	depends = gcc-libs
	depends = systemd-libs
	depends = fontconfig
	depends = icu
	depends = libx11
	depends = libxext
	depends = libxfixes
	depends = libxi
	depends = libxrandr
	depends = libglvnd
	optdepends = vulkan-icd-loader: GPU-accelerated rendering
	options = !strip
	options = staticlibs
	source = ${tar_name}
	sha256sums = ${tar_sha256}

pkgname = subzeroframework-bin
EOF

echo "Packages written to ${output_dir}:"
ls -l "${output_dir}" "${output_dir}/aur"
