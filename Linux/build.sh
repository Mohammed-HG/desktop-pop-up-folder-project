#!/bin/sh
set -eu
root=$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd)
output="$root/artifacts"
version=1.4
archive="$output/desktop-popup-folder-linux-$version.jar"
format=all
reuse=0
for option in "$@"; do
    case "$option" in
        --reuse-jar) reuse=1 ;;
        --deb) format=deb ;;
        --rpm) format=rpm ;;
        --portable) format=portable ;;
        --jar) format=jar ;;
        *) printf 'Usage: %s [--jar|--deb|--rpm|--portable] [--reuse-jar]\n' "$0" >&2; exit 2 ;;
    esac
done

mkdir -p "$output"
work=$(mktemp -d)
trap 'rm -rf -- "$work"' EXIT
if [ "$reuse" -eq 0 ]; then
    mkdir "$work/classes"
    javac --release 17 -encoding UTF-8 -d "$work/classes" "$root"/src/popup/*.java
    jar --create --file "$archive" --main-class popup.App -C "$work/classes" .
fi
test -f "$archive"
java -Djava.awt.headless=true -jar "$archive" --self-test

if [ "$format" = all ] || [ "$format" = deb ]; then
    package="$work/deb"
    install -d "$package/DEBIAN"
    install -Dm644 "$archive" "$package/usr/share/desktop-popup-folder/app.jar"
    install -Dm644 "$root/desktop-popup-folder.desktop" "$package/usr/share/applications/desktop-popup-folder.desktop"
    install -Dm644 "$root/desktop-popup-folder.svg" "$package/usr/share/icons/hicolor/scalable/apps/desktop-popup-folder.svg"
    install -Dm755 "$root/packaging/installed-launcher" "$package/usr/bin/desktop-popup-folder"
    cat > "$package/DEBIAN/control" <<EOF
Package: desktop-popup-folder
Version: $version
Section: utils
Priority: optional
Architecture: all
Maintainer: Mohammed-HG
Depends: default-jre (>= 2:1.17) | java17-runtime, xdg-utils, xdg-user-dirs, libglib2.0-bin, librsvg2-bin
Description: Pop-up folders for the Linux desktop
 Open a folder from its desktop launcher, arrange application shortcuts,
 and move files into and out of the folder.
EOF
    dpkg-deb --build --root-owner-group "$package" "$output/desktop-popup-folder_${version}_all.deb"
fi

if [ "$format" = all ] || [ "$format" = rpm ]; then
    rpmdir="$work/rpm"
    mkdir -p "$rpmdir/SOURCES"
    cp "$archive" "$root/desktop-popup-folder.desktop" "$root/desktop-popup-folder.svg" \
        "$root/packaging/installed-launcher" "$rpmdir/SOURCES/"
    rpmbuild -bb --define "_topdir $rpmdir" "$root/packaging/desktop-popup-folder.spec"
    cp "$rpmdir/RPMS/noarch/desktop-popup-folder-$version-1.noarch.rpm" "$output/"
fi

if [ "$format" = all ] || [ "$format" = portable ]; then
    name="desktop-popup-folder-linux-$version"
    mkdir "$work/$name"
    install -m 644 "$archive" "$root/README.md" "$work/$name/"
    install -m 755 "$root/packaging/portable-launcher" "$work/$name/run.sh"
    tar -C "$work" -czf "$output/$name-portable.tar.gz" "$name"
fi
