package popup;

import javax.swing.*;
import java.awt.*;
import java.awt.datatransfer.*;
import java.awt.event.*;
import java.awt.image.BufferedImage;
import java.io.*;
import java.nio.file.*;
import java.util.*;
import java.util.List;

public final class App {
    static final String NAME = "Desktop Pop-up folder";
    static final String VERSION = "1.4";
    static final Color BACKGROUND = new Color(31, 36, 48);
    private final Store store;
    private boolean arabic;
    private String theme;

    private App(Store store) throws IOException {
        this.store = store;
        var preferences = store.settings();
        arabic = preferences.getProperty("language", "en").equals("ar");
        theme = preferences.getProperty("theme", "System");
    }

    private String l(String en, String ar) { return arabic ? ar : en; }
    private boolean dark(String mode) {
        var nativeColor = UIManager.getColor("Panel.background");
        return mode.equals("Dark") || (mode.equals("System") && nativeColor != null
            && nativeColor.getRed() + nativeColor.getGreen() + nativeColor.getBlue() < 384);
    }
    private void style(Component component) {
        var isDark = dark(theme);
        component.setForeground(isDark ? new Color(244, 245, 248) : new Color(27, 32, 42));
        component.setBackground(isDark ? new Color(38, 42, 53) : new Color(247, 248, 251));
        if (component instanceof Container container)
            for (var child : container.getComponents()) style(child);
    }
    private void finish(Container container) {
        style(container);
        container.applyComponentOrientation(arabic ? ComponentOrientation.RIGHT_TO_LEFT : ComponentOrientation.LEFT_TO_RIGHT);
    }

    public static void main(String[] args) throws Exception {
        if (Arrays.equals(args, new String[]{"--self-test"})) { selfTest(); return; }
        if (!System.getProperty("os.name").equals("Linux")) {
            System.err.println(NAME + " " + VERSION + " requires a Linux desktop session.");
            return;
        }
        if (GraphicsEnvironment.isHeadless()) {
            System.err.println("No graphical Linux desktop session was detected.");
            System.exit(1);
        }
        UIManager.setLookAndFeel(UIManager.getSystemLookAndFeelClassName());
        ToolTipManager.sharedInstance().setLightWeightPopupEnabled(false);
        var jar = Path.of(App.class.getProtectionDomain().getCodeSource().getLocation().toURI()).toAbsolutePath();
        var java = Path.of(System.getProperty("java.home"), "bin", "java").toString();
        var home = Path.of(System.getProperty("user.home"));
        var xdgData = System.getenv("XDG_DATA_HOME");
        var data = xdgData == null || xdgData.isBlank() || !Path.of(xdgData).isAbsolute()
            ? home.resolve(".local/share") : Path.of(xdgData);
        if (Arrays.equals(args, new String[]{"--smoke-ui"})) {
            var fixture = Files.createTempDirectory("popup-linux-ui-");
            var store = new Store(fixture.resolve("data"), fixture.resolve("desktop"), "desktop-popup-folder");
            var folder = store.create("UI test");
            var icon = fixture.resolve("test.png");
            javax.imageio.ImageIO.write(folderIcon(128), "png", icon.toFile());
            for (int i = 0; i < 9; i++) {
                var item = fixture.resolve("Test " + i + ".desktop");
                Files.writeString(item, "[Desktop Entry]\nType=Application\nName=Test\nExec=true\nIcon=" + icon + "\n");
                folder.files().add(item);
            }
            var testApp = new App(store);
            SwingUtilities.invokeLater(() -> {
                var popup = testApp.new Popup(folder);
                popup.show();
                var firstTile = (JButton)popup.grid.getComponent(0);
                boolean[] activated = {false};
                for (var listener : firstTile.getActionListeners()) firstTile.removeActionListener(listener);
                firstTile.addActionListener(e -> activated[0] = true);
                popup.frame.setSize(740, 440);
                var stop = new javax.swing.Timer(650, event -> {
                    firstTile.doClick(0);
                    boolean passed = popup.frame.isShowing() && activated[0]
                        && popup.grid.getComponent(0) == firstTile && firstTile.getWidth() > 0
                        && firstTile.getIcon() != null && firstTile.getIcon().getIconWidth() == 48;
                    popup.frame.dispose();
                    try { deleteFixture(fixture); } catch (IOException e) { throw new UncheckedIOException(e); }
                    if (!passed) System.exit(1);
                    System.out.println("Linux popup rendered icons and retained clickable tiles after resize");
                    System.exit(0);
                });
                stop.setRepeats(false);
                stop.start();
            });
            return;
        }
        var launcher = jar.equals(Path.of("/usr/share/desktop-popup-folder/app.jar")) ? "desktop-popup-folder"
            : Store.argument(java) + " -jar " + Store.argument(jar.toString());
        var app = new App(new Store(data.resolve("desktop-popup-folder"), desktop(home), launcher));
        if (args.length == 0) SwingUtilities.invokeLater(app::manager);
        else if (args.length == 2 && args[0].equals("--open")) {
            var folder = app.store.load(UUID.fromString(args[1]));
            SwingUtilities.invokeLater(() -> app.open(folder));
        } else throw new IllegalArgumentException("Expected --open <folder-id>");
    }

    private static Path desktop(Path home) {
        try {
            var command = new ProcessBuilder("xdg-user-dir", "DESKTOP").start();
            try (var in = command.inputReader()) {
                var line = in.readLine();
                if (line != null && !line.isBlank()) {
                    var path = Path.of(line);
                    if (path.isAbsolute() && !path.equals(home)) return path;
                }
            }
        } catch (IOException ignored) { }
        return home.resolve("Desktop");
    }

