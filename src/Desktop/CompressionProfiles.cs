using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using ArchivePassword.Core;

namespace ArchivePassword.Desktop;

// Parameters only: source/output paths and passwords never belong in a profile.
public sealed record CompressionProfile(string Name, int Format, int Level, int VolumeOption, int VolumeUnit, string VolumeCustom)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsValid => Format >= 0 && Format < CompressionFormats.All.Count && Level is >= 0 and <= 4
        && VolumeOption is >= 0 and <= 4 && VolumeUnit is >= 0 and <= 1
        && VolumeCustom is not null && VolumeSizeOptions.TryGetBytes(VolumeOption, VolumeCustom, VolumeUnit, out _)
        && (VolumeOption == 0 || CompressionFormats.All[Format].Encryption);
}

public sealed partial class MainViewModel
{
    public ObservableCollection<CompressionProfile> CompressionProfiles { get; } = [];
    private CompressionProfile? selectedCompressionProfile, lastCompressionProfile;
    private bool applyingCompressionProfile;
    private string compressionProfileName = "";
    public string CompressionProfileName { get => compressionProfileName; set { if (Set(ref compressionProfileName, value)) CommandManager.InvalidateRequerySuggested(); } }
    public CompressionProfile? SelectedCompressionProfile
    {
        get => selectedCompressionProfile;
        set
        {
            if (!IsIdle || !Set(ref selectedCompressionProfile, value) || value is null) return;
            ApplyCompressionProfile(value); CompressionProfileName = value.Name;
        }
    }
    public ICommand SaveCompressionProfileCommand { get; private set; } = null!;
    public ICommand DeleteCompressionProfileCommand { get; private set; } = null!;
    private CompressionProfile CaptureCompressionProfile(string name) => new(name, CompressionFormat, CompressionLevel, volumeOption, volumeUnit, volumeCustom);
    private void ApplyCompressionProfile(CompressionProfile profile)
    {
        if (!profile.IsValid) return;
        applyingCompressionProfile=true;
        try
        {
            CompressionFormat = profile.Format; CompressionLevel = profile.Level;
            CompressionVolumeUnit = profile.VolumeUnit; CompressionVolumeCustom = profile.VolumeCustom; CompressionVolumeOption = profile.VolumeOption;
        }
        finally {applyingCompressionProfile=false;}
    }
    private void CompressionProfileOptionsChanged()
    {
        if(applyingCompressionProfile || selectedCompressionProfile is null)return;
        selectedCompressionProfile=null;Changed(nameof(SelectedCompressionProfile));
    }
    private void InitializeCompressionProfiles()
    {
        SaveCompressionProfileCommand = new AsyncCommand(async () =>
        {
            var profile = CaptureCompressionProfile(CompressionProfileName.Trim());
            var previous = CompressionProfiles.FirstOrDefault(item => item.Name.Equals(profile.Name, StringComparison.OrdinalIgnoreCase));
            if (previous is null) CompressionProfiles.Add(profile); else CompressionProfiles[CompressionProfiles.IndexOf(previous)] = profile;
            if (!await PersistCompressionProfilesAsync())
            {
                CompressionProfiles.Remove(profile); if (previous is not null) CompressionProfiles.Add(previous);
                return;
            }
            SelectedCompressionProfile = profile; CompressionMessage = UiText.Get("Compression.ProfileSaved");
        }, () => IsIdle && CompressionProfileName.Trim().Length is > 0 and <= 64 && CaptureCompressionProfile("").IsValid);
        DeleteCompressionProfileCommand = new AsyncCommand(async () =>
        {
            var profile = SelectedCompressionProfile!; var index = CompressionProfiles.IndexOf(profile);
            CompressionProfiles.Remove(profile);
            if (!await PersistCompressionProfilesAsync()) { CompressionProfiles.Insert(index, profile); return; }
            SelectedCompressionProfile = null; CompressionProfileName = ""; CompressionMessage = UiText.Get("Compression.ProfileDeleted");
        }, () => IsIdle && SelectedCompressionProfile is not null);
    }
    private async Task<bool> PersistCompressionProfilesAsync()
    {
        var ownsBusy=IsIdle;
        if(ownsBusy) {IsBusy=true;idle=new TaskCompletionSource();}
        try
        {
            if (!settingsWritable) { CompressionMessage = UiText.Get("Compression.ProfileSaveFailed"); return false; }
            await SaveSettingsAsync(); return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { CompressionMessage = UiText.Get("Compression.ProfileSaveFailed"); return false; }
        finally {if(ownsBusy) {IsBusy=false;idle.TrySetResult();}}
    }
}
