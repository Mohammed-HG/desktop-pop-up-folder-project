Name:           desktop-popup-folder
Version:        1.2
Release:        1
Summary:        Pop-up folders for the Linux desktop
License:        LicenseRef-Unspecified
BuildArch:      noarch
Requires:       java >= 17
Requires:       xdg-utils
Requires:       xdg-user-dirs
Requires:       glib2

Source0:        desktop-popup-folder-linux-1.2.jar
Source1:        desktop-popup-folder.desktop
Source2:        desktop-popup-folder.svg
Source3:        installed-launcher

%description
Open a folder from its desktop launcher, arrange application shortcuts,
and move files into and out of the folder.

%install
install -Dm644 %{SOURCE0} %{buildroot}%{_datadir}/desktop-popup-folder/app.jar
install -Dm644 %{SOURCE1} %{buildroot}%{_datadir}/applications/desktop-popup-folder.desktop
install -Dm644 %{SOURCE2} %{buildroot}%{_datadir}/icons/hicolor/scalable/apps/desktop-popup-folder.svg
install -Dm755 %{SOURCE3} %{buildroot}%{_bindir}/desktop-popup-folder

%files
%{_bindir}/desktop-popup-folder
%{_datadir}/desktop-popup-folder/
%{_datadir}/applications/desktop-popup-folder.desktop
%{_datadir}/icons/hicolor/scalable/apps/desktop-popup-folder.svg
