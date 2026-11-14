package popup;

import javax.imageio.ImageIO;
import javax.swing.*;
import javax.swing.filechooser.FileSystemView;
import java.awt.*;
import java.awt.image.BufferedImage;
import java.nio.file.*;

final class LinuxIcons {
    private LinuxIcons() {}

    static Icon load(Path file, int size) {
        try {
            String name = desktopIcon(file);
            if (!name.isEmpty()) {
                var image = find(name, size);
                if (image != null) return fit(image, size);
            }
            var image = FileSystemView.getFileSystemView().getSystemIcon(file.toFile(), size, size);
            if (image != null && image.getIconWidth() > 0 && image.getIconHeight() > 0) {
                var buffer = new BufferedImage(image.getIconWidth(), image.getIconHeight(), BufferedImage.TYPE_INT_ARGB);
                var g = buffer.createGraphics();
                image.paintIcon(null, g, 0, 0);
                g.dispose();
                return fit(buffer, size);
            }
        } catch (Exception ignored) { }
        return new ImageIcon(App.folderIcon(size));
    }

    static String desktopIcon(Path file) throws java.io.IOException {
        if (!file.toString().endsWith(".desktop") || !Files.isRegularFile(file) || Files.size(file) > 262144) return "";
        boolean main = false;
        for (var line : Files.readAllLines(file)) {
            if (line.startsWith("[")) main = line.equals("[Desktop Entry]");
            else if (main && line.startsWith("Icon=")) return line.substring(5).strip();
        }
        return "";
    }

    private static BufferedImage find(String name, int size) throws java.io.IOException {
        var path = Path.of(name);
        if (path.isAbsolute()) return read(path, size);
        if (path.getNameCount() != 1 || name.equals(".") || name.equals("..")) return null;
        var home = Path.of(System.getProperty("user.home"));
        String data = System.getenv().getOrDefault("XDG_DATA_HOME", home.resolve(".local/share").toString());
        for (var root : new Path[]{Path.of(data, "icons"), home.resolve(".icons"), Path.of("/usr/share/icons")}) {
            for (var theme : new String[]{"hicolor", "Mint-Y", "Adwaita", "Papirus"}) {
                for (var scale : new String[]{"128x128", "96x96", "64x64", "48x48", "scalable", "256x256", "32x32"})
                    for (var category : new String[]{"apps", "mimetypes"})
                        for (var extension : new String[]{".png", ".svg", ""}) {
                            var image = read(root.resolve(theme).resolve(scale).resolve(category).resolve(name + extension), size);
                            if (image != null) return image;
                        }
            }
        }
        for (var suffix : new String[]{".png", ".svg", ""}) {
            var image = read(Path.of("/usr/share/pixmaps", name + suffix), size);
            if (image != null) return image;
        }
        return null;
    }

    private static BufferedImage read(Path path, int size) throws java.io.IOException {
        if (!Files.isRegularFile(path)) return null;
        if (!path.toString().endsWith(".svg")) return ImageIO.read(path.toFile());
        try {
            var process = new ProcessBuilder("rsvg-convert", "-w", Integer.toString(size * 2), "-h",
                Integer.toString(size * 2), "--keep-aspect-ratio", "--", path.toString())
                .redirectError(ProcessBuilder.Redirect.DISCARD).start();
            try (var input = process.getInputStream()) { return ImageIO.read(input); }
            finally { process.destroy(); }
        } catch (java.io.IOException ignored) { return null; }
    }

    private static Icon fit(BufferedImage source, int size) {
        var target = new BufferedImage(size, size, BufferedImage.TYPE_INT_ARGB);
        var g = target.createGraphics();
        double scale = Math.min((double)size / source.getWidth(), (double)size / source.getHeight());
        int width = (int)Math.round(source.getWidth() * scale), height = (int)Math.round(source.getHeight() * scale);
        g.setRenderingHint(RenderingHints.KEY_INTERPOLATION, RenderingHints.VALUE_INTERPOLATION_BICUBIC);
        g.drawImage(source, (size - width) / 2, (size - height) / 2, width, height, null);
        g.dispose();
        return new ImageIcon(target);
    }
}
