using EocTrainer.Core;

namespace EocTrainer;

public sealed class MainForm : Form
{
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly BridgeClient _bridge;

    private GameInstall? _install;

    // Header controls
    private readonly Label _gameLabel = new() { AutoSize = true };
    private readonly Label _extenderLabel = new() { AutoSize = true };
    private readonly Label _modLabel = new() { AutoSize = true };
    private readonly Label _connectionLabel = new() { AutoSize = true };
    private readonly Button _browseButton = new() { Text = "选择游戏目录…", AutoSize = true };
    private readonly Button _launchButton = new() { Text = "启动游戏", AutoSize = true };
    private readonly Button _installButton = new() { Text = "安装 / 更新模组", AutoSize = true };
    private readonly Button _uninstallButton = new() { Text = "卸载模组", AutoSize = true };

    // Party
    private readonly ListBox _partyList = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly Label _partyHint = new() { Dock = DockStyle.Bottom, Height = 44, AutoSize = false, Text = "等待游戏…" };

    // Console injection (no mod required)
    private readonly CheckBox _consoleMode = new() { Text = "免模组模式（控制台注入）", AutoSize = true };
    private readonly Button _consoleEnableButton = new() { Text = "启用控制台", AutoSize = true };
    private readonly Button _consoleWindowButton = new() { Text = "隐藏控制台窗口", AutoSize = true };
    private ConsoleChannel? _consoleChannel;
    private DateTime _lastConsoleAttempt = DateTime.MinValue;
    private DateTime _consoleSuppressedUntil = DateTime.MinValue;
    private string _lastConsoleMessage = "";

    // Points tab
    private readonly Dictionary<string, Label> _pointValues = new();
    private readonly Dictionary<string, NumericUpDown> _pointAmounts = new();

    // Attributes / abilities
    private readonly DataGridView _attributeGrid = MakeGrid();
    private readonly DataGridView _abilityGrid = MakeGrid();
    private readonly CheckBox _showLegacyAbilities = new() { Text = "显示旧版/隐藏能力", AutoSize = true };
    private readonly CheckBox _showLegacyTalents = new() { Text = "显示全部天赋条目（含未在角色面板出现的旧版条目）", AutoSize = true };

    // Talents
    private readonly CheckedListBox _talentList = new() { Dock = DockStyle.Fill, CheckOnClick = true };
    private readonly TextBox _talentFilter = new() { Dock = DockStyle.Top };
    private readonly Label _talentStatus = new() { Dock = DockStyle.Bottom, Height = 24 };

