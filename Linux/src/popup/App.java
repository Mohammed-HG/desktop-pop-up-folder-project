package popup;

import javax.swing.*;
import javax.swing.filechooser.FileSystemView;
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
    static final String VERSION = "1.3";
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
            var testApp = new App(store);
            SwingUtilities.invokeLater(() -> {
                testApp.open(folder);
                var stop = new javax.swing.Timer(650, event -> {
                    try { deleteFixture(fixture); } catch (IOException e) { throw new UncheckedIOException(e); }
                    System.out.println("Linux popup rendered");
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
            var name = JOptionPane.showInputDialog(frame, l("Folder name", "اسم المجلد"));
            if (name != null) { var folder = store.create(name); reload.run(); list.setSelectedValue(folder, true); }
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
                setIcon(new ImageIcon(folderIcon(24)));
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
        finish(content);
        if (JOptionPane.showConfirmDialog(owner, content, l("Settings", "الإعدادات"), JOptionPane.OK_CANCEL_OPTION,
            JOptionPane.PLAIN_MESSAGE) != JOptionPane.OK_OPTION) return;
        var selected = new String[]{"System", "Light", "Dark"}[themes.getSelectedIndex()];
        store.settings(selected, language.getSelectedIndex() == 1);
        theme = selected;
        arabic = language.getSelectedIndex() == 1;
        owner.dispose();
        manager();
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
        var a = folder.appearance();
        var name = new JTextField(folder.name(), 20);
        var columns = spinner(folder.columns(), 1, 8);
        var width = spinner(a.width(), 260, 1200);
        var height = spinner(a.height(), 220, 900);
        var size = spinner(a.iconSize(), 24, 96);
        var theme = new JComboBox<>(new String[]{l("Follow Linux", "مظهر لينكس"), l("Light", "فاتح"), l("Dark", "داكن")});
        theme.setSelectedIndex(a.theme().equals("Light") ? 1 : a.theme().equals("Dark") ? 2 : 0);
        var transparency = new JSlider(0, 100, a.transparency());
        var labels = new JCheckBox(l("Show names", "إظهار الأسماء"), a.labels());
        var outside = new JCheckBox(l("Close when clicking outside", "الإغلاق عند النقر خارج المجلد"), a.closeOutside());
        var close = new JCheckBox(l("Show close icon", "إظهار علامة الإغلاق"), a.closeButton());
        var files = new ArrayList<>(folder.files());
        var entries = new DefaultListModel<Path>();
        files.forEach(entries::addElement);
        var list = new JList<>(entries);
        list.setVisibleRowCount(6);
        var shortcuts = new JPanel(new BorderLayout());
        var shortcutTools = form(l("Folder name", "اسم المجلد"), name);
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
            l("Theme", "المظهر"), theme, l("Transparency", "الشفافية"), transparency, "", labels));
        tabs.addTab(l("Behavior", "السلوك"), form("", outside, "", close));
        finish(tabs);
        if (JOptionPane.showConfirmDialog(owner, tabs, l("Edit folder", "تعديل المجلد"), JOptionPane.OK_CANCEL_OPTION,
            JOptionPane.PLAIN_MESSAGE) != JOptionPane.OK_OPTION) return folder;
        var updated = store.edit(folder.id(), name.getText(), (int)columns.getValue(),
            new Store.Appearance((int)width.getValue(), (int)height.getValue(), (int)size.getValue(),
                labels.isSelected(), outside.isSelected(), close.isSelected(),
                new String[]{"System", "Light", "Dark"}[theme.getSelectedIndex()],
                transparency.getValue()));
        var edited = Collections.list(entries.elements());
        return store.change(updated.id(), current -> {
            current.files().removeIf(file -> files.contains(file) && !edited.contains(file));
            for (var file : edited) if (!files.contains(file) && !current.files().contains(file)) current.files().add(file);
        });
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
        private int renderedColumns = -1;
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
            if (alpha) frame.setBackground(new Color(0, 0, 0, 0));
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
                    if (columns() != renderedColumns) render();
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
                        new Store.Appearance(frame.getWidth(), frame.getHeight(), a.iconSize(), a.labels(),
                            a.closeOutside(), a.closeButton(), a.theme(), a.transparency())));
                }
            };
            handle.addMouseListener(mouse);
            handle.addMouseMotionListener(mouse);
        }

        private int columns() { return Math.max(1, Math.min(folder.columns(), (scroll.getViewport().getWidth() - 16) / 102)); }

        private void render() {
            renderedColumns = columns();
            grid.removeAll();
            if (folder.files().isEmpty()) {
                grid.setLayout(new BorderLayout());
                var empty = new JLabel(l("Drop files here or use Ctrl+V to move them.", "أفلت الملفات هنا أو اضغط Ctrl+V لنقلها."), SwingConstants.CENTER);
                empty.setForeground(Color.WHITE);
                grid.add(empty);
            } else {
                grid.setLayout(new GridLayout(0, renderedColumns, 8, 8));
                for (Path file : folder.files()) {
                    var name = file.getFileName().toString().replaceFirst("\\.desktop$", "");
                    var label = name.length() > 20 ? name.substring(0, 17) + "…" : name;
                    var tile = new JButton(folder.appearance().labels() ? label : "");
                    tile.setToolTipText(name);
                    var size = folder.appearance().iconSize();
                    tile.setIcon(FileSystemView.getFileSystemView().getSystemIcon(file.toFile(), size, size));
                    tile.setHorizontalTextPosition(SwingConstants.CENTER);
                    tile.setVerticalTextPosition(SwingConstants.BOTTOM);
                    tile.setForeground(ink);
                    tile.setContentAreaFilled(false);
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
                for (int i = folder.files().size() % renderedColumns; i > 0 && i < renderedColumns; i++)
                    grid.add(Box.createGlue());
                grid.setPreferredSize(new Dimension(90, ((folder.files().size() + renderedColumns - 1) / renderedColumns)
                    * (folder.appearance().iconSize() + 42) + 16));
            }
            if (folder.files().isEmpty()) grid.setPreferredSize(null);
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
        if (name.endsWith(".desktop")) new ProcessBuilder("gio", "launch", path.toString()).start();
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
        var icon = new BufferedImage(size, size, BufferedImage.TYPE_INT_ARGB);
        var graphics = icon.createGraphics();
        try {
            graphics.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON);
            graphics.scale(size / 64d, size / 64d);
            graphics.setColor(new Color(29, 87, 177));
            graphics.fillRoundRect(4, 11, 26, 19, 8, 8);
            graphics.fillRoundRect(4, 17, 56, 38, 10, 10);
            graphics.setColor(new Color(66, 147, 238));
            graphics.fillRoundRect(4, 24, 56, 32, 9, 9);
            graphics.setColor(Color.WHITE);
            for (int x : new int[]{23, 34})
                for (int y : new int[]{31, 42}) graphics.fillRoundRect(x, y, 8, 7, 2, 2);
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