    private void manager() {
        var frame = frame(NAME + " · Linux " + VERSION);
        frame.setDefaultCloseOperation(WindowConstants.DISPOSE_ON_CLOSE);
        var model = new DefaultListModel<Store.Folder>();
        var list = new JList<>(model);
        list.setSelectionMode(ListSelectionModel.SINGLE_SELECTION);
        Runnable reload = () -> run(frame, () -> {
            var selected = list.getSelectedValue();
            model.clear();
            for (var folder : store.folders()) {
                model.addElement(folder);
                if (selected != null && folder.id().equals(selected.id())) list.setSelectedIndex(model.size() - 1);
            }
            if (list.isSelectionEmpty() && !model.isEmpty()) list.setSelectedIndex(0);
        });
        var actions = new JPanel(new FlowLayout(FlowLayout.LEADING));
        actions.setBorder(BorderFactory.createEmptyBorder(12, 12, 8, 12));
        var create = new JPanel(new FlowLayout(FlowLayout.LEADING));
        create.add(button("+", l("New folder", "مجلد جديد"), () -> run(frame, () -> {
            var folder = edit(frame, null);
            if (folder != null) { reload.run(); list.setSelectedValue(folder, true); }
        })));
        actions.add(button("↗", l("Open", "فتح"), () -> { if (list.getSelectedValue() != null) open(list.getSelectedValue()); }));
        actions.add(button("✎", l("Edit", "تعديل"), () -> run(frame, () -> {
            var folder = list.getSelectedValue();
            if (folder == null) return;
            edit(frame, folder);
            reload.run();
        })));
        actions.add(button("⟳", l("Repair shortcut", "إصلاح الاختصار"), () -> run(frame, () -> {
            if (list.getSelectedValue() != null) store.launcher(store.load(list.getSelectedValue().id()));
        })));
        actions.add(button("−", l("Delete", "حذف"), () -> run(frame, () -> {
            var folder = list.getSelectedValue();
            if (folder != null && JOptionPane.showConfirmDialog(frame, l("Remove folder and desktop shortcut? Stored files are kept.",
                "إزالة المجلد واختصار سطح المكتب؟ تبقى الملفات المخزنة."),
                NAME, JOptionPane.YES_NO_OPTION) == JOptionPane.YES_OPTION) { store.delete(folder.id()); reload.run(); }
        })));
        list.addMouseListener(new MouseAdapter() {
            @Override public void mouseClicked(MouseEvent e) {
                if (e.getClickCount() == 2 && list.getSelectedValue() != null) open(list.getSelectedValue());
            }
        });
        actions.add(button("▣", l("Stored files", "الملفات المخزنة"), () -> run(frame, () -> {
            if (list.getSelectedValue() != null) launch(Files.createDirectories(store.storage(list.getSelectedValue().id())));
        })));
        var global = new JPanel(new FlowLayout(FlowLayout.TRAILING));
        global.add(button("⚙", l("Settings", "الإعدادات"), () -> run(frame, () -> settings(frame))));
        global.add(button("ⓘ", l("About", "حول التطبيق"), () -> about(frame)));
        var header = new JPanel(new BorderLayout());
        var heading = new JLabel(NAME);
        heading.setFont(heading.getFont().deriveFont(Font.BOLD, 25f));
        var title = new JPanel(new BorderLayout(8, 8));
        title.add(heading, BorderLayout.CENTER);
        title.add(global, BorderLayout.LINE_END);
        header.add(title, BorderLayout.NORTH);
        var intro = new JPanel(new BorderLayout());
        intro.add(new JLabel(l("Create and manage your desktop folders.", "أنشئ مجلدات سطح المكتب وأدرها.")), BorderLayout.NORTH);
        intro.add(create);
        header.add(intro);
        frame.getRootPane().setBorder(BorderFactory.createEmptyBorder(20, 22, 14, 22));
        frame.add(header, BorderLayout.NORTH);
        frame.add(new JScrollPane(list), BorderLayout.CENTER);
        frame.add(actions, BorderLayout.SOUTH);
        list.setFixedCellHeight(44);
        list.setCellRenderer(new DefaultListCellRenderer() {
            @Override public Component getListCellRendererComponent(JList<?> source, Object value, int index, boolean selected, boolean focus) {
                super.getListCellRendererComponent(source, value, index, selected, focus);
                if (value instanceof Store.Folder folder) setIcon(new ImageIcon(folderIcon(24, folder.appearance())));
                setBorder(BorderFactory.createEmptyBorder(0, 10, 0, 10));
                return this;
            }
        });
        finish(frame.getContentPane());
        frame.setSize(690, 500);
        frame.setLocationByPlatform(true);
        reload.run();
        frame.setVisible(true);
    }

    private void settings(JFrame owner) throws IOException {
        var themes = new JComboBox<>(new String[]{l("Follow Linux", "مظهر لينكس"), l("Light", "فاتح"), l("Dark", "داكن")});
        themes.setSelectedIndex(theme.equals("Light") ? 1 : theme.equals("Dark") ? 2 : 0);
        var language = new JComboBox<>(new String[]{"English", "العربية"});
        language.setSelectedIndex(arabic ? 1 : 0);
        var content = form(l("Appearance", "المظهر"), themes, l("Language", "اللغة"), language);
        Runnable apply = () -> run(owner, () -> {
            theme = new String[]{"System", "Light", "Dark"}[themes.getSelectedIndex()];
            arabic = language.getSelectedIndex() == 1;
            store.settings(theme, arabic);
            finish(content);
            finish(owner.getContentPane());
        });
        themes.addActionListener(e -> apply.run());
        language.addActionListener(e -> apply.run());
        showEditor(owner, l("Settings", "الإعدادات"), content, null);
        owner.dispose();
        manager();
    }

