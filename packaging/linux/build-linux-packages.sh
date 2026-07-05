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
# package manager, and the in-app install flow should detect and defer to them (see ReleasePlan.md).
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
icon_source="${repo_root}/SubZeroFramework/Assets/Icons/icon.svg"
desktop_source="${script_dir}/subzeroframework.desktop"

# Package-path variant of the repo unit (the repo copy targets the in-app /usr/local install flow).
packaged_unit="${work_dir}/subzeroframework.service"
sed \
  -e 's|ExecStart=/usr/local/bin/SubZeroFramework.Service|ExecStart=/usr/lib/subzeroframework/service/SubZeroFramework.Service|' \
  -e 's|WorkingDirectory=/usr/local/lib/subzeroframework|WorkingDirectory=/usr/lib/subzeroframework/service|' \
  "${repo_root}/SubZeroFramework.Service/subzeroframework.service" > "${packaged_unit}"

stage_ui_payload() { # $1 = destination root
  mkdir -p "$1/usr/lib/subzeroframework/ui" "$1/usr/bin" \
           "$1/usr/share/applications" "$1/usr/share/icons/hicolor/scalable/apps"
  # The UI publish dir doubles as the artifact root; keep the service bundle out of the UI payload.
  (cd "${ui_publish_dir}" && find . -path ./service-package -prune -o -type f -print | while read -r f; do
      mkdir -p "$1/usr/lib/subzeroframework/ui/$(dirname "${f}")"
      cp "${f}" "$1/usr/lib/subzeroframework/ui/${f}"
    done)
  chmod +x "$1/usr/lib/subzeroframework/ui/SubZeroFramework"
  ln -sf /usr/lib/subzeroframework/ui/SubZeroFramework "$1/usr/bin/subzeroframework"
  cp "${desktop_source}" "$1/usr/share/applications/subzeroframework.desktop"
  cp "${icon_source}" "$1/usr/share/icons/hicolor/scalable/apps/subzeroframework.svg"
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

  cat > "${root}/DEBIAN/control" <<EOF
Package: ${name}
Version: ${version}
Architecture: ${deb_arch}
Maintainer: ${maintainer}
Installed-Size: ${installed_size}
Section: utils
Priority: optional
Homepage: ${homepage}
Description: ${summary}
 Self-contained .NET build; no external runtime dependencies are declared.
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
    cat > "${root}/DEBIAN/prerm" <<'EOF'
#!/bin/sh
set -e
if [ -d /run/systemd/system ]; then
    systemctl disable --now subzeroframework.service || true
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
AutoReqProv: no

%description
${summary}. Self-contained .NET build; no external runtime dependencies are declared.

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
cp "${icon_source}" "${tar_root}/subzeroframework.svg"
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
depends=('glibc')
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
    install -Dm644 subzeroframework.svg "\${pkgdir}/usr/share/icons/hicolor/scalable/apps/subzeroframework.svg"
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
	options = !strip
	options = staticlibs
	source = ${tar_name}
	sha256sums = ${tar_sha256}

pkgname = subzeroframework-bin
EOF

echo "Packages written to ${output_dir}:"
ls -l "${output_dir}" "${output_dir}/aur"
