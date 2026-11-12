#!/bin/sh
set -eu
root=$(CDPATH='' cd -- "$(dirname -- "$0")/.." && pwd)
output="$root/artifacts/linux"
archive="$output/desktop-popup-folder-linux-1.2.jar"
mkdir -p "$output"

case "${1:-}" in
    --deb-only) test -f "$archive" ;;
    "")
        work=$(mktemp -d)
        trap 'rm -rf -- "$work"' EXIT
        mkdir "$work/classes"
        javac --release 17 -encoding UTF-8 -d "$work/classes" "$root"/linux/src/popup/*.java
        jar --create --file "$archive" --main-class popup.App -C "$work/classes" .
        java -Djava.awt.headless=true -jar "$archive" --self-test
        ;;
    *) printf 'Usage: %s [--deb-only]\n' "$0" >&2; exit 2 ;;
esac

package=$(mktemp -d)
trap 'rm -rf -- "$package" "${work:-}"' EXIT
install -d "$package/DEBIAN" "$package/usr/bin" "$package/usr/share/desktop-popup-folder" \
    "$package/usr/share/applications" "$package/usr/share/icons/hicolor/scalable/apps"
install -m 644 "$archive" "$package/usr/share/desktop-popup-folder/app.jar"
install -m 644 "$root/linux/desktop-popup-folder.desktop" "$package/usr/share/applications/"
install -m 644 "$root/linux/desktop-popup-folder.svg" "$package/usr/share/icons/hicolor/scalable/apps/"
cat > "$package/usr/bin/desktop-popup-folder" <<'EOF'
#!/bin/sh
exec java -jar /usr/share/desktop-popup-folder/app.jar "$@"
EOF
chmod 755 "$package/usr/bin/desktop-popup-folder"
cat > "$package/DEBIAN/control" <<'EOF'
Package: desktop-popup-folder
Version: 1.2
Section: utils
Priority: optional
Architecture: all
Maintainer: Mohammed-HG
Depends: default-jre (>= 2:1.17) | java17-runtime, xdg-utils, xdg-user-dirs, libglib2.0-bin
Description: Pop-up folders for the Linux desktop
 Open a folder from its desktop launcher, arrange application shortcuts,
 and move files into and out of the folder.
EOF
dpkg-deb --build --root-owner-group "$package" "$output/desktop-popup-folder_1.2_all.deb"
