# Desktop Pop-up folder · Linux 1.2

[صفحة تحميل حزم لينكس](https://github.com/Mohammed-HG/desktop-pop-up-folder-project/releases/tag/linux-v1.2)

| التوزيعة أو طريقة التشغيل | الحزمة | الأمر |
| --- | --- | --- |
| Debian وUbuntu والتوزيعات المبنية عليهما | `desktop-popup-folder_1.2_all.deb` | `sudo apt install ./desktop-popup-folder_1.2_all.deb` |
| Fedora والتوزيعات المتوافقة مع حزمها | `desktop-popup-folder-1.2-1.noarch.rpm` | `sudo dnf install ./desktop-popup-folder-1.2-1.noarch.rpm` |
| Arch أو openSUSE أو توزيعات أخرى | `desktop-popup-folder-linux-1.2-portable.tar.gz` | فك الضغط، ثم شغّل `./run.sh` من المجلد الناتج |
| تشغيل ملف واحد | `desktop-popup-folder-linux-1.2.jar` | `java -jar desktop-popup-folder-linux-1.2.jar` |

تحتوي الصيغ الأربع على نفس التطبيق. حزم التثبيت تضيفه لقائمة التطبيقات وتحل اعتماديات التوزيعة. الحزمة المحمولة وملف JAR يحتاجان Java 17 أو أحدث بدعم الواجهة الرسومية، و`xdg-open` للملفات، و`gio` لتشغيل اختصارات التطبيقات. اترك ملف JAR أو المجلد المحمول في مكان ثابت؛ استخدم **Repair shortcut** بعد نقله.

أنشئ مجلدًا عبر **New folder**، ثم افتح اختصاره من سطح المكتب. بعض الواجهات تحتاج تفعيل أيقونات سطح المكتب أو السماح بتشغيل الاختصار. اسحب ملفًا أو اختصارًا إلى المجلد أو استخدم Ctrl+V لنقله إليه. اسحب ملفًا مخزنًا إلى سطح المكتب أو مستكشف الملفات لإخراجه إذا كان مدير الملفات يدعم النقل. **Add link** يضيف مرجعًا للملف من موقعه الحالي.

توجد البيانات في `$XDG_DATA_HOME/desktop-popup-folder` أو `~/.local/share/desktop-popup-folder` افتراضيًا. نسخة لينكس تستخدم Java Swing بالإنجليزية، مع المجلدات والاختصارات والنقل.

## البناء

من جذر المستودع شغّل `sh Linux/build.sh` باستخدام JDK 17 أو أحدث وأدوات `dpkg-deb` و`rpmbuild` و`tar`. لبناء صيغة واحدة أضف `--jar` أو `--deb` أو `--rpm` أو `--portable`. أضف `--reuse-jar` لتغليف ملف JAR الموجود في `Linux/artifacts`. يجري اختبار التخزين والاختصارات والنقل قبل التغليف.
