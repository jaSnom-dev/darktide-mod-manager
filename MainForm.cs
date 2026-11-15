using System.Diagnostics;
using System.IO.Compression;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DarktideModManager;

internal sealed class MainForm : Form
{
    private static readonly Color BackgroundColor = Color.FromArgb(25, 22, 20);
    private static readonly Color PanelColor = Color.FromArgb(38, 34, 30);
    private static readonly Color ButtonColor = Color.FromArgb(51, 44, 37);
    private static readonly Color TextColor = Color.FromArgb(232, 219, 199);
    private static readonly Color MutedColor = Color.FromArgb(174, 155, 130);
    private static readonly Color AccentColor = Color.FromArgb(211, 126, 54);
    private static readonly Color BorderColor = Color.FromArgb(91, 69, 48);
    private static readonly Color ButtonHoverColor = Color.FromArgb(70, 55, 41);
    private static readonly Color ErrorColor = Color.FromArgb(202, 91, 69);
    private static readonly HttpClient NexusHttpClient = new();
    private const string NexusGameDomain = "warhammer40kdarktide";
    private const string DarktideSteamLaunchUri = "steam://rungameid/1361210";
    private const string DarktideExecutableRelativePath = @"binaries\Darktide.exe";
    private const string NexusGraphQlEndpoint = "https://api.nexusmods.com/v2/graphql";
    private const string NexusModSearchQuery =
        "query SearchDarktideMods($filter: ModsFilter, $count: Int) { mods(filter: $filter, count: $count) { nodes { modId name author endorsements } } }";
    private const string LoaderPatchRelativePath = @"bundle\9ba626afa44a3aa3.patch_999";
    private const string LoaderBinaryRelativePath = @"binaries\mod_loader";

    private readonly TextBox _gamePathBox = new();
    private readonly ListView _modList = new();
    private readonly Label _statusLabel = new();
    private readonly Label _patchStatusLabel = new();
    private readonly Label _countLabel = new();
    private readonly Label _pendingLabel = new();
    private readonly Button _browseButton;
    private readonly Button _enableAllButton;
    private readonly Button _disableAllButton;
    private readonly Button _updateButton;
    private readonly Button _openModsButton;
    private readonly Button _patchButton;
    private readonly Button _discardButton;
    private readonly Button _applyButton;
    private readonly ContextMenuStrip _updateMenu = new();
    private readonly List<string> _headerComments = [];
    private readonly Dictionary<string, long> _nexusModIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, GameFingerprint> _gameFingerprints = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _settingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DarktideModManager");
    private string _gameDirectory = "";
    private string _modsDirectory = "";
    private string _orderFile = "";
    private bool _loading;
    private bool _hasPendingChanges;
    private string _nexusApiKey = "";