    private boolean showEditor(Component owner, String title, JComponent fields, Action create) {
        var parent = owner instanceof Window window ? window : SwingUtilities.getWindowAncestor(owner);
        var dialog = new JDialog(parent, title, Dialog.ModalityType.APPLICATION_MODAL);
        var surface = new JPanel(new BorderLayout(0, 16));
        surface.setBorder(BorderFactory.createEmptyBorder(22, 24, 18, 24));
        var heading = new JLabel(title);
        heading.setFont(heading.getFont().deriveFont(Font.BOLD, 24f));
        surface.add(heading, BorderLayout.NORTH);
        surface.add(fields);
        boolean[] accepted = {false};
        var footer = new JPanel(new FlowLayout(FlowLayout.TRAILING));
        var ok = button(create == null ? "OK" : l("Create", "إنشاء"), () -> {
            if (create != null && !run(dialog, create)) return;
            accepted[0] = true;
            dialog.dispose();
        });
        footer.add(ok);
        surface.add(footer, BorderLayout.SOUTH);
        finish(surface);
        ok.setForeground(Color.WHITE);
        ok.setBackground(new Color(37, 112, 79));
        dialog.setContentPane(surface);
        dialog.getRootPane().setDefaultButton(ok);
        dialog.getRootPane().registerKeyboardAction(e -> dialog.dispose(), KeyStroke.getKeyStroke(KeyEvent.VK_ESCAPE, 0),
            JComponent.WHEN_IN_FOCUSED_WINDOW);
        dialog.pack();
        dialog.setSize(Math.max(480, dialog.getWidth()), dialog.getHeight());
        dialog.setLocationRelativeTo(owner);
        dialog.setVisible(true);
        return accepted[0];
    }

    private void about(JFrame owner) {
        var dialog = new JDialog(owner, l("About", "حول التطبيق"), true);
        var body = new JPanel();
        body.setLayout(new BoxLayout(body, BoxLayout.Y_AXIS));
        body.setBorder(BorderFactory.createEmptyBorder(24, 24, 24, 24));
        var identity = new JLabel(NAME, new ImageIcon(folderIcon(44)), SwingConstants.LEADING);
        identity.setIconTextGap(12);
        identity.setFont(identity.getFont().deriveFont(Font.BOLD, 24f));
        body.add(identity);
        body.add(new JLabel(l("Version", "الإصدار") + " " + VERSION + " · Linux"));
        body.add(Box.createVerticalStrut(14));
        body.add(new JLabel(l("Open your apps and files from a floating desktop folder.",
            "افتح تطبيقاتك وملفاتك من مجلد منبثق على سطح المكتب.")));
        body.add(Box.createVerticalStrut(10));
        body.add(new JLabel("© 2026 Mohammed-HG"));
        var profile = "https://github.com/Mohammed-HG";
        for (var link : List.of(new String[]{"Mohammed-HG", profile},
            new String[]{l("Source code", "الشيفرة المصدرية"), profile + "/desktop-pop-up-folder-project"})) {
            body.add(Box.createVerticalStrut(8));
            body.add(button("↗  " + link[0], () -> run(dialog, () -> new ProcessBuilder("xdg-open", link[1]).start())));
        }
        body.add(Box.createVerticalStrut(14));
        var ok = button("OK", dialog::dispose);
        body.add(ok);
        dialog.getRootPane().setDefaultButton(ok);
        finish(body);
        dialog.setContentPane(body);
        dialog.pack();
        dialog.setLocationRelativeTo(owner);
        dialog.setVisible(true);
    }

