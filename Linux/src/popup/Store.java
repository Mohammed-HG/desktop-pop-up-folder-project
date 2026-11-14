package popup;

import java.io.*;
import java.nio.channels.FileChannel;
import java.nio.file.*;
import java.nio.file.attribute.PosixFilePermissions;
import java.util.*;

final class Store {
    static final String OWNER = "X-DesktopPopupFolder-Id=";
    record Appearance(int width, int height, int iconSize, boolean labels, boolean closeOutside,
                      boolean closeButton, String theme, int transparency, String color, String symbol,
                      int spacing, boolean hover) {
        Appearance(int width, int height, int iconSize, boolean labels, boolean closeOutside,
                   boolean closeButton, String theme, int transparency) {
            this(width, height, iconSize, labels, closeOutside, closeButton, theme, transparency, "#4293ee", "", 8, true);
        }
        Appearance resized(int width, int height) {
            return new Appearance(width, height, iconSize, labels, closeOutside, closeButton,
                theme, transparency, color, symbol, spacing, hover);
        }
        static final Appearance DEFAULT = new Appearance(466, 365, 48, true, false, true, "System", 0);
    }
    record Folder(UUID id, String name, List<Path> files, int columns, Appearance appearance) {
        @Override public String toString() { return name; }
    }
    interface Change { void run(Folder folder) throws IOException; }
    private final Path root, desktop;
    private final String command;

    Store(Path root, Path desktop, String command) throws IOException {
        this.root = Files.createDirectories(root);
        this.desktop = desktop;
        this.command = command;
    }

    Path storage(UUID id) { return root.resolve("files").resolve(id.toString()); }
    private Path config(UUID id) { return root.resolve(id + ".properties"); }

    Properties settings() throws IOException {
        var p = new Properties();
        var path = root.resolve("settings");
        if (Files.exists(path)) try (var in = Files.newBufferedReader(path)) { p.load(in); }
        return p;
    }

    void settings(String theme, boolean arabic) throws IOException {
        locked(() -> {
            var p = new Properties();
            p.setProperty("theme", theme);
            p.setProperty("language", arabic ? "ar" : "en");
            writeProperties(p, root.resolve("settings"));
            return null;
        });
    }

    Folder load(UUID id) throws IOException {
        var p = new Properties();
        try (var in = Files.newBufferedReader(config(id))) { p.load(in); }
        var files = new ArrayList<Path>();
        for (int i = 0; p.containsKey("file." + i); i++) files.add(Path.of(p.getProperty("file." + i)));
        return new Folder(id, p.getProperty("name"), files, number(p, "columns", 4, 1, 8),
            new Appearance(number(p, "width", 466, 260, 1200), number(p, "height", 365, 220, 900),
                number(p, "iconSize", 48, 24, 96), Boolean.parseBoolean(p.getProperty("labels", "true")),
                Boolean.parseBoolean(p.getProperty("closeOutside", "false")),
                Boolean.parseBoolean(p.getProperty("closeButton", "true")), p.getProperty("theme", "System"),
                number(p, "transparency", 0, 0, 100), p.getProperty("color", "#4293ee"),
                p.getProperty("symbol", ""), number(p, "spacing", 8, 0, 24),
                Boolean.parseBoolean(p.getProperty("hover", "true"))));
    }

    private static int number(Properties p, String key, int fallback, int min, int max) {
        try { return Math.max(min, Math.min(max, Integer.parseInt(p.getProperty(key, Integer.toString(fallback))))); }
        catch (NumberFormatException ignored) { return fallback; }
    }

    List<Folder> folders() throws IOException {
        var list = new ArrayList<Folder>();
        try (var files = Files.newDirectoryStream(root, "*.properties")) {
            for (Path path : files) {
                var name = path.getFileName().toString();
                try { list.add(load(UUID.fromString(name.substring(0, name.length() - 11)))); }
                catch (IllegalArgumentException ignored) { }
            }
        }
        list.sort(Comparator.comparing(Folder::name, String.CASE_INSENSITIVE_ORDER));
        return list;
    }