    public MainForm()
    {
        Text = "Darktide Mod Manager";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(940, 700);
        MinimumSize = new Size(800, 600);
        BackColor = BackgroundColor;
        ForeColor = TextColor;
        Font = new Font("Segoe UI", 9);

        var title = new Label
        {
            Text = "DARKTIDE MOD MANAGER",
            Font = new Font("Segoe UI", 19, FontStyle.Bold),
            ForeColor = TextColor,
            Location = new Point(24, 20),
            Size = new Size(450, 36)
        };
        Controls.Add(title);

        var subtitle = new Label
        {
            Text = "Manage your load order. Comments, base, and dmf are excluded from the mod list.",
            ForeColor = MutedColor,
            Location = new Point(26, 59),
            Size = new Size(600, 22)
        };
        Controls.Add(subtitle);

        var nexusButton = CreateButton("Nexus Mods", new Point(748, 52), new Size(150, 32));
        nexusButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        nexusButton.Image = LoadButtonIcon(
            "DarktideModManager.Assets.nexus-mods.png",
            new Size(22, 22),
            cropToLeftSquare: true);
        nexusButton.ImageAlign = ContentAlignment.MiddleLeft;
        nexusButton.TextImageRelation = TextImageRelation.ImageBeforeText;
        nexusButton.Padding = new Padding(12, 0, 0, 0);
        nexusButton.Click += (_, _) => OpenNexusMods();
        Controls.Add(nexusButton);

        var startGameButton = CreateButton("Start Game", new Point(748, 88), new Size(150, 32));
        startGameButton.Image = LoadButtonIcon(
            "DarktideModManager.Assets.darktide-steam.jpg",
            new Size(22, 22));
        startGameButton.ImageAlign = ContentAlignment.MiddleLeft;
        startGameButton.TextImageRelation = TextImageRelation.ImageBeforeText;
        startGameButton.Padding = new Padding(12, 0, 0, 0);
        startGameButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        startGameButton.BackColor = AccentColor;
        startGameButton.ForeColor = BackgroundColor;
        startGameButton.FlatAppearance.BorderSize = 0;
        startGameButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(225, 146, 69);
        startGameButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(169, 91, 39);
        startGameButton.Click += (_, _) => StartGame();
        Controls.Add(startGameButton);

        var pathLabel = new Label
        {
            Text = "GAME FOLDER",
            ForeColor = MutedColor,
            Location = new Point(26, 100),
            Size = new Size(120, 20)
        };
        Controls.Add(pathLabel);

        _gamePathBox.ReadOnly = true;
        _gamePathBox.BackColor = PanelColor;
        _gamePathBox.ForeColor = TextColor;
        _gamePathBox.BorderStyle = BorderStyle.FixedSingle;
        _gamePathBox.Location = new Point(26, 124);
        _gamePathBox.Size = new Size(710, 28);
        Controls.Add(_gamePathBox);

        _browseButton = CreateButton("Browse...", new Point(748, 122), new Size(150, 32));
        _browseButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _browseButton.TextAlign = ContentAlignment.MiddleLeft;
        _browseButton.Padding = new Padding(42, 0, 0, 0);
        _browseButton.Click += BrowseForGameFolder;
        Controls.Add(_browseButton);

        _statusLabel.ForeColor = MutedColor;
        _statusLabel.Location = new Point(26, 163);
        _statusLabel.Size = new Size(490, 22);
        _statusLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        Controls.Add(_statusLabel);

        _updateButton = CreateButton("Update selected", new Point(532, 158), new Size(110, 30));
        _updateButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _updateButton.Enabled = false;
        _updateButton.Click += (_, _) => ShowUpdateMenu();
        Controls.Add(_updateButton);
        _updateMenu.Items.Add("Download latest from Nexus", null, async (_, _) => await UpdateSelectedModFromNexusAsync());
        _updateMenu.Items.Add("Install update from ZIP...", null, (_, _) => UpdateSelectedModFromZip());
        _updateMenu.Items.Add(new ToolStripSeparator());
        _updateMenu.Items.Add("Set Nexus page for selected mod...", null, (_, _) => SetNexusPageForSelectedMod());
        _updateMenu.Items.Add("Change Nexus API key...", null, (_, _) => ChangeNexusApiKey());

        _enableAllButton = CreateButton("Enable all", new Point(650, 158), new Size(112, 30));
        _enableAllButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _enableAllButton.Click += (_, _) => SetAllModsEnabled(true);
        Controls.Add(_enableAllButton);

        _disableAllButton = CreateButton("Disable all", new Point(778, 158), new Size(120, 30));
        _disableAllButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _disableAllButton.Click += (_, _) => SetAllModsEnabled(false);
        Controls.Add(_disableAllButton);

        _modList.View = View.Details;
        _modList.CheckBoxes = true;
        _modList.FullRowSelect = true;
        _modList.GridLines = true;
        _modList.HideSelection = false;
        _modList.UseCompatibleStateImageBehavior = false;
        _modList.BackColor = PanelColor;
        _modList.ForeColor = TextColor;
        _modList.BorderStyle = BorderStyle.FixedSingle;
        _modList.Location = new Point(26, 192);
        _modList.Size = new Size(872, 382);
        _modList.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _modList.Columns.Add("MOD  (checked = enabled)", 470);
        _modList.Columns.Add("VERSION", 170);
        _modList.Columns.Add("FOLDER STATUS", 230);
        _modList.Resize += (_, _) => ResizeLastModColumn();
        _modList.OwnerDraw = true;
        _modList.DrawColumnHeader += (_, e) =>
        {
            using var background = new SolidBrush(ButtonColor);
            e.Graphics.FillRectangle(background, e.Bounds);
            var textBounds = new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height);
            TextRenderer.DrawText(
                e.Graphics,
                e.Header?.Text ?? "",
                _modList.Font,
                textBounds,
                TextColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            using var border = new Pen(BorderColor);
            e.Graphics.DrawLine(border, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
        };
        _modList.DrawItem += (_, e) => e.DrawDefault = true;
        _modList.DrawSubItem += (_, e) => e.DrawDefault = true;
        _modList.ItemCheck += (_, e) =>
        {
            if (!_loading)
            {
                SetDirtyState(true);
                BeginInvoke((Action)UpdateModCount);
            }
        };
        _modList.SelectedIndexChanged += (_, _) => UpdateSelectionControls();
        _modList.DoubleClick += (_, _) => OpenSelectedModFolder();
        Controls.Add(_modList);

        _countLabel.ForeColor = MutedColor;
        _countLabel.Location = new Point(26, 584);
        _countLabel.Size = new Size(220, 24);
        _countLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        Controls.Add(_countLabel);

        _pendingLabel.Text = "UNAPPLIED CHANGES";
        _pendingLabel.ForeColor = AccentColor;
        _pendingLabel.Location = new Point(256, 584);
        _pendingLabel.Size = new Size(200, 24);
        _pendingLabel.Visible = false;
        _pendingLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        Controls.Add(_pendingLabel);

        _patchStatusLabel.Text = "Patch status: not checked";
        _patchStatusLabel.ForeColor = MutedColor;
        _patchStatusLabel.Location = new Point(470, 584);
        _patchStatusLabel.Size = new Size(428, 24);
        _patchStatusLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        Controls.Add(_patchStatusLabel);

        var moveUpButton = CreateButton("Move up", new Point(26, 618), new Size(100, 34));
        moveUpButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        moveUpButton.Click += (_, _) => MoveSelectedMod(-1);
        Controls.Add(moveUpButton);

        var moveDownButton = CreateButton("Move down", new Point(134, 618), new Size(100, 34));
        moveDownButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        moveDownButton.Click += (_, _) => MoveSelectedMod(1);
        Controls.Add(moveDownButton);

        var refreshButton = CreateButton("Refresh", new Point(242, 618), new Size(92, 34));
        refreshButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        refreshButton.Click += (_, _) =>
        {
            if (ConfirmDiscardChanges())
            {
                LoadModList();
            }
        };
        Controls.Add(refreshButton);

        _openModsButton = CreateButton("Open mods folder", new Point(342, 618), new Size(135, 34));
        _openModsButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        _openModsButton.Click += (_, _) => OpenModsFolder();
        Controls.Add(_openModsButton);

        _patchButton = CreateButton("Run patch helper", new Point(487, 618), new Size(145, 34));
        _patchButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        _patchButton.Click += (_, _) => RunPatchHelper();
        Controls.Add(_patchButton);

        _discardButton = CreateButton("Discard", new Point(644, 618), new Size(92, 34));
        _discardButton.Enabled = false;
        _discardButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        _discardButton.Click += (_, _) => LoadModList();
        Controls.Add(_discardButton);

        _applyButton = CreateButton("Apply changes", new Point(746, 618), new Size(152, 34));
        _applyButton.BackColor = AccentColor;
        _applyButton.ForeColor = BackgroundColor;
        _applyButton.FlatAppearance.BorderSize = 0;
        _applyButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(225, 146, 69);
        _applyButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(169, 91, 39);
        _applyButton.Enabled = false;
        _applyButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        _applyButton.Click += (_, _) => SaveModList();
        Controls.Add(_applyButton);

        FormClosing += (_, e) =>
        {
            if (!ConfirmDiscardChanges())
            {
                e.Cancel = true;
            }
        };
        FormClosed += (_, _) =>
        {
            _updateMenu.Dispose();
            nexusButton.Image?.Dispose();
            startGameButton.Image?.Dispose();
        };
        Shown += (_, _) => InitializeGameFolder();
    }

    private static Image LoadButtonIcon(string resourceName, Size size, bool cropToLeftSquare = false)
    {
        using var stream = typeof(MainForm).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded button image '{resourceName}' was not found.");
        using var source = new Bitmap(stream);
        var sourceBounds = cropToLeftSquare
            ? new Rectangle(0, 0, Math.Min(source.Width, source.Height), Math.Min(source.Width, source.Height))
            : new Rectangle(0, 0, source.Width, source.Height);
        var image = new Bitmap(size.Width, size.Height);
        using (var graphics = Graphics.FromImage(image))
        {
            graphics.Clear(Color.Transparent);
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(image: source, destRect: new Rectangle(Point.Empty, size), srcRect: sourceBounds, srcUnit: GraphicsUnit.Pixel);
        }
        return image;
    }

    private static Button CreateButton(string text, Point location, Size size)
    {
        var button = new Button
        {
            Text = text,
            Location = location,
            Size = size,
            BackColor = ButtonColor,
            ForeColor = TextColor,
            FlatStyle = FlatStyle.Flat
        };
        button.FlatAppearance.BorderColor = BorderColor;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = ButtonHoverColor;
        button.FlatAppearance.MouseDownBackColor = AccentColor;
        button.Cursor = Cursors.Hand;
        return button;
    }

    private void InitializeGameFolder()
    {
        var savedFolder = "";
        var settingsFile = Path.Combine(_settingsDirectory, "settings.json");
        try
        {
            if (File.Exists(settingsFile))
            {
                var settings = JsonSerializer.Deserialize<ManagerSettings>(
                    File.ReadAllText(settingsFile),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (settings is not null)
                {
                    savedFolder = settings.GameDirectory;
                    _nexusModIds.Clear();
                    if (settings.NexusModIds is not null)
                    {
                        foreach (var mapping in settings.NexusModIds)
                        {
                            _nexusModIds[mapping.Key] = mapping.Value;
                        }
                    }

                    _gameFingerprints.Clear();
                    if (settings.GameFingerprints is not null)
                    {
                        foreach (var fingerprint in settings.GameFingerprints)
                        {
                            _gameFingerprints[fingerprint.Key] = fingerprint.Value;
                        }
                    }

                    _nexusApiKey = "";
                    if (!string.IsNullOrWhiteSpace(settings.ProtectedNexusApiKey))
                    {
                        try
                        {
                            var protectedKey = Convert.FromBase64String(settings.ProtectedNexusApiKey);
                            _nexusApiKey = Encoding.UTF8.GetString(
                                ProtectedData.Unprotect(protectedKey, null, DataProtectionScope.CurrentUser));
                        }
                        catch (Exception ex) when (ex is CryptographicException or FormatException)
                        {
                            SetStatus("Could not decrypt the saved Nexus API key. Enter it again to use Nexus updates.", true);
                        }
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            SetStatus($"Could not read saved settings: {ex.Message}", true);
        }

        var defaultFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            @"Steam\steamapps\common\Warhammer 40,000 DARKTIDE");
        var folder = IsGameDirectory(savedFolder) ? savedFolder : defaultFolder;
        if (IsGameDirectory(folder))
        {
            SetGameDirectory(folder);
        }
        else
        {
            UpdateGameControls();
            LoadModList();
        }
    }

    private static bool IsGameDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return false;
        }

        return File.Exists(Path.Combine(path, @"binaries\Darktide.exe"))
            || File.Exists(Path.Combine(path, "toggle_darktide_mods.bat"))
            || Directory.Exists(Path.Combine(path, "mods"));
    }

    private void BrowseForGameFolder(object? sender, EventArgs e)
    {
        if (!ConfirmDiscardChanges())
        {
            return;
        }

        using var dialog = new FolderBrowserDialog
        {
            Description = "Select your Warhammer 40,000: Darktide game folder.",
            ShowNewFolderButton = false
        };
        if (Directory.Exists(_gameDirectory))
        {
            dialog.SelectedPath = _gameDirectory;
        }

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            SetGameDirectory(dialog.SelectedPath);
        }
    }

    private void SetGameDirectory(string path)
    {
        if (!IsGameDirectory(path))
        {
            MessageBox.Show(
                this,
                @"That folder does not look like a Darktide installation. Select the game folder containing binaries\Darktide.exe or the mod loader files.",
                "Darktide folder not found",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        _gameDirectory = Path.GetFullPath(path);
        _gamePathBox.Text = _gameDirectory;
        SaveSettings();
        UpdateGameControls();
        LoadModList();
        CheckPatchStatus(showWarning: true);
    }

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(_settingsDirectory);
            var protectedKey = string.IsNullOrEmpty(_nexusApiKey)
                ? null
                : Convert.ToBase64String(ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(_nexusApiKey),
                    null,
                    DataProtectionScope.CurrentUser));
            var settings = JsonSerializer.Serialize(new ManagerSettings(
                _gameDirectory,
                protectedKey,
                new Dictionary<string, long>(_nexusModIds, StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, GameFingerprint>(_gameFingerprints, StringComparer.OrdinalIgnoreCase)));
            File.WriteAllText(Path.Combine(_settingsDirectory, "settings.json"), settings, new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            SetStatus($"Could not save settings: {ex.Message}", true);
        }
    }

    private void UpdateGameControls()
    {
        if (!IsGameDirectory(_gameDirectory))
        {
            _modsDirectory = "";
            _orderFile = "";
            _openModsButton.Enabled = false;
            _patchButton.Enabled = false;
            _enableAllButton.Enabled = false;
            _disableAllButton.Enabled = false;
            _updateButton.Enabled = false;
            SetStatus("Choose your Darktide installation folder to get started.");
            return;
        }

        _modsDirectory = Path.Combine(_gameDirectory, "mods");
        _orderFile = Path.Combine(_modsDirectory, "mod_load_order.txt");
        var hasModsFolder = Directory.Exists(_modsDirectory);
        _openModsButton.Enabled = hasModsFolder;
        _patchButton.Enabled = File.Exists(Path.Combine(_gameDirectory, "toggle_darktide_mods.bat"));
        _enableAllButton.Enabled = hasModsFolder;
        _disableAllButton.Enabled = hasModsFolder;
        UpdateSelectionControls();

        if (!hasModsFolder)
        {
            SetStatus("The mods folder is missing. Install the Darktide Mod Loader first.", true);
        }
        else if (_patchButton.Enabled)
        {
            SetStatus("Patch helper found. Select mods, then apply your changes.");
        }
        else
        {
            SetStatus("Select mods, then apply your changes. Mod Loader patch helper not found.");
        }
    }

    private void CheckPatchStatus(bool showWarning, bool refreshFingerprint = false)
    {
        if (!IsGameDirectory(_gameDirectory))
        {
            SetPatchStatus("Select a game folder to check patch status.", false);
            return;
        }

        var patchHelper = Path.Combine(_gameDirectory, "toggle_darktide_mods.bat");
        if (!File.Exists(patchHelper))
        {
            SetPatchStatus("Mod Loader patch helper not found.", true);
            return;
        }

        var patchFile = Path.Combine(_gameDirectory, LoaderPatchRelativePath);
        if (!File.Exists(patchFile))
        {
            const string message = "Patch not detected; the game may be unpatched or an update may have removed it.";
            SetPatchStatus(message, true);
            if (showWarning)
            {
                MessageBox.Show(
                    this,
                    $"{message}\r\n\r\nRun the Mod Loader patch helper to enable mods again.",
                    "Darktide patch not detected",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            return;
        }

        if (!File.Exists(Path.Combine(_gameDirectory, LoaderBinaryRelativePath)))
        {
            const string message = "Patch data exists, but the Mod Loader binary is missing.";
            SetPatchStatus(message, true);
            if (showWarning)
            {
                MessageBox.Show(
                    this,
                    $"{message}\r\n\r\nReinstall the Mod Loader before launching the game.",
                    "Mod Loader incomplete",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            return;
        }

        GameFingerprint? currentFingerprint;
        try
        {
            currentFingerprint = ReadGameFingerprint();
        }
        catch (Exception ex) when (
            ex is IOException
            or UnauthorizedAccessException
            or System.ComponentModel.Win32Exception)
        {
            SetPatchStatus($"Patch file found; game version check failed: {ex.Message}", true);
            return;
        }
        if (currentFingerprint is null)
        {
            SetPatchStatus("Patch file found; game version could not be checked.", true);
            return;
        }

        var gameDirectory = Path.GetFullPath(_gameDirectory);
        if (refreshFingerprint || !_gameFingerprints.TryGetValue(gameDirectory, out var previousFingerprint))
        {
            _gameFingerprints[gameDirectory] = currentFingerprint;
            SaveSettings();
            SetPatchStatus("Patch file found; game version baseline recorded.", false);
            return;
        }

        if (currentFingerprint != previousFingerprint)
        {
            const string message = "Game files changed since the last patch check; the patch may need updating.";
            SetPatchStatus(message, true);
            if (showWarning)
            {
                MessageBox.Show(
                    this,
                    $"{message}\r\n\r\nRun the Mod Loader patch helper to verify or reapply the patch.",
                    "Darktide update detected",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            return;
        }

        SetPatchStatus("Patch file found; game files match the last patch check.", false);
    }

    private GameFingerprint? ReadGameFingerprint()
    {
        var executable = Path.Combine(_gameDirectory, @"binaries\Darktide.exe");
        if (!File.Exists(executable))
        {
            return null;
        }

        var file = new FileInfo(executable);
        var fileVersion = FileVersionInfo.GetVersionInfo(executable).FileVersion ?? "";
        return new GameFingerprint(fileVersion, file.Length, file.LastWriteTimeUtc.Ticks);
    }

    private void SetPatchStatus(string message, bool isWarning)
    {
        _patchStatusLabel.Text = $"Patch status: {message}";
        _patchStatusLabel.ForeColor = isWarning
            ? AccentColor
            : Color.FromArgb(135, 195, 150);
    }

    private void LoadModList()
    {
        _loading = true;
        _modList.BeginUpdate();
        try
        {
            _modList.Items.Clear();
            if (!Directory.Exists(_modsDirectory))
            {
                SetDirtyState(false);
                UpdateModCount();
                return;
            }

            foreach (var entry in ReadModEntries())
            {
                var item = new ListViewItem(entry.Name)
                {
                    Checked = entry.Enabled,
                    Tag = entry
                };
                item.SubItems.Add(entry.Version);
                item.SubItems.Add(entry.Exists ? "Ready" : "Folder missing");
                _modList.Items.Add(item);
            }

            SetDirtyState(false);
            UpdateModCount();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetDirtyState(false);
            SetStatus($"Could not read the mod list: {ex.Message}", true);
            MessageBox.Show(
                this,
                $"Could not read the mods folder or load order.\r\n\r\n{ex.Message}",
                "Could not load mods",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            _modList.EndUpdate();
            _loading = false;
        }

        UpdateGameControls();
    }

    private List<ModEntry> ReadModEntries()
    {
        var entries = new List<ModEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var modFolders = Directory.GetDirectories(_modsDirectory)
            .Select(path => Path.GetFileName(path))
            .OfType<string>()
            .Where(name => !name.StartsWith(".DarktideModManager-", StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _headerComments.Clear();

        if (File.Exists(_orderFile))
        {
            foreach (var line in File.ReadAllLines(_orderFile))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0)
                {
                    if (entries.Count == 0)
                    {
                        _headerComments.Add(line);
                    }
                    continue;
                }

                var disabled = trimmed.StartsWith("--", StringComparison.Ordinal);
                var name = disabled ? trimmed[2..].Trim() : trimmed;
                if (disabled && !modFolders.Contains(name))
                {
                    _headerComments.Add(line);
                    continue;
                }

                if (name.Length == 0 || !seen.Add(name))
                {
                    continue;
                }

                if (IsCoreMod(name))
                {
                    continue;
                }

                var exists = Directory.Exists(Path.Combine(_modsDirectory, name));
                entries.Add(new ModEntry(name, !disabled, exists, exists ? ReadModVersion(name) : "—"));
            }
        }

        foreach (var directory in modFolders.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            if (!IsCoreMod(directory) && seen.Add(directory))
            {
                entries.Add(new ModEntry(directory, false, true, ReadModVersion(directory)));
            }
        }

        return entries;
    }

    private string ReadModVersion(string modName)
    {
        var metadataPath = Path.Combine(_modsDirectory, modName, "info.json");
        if (!File.Exists(metadataPath))
        {
            return "Unknown";
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(metadataPath));
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (property.Name.Equals("version", StringComparison.OrdinalIgnoreCase)
                        && property.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                    {
                        var version = property.Value.ToString().Trim();
                        return string.IsNullOrEmpty(version) ? "Unknown" : version;
                    }
                }
            }

            return "Unknown";
        }
        catch (JsonException ex)
        {
            SetStatus($"Could not read the version for {modName}: {ex.Message}", true);
            return "Invalid";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus($"Could not read the version for {modName}: {ex.Message}", true);
            return "Unavailable";
        }
    }

    private static bool IsCoreMod(string name)
    {
        return name.Equals("base", StringComparison.OrdinalIgnoreCase)
            || name.Equals("dmf", StringComparison.OrdinalIgnoreCase);
    }

    private void SetAllModsEnabled(bool enabled)
    {
        foreach (ListViewItem item in _modList.Items)
        {
            if (item.Tag is ModEntry entry && (!enabled || entry.Exists))
            {
                item.Checked = enabled;
            }
        }
        SetDirtyState(true);
        UpdateModCount();
    }

    private void MoveSelectedMod(int offset)
    {
        if (_modList.SelectedIndices.Count == 0)
        {
            return;
        }

        var index = _modList.SelectedIndices[0];
        var destination = index + offset;
        if (destination < 0 || destination >= _modList.Items.Count)
        {
            return;
        }

        var item = _modList.Items[index];
        _modList.Items.RemoveAt(index);
        _modList.Items.Insert(destination, item);
        item.Selected = true;
        SetDirtyState(true);
        UpdateModCount();
    }

    private void OpenNexusMods()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://www.nexusmods.com/warhammer40kdarktide",
                UseShellExecute = true
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            SetStatus($"Could not open Nexus Mods: {ex.Message}", true);
            MessageBox.Show(
                this,
                $"Could not open the Darktide Nexus Mods page.\r\n\r\n{ex.Message}",
                "Could not open website",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void StartGame()
    {
        var gameExecutable = Path.Combine(_gameDirectory, DarktideExecutableRelativePath);
        if (!File.Exists(gameExecutable))
        {
            SetStatus("Darktide.exe was not found in the selected game folder.", true);
            MessageBox.Show(
                this,
                $"Could not find the game executable:\r\n{gameExecutable}",
                "Darktide not found",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = DarktideSteamLaunchUri,
                UseShellExecute = true
            });
            SetStatus("Launching Darktide through Steam...");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            SetStatus($"Could not launch Darktide through Steam: {ex.Message}", true);
            MessageBox.Show(
                this,
                $"Could not launch Darktide through Steam.\r\n\r\n{ex.Message}",
                "Could not start game",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void UpdateSelectionControls()
    {
        _updateButton.Enabled = Directory.Exists(_modsDirectory)
            && _modList.SelectedItems.Count == 1
            && Directory.Exists(Path.Combine(_modsDirectory, _modList.SelectedItems[0].Text));
    }

    private void ShowUpdateMenu()
    {
        _updateMenu.Show(_updateButton, new Point(0, _updateButton.Height));
    }

    private void SetNexusPageForSelectedMod()
    {
        if (_modList.SelectedItems.Count != 1)
        {
            return;
        }

        var modName = _modList.SelectedItems[0].Text;
        var modPage = PromptForNexusText(
            "Link Nexus mod",
            $"Paste the Nexus page URL or numeric mod ID for {modName}.");
        if (modPage is null)
        {
            return;
        }
        if (!TryParseNexusModId(modPage, out var modId))
        {
            MessageBox.Show(
                this,
                "Enter a Darktide Nexus mod URL or a numeric mod ID.",
                "Invalid Nexus mod",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        _nexusModIds[modName] = modId;
        SaveSettings();
        SetStatus($"Nexus page saved for {modName}.");
    }

    private void ChangeNexusApiKey()
    {
        var apiKey = PromptForNexusText(
            "Nexus API key",
            "Paste your Nexus API key. It is encrypted for this Windows account and stored only on this PC.",
            secret: true);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return;
        }

        _nexusApiKey = apiKey;
        SaveSettings();
        SetStatus("Nexus API key saved for this Windows account.");
    }

    private async Task UpdateSelectedModFromNexusAsync()
    {
        if (_modList.SelectedItems.Count != 1 || !ConfirmDiscardChanges())
        {
            return;
        }

        var modName = _modList.SelectedItems[0].Text;
        if (!Directory.Exists(Path.Combine(_modsDirectory, modName)))
        {
            SetStatus("The selected mod folder is missing.", true);
            return;
        }
        var modId = await GetNexusModIdAsync(modName);
        if (modId is null)
        {
            return;
        }

        _updateButton.Enabled = false;
        string? downloadedArchive = null;
        try
        {
            SetStatus($"Checking Nexus for updates to {modName}...");
            var file = await GetLatestNexusFileAsync(modId.Value);
            SetStatus($"Downloading {modName} {file.Version} from Nexus...");
            downloadedArchive = await DownloadNexusArchiveAsync(file.DownloadUri);
            var backupDirectory = InstallModArchive(modName, downloadedArchive);
            LoadModList();
            SetStatus($"{modName} updated from Nexus. Backup: {backupDirectory}");
        }
        catch (HttpRequestException ex)
        {
            SetStatus($"Nexus update failed: {ex.Message}", true);
            var openPage = MessageBox.Show(
                this,
                $"Nexus could not provide a direct download.\r\n\r\n{ex.Message}\r\n\r\nOpen the mod page to download manually? You can then use Update selected > Install update from ZIP.",
                "Nexus download unavailable",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (openPage == DialogResult.Yes)
            {
                OpenNexusModPage(modId.Value);
            }
        }
        catch (Exception ex) when (
            ex is IOException
            or UnauthorizedAccessException
            or JsonException
            or CryptographicException
            or FormatException
            or ArgumentException
            or NotSupportedException
            or TaskCanceledException)
        {
            SetStatus($"Could not update {modName}: {ex.Message}", true);
            MessageBox.Show(
                this,
                $"Could not update {modName} from Nexus.\r\n\r\n{ex.Message}",
                "Mod update failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            if (downloadedArchive is not null && File.Exists(downloadedArchive))
            {
                try
                {
                    File.Delete(downloadedArchive);
                }
                catch (IOException ex)
                {
                    SetStatus($"Mod updated, but the downloaded archive could not be removed: {ex.Message}", true);
                }
            }
            UpdateSelectionControls();
        }
    }

    private void UpdateSelectedModFromZip()
    {
        if (_modList.SelectedItems.Count != 1 || !ConfirmDiscardChanges())
        {
            return;
        }

        var modName = _modList.SelectedItems[0].Text;
        using var dialog = new OpenFileDialog
        {
            Title = $"Choose the downloaded ZIP update for {modName}",
            Filter = "ZIP archives (*.zip)|*.zip",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var backupDirectory = InstallModArchive(modName, dialog.FileName);
            LoadModList();
            SetStatus($"{modName} updated. Previous files are backed up in {backupDirectory}");
        }
        catch (Exception ex) when (
            ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            SetStatus($"Could not update {modName}: {ex.Message}", true);
            MessageBox.Show(
                this,
                $"Could not update {modName}.\r\n\r\n{ex.Message}",
                "Mod update failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private string InstallModArchive(string modName, string archivePath)
    {
        var targetDirectory = Path.Combine(_modsDirectory, modName);
        if (!Directory.Exists(targetDirectory))
        {
            throw new DirectoryNotFoundException($"The mod folder '{modName}' no longer exists.");
        }

        var stagingDirectory = Path.Combine(_modsDirectory, $".DarktideModManager-update-{Guid.NewGuid():N}");
        var backupRoot = Path.Combine(_modsDirectory, ".DarktideModManager-backups", modName);
        var backupDirectory = Path.Combine(backupRoot, $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
        var oldModMoved = false;

        try
        {
            Directory.CreateDirectory(stagingDirectory);
            ExtractModArchive(archivePath, stagingDirectory);
            var updatedModDirectory = FindUpdatedModDirectory(stagingDirectory, modName);
            Directory.CreateDirectory(backupRoot);

            try
            {
                Directory.Move(targetDirectory, backupDirectory);
                oldModMoved = true;
                Directory.Move(updatedModDirectory, targetDirectory);
            }
            catch (Exception installException) when (installException is IOException or UnauthorizedAccessException)
            {
                if (oldModMoved && !Directory.Exists(targetDirectory) && Directory.Exists(backupDirectory))
                {
                    try
                    {
                        Directory.Move(backupDirectory, targetDirectory);
                        oldModMoved = false;
                    }
                    catch (Exception rollbackException) when (rollbackException is IOException or UnauthorizedAccessException)
                    {
                        throw new IOException(
                            $"The update failed and the original mod could not be restored from '{backupDirectory}'.",
                            rollbackException);
                    }
                }

                throw;
            }
        }
        catch (Exception ex) when (
            ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            RemoveStagingDirectory(stagingDirectory);
            throw;
        }

        RemoveStagingDirectory(stagingDirectory);
        return backupDirectory;
    }

    private void RemoveStagingDirectory(string stagingDirectory)
    {
        if (!Directory.Exists(stagingDirectory))
        {
            return;
        }

        try
        {
            Directory.Delete(stagingDirectory, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(
                this,
                $"The update completed, but temporary files remain at:\r\n{stagingDirectory}\r\n\r\n{ex.Message}",
                "Update cleanup warning",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private async Task<long?> GetNexusModIdAsync(string modName)
    {
        if (!_nexusModIds.TryGetValue(modName, out var modId))
        {
            IReadOnlyList<NexusModCandidate>? candidates = null;
            string? lookupError = null;
            try
            {
                SetStatus($"Searching Nexus for {modName}...");
                candidates = await SearchNexusModsAsync(modName);
            }
            catch (Exception ex) when (
                ex is HttpRequestException
                or IOException
                or JsonException
                or InvalidDataException
                or TaskCanceledException)
            {
                lookupError = ex.Message;
            }

            if (lookupError is not null)
            {
                SetStatus($"Automatic Nexus search failed: {lookupError}", true);
                var continueManually = MessageBox.Show(
                    this,
                    $"Nexus could not search for {modName} automatically.\r\n\r\n{lookupError}\r\n\r\nEnter the Nexus page URL or mod ID manually?",
                    "Nexus search unavailable",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (continueManually != DialogResult.Yes)
                {
                    return null;
                }
            }
            else if (candidates is { Count: > 0 })
            {
                var candidate = ChooseNexusCandidate(modName, candidates);
                if (candidate is null)
                {
                    return null;
                }
                modId = candidate.ModId;
            }
            else
            {
                var noMatchesMessage = $"No Nexus mods matched the installed folder name \"{modName}\".";
                SetStatus(noMatchesMessage, true);
                var continueManually = MessageBox.Show(
                    this,
                    $"{noMatchesMessage}\r\n\r\nEnter the Nexus page URL or mod ID manually?",
                    "No Nexus matches",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (continueManually != DialogResult.Yes)
                {
                    return null;
                }
            }

            if (modId == 0)
            {
                var modPage = PromptForNexusText(
                    "Link Nexus mod",
                    $"Paste the Nexus page URL or numeric mod ID for {modName}. This is saved for future updates.");
                if (modPage is null)
                {
                    return null;
                }
                if (!TryParseNexusModId(modPage, out modId))
                {
                    MessageBox.Show(
                        this,
                        "That is not a valid Darktide Nexus mod URL or numeric mod ID.",
                        "Invalid Nexus mod",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return null;
                }
            }

            _nexusModIds[modName] = modId;
            SaveSettings();
        }

        if (string.IsNullOrWhiteSpace(_nexusApiKey))
        {
            var apiKey = PromptForNexusText(
                "Nexus API key",
                "Paste your Nexus API key. It is encrypted for this Windows account and stored only on this PC.",
                secret: true);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return null;
            }

            _nexusApiKey = apiKey;
            SaveSettings();
        }

        return modId;
    }

    private async Task<IReadOnlyList<NexusModCandidate>> SearchNexusModsAsync(string modName)
    {
        var requestBody = JsonSerializer.Serialize(new
        {
            query = NexusModSearchQuery,
            variables = new
            {
                filter = new
                {
                    op = "AND",
                    gameDomainName = new { value = NexusGameDomain, op = "EQUALS" },
                    name = new { value = modName.Replace('_', ' ').Replace('-', ' '), op = "WILDCARD" }
                },
                count = 20
            }
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, NexusGraphQlEndpoint)
        {
            Content = new StringContent(requestBody, Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await NexusHttpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Nexus GraphQL returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).",
                null,
                response.StatusCode);
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        if (root.TryGetProperty("errors", out var errors)
            && errors.ValueKind == JsonValueKind.Array
            && errors.GetArrayLength() > 0)
        {
            var message = errors[0].TryGetProperty("message", out var errorMessage)
                ? errorMessage.GetString()
                : null;
            throw new InvalidDataException(
                string.IsNullOrWhiteSpace(message) ? "Nexus returned a GraphQL error." : message);
        }

        if (!root.TryGetProperty("data", out var data)
            || !data.TryGetProperty("mods", out var mods)
            || !mods.TryGetProperty("nodes", out var nodes)
            || nodes.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Nexus returned an unexpected mod search response.");
        }

        return nodes.EnumerateArray()
            .Select(node =>
            {
                var id = ReadJsonInt64(node, "modId");
                var name = node.TryGetProperty("name", out var nameElement)
                    ? nameElement.GetString()
                    : null;
                var author = node.TryGetProperty("author", out var authorElement)
                    ? authorElement.GetString()
                    : null;
                var endorsements = ReadJsonInt64(node, "endorsements");
                return new NexusModCandidate(id, name ?? "", author ?? "", endorsements);
            })
            .Where(candidate => candidate.ModId > 0 && !string.IsNullOrWhiteSpace(candidate.Name))
            .ToList();
    }

    private NexusModCandidate? ChooseNexusCandidate(
        string installedName,
        IReadOnlyList<NexusModCandidate> candidates)
    {
        using var dialog = new Form
        {
            Text = "Confirm Nexus mod",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            ClientSize = new Size(560, 390),
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            BackColor = BackgroundColor,
            ForeColor = TextColor,
            Font = Font
        };
        dialog.Controls.Add(new Label
        {
            Text = $"Choose the Nexus mod that matches the installed folder \"{installedName}\". Verify the title before continuing.",
            Location = new Point(16, 14),
            Size = new Size(528, 42),
            ForeColor = TextColor
        });

        var candidateList = new ListBox
        {
            Location = new Point(16, 62),
            Size = new Size(528, 268),
            BackColor = PanelColor,
            ForeColor = TextColor,
            BorderStyle = BorderStyle.FixedSingle,
            IntegralHeight = false
        };
        foreach (var candidate in candidates)
        {
            candidateList.Items.Add(candidate);
        }
        if (candidateList.Items.Count > 0)
        {
            candidateList.SelectedIndex = 0;
        }
        dialog.Controls.Add(candidateList);

        var openPageButton = CreateButton("Open page", new Point(16, 342), new Size(100, 32));
        openPageButton.Click += (_, _) =>
        {
            if (candidateList.SelectedItem is NexusModCandidate candidate)
            {
                OpenNexusModPage(candidate.ModId);
            }
        };
        dialog.Controls.Add(openPageButton);

        var okButton = CreateButton("Use selected", new Point(368, 342), new Size(92, 32));
        okButton.DialogResult = DialogResult.OK;
        var cancelButton = CreateButton("Cancel", new Point(464, 342), new Size(80, 32));
        cancelButton.DialogResult = DialogResult.Cancel;
        dialog.Controls.Add(okButton);
        dialog.Controls.Add(cancelButton);
        dialog.AcceptButton = okButton;
        dialog.CancelButton = cancelButton;

        return dialog.ShowDialog(this) == DialogResult.OK
            ? candidateList.SelectedItem as NexusModCandidate
            : null;
    }

    private string? PromptForNexusText(
        string title,
        string prompt,
        bool secret = false,
        bool showApiSettingsLink = true)
    {
        using var dialog = new Form
        {
            Text = title,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            ClientSize = new Size(510, secret ? 190 : 155),
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            BackColor = BackgroundColor,
            ForeColor = TextColor,
            Font = Font
        };
        var promptLabel = new Label
        {
            Text = prompt,
            Location = new Point(16, 14),
            Size = new Size(478, 42),
            ForeColor = TextColor
        };
        dialog.Controls.Add(promptLabel);

        var textBox = new TextBox
        {
            Location = new Point(16, 62),
            Size = new Size(478, 28),
            BackColor = PanelColor,
            ForeColor = TextColor,
            BorderStyle = BorderStyle.FixedSingle,
            UseSystemPasswordChar = secret
        };
        dialog.Controls.Add(textBox);

        if (secret && showApiSettingsLink)
        {
            var settingsLink = new LinkLabel
            {
                Text = "Open Nexus API settings",
                Location = new Point(16, 101),
                Size = new Size(200, 22),
                LinkColor = AccentColor,
                ActiveLinkColor = AccentColor
            };
            settingsLink.LinkClicked += (_, _) => OpenNexusApiSettings();
            dialog.Controls.Add(settingsLink);
        }

        var buttonTop = secret ? 140 : 105;
        var okButton = CreateButton("OK", new Point(326, buttonTop), new Size(80, 32));
        okButton.DialogResult = DialogResult.OK;
        var cancelButton = CreateButton("Cancel", new Point(414, buttonTop), new Size(80, 32));
        cancelButton.DialogResult = DialogResult.Cancel;
        dialog.Controls.Add(okButton);
        dialog.Controls.Add(cancelButton);
        dialog.AcceptButton = okButton;
        dialog.CancelButton = cancelButton;

        return dialog.ShowDialog(this) == DialogResult.OK ? textBox.Text.Trim() : null;
    }

    private static bool TryParseNexusModId(string input, out long modId)
    {
        if (long.TryParse(input.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out modId)
            && modId > 0)
        {
            return true;
        }

        modId = 0;
        if (!Uri.TryCreate(input.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !(uri.Host.Equals("nexusmods.com", StringComparison.OrdinalIgnoreCase)
                || uri.Host.Equals("www.nexusmods.com", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index + 2 < segments.Length; index++)
        {
            if (segments[index].Equals(NexusGameDomain, StringComparison.OrdinalIgnoreCase)
                && segments[index + 1].Equals("mods", StringComparison.OrdinalIgnoreCase)
                && long.TryParse(segments[index + 2], NumberStyles.None, CultureInfo.InvariantCulture, out modId)
                && modId > 0)
            {
                return true;
            }
        }

        modId = 0;
        return false;
    }

    private void OpenNexusApiSettings()
    {
        OpenWebsite("https://www.nexusmods.com/users/myaccount?tab=api");
    }

    private void OpenNexusModPage(long modId)
    {
        OpenWebsite($"https://www.nexusmods.com/{NexusGameDomain}/mods/{modId}");
    }

    private void OpenWebsite(string address)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = address, UseShellExecute = true });
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            SetStatus($"Could not open the Nexus page: {ex.Message}", true);
        }
    }

    private async Task<NexusFile> GetLatestNexusFileAsync(long modId)
    {
        var filesAddress =
            $"https://api.nexusmods.com/v1/games/{NexusGameDomain}/mods/{modId}/files.json?category=main";
        using var document = await GetNexusJsonAsync(filesAddress, "mod file list");
        if (!document.RootElement.TryGetProperty("files", out var filesElement)
            || filesElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Nexus returned an unexpected mod file list.");
        }

        var mainFiles = filesElement.EnumerateArray()
            .Where(file =>
            {
                if (file.TryGetProperty("category_id", out var categoryId)
                    && categoryId.TryGetInt32(out var categoryNumber))
                {
                    return categoryNumber == 1;
                }

                if (file.TryGetProperty("category_name", out var categoryName)
                    && categoryName.ValueKind == JsonValueKind.String)
                {
                    return categoryName.GetString()?.Equals("MAIN", StringComparison.OrdinalIgnoreCase) == true;
                }

                return true;
            })
            .OrderByDescending(file => ReadJsonInt64(file, "uploaded_timestamp"))
            .ToList();

        if (mainFiles.Count == 0)
        {
            throw new InvalidDataException("No main file was found for this Nexus mod.");
        }

        var latest = mainFiles[0];
        var fileId = ReadJsonInt64(latest, "file_id");
        if (fileId <= 0)
        {
            throw new InvalidDataException("Nexus returned a mod file without a valid ID.");
        }
        var version = latest.TryGetProperty("version", out var versionElement)
            ? versionElement.ToString()
            : "";

        var downloadAddress =
            $"https://api.nexusmods.com/v1/games/{NexusGameDomain}/mods/{modId}/files/{fileId}/download_link.json";
        using var links = await GetNexusDownloadLinksAsync(downloadAddress, modId, fileId);
        var downloadUri = ReadNexusDownloadUri(links.RootElement);
        return new NexusFile(fileId, version, downloadUri);
    }

    private async Task<JsonDocument> GetNexusDownloadLinksAsync(
        string address,
        long modId,
        long fileId)
    {
        try
        {
            return await GetNexusJsonAsync(address, "download link");
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            var useWebsiteAuthorization = MessageBox.Show(
                this,
                "Nexus denied the direct download-link request. Premium accounts and files with direct downloads enabled normally work automatically. For other accounts, Nexus requires a short-lived key from its website-generated download link.\r\n\r\nOpen the mod page to get that link?",
                "Nexus download authorization required",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);
            if (useWebsiteAuthorization != DialogResult.Yes)
            {
                throw new HttpRequestException(
                    "Nexus denied the download-link request (HTTP 403).",
                    ex,
                    ex.StatusCode);
            }

            OpenNexusModPage(modId);
            var nxmLink = PromptForNexusText(
                "Nexus download authorization",
                "On the mod page, copy the Download with Manager link for this exact file and paste the complete nxm:// link here.",
                secret: true,
                showApiSettingsLink: false);
            if (string.IsNullOrWhiteSpace(nxmLink))
            {
                throw new HttpRequestException(
                    "No Nexus download authorization link was provided.",
                    ex,
                    ex.StatusCode);
            }
            if (!TryParseNexusDownloadAuthorization(nxmLink, modId, fileId, out var authorization))
            {
                throw new InvalidDataException(
                    "The pasted Nexus link is invalid, expired, or does not match the selected mod's latest main file.");
            }

            var authorizedAddress = new UriBuilder(address)
            {
                Query = $"key={Uri.EscapeDataString(authorization.Key)}&expires={authorization.Expires}"
            }.Uri.AbsoluteUri;
            try
            {
                return await GetNexusJsonAsync(authorizedAddress, "authorized download link");
            }
            catch (HttpRequestException retryException)
                when (retryException.StatusCode == HttpStatusCode.Forbidden)
            {
                throw new HttpRequestException(
                    "Nexus rejected the website-generated download authorization. The link may have expired; retry with a newly copied link for the same file.",
                    retryException,
                    retryException.StatusCode);
            }
        }
    }

    private async Task<JsonDocument> GetNexusJsonAsync(string address, string operation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, address);
        request.Headers.TryAddWithoutValidation("apikey", _nexusApiKey);
        request.Headers.TryAddWithoutValidation("Application-Name", "DarktideModManager");
        request.Headers.TryAddWithoutValidation("Application-Version", "1.0.0");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await NexusHttpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var keyGuidance = response.StatusCode == HttpStatusCode.Forbidden
                && operation == "mod file list"
                ? " Check that the saved Nexus API key is valid."
                : "";
            throw new HttpRequestException(
                $"Nexus API returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}) while requesting the {operation}.{keyGuidance}",
                null,
                response.StatusCode);
        }

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static bool TryParseNexusDownloadAuthorization(
        string input,
        long expectedModId,
        long expectedFileId,
        out NexusDownloadAuthorization authorization)
    {
        authorization = new NexusDownloadAuthorization("", 0);
        if (!Uri.TryCreate(input.Trim(), UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals("nxm", StringComparison.OrdinalIgnoreCase)
            || !uri.Host.Equals(NexusGameDomain, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 4
            || !segments[0].Equals("mods", StringComparison.OrdinalIgnoreCase)
            || !long.TryParse(segments[1], NumberStyles.None, CultureInfo.InvariantCulture, out var modId)
            || modId != expectedModId
            || !segments[2].Equals("files", StringComparison.OrdinalIgnoreCase)
            || !long.TryParse(segments[3], NumberStyles.None, CultureInfo.InvariantCulture, out var fileId)
            || fileId != expectedFileId)
        {
            return false;
        }

        string? key = null;
        long expires = 0;
        foreach (var parameter in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = parameter.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var name = Uri.UnescapeDataString(parameter[..separator].Replace('+', ' '));
            var value = Uri.UnescapeDataString(parameter[(separator + 1)..].Replace('+', ' '));
            if (name.Equals("key", StringComparison.OrdinalIgnoreCase))
            {
                key = value;
            }
            else if (name.Equals("expires", StringComparison.OrdinalIgnoreCase))
            {
                long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out expires);
            }
        }

        if (string.IsNullOrWhiteSpace(key) || expires <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        {
            return false;
        }

        authorization = new NexusDownloadAuthorization(key, expires);
        return true;
    }

    private static long ReadJsonInt64(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return 0;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
        {
            return number;
        }
        return value.ValueKind == JsonValueKind.String
            && long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
                ? number
                : 0;
    }

    private static Uri ReadNexusDownloadUri(JsonElement links)
    {
        IEnumerable<JsonElement> candidates = links.ValueKind switch
        {
            JsonValueKind.Array => links.EnumerateArray().ToArray(),
            JsonValueKind.Object => new[] { links },
            _ => Array.Empty<JsonElement>()
        };

        foreach (var candidate in candidates)
        {
            if (candidate.TryGetProperty("URI", out var uriElement)
                && uriElement.ValueKind == JsonValueKind.String
                && Uri.TryCreate(uriElement.GetString(), UriKind.Absolute, out var uri)
                && uri.Scheme == Uri.UriSchemeHttps)
            {
                return uri;
            }
        }

        throw new InvalidDataException("Nexus did not return a valid secure download link.");
    }

    private static async Task<string> DownloadNexusArchiveAsync(Uri downloadUri)
    {
        const long maximumDownloadSize = 2L * 1024 * 1024 * 1024;
        using var response = await NexusHttpClient.GetAsync(downloadUri, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Nexus file download returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).",
                null,
                response.StatusCode);
        }
        if (response.Content.Headers.ContentLength > maximumDownloadSize)
        {
            throw new InvalidDataException("The Nexus archive is larger than the 2 GB safety limit.");
        }

        var archivePath = Path.Combine(Path.GetTempPath(), $"DarktideModManager-{Guid.NewGuid():N}.zip");
        var completed = false;
        try
        {
            await using var source = await response.Content.ReadAsStreamAsync();
            await using var destination = new FileStream(
                archivePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous);
            var buffer = new byte[81920];
            long totalBytes = 0;
            int bytesRead;
            while ((bytesRead = await source.ReadAsync(buffer.AsMemory(0, buffer.Length))) > 0)
            {
                totalBytes += bytesRead;
                if (totalBytes > maximumDownloadSize)
                {
                    throw new InvalidDataException("The Nexus archive is larger than the 2 GB safety limit.");
                }
                await destination.WriteAsync(buffer.AsMemory(0, bytesRead));
            }

            completed = true;
            return archivePath;
        }
        finally
        {
            if (!completed && File.Exists(archivePath))
            {
                File.Delete(archivePath);
            }
        }
    }
    private static void ExtractModArchive(string archivePath, string destinationDirectory)
    {
        const long maximumExtractedSize = 2L * 1024 * 1024 * 1024;
        const int maximumEntryCount = 20000;
        var destinationRoot = Path.GetFullPath(destinationDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var destinationPrefix = destinationRoot + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count == 0 || archive.Entries.Count > maximumEntryCount)
        {
            throw new InvalidDataException("The ZIP archive is empty or contains too many entries.");
        }

        long extractedSize = 0;
        foreach (var entry in archive.Entries)
        {
            if (entry.Length > maximumExtractedSize - extractedSize)
            {
                throw new InvalidDataException("The ZIP archive expands beyond the 2 GB safety limit.");
            }
            extractedSize += entry.Length;

            var outputPath = Path.GetFullPath(Path.Combine(destinationRoot, entry.FullName));
            if (!outputPath.StartsWith(destinationPrefix, StringComparison.OrdinalIgnoreCase)
                && !outputPath.Equals(destinationRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The ZIP archive contains an unsafe file path.");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(outputPath);
                continue;
            }

            var parentDirectory = Path.GetDirectoryName(outputPath)
                ?? throw new InvalidDataException("The ZIP archive contains an invalid file path.");
            Directory.CreateDirectory(parentDirectory);
            entry.ExtractToFile(outputPath, true);
        }
    }

    private static string FindUpdatedModDirectory(string stagingDirectory, string modName)
    {
        var candidates = Directory.EnumerateFiles(stagingDirectory, "mod.txt", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(stagingDirectory, "*.mod", SearchOption.AllDirectories))
            .Select(Path.GetDirectoryName)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var matchingCandidates = candidates
            .Where(path =>
                Path.GetFileName(path).Equals(modName, StringComparison.OrdinalIgnoreCase)
                || Directory.EnumerateFiles(path, "*.mod", SearchOption.TopDirectoryOnly)
                    .Any(modFile => Path.GetFileNameWithoutExtension(modFile)
                        .Equals(modName, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (matchingCandidates.Count == 1)
        {
            return matchingCandidates[0];
        }
        if (candidates.Count == 1)
        {
            return candidates[0];
        }
        if (candidates.Count == 0)
        {
            throw new InvalidDataException(
                "No supported mod folder was found. A mod folder must contain mod.txt or a .mod file.");
        }

        throw new InvalidDataException("The ZIP contains multiple mod folders, so the update target is ambiguous.");
    }

    private void SaveModList()
    {
        if (!Directory.Exists(_modsDirectory))
        {
            SetStatus("Cannot save: the mods folder does not exist.", true);
            return;
        }

        var lines = _headerComments.Concat(_modList.Items
            .Cast<ListViewItem>()
            .Select(item => item.Checked ? item.Text : $"--{item.Text}"));
        var temporaryFile = $"{_orderFile}.{Guid.NewGuid():N}.tmp";
        var backupFile = $"{_orderFile}.bak";

        try
        {
            File.WriteAllText(temporaryFile, string.Join(Environment.NewLine, lines), new UTF8Encoding(false));
            if (File.Exists(_orderFile))
            {
                File.Replace(temporaryFile, _orderFile, backupFile, true);
            }
            else
            {
                File.Move(temporaryFile, _orderFile);
            }

            SetDirtyState(false);
            UpdateModCount();
            SetStatus("Load order saved. Disabled mods stay in the list for easy re-enabling.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try
            {
                if (File.Exists(temporaryFile))
                {
                    File.Delete(temporaryFile);
                }
            }
            catch (Exception cleanupException) when (cleanupException is IOException or UnauthorizedAccessException)
            {
            }

            SetStatus($"Could not save the load order: {ex.Message}", true);
            MessageBox.Show(
                this,
                $"Could not save mod_load_order.txt.\r\n\r\n{ex.Message}",
                "Save failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void RunPatchHelper()
    {
        var patchScript = Path.Combine(_gameDirectory, "toggle_darktide_mods.bat");
        if (!File.Exists(patchScript))
        {
            SetStatus("The Mod Loader patch helper is missing from this game folder.", true);
            return;
        }

        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = patchScript,
                WorkingDirectory = _gameDirectory,
                UseShellExecute = true
            });
            if (process is null)
            {
                SetStatus("Patch helper opened; patch status will be checked on the next launch.");
                return;
            }

            process.Exited += (_, _) =>
            {
                if (IsDisposed || !IsHandleCreated)
                {
                    process.Dispose();
                    return;
                }

                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        CheckPatchStatus(showWarning: false, refreshFingerprint: true);
                        process.Dispose();
                    }));
                }
                catch (InvalidOperationException)
                {
                    process.Dispose();
                }
            };
            process.EnableRaisingEvents = true;
            SetStatus("Patch helper is open. Patch status will refresh when it closes.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            SetStatus($"Could not start the patch helper: {ex.Message}", true);
            MessageBox.Show(
                this,
                $"Could not start toggle_darktide_mods.bat.\r\n\r\n{ex.Message}",
                "Patch helper failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void OpenModsFolder()
    {
        if (Directory.Exists(_modsDirectory))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _modsDirectory,
                UseShellExecute = true
            });
        }
    }

    private void OpenSelectedModFolder()
    {
        if (_modList.SelectedItems.Count == 0)
        {
            return;
        }

        var path = Path.Combine(_modsDirectory, _modList.SelectedItems[0].Text);
        if (Directory.Exists(path))
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
    }

    private bool ConfirmDiscardChanges()
    {
        if (!_hasPendingChanges)
        {
            return true;
        }

        return MessageBox.Show(
            this,
            "You have unapplied changes. Discard them?",
            "Unapplied changes",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question) == DialogResult.Yes;
    }

    private void SetDirtyState(bool dirty)
    {
        _hasPendingChanges = dirty;
        _applyButton.Enabled = dirty && Directory.Exists(_modsDirectory);
        _discardButton.Enabled = dirty;
        _pendingLabel.Visible = dirty;
    }

    private void UpdateModCount()
    {
        var enabled = _modList.Items.Cast<ListViewItem>().Count(item => item.Checked);
        _countLabel.Text = $"{enabled} enabled  /  {_modList.Items.Count} mods";
    }

    private void ResizeLastModColumn()
    {
        if (_modList.Columns.Count < 3)
        {
            return;
        }

        _modList.Columns[2].Width = Math.Max(
            80,
            _modList.ClientSize.Width - _modList.Columns[0].Width - _modList.Columns[1].Width);
    }

    private void SetStatus(string message, bool isError = false)
    {
        _statusLabel.Text = message;
        _statusLabel.ForeColor = isError ? ErrorColor : MutedColor;
    }

    private sealed record ModEntry(string Name, bool Enabled, bool Exists, string Version);
    private sealed record NexusModCandidate(long ModId, string Name, string Author, long Endorsements)
    {
        public override string ToString() =>
            $"{Name}  |  {Author}  |  ID {ModId}  |  {Endorsements:N0} endorsements";
    }
    private sealed record NexusDownloadAuthorization(string Key, long Expires);
    private sealed record NexusFile(long Id, string Version, Uri DownloadUri);
    private sealed record GameFingerprint(string FileVersion, long FileLength, long LastWriteUtcTicks);
    private sealed record ManagerSettings(
        string GameDirectory,
        string? ProtectedNexusApiKey = null,
        Dictionary<string, long>? NexusModIds = null,
        Dictionary<string, GameFingerprint>? GameFingerprints = null);
}