    private Store.Folder edit(Component owner, Store.Folder folder) throws IOException {
        var a = folder == null ? Store.Appearance.DEFAULT : folder.appearance();
        var name = new JTextField(folder == null ? "" : folder.name(), 20);
        var columns = spinner(folder == null ? 4 : folder.columns(), 1, 8);
        var width = spinner(a.width(), 260, 1200);
        var height = spinner(a.height(), 220, 900);
        var size = spinner(a.iconSize(), 24, 96);
        var theme = new JComboBox<>(new String[]{l("Follow Linux", "مظهر لينكس"), l("Light", "فاتح"), l("Dark", "داكن")});
        theme.setSelectedIndex(a.theme().equals("Light") ? 1 : a.theme().equals("Dark") ? 2 : 0);
        var transparency = new JSlider(0, 100, a.transparency());
        var labels = new JCheckBox(l("Show names", "إظهار الأسماء"), a.labels());
        var outside = new JCheckBox(l("Close when clicking outside", "الإغلاق عند النقر خارج المجلد"), a.closeOutside());
        var close = new JCheckBox(l("Show close icon", "إظهار علامة الإغلاق"), a.closeButton());
        var spacing = spinner(a.spacing(), 0, 24);
        var hover = new JCheckBox(l("Highlight icons on hover", "تمييز الأيقونات عند المرور"), a.hover());
        var color = new JTextField(a.color(), 8);
        var symbol = new JComboBox<>(new String[]{"", "★", "⚙", "⌂", "☁", "⚡", "⚒", "↗"});
        symbol.setSelectedItem(a.symbol());
        var files = new ArrayList<Path>(folder == null ? List.of() : folder.files());
        var entries = new DefaultListModel<Path>();
        files.forEach(entries::addElement);
        var list = new JList<>(entries);
        list.setVisibleRowCount(6);
        var shortcuts = new JPanel(new BorderLayout(0, 8));
        list.setCellRenderer(new DefaultListCellRenderer() {
            @Override public Component getListCellRendererComponent(JList<?> source, Object value, int index, boolean selected, boolean focus) {
                super.getListCellRendererComponent(source, value, index, selected, focus);
                var path = (Path)value;
                setText(path.getFileName().toString().replaceFirst("\\.desktop$", ""));
                setToolTipText(path.toString());
                return this;
            }
        });
        var colorButton = button(l("Choose color", "اختيار اللون"), () -> {
            var selected = JColorChooser.showDialog(owner, l("Folder color", "لون المجلد"), folderColor(color.getText()));
            if (selected != null) color.setText(String.format("#%06x", selected.getRGB() & 0xffffff));
        });
        var colors = new JPanel(new BorderLayout(5, 0));
        colors.add(color);
        colors.add(colorButton, BorderLayout.LINE_END);
        var shortcutTools = form(l("Folder name", "اسم المجلد"), name, l("Folder color", "لون المجلد"), colors,
            l("Folder symbol", "رمز المجلد"), symbol);
        shortcuts.add(shortcutTools, BorderLayout.NORTH);
        shortcuts.add(new JScrollPane(list));
        var controls = new JPanel(new FlowLayout(FlowLayout.LEADING));
        controls.add(button("+", l("Add link", "إضافة اختصار"), () -> {
            var chooser = new JFileChooser();
            chooser.setMultiSelectionEnabled(true);
            if (chooser.showOpenDialog(owner) != JFileChooser.APPROVE_OPTION) return;
            for (var file : chooser.getSelectedFiles()) {
                var path = file.toPath().toAbsolutePath().normalize();
                if (!entries.contains(path)) entries.addElement(path);
            }
        }));
        controls.add(button("−", l("Remove from popup", "إزالة من المجلد"), () -> {
            if (list.getSelectedIndex() >= 0) entries.remove(list.getSelectedIndex());
        }));
        shortcuts.add(controls, BorderLayout.SOUTH);
        var tabs = new JTabbedPane();
        tabs.addTab(l("Shortcuts", "الاختصارات"), shortcuts);
        tabs.addTab(l("Appearance", "المظهر"), form(l("Width", "العرض"), width, l("Height", "الارتفاع"), height,
            l("Columns", "الأعمدة"), columns, l("Icon size", "حجم الأيقونة"), size,
            l("Icon spacing", "المسافة بين الأيقونات"), spacing, l("Theme", "المظهر"), theme,
            l("Transparency", "الشفافية"), transparency, "", labels));
        tabs.addTab(l("Behavior", "السلوك"), form("", outside, "", close, "", hover));
        finish(tabs);
        var preview = new JPanel(new GridLayout(0, 3, 8, 8));
        var thumbnail = new JPanel(new BorderLayout(0, 10));
        thumbnail.setBorder(BorderFactory.createEmptyBorder(12, 14, 12, 6));
        var previewTitle = new JLabel(l("Live preview", "معاينة مباشرة"), new ImageIcon(folderIcon(24)), SwingConstants.LEADING);
        thumbnail.add(previewTitle, BorderLayout.NORTH);
        thumbnail.add(preview, BorderLayout.CENTER);
        var layout = new JPanel(new BorderLayout(14, 0));
        layout.add(tabs);
        layout.add(thumbnail, BorderLayout.LINE_END);
        var previewIcons = new HashMap<Path, Icon>();
        Runnable update = () -> {
            preview.removeAll();
            var isDark = dark(theme.getSelectedIndex() == 0 ? this.theme : theme.getSelectedIndex() == 1 ? "Light" : "Dark");
            var iconSize = Math.max(20, Math.min(52, (int)size.getValue() / 2));
            previewTitle.setIcon(new ImageIcon(folderIcon(32, color.getText(), (String)symbol.getSelectedItem())));
            preview.setBackground(isDark ? BACKGROUND : new Color(234, 238, 244));
            ((GridLayout)preview.getLayout()).setColumns(Math.min(4, (int)columns.getValue()));
            for (int i = 0; i < Math.min(entries.size(), 12); i++) {
                var file = entries.get(i);
                var label = new JLabel(labels.isSelected() ? file.getFileName().toString().replaceFirst("\\.desktop$", "") : "",
                    previewIcons.computeIfAbsent(file, path -> LinuxIcons.load(path, iconSize)), SwingConstants.CENTER);
                label.setPreferredSize(new Dimension(74, 70));
                label.setHorizontalTextPosition(SwingConstants.CENTER);
                label.setVerticalTextPosition(SwingConstants.BOTTOM);
                label.setForeground(isDark ? Color.WHITE : Color.DARK_GRAY);
                preview.add(label);
            }
            preview.revalidate();
            preview.repaint();
        };
        columns.addChangeListener(e -> update.run());
        size.addChangeListener(e -> { previewIcons.clear(); update.run(); });
        symbol.addActionListener(e -> update.run());
        color.getDocument().addDocumentListener(new javax.swing.event.DocumentListener() {
            public void insertUpdate(javax.swing.event.DocumentEvent e) { update.run(); }
            public void removeUpdate(javax.swing.event.DocumentEvent e) { update.run(); }
            public void changedUpdate(javax.swing.event.DocumentEvent e) { update.run(); }
        });
        theme.addActionListener(e -> update.run());
        labels.addActionListener(e -> update.run());
        entries.addListDataListener(new javax.swing.event.ListDataListener() {
            public void intervalAdded(javax.swing.event.ListDataEvent e) { update.run(); }
            public void intervalRemoved(javax.swing.event.ListDataEvent e) { update.run(); }
            public void contentsChanged(javax.swing.event.ListDataEvent e) { update.run(); }
        });
        update.run();
        java.util.function.Supplier<Store.Appearance> readAppearance = () ->
            new Store.Appearance((int)width.getValue(), (int)height.getValue(), (int)size.getValue(),
                labels.isSelected(), outside.isSelected(), close.isSelected(),
                new String[]{"System", "Light", "Dark"}[theme.getSelectedIndex()],
                transparency.getValue(), color.getText(), (String)symbol.getSelectedItem(), (int)spacing.getValue(), hover.isSelected());
        Store.Folder[] current = {folder};
        if (folder == null) {
            showEditor(owner, l("New folder", "مجلد جديد"), layout, () ->
                current[0] = store.create(name.getText(), (int)columns.getValue(), readAppearance.get(),
                    Collections.list(entries.elements())));
        } else {
            Action apply = () -> {
                var before = current[0];
                var edited = Collections.list(entries.elements());
                var appearance = readAppearance.get();
                if (name.getText().isBlank() || !color.getText().matches("#[0-9a-fA-F]{6}")) return;
                if (before.name().equals(name.getText().strip()) && before.columns() == (int)columns.getValue()
                    && before.appearance().equals(appearance) && before.files().equals(edited)) return;
                current[0] = store.edit(before.id(), name.getText(), (int)columns.getValue(), appearance, edited);
            };
            var timer = new javax.swing.Timer(240, e -> run(owner, apply));
            timer.setRepeats(false);
            Runnable schedule = timer::restart;
            for (var control : List.of(columns, width, height, size, spacing))
                control.addChangeListener(e -> schedule.run());
            transparency.addChangeListener(e -> schedule.run());
            theme.addActionListener(e -> schedule.run());
            symbol.addActionListener(e -> schedule.run());
            for (var control : List.of(labels, outside, close, hover)) control.addActionListener(e -> schedule.run());
            for (var field : List.of(name, color)) field.getDocument().addDocumentListener(new javax.swing.event.DocumentListener() {
                public void insertUpdate(javax.swing.event.DocumentEvent e) { schedule.run(); }
                public void removeUpdate(javax.swing.event.DocumentEvent e) { schedule.run(); }
                public void changedUpdate(javax.swing.event.DocumentEvent e) { schedule.run(); }
            });
            entries.addListDataListener(new javax.swing.event.ListDataListener() {
                public void intervalAdded(javax.swing.event.ListDataEvent e) { schedule.run(); }
                public void intervalRemoved(javax.swing.event.ListDataEvent e) { schedule.run(); }
                public void contentsChanged(javax.swing.event.ListDataEvent e) { schedule.run(); }
            });
            showEditor(owner, l("Edit folder", "تعديل المجلد"), layout, null);
            timer.stop();
            run(owner, apply);
        }
        return current[0];
    }

