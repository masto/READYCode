// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.ObjectModel;
using ReadyCode.Assembler;
using ReadyCode.Avalonia.Models;
using ReadyCode.C64U;
using ReadyCode.Diff;
using ReadyCode.Models;
using ReadyCode.Settings;
using ReadyCode.Tokenizer;

namespace ReadyCode.Avalonia.ViewModels;

/// <summary>
/// Connection state of the C64U FTP explorer panel.
/// </summary>
public enum C64UConnectionState
{
    /// <summary>Not connected, and not currently trying to be.</summary>
    NotConnected,

    /// <summary>A connection attempt is in progress.</summary>
    Connecting,

    /// <summary>Connected, with the root folder listing loaded.</summary>
    Connected,
}

/// <summary>
/// The C64 Ultimate half of <see cref="MainViewModel"/>: the FTP explorer tree, drive mount
/// status, and Transfer/Run/machine-action operations against the device's REST API. Ported from
/// the WPF view model, following the same patterns as this file's own VICE region.
/// </summary>
public partial class MainViewModel
{
    #region Private Fields

    private C64UFtpClient? _c64uFtpClient;
    private C64UConnectionState _c64uConnectionState = C64UConnectionState.NotConnected;
    private string? _c64uDeviceHostname;
    private C64UDriveStatus? _c64uDriveA;
    private C64UDriveStatus? _c64uDriveB;

    #endregion

    #region Public Properties

    /// <summary>Gets the root items of the C64U FTP explorer tree.</summary>
    public ObservableCollection<C64UFileItem> C64UFileItems { get; } = new();

