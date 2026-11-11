using DesktopFolders.Core;

namespace DesktopFolders.Tests;

[TestClass]
public sealed class FolderTests
{
    [TestMethod]
    public void NormalizesPanelBoundsAndInvalidValues()
    {
        var settings = new PanelSettings
        {
            Width = double.NaN, Height = 1200, Columns = 0, IconSize = 500, Padding = -2,
            Transparency = 150, Color = "#bad", Background = (Backdrop)70, Duration = -1
        };
        settings.Normalize();
        Assert.AreEqual(240d, settings.Width);
        Assert.AreEqual(1000d, settings.Height);
        Assert.AreEqual(1, settings.Columns);
        Assert.AreEqual(96d, settings.IconSize);
        Assert.AreEqual(0d, settings.Padding);
        Assert.AreEqual(100d, settings.Transparency);
        Assert.AreEqual("#30364C", settings.Color);
        Assert.AreEqual(Backdrop.Light, settings.Background);
        Assert.AreEqual(0, settings.Duration);
    }

    [TestMethod]
    public void UniqueNamesIgnoreCaseAndSkipTheEditedFolder()
    {
        Folder[] folders = [new() { Name = "Work" }, new() { Name = "WORK (2)" }];
        Assert.AreEqual("work (3)", FolderRules.UniqueName(folders, " work "));
        Assert.AreEqual("Work", FolderRules.UniqueName(folders, "Work", folders[0].Id));
    }

    [TestMethod]
    public void DraftEditsAndReorderingLeaveTheOriginalIntact()
    {
        var original = new Folder { Name = "Work", Locked = true };
        original.Entries.Add(new() { Name = "First" });
        original.Entries.Add(new() { Name = "Second" });
        var draft = Store.Clone(original);
        draft.Panel.Width = 750;
        draft.Entries.Move(1, 0);
        draft.Entries[0].Name = "Changed";
        Assert.AreEqual(original.Id, draft.Id);
        Assert.IsTrue(draft.Locked);
        Assert.AreEqual(420d, original.Panel.Width);
        CollectionAssert.AreEqual(new[] { "First", "Second" }, original.Entries.Select(entry => entry.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "Changed", "First" }, draft.Entries.Select(entry => entry.Name).ToArray());
    }

    [TestMethod]
    [DataRow("https://example.org/docs", TargetKind.Web)]
    [DataRow("https://", TargetKind.Invalid)]
    [DataRow("http://example.org/", TargetKind.Web)]
    [DataRow("javascript:alert(1)", TargetKind.Invalid)]
    [DataRow(@"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", TargetKind.AppsFolder)]
    [DataRow(@"shell:AppsFolder\..\other", TargetKind.Invalid)]
    [DataRow(@"shell:AppsFolder\", TargetKind.Invalid)]
    [DataRow("notepad.exe", TargetKind.Invalid)]
    [DataRow(null, TargetKind.Invalid)]
    public void ClassifiesTargets(string? value, TargetKind expected) =>
        Assert.AreEqual(expected, Targets.Classify(value));

    [TestMethod]
    public void ManagerRestoresFoldersWhenSaveFails()
    {
        var data = new Preferences();
        var folder = new Folder { Name = "Work", Locked = true };
        data.Folders.Add(folder);
        var manager = new FolderManagerViewModel(data, () => false) { Selected = folder };
        var draft = Store.Clone(folder);
        draft.Name = "Changed";
        Assert.IsFalse(manager.Apply(folder, draft));
        Assert.AreSame(folder, manager.Selected);
        Assert.IsFalse(manager.Add(new() { Name = "Other" }));
        Assert.IsFalse(manager.Delete(folder));
        Assert.AreSame(folder, data.Folders.Single());
        Assert.AreEqual("Work", folder.Name);
    }

    [TestMethod]
    public void ManagerDuplicatesAndMovesFoldersWithIndependentIds()
    {
        var data = new Preferences();
        var manager = new FolderManagerViewModel(data, () => true);
        var folder = new Folder { Name = "Work", Locked = true };
        Assert.IsTrue(manager.Add(folder));
        var copy = FolderManagerViewModel.Duplicate(folder, "Work");
        Assert.IsTrue(manager.Add(copy));
        manager.MoveSelection(-1);
        Assert.AreSame(copy, data.Folders[0]);
        Assert.AreEqual("Work (2)", copy.Name);
        Assert.AreNotEqual(folder.Id, copy.Id);
        Assert.IsTrue(copy.Locked);
    }

    [TestMethod]
    public void AcceptsAbsoluteAppAndDocumentPaths()
    {
        var root = Path.GetTempPath();
        foreach (var extension in new[] { ".EXE", ".lnk", ".url", ".pdf", ".txt" })
            Assert.AreEqual(TargetKind.File, Targets.Classify(Path.Combine(root, "example" + extension)));
    }

    [TestMethod]
    public void ImportsExistingFilesAndUrlsWithoutDuplicatingTargets()
    {
        var path = Path.GetTempFileName();
        try
        {
            var entries = Targets.NewEntries([path, path, Path.GetDirectoryName(path)!, "https://example.org/", "https://example.org"], []).ToArray();
            Assert.HasCount(2, entries);
            Assert.AreEqual(Path.GetFileName(path), entries[0].Name);
            Assert.AreEqual("https://example.org/", entries[1].Target);
            Assert.IsEmpty(Targets.NewEntries([path, "https://example.org"], entries));
        }
        finally { File.Delete(path); }
    }
}
