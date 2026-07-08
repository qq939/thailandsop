using Microsoft.Win32;
using System.Windows;

namespace VideoInferenceDemo;

public sealed class WpfFolderPickerService : IFolderPickerService
{
    private readonly Window? _owner;

    public WpfFolderPickerService(Window? owner = null)
    {
        _owner = owner;
    }

    public string? PickFolder(string title, string? initialDirectory = null)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title
        };

        if (!string.IsNullOrWhiteSpace(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        var owner = _owner ?? Application.Current?.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive);
        return dialog.ShowDialog(owner) == true ? dialog.FolderName : null;
    }
}
