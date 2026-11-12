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
    static final String VERSION = "1.2";
    static final Color BACKGROUND = new Color(31, 36, 48), TILE = new Color(46, 53, 69);
    private final Store store;

    private App(Store store) { this.store = store; }

    public static void main(String[] args) throws Exception {
        if (Arrays.equals(args, new String[]{"--self-test"})) { selfTest(); return; }
        if (!System.getProperty("os.name").equals("Linux")) {
            System.err.println("Desktop Pop-up folder 1.2 requires a Linux desktop session.");
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
            SwingUtilities.invokeLater(() -> {
                new App(store).open(folder);
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
        actions.add(button("New folder", () -> run(frame, () -> {
            var name = JOptionPane.showInputDialog(frame, "Folder name");
            if (name != null) { var folder = store.create(name); reload.run(); list.setSelectedValue(folder, true); }
        })));
        actions.add(button("Open", () -> { if (list.getSelectedValue() != null) open(list.getSelectedValue()); }));
        actions.add(button("Edit", () -> run(frame, () -> {
            var folder = list.getSelectedValue();
            if (folder == null) return;
            var name = new JTextField(folder.name());
            var columns = new JSpinner(new SpinnerNumberModel(folder.columns(), 1, 8, 1));
            if (JOptionPane.showConfirmDialog(frame, new Object[]{"Name", name, "Maximum columns", columns},
                "Edit folder", JOptionPane.OK_CANCEL_OPTION) == JOptionPane.OK_OPTION) {
                store.edit(folder.id(), name.getText(), (int)columns.getValue());
                reload.run();
            }
        })));
        actions.add(button("Repair shortcut", () -> run(frame, () -> {
            if (list.getSelectedValue() != null) store.launcher(store.load(list.getSelectedValue().id()));
        })));
        actions.add(button("Delete", () -> run(frame, () -> {
            var folder = list.getSelectedValue();
            if (folder != null && JOptionPane.showConfirmDialog(frame, "Remove folder and desktop shortcut? Stored files are kept.",
                NAME, JOptionPane.YES_NO_OPTION) == JOptionPane.YES_OPTION) { store.delete(folder.id()); reload.run(); }
        })));
        list.addMouseListener(new MouseAdapter() {
            @Override public void mouseClicked(MouseEvent e) {
                if (e.getClickCount() == 2 && list.getSelectedValue() != null) open(list.getSelectedValue());
            }
        });
        frame.add(actions, BorderLayout.NORTH);
        frame.add(new JScrollPane(list), BorderLayout.CENTER);
        frame.setSize(600, 350);
        frame.setLocationByPlatform(true);
        reload.run();
        frame.setVisible(true);
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
        Popup(Store.Folder folder) {
            this.folder = folder;
            frame = frame(folder.name());
            frame.setAlwaysOnTop(true);
            frame.setDefaultCloseOperation(WindowConstants.DISPOSE_ON_CLOSE);
            frame.setMinimumSize(new Dimension(260, 220));
            frame.setSize(466, 365);
            grid.setBackground(BACKGROUND);
            grid.setBorder(BorderFactory.createEmptyBorder(12, 12, 12, 12));
            scroll.getViewport().setBackground(BACKGROUND);
            scroll.getViewport().addComponentListener(new ComponentAdapter() {
                @Override public void componentResized(ComponentEvent e) {
                    if (columns() != renderedColumns) render();
                }
            });
            var transfer = new FileTransfer(null);
            grid.setTransferHandler(transfer);
            scroll.getViewport().setTransferHandler(transfer);
            var toolbar = new JPanel(new FlowLayout(FlowLayout.LEADING));
            toolbar.add(button("Add link", () -> run(frame, () -> {
                var chooser = new JFileChooser();
                chooser.setMultiSelectionEnabled(true);
                if (chooser.showOpenDialog(frame) != JFileChooser.APPROVE_OPTION) return;
                this.folder = store.change(this.folder.id(), current -> {
                    for (File file : chooser.getSelectedFiles()) {
                        var path = file.toPath().toAbsolutePath();
                        if (!current.files().contains(path)) current.files().add(path);
                    }
                });
                render();
            })));
            toolbar.add(button("Stored files", () -> run(frame,
                () -> launch(Files.createDirectories(store.storage(this.folder.id()))))));
            frame.add(toolbar, BorderLayout.NORTH);
            frame.add(scroll);
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

        private int columns() { return Math.max(1, Math.min(folder.columns(), (scroll.getViewport().getWidth() - 16) / 102)); }

        private void render() {
            renderedColumns = columns();
            grid.removeAll();
            if (folder.files().isEmpty()) {
                grid.setLayout(new BorderLayout());
                var empty = new JLabel("Drop files here or use Ctrl+V to move them.", SwingConstants.CENTER);
                empty.setForeground(Color.WHITE);
                grid.add(empty);
            } else {
                grid.setLayout(new GridLayout(0, renderedColumns, 8, 8));
                for (Path file : folder.files()) {
                    var name = file.getFileName().toString().replaceFirst("\\.desktop$", "");
                    var label = name.length() > 20 ? name.substring(0, 17) + "…" : name;
                    var tile = new JButton(label);
                    tile.setToolTipText(name);
                    tile.setIcon(FileSystemView.getFileSystemView().getSystemIcon(file.toFile(), 48, 48));
                    tile.setHorizontalTextPosition(SwingConstants.CENTER);
                    tile.setVerticalTextPosition(SwingConstants.BOTTOM);
                    tile.setForeground(Color.WHITE);
                    tile.setBackground(TILE);
                    tile.setOpaque(true);
                    tile.setContentAreaFilled(true);
                    tile.setBorder(BorderFactory.createEmptyBorder(8, 4, 8, 4));
                    tile.setPreferredSize(new Dimension(94, 91));
                    tile.addActionListener(e -> run(frame, () -> launch(file)));
                    tile.setTransferHandler(new FileTransfer(file));
                    tile.addMouseMotionListener(new MouseMotionAdapter() {
                        @Override public void mouseDragged(MouseEvent e) {
                            if (file.startsWith(store.storage(folder.id()))) {
                                tile.getModel().setArmed(false);
                                tile.getTransferHandler().exportAsDrag(tile, e, TransferHandler.MOVE);
                            }
                        }
                    });
                    grid.add(tile);
                }
                for (int i = folder.files().size() % renderedColumns; i > 0 && i < renderedColumns; i++)
                    grid.add(Box.createGlue());
                grid.setPreferredSize(new Dimension(90, ((folder.files().size() + renderedColumns - 1) / renderedColumns) * 99 + 16));
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
                if (source != null && Files.notExists(source))
                    run(frame, () -> { folder = store.change(folder.id(), item -> item.files().remove(source)); render(); });
            }
        }
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