    // Other tab
    private readonly NumericUpDown _goldAmount = new() { Minimum = -99999999, Maximum = 99999999, Value = 1000 };
    private readonly NumericUpDown _pointAmountShared = new() { Minimum = -999, Maximum = 999, Value = 10 };
    private readonly NumericUpDown _vitalityValue = new() { Minimum = 0, Maximum = 999999, Value = 0 };
    private readonly NumericUpDown _armorValue = new() { Minimum = 0, Maximum = 999999, Value = 0 };
    private readonly NumericUpDown _magicArmorValue = new() { Minimum = 0, Maximum = 999999, Value = 0 };
    private readonly Label _otherValues = new() { AutoSize = true };
    private readonly ComboBox _boostPicker = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240 };
    private readonly NumericUpDown _boostValue = new() { Minimum = -9999, Maximum = 9999 };

    // Log tab
    private readonly TextBox _logBox = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true, Dock = DockStyle.Fill };
    private readonly TextBox _evalBox = new() { Multiline = true, Height = 60, Dock = DockStyle.Top };
    private readonly TextBox _l10nBox = new() { Text = "WarriorLore, TALENT_Bully, Bully", Dock = DockStyle.Top };

    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 250 };
    private readonly ToolStripStatusLabel _status = new() { Text = "就绪" };
    private TabControl _tabs = new();

    private string? _selectedGuid;
    private bool _updating;

    public MainForm(int initialTab = 0)
    {
        _bridge = new BridgeClient(_settings);
        Text = "神界：原罪 2 修改器 · EoC Trainer";
        MinimumSize = new Size(1100, 720);
        StartPosition = FormStartPosition.CenterScreen;

        BuildLayout();
        RefreshInstallation();

        if (initialTab > 0 && initialTab < _tabs.TabCount) _tabs.SelectedIndex = initialTab;

        _timer.Tick += (_, _) => OnTick();
        _timer.Start();
    }

    // ---------------------------------------------------------------- layout

    private void BuildLayout()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 104, Padding = new Padding(12, 8, 12, 8) };

        var gameRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 32, AutoSize = false, WrapContents = false };
        gameRow.Controls.Add(_gameLabel);
        gameRow.Controls.Add(_browseButton);
        gameRow.Controls.Add(_launchButton);
        gameRow.Controls.Add(_installButton);
        gameRow.Controls.Add(_uninstallButton);

        var statusRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 28, AutoSize = false, WrapContents = false };
        statusRow.Controls.Add(_extenderLabel);
        statusRow.Controls.Add(new Label { Text = "   ", AutoSize = true });
        statusRow.Controls.Add(_modLabel);

        var connectionRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 28, AutoSize = false, WrapContents = false };
        connectionRow.Controls.Add(_connectionLabel);
        connectionRow.Controls.Add(new Label { Text = "   ", AutoSize = true });
        connectionRow.Controls.Add(_consoleMode);
        connectionRow.Controls.Add(_consoleEnableButton);
        connectionRow.Controls.Add(_consoleWindowButton);

        header.Controls.Add(connectionRow);
        header.Controls.Add(statusRow);
        header.Controls.Add(gameRow);

        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 260, FixedPanel = FixedPanel.Panel1 };

        var partyBox = new GroupBox { Dock = DockStyle.Fill, Text = "角色" };
        partyBox.Controls.Add(_partyList);
        partyBox.Controls.Add(_partyHint);
        split.Panel1.Controls.Add(partyBox);

        split.Panel2.Controls.Add(BuildTabs());

        var statusStrip = new StatusStrip();
        statusStrip.Items.Add(_status);

        Controls.Add(split);
        Controls.Add(header);
        Controls.Add(statusStrip);

        _browseButton.Click += (_, _) => BrowseForGame();
        _launchButton.Click += (_, _) => LaunchGame();
        _installButton.Click += (_, _) => InstallMod();
        _uninstallButton.Click += (_, _) => UninstallMod();
        _partyList.SelectedIndexChanged += (_, _) => OnCharacterChanged();
        _showLegacyAbilities.CheckedChanged += (_, _) => RefreshGrids(force: true);
        _showLegacyTalents.CheckedChanged += (_, _) => RefreshTalents(force: true);
        _talentFilter.TextChanged += (_, _) => RefreshTalents(force: true);

        _showLegacyAbilities.Checked = _settings.ShowLegacyAbilities;
        _showLegacyTalents.Checked = _settings.ShowAllTalents;

        _consoleMode.Checked = _settings.UseConsoleMode;
        _consoleMode.CheckedChanged += (_, _) =>
        {
            _settings.UseConsoleMode = _consoleMode.Checked;
            _settings.Save();
            Log(_consoleMode.Checked
                ? "已开启免模组模式：游戏里没有装模组时，程序会自动通过控制台注入桥接代码。"
                : "已关闭免模组模式。");
            _lastConsoleAttempt = DateTime.MinValue;
        };

        _consoleEnableButton.Click += (_, _) =>
        {
            var needsRestart = ConsoleChannel.EnsureConsoleEnabled(out var message);
            Log(message);
            if (needsRestart)
            {
                MessageBox.Show(this,
                    message + "\n\n请完全退出游戏并重新启动，然后读取存档，程序会自动注入。",
                    "需要重启游戏", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                _consoleMode.Checked = true;
            }
        };

        _consoleWindowButton.Click += (_, _) =>
        {
            try
            {
                _consoleChannel ??= new ConsoleChannel();
                if (!_consoleChannel.Console.Attached && !_consoleChannel.Console.TryAttach(out var error))
                {
                    Log("连接控制台失败：" + error);
                    return;
                }

                if (_consoleChannel.Console.IsWindowVisible())
                {
                    _consoleChannel.Console.HideWindow();
                    _consoleWindowButton.Text = "显示控制台窗口";
                }
                else
                {
                    _consoleChannel.Console.ShowWindow();
                    _consoleWindowButton.Text = "隐藏控制台窗口";
                }
            }
            catch (Exception ex)
            {
                Log("切换控制台窗口失败：" + ex.Message);
            }
        };
    }

    private TabControl BuildTabs()
    {
        _tabs = new TabControl { Dock = DockStyle.Fill };
        _tabs.TabPages.Add(BuildPointsTab());
        _tabs.TabPages.Add(BuildAttributeTab());
        _tabs.TabPages.Add(BuildAbilityTab());
        _tabs.TabPages.Add(BuildTalentTab());
        _tabs.TabPages.Add(BuildOtherTab());
        _tabs.TabPages.Add(BuildLogTab());
        return _tabs;
    }

    private static DataGridView MakeGrid() => new()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        RowHeadersVisible = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        SelectionMode = DataGridViewSelectionMode.CellSelect,
        EditMode = DataGridViewEditMode.EditOnEnter,
        BackgroundColor = SystemColors.Window,
    };

    private TabPage BuildPointsTab()
    {
        var page = new TabPage("点数 (未分配)");
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 5,
            RowCount = 6,
            AutoSize = true,
            Padding = new Padding(12),
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        panel.Controls.Add(new Label { Text = "点数类型", AutoSize = true }, 0, 0);
        panel.Controls.Add(new Label { Text = "当前", AutoSize = true }, 1, 0);
        panel.Controls.Add(new Label { Text = "数量", AutoSize = true }, 2, 0);
        panel.Controls.Add(new Label { Text = "操作", AutoSize = true }, 3, 0);

        var kinds = new (string Key, string Label)[]
        {
            ("attribute", "属性点（力量/敏捷/智力/体质/记忆/智慧）"),
            ("combatAbility", "战斗能力点"),
            ("civilAbility", "民事能力点"),
            ("talent", "天赋点"),
        };

        var row = 1;
        foreach (var (key, label) in kinds)
        {
            panel.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);

            var value = new Label { Text = "—", AutoSize = true, Anchor = AnchorStyles.Left, Font = new Font(Font, FontStyle.Bold) };
            _pointValues[key] = value;
            panel.Controls.Add(value, 1, row);

            var amount = new NumericUpDown { Minimum = -999, Maximum = 999, Value = 1, Width = 90 };
            _pointAmounts[key] = amount;
            panel.Controls.Add(amount, 2, row);

            var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            buttons.Controls.Add(MakeButton("＋增加", () => SendPoint(key, 1)));
            buttons.Controls.Add(MakeButton("－减少", () => SendPoint(key, -1)));
            buttons.Controls.Add(MakeButton("设为", () => SendPointTarget(key)));
            panel.Controls.Add(buttons, 3, row);
            row++;
        }

        var note = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(820, 0),
            Text = "「设为」会把点数池调整到输入的数量。游戏内的角色面板可能要到升级/关闭并重开面板时才刷新，但数值已经生效并会存进存档。",
            ForeColor = SystemColors.GrayText,
        };
        panel.Controls.Add(note, 0, row);
        panel.SetColumnSpan(note, 5);

        page.Controls.Add(panel);

        var extras = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(12, 6, 12, 6) };
        extras.Controls.Add(new Label { Text = "源力点/行动点数量：", AutoSize = true, Anchor = AnchorStyles.Left });
        extras.Controls.Add(_pointAmountShared);
        extras.Controls.Add(MakeButton("＋源力点", () => Send("addPoint", o => { o.Kind = "source"; o.Amount = (double)_pointAmountShared.Value; })));
        extras.Controls.Add(MakeButton("＋行动点", () => Send("addPoint", o => { o.Kind = "actionPoint"; o.Amount = (double)_pointAmountShared.Value; })));
        page.Controls.Add(extras);

        return page;
    }

    private TabPage BuildAttributeTab()
    {
        var page = new TabPage("属性");
        ConfigureGrid(_attributeGrid, new[] { "属性", "设置值", "面板显示", "新设置值（可输入）" });

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(8, 6, 8, 6) };
        bar.Controls.Add(MakeButton("应用所有改动", () => ApplyGrid(_attributeGrid, "setAttribute")));
        bar.Controls.Add(MakeButton("全部 +1", () => NudgeGrid(_attributeGrid, "setAttribute", 1, 1)));
        bar.Controls.Add(MakeButton("全部 -1", () => NudgeGrid(_attributeGrid, "setAttribute", -1, 1)));
        bar.Controls.Add(MakeButton("清空输入", () => ResetGridInput(_attributeGrid)));

        var hint = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 62,
            Padding = new Padding(10, 4, 10, 4),
            Text = "「设置值」是游戏里保存的原始数值，也是这个修改器写入的数值（0–40）。\r\n" +
                   "「面板显示」是角色面板上看到的数值：装备、天赋会在此基础上加成，" +
                   "例如「独狼」会把 10 以上的属性翻倍、上限仍是 40。",
            ForeColor = SystemColors.GrayText,
        };

        page.Controls.Add(_attributeGrid);
        page.Controls.Add(bar);
        page.Controls.Add(hint);
        return page;
    }

    private TabPage BuildAbilityTab()
    {
        var page = new TabPage("战斗 / 民事能力");
        ConfigureGrid(_abilityGrid, new[] { "能力", "分类", "当前等级", "新等级（可输入）" });

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(8, 6, 8, 6) };
        bar.Controls.Add(MakeButton("应用所有改动", () => ApplyGrid(_abilityGrid, "setAbility")));
        bar.Controls.Add(MakeButton("全部 +1", () => NudgeGrid(_abilityGrid, "setAbility", 1, 2)));
        bar.Controls.Add(MakeButton("全部 -1", () => NudgeGrid(_abilityGrid, "setAbility", -1, 2)));
        bar.Controls.Add(MakeButton("清空输入", () => ResetGridInput(_abilityGrid)));
        bar.Controls.Add(_showLegacyAbilities);

        page.Controls.Add(_abilityGrid);
        page.Controls.Add(bar);
        return page;
    }

    private TabPage BuildTalentTab()
    {
        var page = new TabPage("天赋");

        _talentFilter.PlaceholderText = "输入关键字过滤（例如 Bully / 欺凌）";
        _talentFilter.Height = 26;

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(8, 6, 8, 6) };
        bar.Controls.Add(MakeButton("应用勾选改动", ApplyTalents));
        bar.Controls.Add(MakeButton("勾选过滤结果", () => SetFilteredTalents(true)));
        bar.Controls.Add(MakeButton("取消勾选过滤结果", () => SetFilteredTalents(false)));
        bar.Controls.Add(_showLegacyTalents);
        bar.Controls.Add(_talentStatus);

        page.Controls.Add(_talentList);
        page.Controls.Add(bar);
        page.Controls.Add(_talentFilter);
        return page;
    }

    private TabPage BuildOtherTab()
    {
        var page = new TabPage("其他");
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 4,
            AutoSize = true,
            Padding = new Padding(12),
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _otherValues.AutoSize = true;
        _otherValues.MaximumSize = new Size(820, 0);

        var row = 0;
        panel.Controls.Add(new Label { Text = "金币", AutoSize = true }, 0, row);
        panel.Controls.Add(_goldAmount, 2, row);
        var goldButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        goldButtons.Controls.Add(MakeButton("增加", () => Send("addGold", o => o.Amount = (double)_goldAmount.Value)));
        goldButtons.Controls.Add(MakeButton("减少", () => Send("addGold", o => o.Amount = -(double)_goldAmount.Value)));
        goldButtons.Controls.Add(MakeButton("设为", () => Send("setGold", o => o.Value = (double)_goldAmount.Value)));
        panel.Controls.Add(goldButtons, 3, row);
        row++;

        panel.Controls.Add(new Label { Text = "当前状态", AutoSize = true }, 0, row);
        panel.Controls.Add(_otherValues, 1, row);
        panel.SetColumnSpan(_otherValues, 3);
        row++;

        panel.Controls.Add(new Label { Text = "生命值（当前）", AutoSize = true }, 0, row);
        panel.Controls.Add(_vitalityValue, 2, row);
        var vitButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        vitButtons.Controls.Add(MakeButton("设为", () => Send("setStat", o => { o.Name = "CurrentVitality"; o.Value = (double)_vitalityValue.Value; })));
        vitButtons.Controls.Add(MakeButton("回满", () => Send("setStat", o => { o.Name = "CurrentVitality"; o.Value = CurrentMemberValue("MaxVitality"); })));
        panel.Controls.Add(vitButtons, 3, row);
        row++;

        panel.Controls.Add(new Label { Text = "物理护甲（当前）", AutoSize = true }, 0, row);
        panel.Controls.Add(_armorValue, 2, row);
        var armorButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        armorButtons.Controls.Add(MakeButton("设为", () => Send("setStat", o => { o.Name = "CurrentArmor"; o.Value = (double)_armorValue.Value; })));
        armorButtons.Controls.Add(MakeButton("回满", () => Send("setStat", o => { o.Name = "CurrentArmor"; o.Value = CurrentMemberValue("MaxArmor"); })));
        panel.Controls.Add(armorButtons, 3, row);
        row++;

        panel.Controls.Add(new Label { Text = "魔法护甲（当前）", AutoSize = true }, 0, row);
        panel.Controls.Add(_magicArmorValue, 2, row);
        var magicButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        magicButtons.Controls.Add(MakeButton("设为", () => Send("setStat", o => { o.Name = "CurrentMagicArmor"; o.Value = (double)_magicArmorValue.Value; })));
        magicButtons.Controls.Add(MakeButton("回满", () => Send("setStat", o => { o.Name = "CurrentMagicArmor"; o.Value = CurrentMemberValue("MaxMagicArmor"); })));
        panel.Controls.Add(magicButtons, 3, row);
        row++;

        panel.Controls.Add(new Label { Text = "永久增益（存进存档）", AutoSize = true }, 0, row);
        _boostPicker.Items.AddRange(GameEnums.PermanentBoosts.Cast<object>().ToArray());
        _boostPicker.SelectedIndex = 0;
        panel.Controls.Add(_boostPicker, 2, row);
        var boostButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        boostButtons.Controls.Add(_boostValue);
        boostButtons.Controls.Add(MakeButton("设置", () => Send("permanentBoost", o =>
        {
            o.Name = _boostPicker.SelectedItem?.ToString();
            o.Value = (double)_boostValue.Value;
        })));
        panel.Controls.Add(boostButtons, 3, row);
        row++;

        var actionButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        actionButtons.Controls.Add(MakeButton("复活角色", () => Send("resurrect")));
        actionButtons.Controls.Add(MakeButton("重置技能冷却", () => Send("resetCooldowns")));
        panel.Controls.Add(actionButtons, 0, row);
        panel.SetColumnSpan(actionButtons, 4);

        page.Controls.Add(panel);
        return page;
    }

    private TabPage BuildLogTab()
    {
        var page = new TabPage("日志 / 调试");

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 74, Padding = new Padding(8), WrapContents = false };
        bottom.Controls.Add(new Label { Text = "在服务器 Lua 里执行：", AutoSize = true });
        bottom.Controls.Add(MakeButton("发送", () =>
        {
            var code = _evalBox.Text.Trim();
            if (code.Length == 0) return;
            Send("eval", o => o.Code = code);
        }));
        bottom.Controls.Add(MakeButton("查看游戏侧日志", AppendGameLog));
        bottom.Controls.Add(new Label { Text = "  L10N 键：", AutoSize = true });
        bottom.Controls.Add(MakeButton("探测游戏内显示名", () =>
        {
            var keys = _l10nBox.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            Send("probeL10N", o => o.Keys = keys.ToList());
        }));
        bottom.Controls.Add(_l10nBox);

        page.Controls.Add(_logBox);
        page.Controls.Add(_evalBox);
        page.Controls.Add(bottom);
        return page;
    }

    private static void ConfigureGrid(DataGridView grid, string[] headers)
    {
        foreach (var header in headers)
        {
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = header,
                ReadOnly = header != headers[^1],
            });
        }
    }

    private Button MakeButton(string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true, Margin = new Padding(3) };
        button.Click += (_, _) =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Log($"操作失败：{ex.Message}");
            }
        };
        return button;
    }

    // ------------------------------------------------------------- behaviour

    private void OnTick()
    {
        _bridge.Poll();
        _bridge.Flush();
        _bridge.ClearAcknowledgedCommand();

        UpdateConsoleChannel();
        UpdateConnectionStatus();
        _status.Text = _bridge.QueuedCount > 0
            ? $"待发送操作：{_bridge.QueuedCount}"
            : _bridge.LastError is { Length: > 0 } error ? $"错误：{error}" : "就绪";

        RefreshState();
    }

    /// <summary>
    /// In console mode the bridge code has to be (re)injected whenever the game's
    /// Lua state is fresh: after a game start, and again after every savegame load
    /// (which recreates the Lua VM). A stale bridge therefore means "inject again".
    /// </summary>
    private void UpdateConsoleChannel()
    {
        if (!_consoleMode.Checked) return;
        if (BridgeIsFresh(TimeSpan.FromSeconds(4))) return;
        if (DateTime.UtcNow < _consoleSuppressedUntil) return;
        if (DateTime.UtcNow - _lastConsoleAttempt < TimeSpan.FromSeconds(5)) return;

        _lastConsoleAttempt = DateTime.UtcNow;

        try
        {
            _consoleChannel ??= new ConsoleChannel();
            var ok = _consoleChannel.TryActivate(out var message);

            if (message != _lastConsoleMessage)
            {
                _lastConsoleMessage = message;
                Log((ok ? "控制台注入：" : "控制台注入失败：") + message);
            }

            if (_consoleChannel.Mode == ChannelMode.Mod)
            {
                // The packaged mod is doing the work; do not keep injecting.
                _consoleSuppressedUntil = DateTime.UtcNow.AddMinutes(5);
                Log("检测到模组模式在运行，暂停控制台注入 5 分钟。");
            }
        }
        catch (Exception ex)
        {
            if (ex.Message != _lastConsoleMessage)
            {
                _lastConsoleMessage = ex.Message;
                Log("控制台注入异常：" + ex.Message);
            }
        }
    }

    private bool BridgeIsFresh(TimeSpan maxAge)
    {
        try
        {
            if (!File.Exists(Paths.StateFile)) return false;
            return DateTime.UtcNow - File.GetLastWriteTimeUtc(Paths.StateFile) <= maxAge;
        }
        catch
        {
            return false;
        }
    }

    private void UpdateConnectionStatus()
    {
        var state = _bridge.State;
        if (state == null)
        {
            _connectionLabel.Text = _bridge.BridgeDirectoryExists
                ? "连接：等待游戏中的桥接回应（模组模式需安装模组；免模组模式需启用控制台并读取存档）"
                : $"连接：未找到桥接目录 {Paths.BridgeDir}";
            return;
        }

        var age = _bridge.BridgeDirectoryExists && File.Exists(Paths.StateFile)
            ? DateTime.UtcNow - File.GetLastWriteTimeUtc(Paths.StateFile)
            : TimeSpan.MaxValue;

        var freshness = age.TotalSeconds < 6
            ? (_consoleChannel?.Mode == ChannelMode.Console ? "已连接（控制台注入）" : "已连接（模组）")
            : $"已断开（最后回应 {age.TotalSeconds:F0} 秒前）";

        if (age.TotalSeconds >= 6 && _consoleMode.Checked)
        {
            freshness += GameConsole.IsPresent()
                ? " · 正在尝试控制台注入"
                : " · 未发现游戏控制台（点「启用控制台」后需重启游戏）";
        }

        _connectionLabel.Text = $"连接：{freshness} · 模组 v{state.Version} · 游戏内角色 {state.Party.Count} · 最近序号 {state.Seq}";
    }

    private void RefreshInstallation()
    {
        _install = GameInstall.Find();

        if (_install == null)
        {
            _gameLabel.Text = "游戏：未找到（请手动选择 Divinity Original Sin 2 目录）";
            _extenderLabel.Text = "扩展器：—";
            _modLabel.Text = "模组：—";
            return;
        }

        _gameLabel.Text = $"游戏：{_install.Root}";
        _extenderLabel.Text = $"Script Extender：{_install.Extender.Describe()}";
        _modLabel.Text = _install.Mod switch
        {
            { PakPresent: false, Deployed: true } => "模组：包位于游戏目录（用户目录里没有）",
            { PakPresent: true, PakUpToDate: false } => "模组：包需要更新（关闭游戏后点「安装 / 更新模组」）",
            { PakPresent: true, RegisteredAnywhere: false } => "模组：文件已就位，但还没有在任何存档配置中启用",
            { PakPresent: true } => "模组：已安装并启用",
            _ => "模组：未安装（可用免模组模式）",
        };

        _status.Text = _install.Extender.State switch
        {
            ExtenderState.Missing => "提示：需要先安装 Norbyte 的 Script Extender（把 Script Extender 的 dxgi.dll 放进 bin 目录并启动一次游戏）",
            ExtenderState.UpdaterOnly => "提示：扩展器主体还没下载，启动一次游戏让它自动下载",
            _ => "就绪",
        };
    }

    private void BrowseForGame()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择「Divinity Original Sin 2」游戏目录（里面应有 DefEd\\bin\\EoCApp.exe）",
            SelectedPath = _install?.Root ?? "",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var install = GameInstall.Inspect(dialog.SelectedPath);
        if (install == null)
        {
            MessageBox.Show(this, "这个目录里没找到 DefEd\\bin\\EoCApp.exe，请重新选择。", "路径不对",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _settings.GameDirectory = install.Root;
        _settings.Save();
        RefreshInstallation();
    }

    private void LaunchGame()
    {
        if (_install == null)
        {
            MessageBox.Show(this, "还没有找到游戏目录。", "无法启动", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _install.AppPath,
                WorkingDirectory = _install.BinDirectory,
                UseShellExecute = true,
            });
            Log("已启动游戏（首次启动前请确认模组已安装）。");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void InstallMod()
    {
        var install = _install ?? GameInstall.Find();
        if (install == null)
        {
            MessageBox.Show(this, "还没有找到游戏目录。", "无法安装", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var result = ModDeployer.Deploy(install);
        foreach (var message in result.Messages) Log(message);
        foreach (var error in result.Errors) Log("错误：" + error);

        MessageBox.Show(this,
            string.Join("\n", result.Messages.Concat(result.Errors)),
            result.Ok ? "安装完成" : "安装遇到问题",
            MessageBoxButtons.OK,
            result.Ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);

        RefreshInstallation();
    }

    private void UninstallMod()
    {
        var install = _install ?? GameInstall.Find();
        if (install == null) return;

        if (MessageBox.Show(this, "确定要卸载模组吗？（会从存档配置中移除，并删除模组文件）", "确认卸载",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        var result = ModDeployer.Uninstall(install);
        foreach (var message in result.Messages) Log(message);
        foreach (var error in result.Errors) Log("错误：" + error);
        RefreshInstallation();
    }

    // ------------------------------------------------------------ state sync

    private void RefreshState()
    {
        var state = _bridge.State;
        if (state == null) return;

        var members = state.Party;
        var signature = string.Join(",", members.Select(m => m.Guid));

        if (_updating) return;
        _updating = true;
        try
        {
            var previousSignature = string.Join(",", _partyList.Items.Cast<PartyItem>().Select(i => i.Member.Guid));
            var selection = _selectedGuid;

            if (signature != previousSignature)
            {
                _partyList.Items.Clear();
                foreach (var member in members) _partyList.Items.Add(new PartyItem(member));

                var index = members.FindIndex(m => m.Guid == selection);
                if (index < 0 && members.Count > 0) index = members.FindIndex(m => m.IsHost);
                if (index < 0 && members.Count > 0) index = 0;
                if (index >= 0 && index < _partyList.Items.Count)
                {
                    _partyList.SelectedIndex = index;
                    _selectedGuid = members[index].Guid;
                }
            }
            else
            {
                for (var i = 0; i < _partyList.Items.Count && i < members.Count; i++)
                {
                    ((PartyItem)_partyList.Items[i]!).Member = members[i];
                }
                _partyList.Refresh();
            }

            var member_ = CurrentMember;
            UpdatePointLabels(member_);
            RefreshGrids(force: false);
            RefreshTalents(force: false);
            UpdateOtherValues(member_);
            UpdatePartyHint();
        }
        finally
        {
            _updating = false;
        }
    }

    private void OnCharacterChanged()
    {
        if (_partyList.SelectedItem is not PartyItem item) return;
        _selectedGuid = item.Member.Guid;
        RefreshGrids(force: true);
        RefreshTalents(force: true);
        UpdatePointLabels(item.Member);
        UpdateOtherValues(item.Member);
        UpdatePartyHint();
    }

    /// <summary>Short summary under the party list: connection and selection state.</summary>
    private void UpdatePartyHint()
    {
        var state = _bridge.State;
        if (state == null || state.Party.Count == 0)
        {
            _partyHint.Text = _bridge.BridgeDirectoryExists
                ? "等待游戏回应…\r\n请确认模组已安装、且重启过游戏。"
                : "还没建立桥接目录。\r\n点击上方「安装 / 更新模组」。";
            return;
        }

        var member = CurrentMember;
        _partyHint.Text = member == null
            ? $"{state.Party.Count} 名角色"
            : $"已选：{member.Display}\r\n{member.Guid}";
    }

    private PartyMember? CurrentMember
    {
        get
        {
            if (_partyList.SelectedItem is PartyItem item) return item.Member;
            return _bridge.State?.Party.FirstOrDefault(p => p.IsHost) ?? _bridge.State?.Party.FirstOrDefault();
        }
    }

    private void UpdatePointLabels(PartyMember? member)
    {
        foreach (var (key, label) in _pointValues)
        {
            label.Text = member == null ? "—" : Format(Get(member.Points, key));
        }
    }

    private void UpdateOtherValues(PartyMember? member)
    {
        if (member == null)
        {
            _otherValues.Text = "—";
            return;
        }

        _otherValues.Text = string.Join("   ", new[]
        {
            $"金币 {Format(member.Gold)}",
            $"等级 {Format(member.Level)}",
            $"生命 {Format(Get(member.Vitals, "vitality"))}/{Format(Get(member.Vitals, "maxVitality"))}",
            $"物理护甲 {Format(Get(member.Vitals, "armor"))}/{Format(Get(member.Vitals, "maxArmor"))}",
            $"魔法护甲 {Format(Get(member.Vitals, "magicArmor"))}/{Format(Get(member.Vitals, "maxMagicArmor"))}",
            $"行动点 {Format(Get(member.Vitals, "ap"))}/{Format(Get(member.Vitals, "maxAp"))}",
            $"源力 {Format(Get(member.Vitals, "source"))}/{Format(Get(member.Vitals, "maxSource"))}",
        });
    }

    private void RefreshGrids(bool force)
    {
        var member = CurrentMember;
        if (member == null) return;

        RefreshAttributeGrid(member, force);
        RefreshAbilityGrid(member, force);
    }

    private void RefreshAttributeGrid(PartyMember member, bool force)
    {
        if (force || _attributeGrid.Rows.Count != GameEnums.Attributes.Length)
        {
            _attributeGrid.Rows.Clear();
            foreach (var name in GameEnums.Attributes)
            {
                _attributeGrid.Rows.Add(Labels.AttributeEntry(name), "—", "—", "");
            }
            _attributeGrid.ClearSelection();
        }

        for (var i = 0; i < GameEnums.Attributes.Length; i++)
        {
            var name = GameEnums.Attributes[i];
            var raw = Get(member.UpgradeAttributes, name);
            var sheet = Get(member.BaseAttributes, name) ?? Get(member.Attributes, name);
            var row = _attributeGrid.Rows[i];
            if (row.IsNewRow) continue;
            row.Cells[0].Value = Labels.AttributeEntry(name);
            row.Cells[1].Value = Format(raw);
            row.Cells[2].Value = Format(sheet);
            row.Tag = name;
        }
    }

    private void RefreshAbilityGrid(PartyMember member, bool force)
    {
        var names = GameEnums.Abilities
            .Where(n => _showLegacyAbilities.Checked || Labels.IsCuratedAbility(n))
            .ToArray();

        if (force || _abilityGrid.Rows.Count != names.Length)
        {
            _abilityGrid.Rows.Clear();
            foreach (var name in names)
            {
                _abilityGrid.Rows.Add(Labels.AbilityEntry(name), Labels.AbilityGroup(name), "—", "");
            }
            _abilityGrid.ClearSelection();
        }

        for (var i = 0; i < names.Length && i < _abilityGrid.Rows.Count; i++)
        {
            var name = names[i];
            var current = Get(member.BaseAbilities, name) ?? Get(member.Abilities, name);
            var row = _abilityGrid.Rows[i];
            row.Cells[0].Value = Labels.AbilityEntry(name);
            row.Cells[1].Value = Labels.AbilityGroup(name);
            row.Cells[2].Value = Format(current);
            row.Tag = name;
        }
    }

    private void RefreshTalents(bool force)
    {
        var member = CurrentMember;
        if (member == null) return;

        var filter = _talentFilter.Text.Trim();
        var names = GameEnums.Talents
            .Where(n => _showLegacyTalents.Checked || !Labels.IsLegacyTalent(n))
            .Where(n => filter.Length == 0
                        || n.Contains(filter, StringComparison.OrdinalIgnoreCase)
                        || Labels.Talent(n).Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var signature = string.Join(",", names) + "|" + member.Guid;
        if (!force && _talentList.Tag as string == signature) return;
        _talentList.Tag = signature;

        _talentList.BeginUpdate();
        _talentList.Items.Clear();
        var owned = new HashSet<string>(member.Talents, StringComparer.Ordinal);
        foreach (var name in names)
        {
            var item = new TalentItem(name);
            _talentList.Items.Add(item, owned.Contains(name));
        }
        _talentList.EndUpdate();

        _talentStatus.Text = $"已拥有 {member.Talents.Count} 个 · 列表显示 {names.Length} / {GameEnums.Talents.Length}";
    }

    // --------------------------------------------------------------- sending

    private void Send(string opName, Action<Op>? configure = null)
    {
        var member = CurrentMember;
        if (member == null)
        {
            Log("还没有选中角色（游戏里需要先进入存档）。");
            return;
        }

        var op = new Op { OpName = opName, Target = member.Guid };
        configure?.Invoke(op);
        _bridge.Enqueue(op);
        _bridge.Flush();
        Log($"→ {Describe(op)}");
    }

    private static string Describe(Op op)
    {
        var parts = new List<string> { op.OpName };
        if (op.Kind != null) parts.Add($"kind={op.Kind}");
        if (op.Name != null) parts.Add($"name={op.Name}");
        if (op.Value != null) parts.Add($"value={op.Value}");
        if (op.Amount != null) parts.Add($"amount={op.Amount}");
        if (op.Code != null) parts.Add($"code={op.Code}");
        if (op.Keys != null) parts.Add($"keys={string.Join("/", op.Keys)}");
        return string.Join(" ", parts);
    }

    private void SendPoint(string kind, int sign)
    {
        var amount = _pointAmounts.TryGetValue(kind, out var box) ? (double)box.Value : 1;
        Send("addPoint", o =>
        {
            o.Kind = kind;
            o.Amount = sign * Math.Abs(amount);
        });
    }

    private void SendPointTarget(string kind)
    {
        var member = CurrentMember;
        if (member == null) return;

        var target = _pointAmounts.TryGetValue(kind, out var box) ? (double)box.Value : 0;
        var current = Get(member.Points, kind) ?? 0;
        Send("addPoint", o =>
        {
            o.Kind = kind;
            o.Amount = target - current;
        });
    }

    private void ApplyGrid(DataGridView grid, string opName)
    {
        if (grid.Rows.Count == 0) return;

        var applied = 0;
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.Tag is not string name) continue;

            var input = row.Cells[^1].Value?.ToString()?.Trim();
            if (string.IsNullOrEmpty(input)) continue;
            if (!double.TryParse(input, out var value)) continue;

            var target = value;
            Send(opName, o =>
            {
                o.Name = name;
                o.Value = target;
            });
            row.Cells[^1].Value = null;
            applied++;
        }

        Log(applied == 0 ? "没有需要应用的改动。" : $"已提交 {applied} 项改动。");
        _bridge.Flush();
    }

    private void NudgeGrid(DataGridView grid, string opName, int delta, int currentColumn)
    {
        if (grid.Rows.Count == 0) return;

        var count = 0;
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.Tag is not string name) continue;
            if (!double.TryParse(row.Cells[currentColumn].Value?.ToString(), out var current)) continue;

            var target = current + delta;
            Send(opName, o =>
            {
                o.Name = name;
                o.Value = target;
            });
            count++;
        }

        Log(count == 0 ? "没有可调整的行。" : $"已提交 {count} 行 {delta:+#;-#;0}。");
        _bridge.Flush();
    }

    private void ResetGridInput(DataGridView grid)
    {
        foreach (DataGridViewRow row in grid.Rows) row.Cells[^1].Value = null;
    }

    private void ApplyTalents()
    {
        var member = CurrentMember;
        if (member == null) return;

        var owned = new HashSet<string>(member.Talents, StringComparer.Ordinal);
        var changes = 0;

        foreach (var entry in _talentList.Items)
        {
            if (entry is not TalentItem item) continue;
            var shouldHave = _talentList.CheckedItems.Contains(entry);
            if (shouldHave == owned.Contains(item.Name)) continue;

            var name = item.Name;
            Send("setTalent", o =>
            {
                o.Name = name;
                o.Value = shouldHave ? 1 : 0;
            });
            changes++;
        }

        Log(changes == 0 ? "天赋没有改动。" : $"已提交 {changes} 项天赋改动。");
        _bridge.Flush();
    }

    private void SetFilteredTalents(bool state)
    {
        for (var i = 0; i < _talentList.Items.Count; i++) _talentList.SetItemChecked(i, state);
    }

    private double CurrentMemberValue(string field)
    {
        var member = CurrentMember;
        if (member == null) return 0;
        return Get(member.Vitals, field) ?? 0;
    }

    private static double? Get(Dictionary<string, double?> map, string key) =>
        map.TryGetValue(key, out var value) ? value : null;

    private static string Format(double? value) =>
        value.HasValue ? value.Value.ToString("0.##") : "—";

    private void Log(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        var text = _logBox.Text.Length > 40000 ? _logBox.Text[^20000..] : _logBox.Text;
        _logBox.Text = text + line + Environment.NewLine;
        _logBox.SelectionStart = _logBox.TextLength;
        _logBox.ScrollToCaret();
    }

    /// <summary>Appends the tail of the mod's own log file to the log view.</summary>
    private void AppendGameLog()
    {
        var gameLog = _bridge.ReadGameLog();
        if (gameLog.Length == 0)
        {
            Log("游戏侧还没有写日志（可能还没进入存档）。");
            return;
        }

        var lines = gameLog.Split('\n');
        var tail = string.Join('\n', lines.TakeLast(60));
        _logBox.Text += "----- 游戏侧日志（末尾 60 行） -----" + Environment.NewLine + tail + Environment.NewLine;
        _logBox.SelectionStart = _logBox.TextLength;
        _logBox.ScrollToCaret();
    }

    private sealed class TalentItem
    {
        public TalentItem(string name) => Name = name;
        public string Name { get; }
        public override string ToString() => Labels.Talent(Name);
    }

    private sealed class PartyItem
    {
        public PartyItem(PartyMember member) => Member = member;
        public PartyMember Member { get; set; }

        public override string ToString()
        {
            var host = Member.IsHost ? "★ " : "   ";
            var dead = Member.Dead ? "（倒地）" : "";
            return $"{host}{Member.Display}{dead}  Lv{Format(Member.Level)}";
        }
    }
}