    private static JSpinner spinner(int value, int low, int high) {
        return new JSpinner(new SpinnerNumberModel(value, low, high, 1));
    }

    private static JPanel form(Object... pairs) {
        var panel = new JPanel(new GridLayout(0, 2, 12, 8));
        panel.setBorder(BorderFactory.createEmptyBorder(14, 14, 14, 14));
        for (int i = 0; i < pairs.length; i += 2) {
            panel.add(new JLabel((String)pairs[i]));
            panel.add((Component)pairs[i + 1]);
        }
        return panel;
    }

    private void open(Store.Folder folder) {
        run(null, () -> {
            var panel = new Popup(store.load(folder.id()));
            panel.show();
        });
    }

    private final class Popup {
        private Store.Folder folder;
        private final JFrame frame;
        private final JPanel grid = new JPanel();
        private final JScrollPane scroll = new JScrollPane(grid,
            ScrollPaneConstants.VERTICAL_SCROLLBAR_ALWAYS, ScrollPaneConstants.HORIZONTAL_SCROLLBAR_NEVER);
        private final Map<Path, Icon> icons = new HashMap<>();
        private boolean dragging;
        private final Color ink;
        private final Color backdrop;
        Popup(Store.Folder folder) {
            this.folder = folder;
            frame = frame(folder.name());
            var a = folder.appearance();
            boolean dark = dark(a.theme().equals("System") ? theme : a.theme());
            ink = dark ? Color.WHITE : new Color(27, 31, 40);
            var fill = dark ? BACKGROUND : new Color(245, 246, 249);
            boolean alpha = frame.getGraphicsConfiguration().getDevice().isWindowTranslucencySupported(
                GraphicsDevice.WindowTranslucency.PERPIXEL_TRANSLUCENT);
            backdrop = new Color(fill.getRed(), fill.getGreen(), fill.getBlue(),
                alpha ? 255 - a.transparency() * 255 / 100 : 255);
            frame.setUndecorated(true);
            if (alpha && a.transparency() > 0) frame.setBackground(new Color(0, 0, 0, 1));
            else frame.setBackground(fill);
            frame.setAlwaysOnTop(true);
            frame.setDefaultCloseOperation(WindowConstants.DISPOSE_ON_CLOSE);
            frame.setMinimumSize(new Dimension(260, 220));
            frame.setSize(a.width(), a.height());
            grid.setOpaque(false);
            grid.setBorder(BorderFactory.createEmptyBorder(12, 12, 12, 12));
            scroll.setOpaque(false);
            scroll.setBorder(BorderFactory.createEmptyBorder());
            scroll.getViewport().setOpaque(false);
            scroll.getViewport().addComponentListener(new ComponentAdapter() {
                @Override public void componentResized(ComponentEvent e) {
                    var layout = grid.getLayout();
                    if (layout instanceof GridLayout rows && rows.getColumns() != columns()) {
                        rows.setColumns(columns());
                        updateGridHeight();
                    }
                }
            });
            var transfer = new FileTransfer(null);
            grid.setTransferHandler(transfer);
            scroll.getViewport().setTransferHandler(transfer);
            var title = new JLabel(folder.name());
            title.setForeground(ink);
            title.setFont(title.getFont().deriveFont(Font.BOLD, 17f));
            var top = new JPanel(new BorderLayout(8, 0));
            top.setOpaque(false);
            top.add(title, BorderLayout.CENTER);
            if (a.closeButton()) {
                var close = button("×", l("Close", "إغلاق"), frame::dispose);
                close.setForeground(ink);
                close.setContentAreaFilled(false);
                top.add(close, BorderLayout.LINE_END);
            }
            dragWindow(title, false);
            var grip = new JLabel("◢", SwingConstants.RIGHT);
            grip.setForeground(ink);
            grip.setCursor(Cursor.getPredefinedCursor(Cursor.SE_RESIZE_CURSOR));
            dragWindow(grip, true);
            var surface = new JPanel(new BorderLayout()) {
                @Override protected void paintComponent(Graphics graphics) {
                    var g = (Graphics2D)graphics.create();
                    g.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON);
                    g.setColor(backdrop);
                    g.fillRoundRect(0, 0, getWidth(), getHeight(), 24, 24);
                    g.dispose();
                }
            };
            surface.setOpaque(false);
            surface.setBorder(BorderFactory.createEmptyBorder(9, 12, 5, 12));
            surface.add(top, BorderLayout.NORTH);
            surface.add(scroll);
            surface.add(grip, BorderLayout.SOUTH);
            surface.applyComponentOrientation(arabic ? ComponentOrientation.RIGHT_TO_LEFT : ComponentOrientation.LEFT_TO_RIGHT);
            frame.setContentPane(surface);
            frame.addWindowFocusListener(new WindowAdapter() {
                @Override public void windowLostFocus(WindowEvent e) {
                    if (a.closeOutside() && !dragging
                        && (e.getOppositeWindow() == null || e.getOppositeWindow().getOwner() != frame)) frame.dispose();
                }
            });
            var keys = frame.getRootPane();
            keys.getInputMap(JComponent.WHEN_IN_FOCUSED_WINDOW).put(KeyStroke.getKeyStroke(KeyEvent.VK_V, InputEvent.CTRL_DOWN_MASK), "paste");
            keys.getActionMap().put("paste", new AbstractAction() {
                @Override public void actionPerformed(ActionEvent e) {
                    run(frame, () -> transfer.receive(Toolkit.getDefaultToolkit().getSystemClipboard().getContents(null)));
                }
            });
            keys.getInputMap(JComponent.WHEN_IN_FOCUSED_WINDOW).put(KeyStroke.getKeyStroke(KeyEvent.VK_ESCAPE, 0), "close");
            keys.getActionMap().put("close", new AbstractAction() {
                @Override public void actionPerformed(ActionEvent e) { frame.dispose(); }
            });
        }