    Folder create(String name) throws IOException {
        return create(name, 4, Appearance.DEFAULT, List.of());
    }

    Folder create(String name, int columns, Appearance appearance, List<Path> files) throws IOException {
        return locked(() -> {
            var folder = new Folder(UUID.randomUUID(), name.strip(), new ArrayList<>(files), columns, appearance);
            launcher(folder);
            try { write(folder); }
            catch (IOException error) { removeLauncher(folder); throw error; }
            return folder;
        });
    }

    Folder edit(UUID id, String name, int columns) throws IOException {
        return edit(id, name, columns, load(id).appearance());
    }

    Folder edit(UUID id, String name, int columns, Appearance appearance) throws IOException {
        return edit(id, name, columns, appearance, null);
    }

    Folder edit(UUID id, String name, int columns, Appearance appearance, List<Path> files) throws IOException {
        return locked(() -> {
            var old = load(id);
            var updated = new Folder(id, name.strip(), files == null ? old.files() : new ArrayList<>(files), columns, appearance);
            launcher(updated);
            try { write(updated); }
            catch (IOException error) {
                if (!shortcut(old).equals(shortcut(updated))) removeLauncher(updated);
                else launcher(old);
                throw error;
            }
            if (!shortcut(old).equals(shortcut(updated))) removeLauncher(old);
            return updated;
        });
    }

    void delete(UUID id) throws IOException {
        locked(() -> {
            var folder = load(id);
            removeLauncher(folder);
            Files.delete(config(id));
            return null;
        });
    }

    Folder change(UUID id, Change change) throws IOException {
        return locked(() -> {
            var folder = load(id);
            change.run(folder);
            write(folder);
            return folder;
        });
    }

    Folder moveIn(UUID id, List<Path> sources) throws IOException {
        return locked(() -> {
            var folder = load(id);
            var directory = Files.createDirectories(storage(id));
            var moves = new LinkedHashMap<Path, Path>();
            try {
                for (Path original : new LinkedHashSet<>(sources)) {
                    var source = original.toAbsolutePath().normalize();
                    if (source.startsWith(directory) || !Files.isRegularFile(source, LinkOption.NOFOLLOW_LINKS))
                        throw new IOException("Choose regular files outside this folder.");
                    var name = source.getFileName().toString();
                    var destination = directory.resolve(name);
                    int dot = name.lastIndexOf('.');
                    var stem = dot > 0 ? name.substring(0, dot) : name;
                    var extension = dot > 0 ? name.substring(dot) : "";
                    for (int n = 2; Files.exists(destination); n++)
                        destination = directory.resolve(stem + " (" + n + ")" + extension);
                    Files.move(source, destination);
                    moves.put(source, destination);
                    if (Files.exists(source)) throw new IOException("The original file could not be moved.");
                    folder.files().remove(source);
                    folder.files().add(destination);
                }
                write(folder);
                return folder;
            } catch (IOException failure) {
                var reversed = new ArrayList<>(moves.entrySet());
                Collections.reverse(reversed);
                for (var move : reversed) {
                    try {
                        if (Files.exists(move.getKey())) Files.deleteIfExists(move.getValue());
                        else Files.move(move.getValue(), move.getKey());
                    } catch (IOException restore) { failure.addSuppressed(restore); }
                }
                throw failure;
            }
        });
    }

