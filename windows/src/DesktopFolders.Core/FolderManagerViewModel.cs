using System.Collections.ObjectModel;
using System.ComponentModel;

namespace DesktopFolders.Core;

public sealed class FolderManagerViewModel(Preferences preferences, Func<bool> save) : INotifyPropertyChanged
{
    private Folder? selected;
    public ObservableCollection<Folder> Folders => preferences.Folders;
    public event PropertyChangedEventHandler? PropertyChanged;

    public Folder? Selected
    {
        get => selected;
        set
        {
            if (ReferenceEquals(selected, value)) return;
            selected = value;
            PropertyChanged?.Invoke(this, new(nameof(Selected)));
        }
    }

    public bool Add(Folder folder)
    {
        folder.Name = FolderRules.UniqueName(Folders, folder.Name);
        Folders.Add(folder);
        if (!save())
        {
            Folders.Remove(folder);
            return false;
        }
        Selected = folder;
        return true;
    }

    public bool Apply(Folder current, Folder draft)
    {
        var index = Folders.IndexOf(current);
        if (index < 0 || current.Id != draft.Id) return false;
        draft.Name = FolderRules.UniqueName(Folders, draft.Name, current.Id);
        Folders[index] = draft;
        if (!save())
        {
            Folders[index] = current;
            Selected = current;
            return false;
        }
        Selected = draft;
        return true;
    }

    public bool Delete(Folder folder)
    {
        var index = Folders.IndexOf(folder);
        if (index < 0) return false;
        Folders.RemoveAt(index);
        if (!save())
        {
            Folders.Insert(index, folder);
            Selected = folder;
            return false;
        }
        Selected = Folders.Count == 0 ? null : Folders[Math.Min(index, Folders.Count - 1)];
        return true;
    }

    public void MoveSelection(int offset)
    {
        if (Selected is not { } folder) return;
        var index = Folders.IndexOf(folder);
        var destination = index + offset;
        if (index < 0 || destination < 0 || destination >= Folders.Count) return;
        Folders.Move(index, destination);
        if (!save()) Folders.Move(destination, index);
        Selected = folder;
    }

    public static Folder Duplicate(Folder folder, string name)
    {
        var copy = Store.Clone(folder);
        copy.Id = Guid.NewGuid();
        copy.Name = name;
        return copy;
    }
}
