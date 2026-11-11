using DesktopFolders.Core;

namespace DesktopFolders.Tests;

[TestClass]
public sealed class StoreTests
{
    [TestMethod]
    public void PreservesFolderOrderAndIndependentSettings()
    {
        using var fixture = new Fixture();
        var data = new Preferences { Arabic = true, Animate = false, StartWithWindows = true };
        var folder = new Folder { Name = "عمل", Locked = true };
        folder.Entries.Add(new() { Name = "Docs", Target = "https://example.org/" });
        folder.Panel.Background = Backdrop.Translucent;
        folder.Panel.Transparency = 100;
        folder.Panel.RememberPosition = true;
        folder.Panel.X = -400;
        var native = new Folder { Name = "Demo", ExplorerFolder = true, IconColor = "#72B755" };
        native.Entries.Add(new() { Name = "Notes", Icon = "office:Word" });
        data.Folders.Add(native);
        data.Folders.Add(folder);
        data.Folders.Move(1, 0);
        fixture.Store.Save(data);

        var loaded = fixture.Store.Load();
        Assert.IsTrue(loaded.Arabic);
        Assert.IsTrue(loaded.StartWithWindows);
        Assert.IsFalse(loaded.Animate);
        Assert.AreEqual(folder.Id, loaded.Folders[0].Id);
        Assert.AreEqual("عمل", loaded.Folders[0].Name);
        Assert.IsTrue(loaded.Folders[0].Locked);
        Assert.AreEqual(-400, loaded.Folders[0].Panel.X);
        Assert.AreEqual(100d, loaded.Folders[0].Panel.Transparency);
        Assert.AreEqual(Backdrop.Translucent, loaded.Folders[0].Panel.Background);
        Assert.AreEqual("https://example.org/", loaded.Folders[0].Entries.Single().Target);
        Assert.AreEqual(0d, loaded.Folders[1].Panel.Transparency);
        Assert.IsFalse(loaded.Folders[0].ExplorerFolder);
        Assert.IsTrue(loaded.Folders[1].ExplorerFolder);
        Assert.AreEqual("#72B755", loaded.Folders[1].IconColor);
        Assert.AreEqual("office:Word", loaded.Folders[1].Entries.Single().Icon);
    }

    [TestMethod]
    public void RecoversPreviousSnapshotWhenCurrentFileIsCorrupt()
    {
        using var fixture = new Fixture();
        var first = new Preferences();
        first.Folders.Add(new() { Name = "Original", Locked = true });
        fixture.Store.Save(first);
        fixture.Store.Save(new Preferences());
        File.WriteAllText(fixture.Store.Pathname, "{ broken");
        var loaded = fixture.Store.Load();
        Assert.AreEqual("backup", fixture.Store.Recovery);
        Assert.AreEqual("Original", loaded.Folders.Single().Name);
        Assert.IsTrue(loaded.Folders.Single().Locked);
        Assert.HasCount(1, Directory.GetFiles(fixture.Directory, "*.corrupt-*"));
        fixture.Store.Save(loaded);
        Assert.AreEqual("Original", new Store(fixture.Directory).Load().Folders.Single().Name);
    }

    [TestMethod]
    public void SetsAsideInvalidStructureWhenBackupIsAbsent()
    {
        using var fixture = new Fixture();
        File.WriteAllText(fixture.Store.Pathname, """{"Folders":[null]}""");
        Assert.HasCount(0, fixture.Store.Load().Folders);
        Assert.AreEqual("reset", fixture.Store.Recovery);
        Assert.HasCount(1, Directory.GetFiles(fixture.Directory, "*.corrupt-*"));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly DirectoryInfo directory = System.IO.Directory.CreateTempSubdirectory("DesktopFolders.Tests-");
        public string Directory => directory.FullName;
        public Store Store => field ??= new Store(Directory);
        public void Dispose() => directory.Delete(recursive: true);
    }
}