        private void dragWindow(JComponent handle, boolean resize) {
            var mouse = new MouseAdapter() {
                Point start, origin;
                Dimension initial;
                @Override public void mousePressed(MouseEvent e) {
                    start = e.getLocationOnScreen();
                    origin = frame.getLocation();
                    initial = frame.getSize();
                }
                @Override public void mouseDragged(MouseEvent e) {
                    var pointer = e.getLocationOnScreen();
                    int dx = pointer.x - start.x, dy = pointer.y - start.y;
                    if (resize) frame.setSize(Math.max(260, Math.min(1200, initial.width + dx)), Math.max(220, Math.min(900, initial.height + dy)));
                    else frame.setLocation(origin.x + dx, origin.y + dy);
                }
                @Override public void mouseReleased(MouseEvent e) {
                    if (!resize) return;
                    var a = folder.appearance();
                    run(frame, () -> folder = store.edit(folder.id(), folder.name(), folder.columns(),
                        a.resized(frame.getWidth(), frame.getHeight())));
                }
            };
            handle.addMouseListener(mouse);
            handle.addMouseMotionListener(mouse);
        }

        private int columns() {
            int cell = Math.max(84, folder.appearance().iconSize() + 16) + folder.appearance().spacing();
            return Math.max(1, Math.min(folder.columns(), (scroll.getViewport().getWidth() - 24) / cell));
        }