    /// <summary>Gets the C64U explorer's connection state.</summary>
    public C64UConnectionState C64UConnectionState
    {
        get => _c64uConnectionState;
        private set
        {
            if (_c64uConnectionState == value) return;
            _c64uConnectionState = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsC64UNotConnected));
            OnPropertyChanged(nameof(IsC64UConnecting));
            OnPropertyChanged(nameof(IsC64UConnected));
        }
    }

    /// <summary>Gets whether the C64U explorer is not connected.</summary>
    public bool IsC64UNotConnected => C64UConnectionState == C64UConnectionState.NotConnected;

    /// <summary>Gets whether a connection attempt is in progress.</summary>
    public bool IsC64UConnecting => C64UConnectionState == C64UConnectionState.Connecting;

    /// <summary>Gets whether the C64U explorer is connected.</summary>
    public bool IsC64UConnected => C64UConnectionState == C64UConnectionState.Connected;

    /// <summary>Gets the host or IP address parsed out of <see cref="AppSettings.C64UUrl"/>.</summary>
    public string C64UFtpHost => GetC64UFtpHost(Settings.C64UUrl);

    /// <summary>Gets the device's own reported hostname, once connected, or null.</summary>
    public string? C64UDeviceHostname
    {
        get => _c64uDeviceHostname;
        private set { if (_c64uDeviceHostname == value) return; _c64uDeviceHostname = value; OnPropertyChanged(); OnPropertyChanged(nameof(C64UHeaderText)); }
    }

    /// <summary>Gets the header text shown above the C64U tree: host and device hostname, if known.</summary>
    public string C64UHeaderText => string.IsNullOrEmpty(C64UFtpHost)
        ? "C64U"
        : C64UDeviceHostname != null ? $"{C64UFtpHost} — {C64UDeviceHostname}" : C64UFtpHost;

    /// <summary>Gets the live FTP connection, for the view's direct download/upload calls.</summary>
    public C64UFtpClient? C64UFtp => _c64uFtpClient;

    /// <summary>Gets the status of Drive A, or null if unknown/not connected.</summary>
    public C64UDriveStatus? C64UDriveA
    {
        get => _c64uDriveA;
        private set { if (ReferenceEquals(_c64uDriveA, value)) return; _c64uDriveA = value; OnPropertyChanged(); OnPropertyChanged(nameof(C64UDriveALabel)); OnPropertyChanged(nameof(IsC64UDriveAMounted)); }
    }

    /// <summary>Gets the status of Drive B, or null if unknown/not connected.</summary>
    public C64UDriveStatus? C64UDriveB
    {
        get => _c64uDriveB;
        private set { if (ReferenceEquals(_c64uDriveB, value)) return; _c64uDriveB = value; OnPropertyChanged(); OnPropertyChanged(nameof(C64UDriveBLabel)); OnPropertyChanged(nameof(IsC64UDriveBMounted)); }
    }

    /// <summary>Gets the file name mounted in Drive A, or "empty".</summary>
    public string C64UDriveALabel => string.IsNullOrEmpty(C64UDriveA?.ImageFile) ? "empty" : Path.GetFileName(C64UDriveA.ImageFile);

    /// <summary>Gets the file name mounted in Drive B, or "empty".</summary>
    public string C64UDriveBLabel => string.IsNullOrEmpty(C64UDriveB?.ImageFile) ? "empty" : Path.GetFileName(C64UDriveB.ImageFile);

    /// <summary>Gets whether Drive A has a disk image mounted.</summary>
    public bool IsC64UDriveAMounted => !string.IsNullOrEmpty(C64UDriveA?.ImageFile);

    /// <summary>Gets whether Drive B has a disk image mounted.</summary>
    public bool IsC64UDriveBMounted => !string.IsNullOrEmpty(C64UDriveB?.ImageFile);

    /// <summary>
    /// Gets or sets which left-panel tab is active ("Explorer" or "C64U"). Persisted in settings.
    /// </summary>
    public string ActiveLeftPanelTab
    {
        get => Settings.ActiveLeftPanel;
        set
        {
            if (Settings.ActiveLeftPanel == value) return;
            Settings.ActiveLeftPanel = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsExplorerTabActive));
            OnPropertyChanged(nameof(IsC64UTabActive));
        }
    }

    /// <summary>Gets whether the Explorer tab of the left panel is the active one.</summary>
    public bool IsExplorerTabActive => ActiveLeftPanelTab != "C64U";

    /// <summary>Gets whether the C64U tab of the left panel is the active one.</summary>
    public bool IsC64UTabActive => ActiveLeftPanelTab == "C64U";

    #endregion

    #region Public Methods - Connection

    /// <summary>
    /// Connects to the C64 Ultimate's FTP server using the host derived from
    /// <see cref="AppSettings.C64UUrl"/>, and loads the root folder listing on success.
    /// </summary>
    public async Task ConnectToC64UAsync()
    {
        string host = C64UFtpHost;
        if (string.IsNullOrWhiteSpace(host))
        {
            SetStatus("Please set the Commodore 64 Ultimate URL in Settings first.", StatusType.Error);
            return;
        }

        C64UConnectionState = C64UConnectionState.Connecting;

        var client = new C64UFtpClient();
        try
        {
            await client.ConnectAsync(host);
            var entries = await client.ListDirectoryAsync("/");

            _c64uFtpClient?.Dispose();
            _c64uFtpClient = client;

            C64UFileItems.Clear();
            foreach (var entry in entries)
                C64UFileItems.Add(new C64UFileItem(client, entry.FullPath, entry.IsFolder, entry.Size));

            C64UConnectionState = C64UConnectionState.Connected;

            // Best-effort: the panel header still shows the FTP host if the REST API is
            // unreachable, so a failure here shouldn't affect the FTP connection itself.
            try { C64UDeviceHostname = (await new C64UltimateClient().GetInfoAsync(Settings.C64UUrl)).Hostname; }
            catch { C64UDeviceHostname = null; }

            await RefreshC64UDriveStatusAsync();
        }
        catch (Exception ex)
        {
            client.Dispose();
            C64UConnectionState = C64UConnectionState.NotConnected;
            C64UDeviceHostname = null;
            C64UDriveA = null;
            C64UDriveB = null;
            SetStatus($"Could not connect to the C64 Ultimate: {ex.Message}", StatusType.Error);
        }
    }

    /// <summary>Disconnects from the C64U FTP server and resets the explorer's state.</summary>
    public void DisconnectC64U()
    {
        _c64uFtpClient?.Dispose();
        _c64uFtpClient = null;
        C64UFileItems.Clear();
        C64UDeviceHostname = null;
        C64UDriveA = null;
        C64UDriveB = null;
        C64UConnectionState = C64UConnectionState.NotConnected;
    }

    /// <summary>
    /// Reloads the C64U FTP explorer's root folder listing, preserving the expanded state of any
    /// folders.
    /// </summary>
    public async Task RefreshC64UFolderAsync()
    {
        if (_c64uFtpClient == null) return;

        var expandedPaths = C64UFileItems
            .Where(i => i.IsFolder && i.IsExpanded)
            .Select(i => i.FullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        try
        {
            var entries = await _c64uFtpClient.ListDirectoryAsync("/");
            C64UFileItems.Clear();
            foreach (var entry in entries)
                C64UFileItems.Add(new C64UFileItem(_c64uFtpClient, entry.FullPath, entry.IsFolder, entry.Size));

            foreach (var item in C64UFileItems.Where(i => i.IsFolder && expandedPaths.Contains(i.FullPath)))
                item.IsExpanded = true;

            await RefreshC64UDriveStatusAsync();
        }
        catch (Exception ex)
        {
            DisconnectC64U();
            SetStatus($"Lost connection to the C64 Ultimate: {ex.Message}", StatusType.Error);
        }
    }

    /// <summary>
    /// Refreshes Drive A/B mount status from the device's REST API. Best-effort - a failure here
    /// doesn't affect the FTP connection or file listing.
    /// </summary>
    public async Task RefreshC64UDriveStatusAsync()
    {
        try
        {
            var drives = await new C64UltimateClient().GetDrivesAsync(Settings.C64UUrl);
            C64UDriveA = drives.FirstOrDefault(d => d.Id == "a");
            C64UDriveB = drives.FirstOrDefault(d => d.Id == "b");
        }
        catch
        {
            C64UDriveA = null;
            C64UDriveB = null;
        }
    }

    /// <summary>
    /// Mounts a disk image already on the device's storage to the given drive, then refreshes
    /// drive status so the footer reflects the change.
    /// </summary>
    public async Task MountC64UDriveAsync(string driveId, string imagePath)
    {
        if (!EnsureC64UUrlConfigured()) return;

        try
        {
            await new C64UltimateClient().MountDriveAsync(Settings.C64UUrl, driveId, imagePath);
            SetStatus($"Mounted \"{Path.GetFileName(imagePath)}\" to Drive {driveId.ToUpperInvariant()}.");
            await RefreshC64UDriveStatusAsync();
        }
        catch (Exception ex)
        {
            SetStatus($"Could not mount to Drive {driveId.ToUpperInvariant()}: {ex.Message}", StatusType.Error);
        }
    }

    /// <summary>Ejects the disk image currently mounted on the given drive.</summary>
    public async Task EjectC64UDriveAsync(string driveId)
    {
        if (!EnsureC64UUrlConfigured()) return;

        try
        {
            await new C64UltimateClient().RemoveDriveAsync(Settings.C64UUrl, driveId);
            SetStatus($"Ejected Drive {driveId.ToUpperInvariant()}.");
            await RefreshC64UDriveStatusAsync();
        }
        catch (Exception ex)
        {
            SetStatus($"Could not eject Drive {driveId.ToUpperInvariant()}: {ex.Message}", StatusType.Error);
        }
    }

    /// <summary>Retrieves device information for the About C64U dialog, or null on failure.</summary>
    public async Task<C64UInfo?> FetchC64UInfoAsync()
    {
        if (!EnsureC64UUrlConfigured()) return null;

        try
        {
            return await new C64UltimateClient().GetInfoAsync(Settings.C64UUrl);
        }
        catch (Exception ex)
        {
            ErrorRaised?.Invoke("About Commodore 64 Ultimate", $"Error retrieving information from the Commodore 64 Ultimate: {ex.Message}");
            return null;
        }
    }

    #endregion

    #region Public Methods - Transfer and Run

    /// <summary>Loads the active tab's program into the C64 Ultimate without running it.</summary>
    public Task TransferToC64UAsync() => SendToC64UAsync(run: false);

    /// <summary>Loads and runs the active tab's program on the C64 Ultimate.</summary>
    public Task RunOnC64UAsync() => SendToC64UAsync(run: true);

    /// <summary>
    /// Loads (or loads and runs) a local explorer item on the C64 Ultimate without opening it:
    /// an .asm file is assembled, a .bas listing is tokenized, and a .prg/.ml file's bytes are
    /// sent as-is.
    /// </summary>
    public async Task SendFileToC64UAsync(FileTreeItem item, bool run)
    {
        if (!EnsureC64UUrlConfigured()) return;

        byte[] raw;
        try
        {
            raw = item.Content ?? File.ReadAllBytes(item.FullPath);
        }
        catch (Exception ex)
        {
            ErrorRaised?.Invoke("Load/Run File", $"Error reading file: {ex.Message}");
            return;
        }

        await SendRawBytesToC64UAsync(raw, item.Kind, item.Name, run);
    }

    /// <summary>
    /// Loads (or loads and runs) an item from the C64U explorer tree itself, downloading its
    /// bytes first if it's a real remote file rather than a disk-image entry already in memory.
    /// </summary>
    public async Task SendC64UItemAsync(C64UFileItem item, bool run)
    {
        if (!EnsureC64UUrlConfigured()) return;
        if (item.Content == null && C64UFtp == null)
        {
            SetStatus("Not connected to the C64 Ultimate.", StatusType.Error);
            return;
        }

        byte[] raw;
        try
        {
            raw = item.Content ?? await C64UFtp!.DownloadBytesAsync(item.FullPath);
        }
        catch (Exception ex)
        {
            ErrorRaised?.Invoke("Load/Run File", $"Error downloading file: {ex.Message}");
            return;
        }

        await SendRawBytesToC64UAsync(raw, item.Kind, item.Name, run);
    }

    /// <summary>Performs a machine action (reset, reboot, pause, resume, power off) on the C64 Ultimate.</summary>
    public async Task C64UMachineActionAsync(string action, string successMessage)
    {
        if (!EnsureC64UUrlConfigured()) return;

        try
        {
            await new C64UltimateClient().MachineActionAsync(Settings.C64UUrl, action);
            SetStatus(successMessage);
        }
        catch (Exception ex)
        {
            SetStatus($"C64 Ultimate machine action failed: {ex.Message}", StatusType.Error);
        }
    }

    #endregion

    #region Public Methods - C64U Explorer

    /// <summary>Creates a new folder on the C64 Ultimate's storage.</summary>
    public async Task<bool> CreateC64UFolderAsync(string parentPath, string folderName)
    {
        if (C64UFtp == null) return false;

        try
        {
            await C64UFtp.CreateFolderAsync(CombineC64UPath(parentPath, folderName));
            await RefreshAfterC64UChangeAsync(parentPath);
            SetStatus($"Created folder {folderName} on the C64 Ultimate.");
            return true;
        }
        catch (Exception ex)
        {
            ErrorRaised?.Invoke("New Folder", $"Could not create folder: {ex.Message}");
            return false;
        }
    }

    /// <summary>Creates a new blank disk image on the C64 Ultimate's storage.</summary>
    public async Task<bool> CreateC64UDiskImageAsync(string parentPath, string diskName, C64UFileKind kind)
    {
        if (C64UFtp == null) return false;

        string extension = kind == C64UFileKind.D64 ? ".d64" : ".d81";
        string fileName = diskName.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? diskName : diskName + extension;

        try
        {
            byte[] blankImage = DiskImage.ForKind(kind).CreateBlankImage(Path.GetFileNameWithoutExtension(fileName));
            await C64UFtp.UploadBytesAsync(CombineC64UPath(parentPath, fileName), blankImage);
            await RefreshAfterC64UChangeAsync(parentPath);
            SetStatus($"Created {fileName} on the C64 Ultimate.");
            return true;
        }
        catch (Exception ex)
        {
            ErrorRaised?.Invoke("New Disk Image", $"Could not create disk image: {ex.Message}");
            return false;
        }
    }

    /// <summary>Uploads a local file's bytes into a folder on the C64 Ultimate's storage.</summary>
    public async Task<bool> UploadFileToC64UAsync(string parentPath, string fileName, byte[] data)
    {
        if (C64UFtp == null) return false;

        try
        {
            await C64UFtp.UploadBytesAsync(CombineC64UPath(parentPath, fileName), data);
            await RefreshAfterC64UChangeAsync(parentPath);
            SetStatus($"Uploaded {fileName} to the C64 Ultimate.");
            return true;
        }
        catch (Exception ex)
        {
            ErrorRaised?.Invoke("Upload", $"Could not upload \"{fileName}\": {ex.Message}");
            return false;
        }
    }

    /// <summary>Embeds a file into a disk image already on the C64 Ultimate's storage.</summary>
    public async Task<bool> AddFileToC64UDiskImageAsync(C64UFileItem diskItem, string fileName, byte[] content, C64UFileKind kind)
    {
        if (C64UFtp == null) return false;

        try
        {
            byte[] diskBytes = await C64UFtp.DownloadBytesAsync(diskItem.FullPath);
            byte[] updated = DiskImage.ForKind(diskItem.Kind).AddEntry(diskBytes, Path.GetFileNameWithoutExtension(fileName), kind, content);
            await C64UFtp.UploadBytesAsync(diskItem.FullPath, updated);
            await diskItem.RefreshChildrenAsync();
            SetStatus($"Added {fileName} to {diskItem.Name}.");
            return true;
        }
        catch (Exception ex)
        {
            ErrorRaised?.Invoke("Add File", $"Could not add \"{fileName}\" to {diskItem.Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>Renames a remote file, folder, or disk-image entry on the C64 Ultimate.</summary>
    public async Task<bool> RenameC64UItemAsync(C64UFileItem item, string newName)
    {
        if (C64UFtp == null || string.IsNullOrWhiteSpace(newName) || newName == item.Name) return false;

        try
        {
            if (item.IsVirtual)
            {
                if (item.SourcePath == null) return false;
                byte[] diskBytes = await C64UFtp.DownloadBytesAsync(item.SourcePath);
                byte[] updated = DiskImage.ForKind(FileClassifier.Classify(item.SourcePath, isFolder: false)).RenameEntry(diskBytes, item.Name, newName);
                await C64UFtp.UploadBytesAsync(item.SourcePath, updated);

                string oldId = $"{item.SourcePath}!{item.Name}";
                foreach (var tab in OpenTabs.Where(t => t.VirtualSourceId == oldId))
                {
                    tab.VirtualSourceId = $"{item.SourcePath}!{newName}";
                    tab.DisplayName = newName;
                }

                await (FindC64UItemByPath(item.SourcePath)?.RefreshChildrenAsync() ?? Task.CompletedTask);
                return true;
            }

            string newPath = CombineC64UPath(GetC64UParentPath(item.FullPath), newName);
            await C64UFtp.RenameAsync(item.FullPath, newPath);
            await RefreshAfterC64UChangeAsync(GetC64UParentPath(item.FullPath));
            return true;
        }
        catch (Exception ex)
        {
            ErrorRaised?.Invoke("Rename", $"Could not rename \"{item.Name}\": {ex.Message}");
            return false;
        }
    }

    /// <summary>Permanently deletes a remote file, folder, or disk-image entry from the C64 Ultimate.</summary>
    public async Task<bool> DeleteC64UItemAsync(C64UFileItem item)
    {
        if (C64UFtp == null) return false;

        try
        {
            if (item.IsVirtual)
            {
                if (item.SourcePath == null) return false;
                byte[] diskBytes = await C64UFtp.DownloadBytesAsync(item.SourcePath);
                byte[] updated = DiskImage.ForKind(FileClassifier.Classify(item.SourcePath, isFolder: false)).DeleteEntry(diskBytes, item.Name);
                await C64UFtp.UploadBytesAsync(item.SourcePath, updated);

                string id = $"{item.SourcePath}!{item.Name}";
                foreach (var tab in OpenTabs.Where(t => t.VirtualSourceId == id).ToList())
                {
                    tab.IsModified = false;
                    CloseTab(tab);
                }

                await (FindC64UItemByPath(item.SourcePath)?.RefreshChildrenAsync() ?? Task.CompletedTask);
                return true;
            }

            if (item.IsFolder) await C64UFtp.DeleteFolderAsync(item.FullPath);
            else await C64UFtp.DeleteFileAsync(item.FullPath);

            await RefreshAfterC64UChangeAsync(GetC64UParentPath(item.FullPath));
            SetStatus($"Deleted {item.Name} from the C64 Ultimate.");
            return true;
        }
        catch (Exception ex)
        {
            ErrorRaised?.Invoke("Delete", $"Could not delete \"{item.Name}\": {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Opens a program from the C64U explorer tree in a tab: a real remote file is downloaded
    /// first, a disk-image entry's bytes are already in memory.
    /// </summary>
    public async Task<bool> OpenC64UItemAsync(C64UFileItem item)
    {
        if (!item.IsOpenable)
        {
            SetStatus($"{item.Name} isn't a text-editable program type.", StatusType.Warning);
            return false;
        }

        if (item.Kind == C64UFileKind.Ml)
        {
            SetStatus("Machine-language files need the hex editor, which isn't available yet in this version.", StatusType.Warning);
            return false;
        }

        // Only a disk-image entry gets a VirtualSourceId: that's what lets a later Save write it
        // back into the image (see SaveC64UVirtualTabAsync, which expects the
        // "<disk image path>!<entry name>" shape). A real remote file has no such write-back
        // path - editing and saving it behaves like an ordinary new, path-less file, the same as
        // it would if the C64 Ultimate had no FTP write-back support at all.
        string? sourceId = item.IsVirtual ? $"{item.SourcePath}!{item.Name}" : null;
        if (sourceId != null)
        {
            var existing = OpenTabs.FirstOrDefault(t => t.VirtualSourceId == sourceId);
            if (existing != null)
            {
                ActiveTab = existing;
                return true;
            }
        }

        byte[] content;
        try
        {
            content = item.Content ?? (C64UFtp != null ? await C64UFtp.DownloadBytesAsync(item.FullPath) : throw new InvalidOperationException("Not connected to the C64 Ultimate."));
        }
        catch (Exception ex)
        {
            ErrorRaised?.Invoke("Open File Error", $"Error downloading file: {ex.Message}");
            return false;
        }

        try
        {
            var tab = new EditorTab
            {
                DisplayName = item.Name,
                VirtualSourceId = sourceId,
                IsC64UVirtual = sourceId != null,
                Kind = item.Kind,
                Language = item.Kind == C64UFileKind.Asm ? EditorLanguage.Asm : EditorLanguage.Basic,
            };
            tab.Document.Text = item.Kind == C64UFileKind.Prg
                ? PadLineNumbers(new PrgConverter().ConvertFromPrg(content))
                : CompareFileResolver.DecodeSourceText(content);
            tab.IsModified = false;

            AddTab(tab);
            ActiveTab = tab;
            SetStatus($"Opened {item.Name} from the C64 Ultimate.");
            return true;
        }
        catch (Exception ex)
        {
            ErrorRaised?.Invoke("Open File Error", $"Error opening file: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Writes a tab opened from a mounted C64U disk image back into it (plain text for assembly,
    /// tokenized PRG bytes otherwise) and refreshes the image's node in the explorer.
    /// </summary>
    public async Task<bool> SaveC64UVirtualTabAsync(EditorTab tab)
    {
        if (tab.VirtualSourceId == null || C64UFtp == null) return false;

        var parts = tab.VirtualSourceId.Split('!', 2);
        if (parts.Length != 2) return false;
        string sourcePath = parts[0];
        string entryName = parts[1];

        try
        {
            byte[] newContent = tab.Language == EditorLanguage.Asm
                ? System.Text.Encoding.UTF8.GetBytes(tab.Document.Text)
                : new PrgConverter().ConvertToPrg(tab.Document.Text);

            byte[] diskBytes = await C64UFtp.DownloadBytesAsync(sourcePath);
            var kind = FileClassifier.Classify(sourcePath, isFolder: false);
            byte[] updated = DiskImage.ForKind(kind).ReplaceEntry(diskBytes, entryName, newContent);
            await C64UFtp.UploadBytesAsync(sourcePath, updated);

            tab.IsModified = false;
            await (FindC64UItemByPath(sourcePath)?.RefreshChildrenAsync() ?? Task.CompletedTask);
            SetStatus($"{entryName} saved into {Path.GetFileName(sourcePath)} on the C64 Ultimate.");
            return true;
        }
        catch (Exception ex)
        {
            ErrorRaised?.Invoke("Save File Error", $"Error saving to disk image: {ex.Message}");
            return false;
        }
    }

    /// <summary>Finds the loaded C64U explorer item for a remote path, if it's been loaded.</summary>
    public C64UFileItem? FindC64UItemByPath(string path) => FindC64UItemByPath(C64UFileItems, path);

    #endregion

    #region Private Methods - C64U

    private bool EnsureC64UUrlConfigured()
    {
        if (!string.IsNullOrWhiteSpace(Settings.C64UUrl) && Uri.TryCreate(Settings.C64UUrl, UriKind.Absolute, out _)) return true;
        SetStatus("Please set the Commodore 64 Ultimate URL in Settings first.", StatusType.Error);
        return false;
    }

    private static string GetC64UFtpHost(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : "";

    private static string CombineC64UPath(string parentPath, string name) =>
        parentPath.TrimEnd('/') + "/" + name;

    private static string GetC64UParentPath(string path)
    {
        string trimmed = path.TrimEnd('/');
        int slash = trimmed.LastIndexOf('/');
        return slash <= 0 ? "/" : trimmed[..slash];
    }

    // Reloads the folder containing a just-created/renamed/deleted item, or the whole root if
    // that folder isn't currently loaded (or is the root itself).
    private async Task RefreshAfterC64UChangeAsync(string parentPath)
    {
        if (parentPath == "/" || FindC64UItemByPath(parentPath) is not { } parent)
        {
            await RefreshC64UFolderAsync();
            return;
        }

        await parent.RefreshChildrenAsync();
    }

    private static C64UFileItem? FindC64UItemByPath(IEnumerable<C64UFileItem> items, string path)
    {
        foreach (var item in items)
        {
            if (!item.IsVirtual && string.Equals(item.FullPath, path, StringComparison.OrdinalIgnoreCase))
                return item;
            if (item.IsFolder && path.StartsWith(item.FullPath.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase))
            {
                var found = FindC64UItemByPath(item.Children, path);
                if (found != null) return found;
            }
        }
        return null;
    }

    private async Task SendToC64UAsync(bool run)
    {
        string? text = ActiveTab?.Document.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            SetStatus("There is no code to run. Please write some code first.", StatusType.Error);
            return;
        }

        if (!EnsureC64UUrlConfigured()) return;

        if (!TryBuildPrgData(text, out byte[]? prgData, out AssemblyResult? asmResult))
            return;

        try
        {
            var client = new C64UltimateClient();
            SetStatus("Transferring program to C64 Ultimate…");

            if (run && NeedsSysCommand(asmResult))
            {
                await client.LoadPrgAsync(Settings.C64UUrl, prgData!);
                await Task.Delay(_sysCommandDelay);
                await client.TypeAsync(Settings.C64UUrl, $"SYS{asmResult!.Origin}\r");
                SetStatus($"Program transferred and running on the C64 Ultimate (SYS{asmResult.Origin}).");
            }
            else if (run)
            {
                await client.RunPrgAsync(Settings.C64UUrl, prgData!);
                SetStatus("Program transferred and running on the C64 Ultimate.");
            }
            else
            {
                await client.LoadPrgAsync(Settings.C64UUrl, prgData!);
                SetStatus("Program transferred to C64 Ultimate.");
            }
        }
        catch (Exception ex)
        {
            SetStatus($"C64 Ultimate transfer failed: {ex.Message}", StatusType.Error);
        }
    }

    // Shared by SendFileToC64UAsync and SendC64UItemAsync: assembles/tokenizes raw bytes as
    // needed, then loads (or loads and runs) the result. Unlike the active-tab path above, the
    // "needs SYS" decision is made by inspecting the final PRG bytes themselves rather than an
    // assembly result, since a .prg/.ml file never has one - matching SendFileToViceAsync.
    private async Task SendRawBytesToC64UAsync(byte[] raw, C64UFileKind kind, string displayName, bool run)
    {
        byte[] prgBytes;
        if (kind == C64UFileKind.Asm)
        {
            var result = new Asm6502Assembler().Assemble(
                CompareFileResolver.DecodeSourceText(raw), Settings.AsmOutputMode == "Standalone", (ushort)Settings.AsmDefaultOriginAddress);
            if (!result.Success)
            {
                ErrorRaised?.Invoke("Assembly Errors",
                    string.Join(Environment.NewLine, result.Errors.Select(e => $"Line {e.LineNumber}: {e.Message}")));
                SetStatus($"Assembly failed with {result.Errors.Count} error(s).", StatusType.Error);
                return;
            }
            prgBytes = result.PrgBytes!;
        }
        else if (kind == C64UFileKind.Bas)
        {
            prgBytes = new PrgConverter().ConvertToPrg(CompareFileResolver.DecodeSourceText(raw));
        }
        else
        {
            prgBytes = raw;
        }

        try
        {
            var client = new C64UltimateClient();
            SetStatus($"Transferring '{displayName}' to C64 Ultimate…");

            if (!run)
            {
                await client.LoadPrgAsync(Settings.C64UUrl, prgBytes);
                SetStatus($"'{displayName}' transferred to C64 Ultimate.");
            }
            else if (new PrgConverter().NeedsSysToRun(prgBytes, out ushort origin))
            {
                await client.LoadPrgAsync(Settings.C64UUrl, prgBytes);
                await Task.Delay(_sysCommandDelay);
                await client.TypeAsync(Settings.C64UUrl, $"SYS{origin}\r");
                SetStatus($"'{displayName}' transferred and running on the C64 Ultimate (SYS{origin}).");
            }
            else
            {
                await client.RunPrgAsync(Settings.C64UUrl, prgBytes);
                SetStatus($"'{displayName}' transferred and running on the C64 Ultimate.");
            }
        }
        catch (Exception ex)
        {
            SetStatus($"C64 Ultimate transfer failed: {ex.Message}", StatusType.Error);
        }
    }

    #endregion
}
