using System.IO;
using ArchivePassword.Core;

namespace ArchivePassword.Desktop;
public sealed partial class MainViewModel
{
    private string shellDestinationDescription = "";
    private ShellAction? shellAction;
    public string ShellDestinationDescription { get => shellDestinationDescription; private set => Set(ref shellDestinationDescription, value); }
    public bool HasShellDestinations => ShellDestinationDescription.Length > 0;
    private void ClearShellDestinations()
    {
        foreach (var task in Tasks) task.ShellOutputPath = null;
        shellAction = null;
        ShellDestinationDescription = ""; Changed(nameof(HasShellDestinations));
        Changed(nameof(IsOutputPathReadOnly));Changed(nameof(DisplayOutputPath));
    }
    public async Task ApplyShellSelectionAsync(ShellSelection supplied)
    {
        var selection = supplied.Validate();
        if (!IsIdle) throw new InvalidOperationException("Application is busy.");
        if (selection.Action == ShellAction.Compress)
        {
            Page = 3; AddCompressionSources(selection.Paths);
            var first = selection.Paths[0].TrimEnd(Path.DirectorySeparatorChar);
            var parent = Path.GetDirectoryName(first);
            var name = selection.Paths.Count == 1 && parent is not null ? Path.GetFileName(first) : UiText.Get("T_8b512613643a");
            CompressionOutput = Path.Combine(parent ?? selection.Paths[0], name + (Is7zCompression ? ".7z" : ".zip"));
            CompressionMessage = UiText.Format("T_1c7107214dba", selection.Paths.Count);
            return;
        }
        Page = 0; Tasks.Clear(); OutputPath = selection.OutputFor(selection.Paths[0]);
        shellAction = selection.Action;
        foreach (var path in selection.Paths) Tasks.Add(new ArchiveTaskRow(path) { ShellOutputPath = selection.OutputFor(path) });
        ShellDestinationDescription = selection.Action == ShellAction.ExtractHere
            ? UiText.Get("T_d1de1249c3ed")
            : UiText.Get("T_1015b5abe5e7");
        Changed(nameof(HasShellDestinations));
        Changed(nameof(IsOutputPathReadOnly));Changed(nameof(DisplayOutputPath));
        if (!ExtractCommand.CanExecute(null)) { Banner = UiText.Get("T_f024ef58cfce"); return; }
        ExtractCommand.Execute(null); await WaitForIdleAsync();
    }
}