        private void render() {
            grid.removeAll();
            if (folder.files().isEmpty()) {
                grid.setLayout(new BorderLayout());
                var empty = new JLabel(l("Drop files here or use Ctrl+V to move them.", "أفلت الملفات هنا أو اضغط Ctrl+V لنقلها."), SwingConstants.CENTER);
                empty.setForeground(ink);
                grid.add(empty);
            } else {
                int gap = folder.appearance().spacing();
                grid.setLayout(new GridLayout(0, columns(), gap, gap));
                for (Path file : folder.files()) {
                    var name = file.getFileName().toString().replaceFirst("\\.desktop$", "");
                    var label = name.length() > 20 ? name.substring(0, 17) + "…" : name;
                    var tile = new JButton(folder.appearance().labels() ? label : "");
                    tile.setToolTipText(name);
                    var size = folder.appearance().iconSize();
                    tile.setIcon(icons.computeIfAbsent(file, path -> LinuxIcons.load(path, size)));
                    tile.setHorizontalTextPosition(SwingConstants.CENTER);
                    tile.setVerticalTextPosition(SwingConstants.BOTTOM);
                    tile.setForeground(ink);
                    tile.setContentAreaFilled(false);
                    if (folder.appearance().hover()) {
                        tile.addMouseListener(new MouseAdapter() {
                            @Override public void mouseEntered(MouseEvent e) {
                                if (backdrop.getAlpha() == 0) return;
                                tile.setBackground(ink == Color.WHITE ? new Color(59, 68, 83) : new Color(223, 234, 229));
                                tile.setContentAreaFilled(true);
                            }
                            @Override public void mouseExited(MouseEvent e) { tile.setContentAreaFilled(false); }
                        });
                    }
                    tile.setMinimumSize(new Dimension(0, 0));
                    tile.setBorder(BorderFactory.createEmptyBorder(8, 4, 8, 4));
                    tile.setPreferredSize(new Dimension(94, size + 34));
                    tile.addActionListener(e -> run(frame, () -> launch(file)));
                    var menu = new JPopupMenu();
                    for (int offset : new int[]{-1, 1}) {
                        var order = new JMenuItem(offset < 0 ? l("Move earlier", "نقل للأمام") : l("Move later", "نقل للخلف"));
                        order.addActionListener(e -> run(frame, () -> {
                            folder = store.change(folder.id(), current -> {
                                int index = current.files().indexOf(file), next = index + offset;
                                if (index >= 0 && next >= 0 && next < current.files().size())
                                    Collections.swap(current.files(), index, next);
                            });
                            render();
                        }));
                        menu.add(order);
                    }
                    var remove = new JMenuItem(l("Remove from popup", "إزالة من المجلد"));
                    remove.addActionListener(e -> run(frame, () -> {
                        folder = store.change(folder.id(), current -> current.files().remove(file));
                        render();
                    }));
                    menu.add(remove);
                    tile.setComponentPopupMenu(menu);
                    tile.setTransferHandler(new FileTransfer(file));
                    var mouse = new MouseAdapter() {
                        Point start;
                        @Override public void mousePressed(MouseEvent e) { start = e.getPoint(); }
                        @Override public void mouseDragged(MouseEvent e) {
                            if (start != null && start.distance(e.getPoint()) >= DragSourceThreshold.VALUE
                                && file.startsWith(store.storage(folder.id()))) {
                                start = null;
                                dragging = true;
                                tile.getModel().setArmed(false);
                                tile.getTransferHandler().exportAsDrag(tile, e, TransferHandler.MOVE);
                            }
                        }
                    };
                    tile.addMouseListener(mouse);
                    tile.addMouseMotionListener(mouse);
                    grid.add(tile);
                }
            }
            updateGridHeight();
        }

        private void updateGridHeight() {
            int columns = columns();
            int rows = (folder.files().size() + columns - 1) / columns;
            grid.setPreferredSize(rows == 0 ? null : new Dimension(0,
                rows * (folder.appearance().iconSize() + 34 + folder.appearance().spacing()) + 24));
            grid.revalidate();
            grid.repaint();
        }

        void show() {
            var pointer = MouseInfo.getPointerInfo();
            if (pointer != null) {
                var point = pointer.getLocation();
                var area = pointer.getDevice().getDefaultConfiguration().getBounds();
                frame.setLocation(Math.max(area.x, Math.min(point.x, area.x + area.width - frame.getWidth())),
                    Math.max(area.y, Math.min(point.y, area.y + area.height - frame.getHeight())));
            } else frame.setLocationByPlatform(true);
            render();
            frame.setVisible(true);
        }