    void launcher(Folder folder) throws IOException {
        Files.createDirectories(desktop);
        var path = shortcut(folder);
        if (Files.exists(path, LinkOption.NOFOLLOW_LINKS) && !owned(path, folder.id()))
            throw new IOException("A desktop shortcut with that name already exists.");
        var icons = Files.createDirectories(root.resolve("icons"));
        var icon = icons.resolve(folder.id() + "-" + Integer.toUnsignedString(
            Objects.hash(folder.appearance().color(), folder.appearance().symbol())) + ".png");
        if (!Files.exists(icon)) javax.imageio.ImageIO.write(App.folderIcon(256, folder.appearance()), "png", icon.toFile());
        var content = "[Desktop Entry]\nType=Application\nName=" + value(folder.name()) +
            "\nExec=" + command + " --open " + folder.id() + "\nIcon=" + value(icon.toString()) +
            "\nTerminal=false\n" + OWNER + folder.id() + "\n";
        var temp = Files.createTempFile(desktop, ".desktop-popup-", ".tmp");
        try {
            Files.writeString(temp, content);
            if (temp.getFileSystem().supportedFileAttributeViews().contains("posix"))
                Files.setPosixFilePermissions(temp, PosixFilePermissions.fromString("rwxr-xr-x"));
            replace(temp, path);
        } finally { Files.deleteIfExists(temp); }
    }

    private Path shortcut(Folder folder) {
        String name = folder.name().strip();
        if (name.isEmpty() || name.length() > 80 || name.startsWith(".") || name.indexOf('/') >= 0 ||
            name.codePoints().anyMatch(Character::isISOControl))
            throw new IllegalArgumentException("Enter a folder name of 1–80 characters without a slash or leading dot.");
        return desktop.resolve(name + ".desktop");
    }

    private void removeLauncher(Folder folder) throws IOException {
        Path path = shortcut(folder);
        if (owned(path, folder.id())) Files.delete(path);
    }

    private static boolean owned(Path path, UUID id) throws IOException {
        return Files.isRegularFile(path, LinkOption.NOFOLLOW_LINKS) && Files.size(path) < 32768
            && Files.readAllLines(path).contains(OWNER + id);
    }

    static String value(String raw) { return raw.replace("\\", "\\\\").replace("\n", "\\n").replace("\r", "\\r"); }
    static String argument(String raw) {
        return value("\"" + raw.replace("\\", "\\\\").replace("\"", "\\\"").replace("$", "\\$")
            .replace("\u0060", "\\\u0060").replace("%", "%%") + "\"");
    }

    private void write(Folder folder) throws IOException {
        var p = new Properties();
        p.setProperty("name", folder.name());
        p.setProperty("columns", Integer.toString(folder.columns()));
        var a = folder.appearance();
        p.setProperty("width", Integer.toString(a.width()));
        p.setProperty("height", Integer.toString(a.height()));
        p.setProperty("iconSize", Integer.toString(a.iconSize()));
        p.setProperty("labels", Boolean.toString(a.labels()));
        p.setProperty("closeOutside", Boolean.toString(a.closeOutside()));
        p.setProperty("closeButton", Boolean.toString(a.closeButton()));
        p.setProperty("theme", a.theme());
        p.setProperty("transparency", Integer.toString(a.transparency()));
        p.setProperty("color", a.color());
        p.setProperty("symbol", a.symbol());
        p.setProperty("spacing", Integer.toString(a.spacing()));
        p.setProperty("hover", Boolean.toString(a.hover()));
        for (int i = 0; i < folder.files().size(); i++) p.setProperty("file." + i, folder.files().get(i).toString());
        writeProperties(p, config(folder.id()));
    }

    private void writeProperties(Properties p, Path destination) throws IOException {
        var temp = Files.createTempFile(root, ".folder-", ".tmp");
        try {
            try (var out = Files.newBufferedWriter(temp)) { p.store(out, null); }
            replace(temp, destination);
        } finally { Files.deleteIfExists(temp); }
    }

    private static void replace(Path source, Path target) throws IOException {
        try { Files.move(source, target, StandardCopyOption.REPLACE_EXISTING, StandardCopyOption.ATOMIC_MOVE); }
        catch (AtomicMoveNotSupportedException error) { Files.move(source, target, StandardCopyOption.REPLACE_EXISTING); }
    }

    private interface Operation<T> { T run() throws IOException; }
    private <T> T locked(Operation<T> operation) throws IOException {
        try (var file = FileChannel.open(root.resolve(".lock"), StandardOpenOption.CREATE, StandardOpenOption.WRITE);
             var lock = file.lock()) { return operation.run(); }
    }
}