        private final class FileTransfer extends TransferHandler {
            private final Path source;
            FileTransfer(Path source) { this.source = source; }
            @Override public boolean canImport(TransferSupport support) {
                if (!support.isDataFlavorSupported(DataFlavor.javaFileListFlavor)) return false;
                if (support.isDrop()) {
                    if ((support.getSourceDropActions() & MOVE) == 0) return false;
                    support.setDropAction(MOVE);
                }
                return true;
            }
            @Override public boolean importData(TransferSupport support) {
                return canImport(support) && run(frame, () -> receive(support.getTransferable()));
            }
            void receive(Transferable data) throws Exception {
                if (data == null || !data.isDataFlavorSupported(DataFlavor.javaFileListFlavor)) return;
                var paths = new ArrayList<Path>();
                if (data.getTransferData(DataFlavor.javaFileListFlavor) instanceof List<?> files)
                    for (Object item : files) if (item instanceof File file) paths.add(file.toPath());
                if (!paths.isEmpty()) { folder = store.moveIn(folder.id(), paths); render(); }
            }
            @Override public int getSourceActions(JComponent component) {
                return source != null && source.startsWith(store.storage(folder.id())) && Files.isRegularFile(source) ? MOVE : NONE;
            }
            @Override protected Transferable createTransferable(JComponent component) {
                return new Transferable() {
                    public DataFlavor[] getTransferDataFlavors() { return new DataFlavor[]{DataFlavor.javaFileListFlavor}; }
                    public boolean isDataFlavorSupported(DataFlavor flavor) { return DataFlavor.javaFileListFlavor.equals(flavor); }
                    public Object getTransferData(DataFlavor flavor) throws UnsupportedFlavorException {
                        if (!isDataFlavorSupported(flavor)) throw new UnsupportedFlavorException(flavor);
                        return List.of(source.toFile());
                    }
                };
            }
            @Override protected void exportDone(JComponent component, Transferable data, int action) {
                dragging = false;
                if (source != null && Files.notExists(source))
                    run(frame, () -> { folder = store.change(folder.id(), item -> item.files().remove(source)); render(); });
            }
        }
    }

    private static final class DragSourceThreshold {
        static final int VALUE = java.awt.dnd.DragSource.getDragThreshold();
    }

    private static void launch(Path path) throws IOException {
        if (!Files.exists(path)) throw new IOException("The file was moved or deleted.");
        var name = path.getFileName().toString();
        if (name.endsWith(".desktop")) {
            var process = new ProcessBuilder("gio", "launch", path.toString()).redirectErrorStream(true).start();
            try {
                if (process.waitFor(900, java.util.concurrent.TimeUnit.MILLISECONDS) && process.exitValue() != 0)
                    throw new IOException(process.inputReader().readLine());
            } catch (InterruptedException interrupted) { Thread.currentThread().interrupt(); }
        }
        else if (name.toLowerCase(Locale.ROOT).endsWith(".appimage") && Files.isExecutable(path))
            new ProcessBuilder(path.toString()).start();
        else new ProcessBuilder("xdg-open", path.toString()).start();
    }

    private static JFrame frame(String title) {
        var frame = new JFrame(title);
        frame.setIconImage(folderIcon(96));
        return frame;
    }

    private static JButton button(String name, Runnable action) {
        var button = new JButton(name);
        button.addActionListener(e -> action.run());
        return button;
    }

    private static JButton button(String glyph, String label, Runnable action) {
        var button = button(glyph, action);
        button.setPreferredSize(new Dimension(38, 34));
        button.setFont(new Font(Font.SANS_SERIF, Font.PLAIN, 18));
        button.setMargin(new Insets(2, 4, 2, 4));
        button.setToolTipText(label);
        button.getAccessibleContext().setAccessibleName(label);
        return button;
    }

    private interface Action { void run() throws Exception; }
    private static boolean run(Component owner, Action action) {
        try { action.run(); return true; }
        catch (Exception e) { JOptionPane.showMessageDialog(owner, e.getMessage(), NAME, JOptionPane.ERROR_MESSAGE); return false; }
    }

    static BufferedImage folderIcon(int size) {
        return folderIcon(size, Store.Appearance.DEFAULT);
    }

    static BufferedImage folderIcon(int size, Store.Appearance appearance) {
        return folderIcon(size, appearance.color(), appearance.symbol());
    }

    private static Color folderColor(String color) {
        try { return Color.decode(color); }
        catch (NumberFormatException ignored) { return new Color(66, 147, 238); }
    }

    private static BufferedImage folderIcon(int size, String color, String symbol) {
        var icon = new BufferedImage(size, size, BufferedImage.TYPE_INT_ARGB);
        var graphics = icon.createGraphics();
        try {
            graphics.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON);
            graphics.scale(size / 64d, size / 64d);
            var tint = folderColor(color);
            graphics.setColor(tint.darker());
            graphics.fillRoundRect(4, 11, 26, 19, 8, 8);
            graphics.fillRoundRect(4, 17, 56, 38, 10, 10);
            graphics.setColor(tint);
            graphics.fillRoundRect(4, 24, 56, 32, 9, 9);
            graphics.setColor(Color.WHITE);
            if (symbol == null || symbol.isBlank()) {
                for (int x : new int[]{23, 34})
                    for (int y : new int[]{31, 42}) graphics.fillRoundRect(x, y, 8, 7, 2, 2);
            } else {
                graphics.setFont(new Font(Font.SANS_SERIF, Font.BOLD, 25));
                graphics.drawString(symbol, (64 - graphics.getFontMetrics().stringWidth(symbol)) / 2, 48);
            }
        } finally { graphics.dispose(); }
        return icon;
    }

    private static void selfTest() throws IOException {
        var root = Files.createTempDirectory("popup-linux-test-");
        try {
            var desktop = root.resolve("desktop");
            var store = new Store(root.resolve("data"), desktop, Store.argument("/tmp/a b%$") + " -jar /tmp/app.jar");
            var folder = store.create("ألعاب");
            var shortcut = desktop.resolve("ألعاب.desktop");
            require(Files.readString(shortcut).contains(Store.OWNER + folder.id()));
            var original = root.resolve("test.txt");
            Files.writeString(original, "saved");
            try { store.moveIn(folder.id(), List.of(original, root.resolve("missing"))); throw new AssertionError("missing file"); }
            catch (IOException expected) { require(Files.exists(original)); }
            folder = store.moveIn(folder.id(), List.of(original));
            require(Files.notExists(original) && Files.readString(folder.files().get(0)).equals("saved"));
            folder = store.edit(folder.id(), "Games", 3);
            require(Files.notExists(shortcut) && Files.exists(desktop.resolve("Games.desktop")) && store.folders().get(0).columns() == 3);
            var appearance = new Store.Appearance(560, 420, 64, false, true, false, "Dark", 25);
            folder = store.edit(folder.id(), "Games", 3, appearance);
            require(store.load(folder.id()).appearance().equals(appearance));
            store.delete(folder.id());
            require(Files.notExists(desktop.resolve("Games.desktop")) && Files.exists(folder.files().get(0)));
            System.out.println("Linux storage, launcher, and move tests passed.");
        } finally { deleteFixture(root); }
    }

    private static void require(boolean condition) { if (!condition) throw new AssertionError("Linux self-test failed"); }
    private static void deleteFixture(Path root) throws IOException {
        try (var paths = Files.walk(root)) {
            for (var path : paths.sorted(Comparator.reverseOrder()).toList()) Files.deleteIfExists(path);
        }
    }
}
