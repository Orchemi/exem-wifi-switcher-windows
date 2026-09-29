using System.Runtime.InteropServices;
using WifiProfileSwitcher.Core;
using WifiProfileSwitcher.Windows;

namespace WifiProfileSwitcher.Setup;

internal sealed class SetupForm : Form
{
    private readonly UiSpec spec = UiSpec.Load();
    private readonly SetupPayload? payload;
    private readonly SetupOperations? operations;
    private readonly bool preview;
    private bool busy, filling, dirty;
    private bool editorCompatible = true;
    private bool startUncertain;
    private SwitcherConfig? saved;
    private Exception? lastError;
    private readonly ComboBox adapter = new() { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat };
    private readonly TextBox ssid = new(), address = new(), subnet = new(), gateway = new(), dns = new();
    private readonly Label installState = new() { AutoSize = true }, switchState = new() { AutoSize = true };
    private readonly Label status = new() { Dock = DockStyle.Fill, AutoSize = false, Text = "현재 설정을 읽는 중…" };
    private readonly LinkLabel detect = new() { Text = "감지", AutoSize = true }, toggle = new() { Text = "끄기", AutoSize = true };
    private readonly Button more = new() { Text = "더 보기", AutoSize = true }, close = new() { Text = "닫기", AutoSize = true }, save = new() { Text = "저장하고 시작", AutoSize = true };
    private readonly ContextMenuStrip menu = new();
    private readonly ToolStripMenuItem detailsItem = new("오류 자세히"), copyItem = new("오류 정보 복사"), removeItem = new("앱 제거"), dhcpItem = new("DHCP로 복구"), restoreItem = new("최초 백업 복원");

    public SetupForm(SetupPayload? payload, bool preview = false)
    {
        this.payload = payload; this.preview = preview;
        operations = payload is null ? null : new SetupOperations(payload);
        Text = spec.Title; Font = new Font("Segoe UI", 10);
        BackColor = ColorTranslator.FromHtml(spec.Background); ForeColor = ColorTranslator.FromHtml(spec.Text);
        AutoScaleMode = AutoScaleMode.Dpi; StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(spec.Width, spec.Height); MinimumSize = new Size(500, 550);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(spec.Padding), ColumnCount = 1, RowCount = 6 };
        for (var i = 0; i < 6; i++) root.RowStyles.Add(new RowStyle(i == 4 ? SizeType.Percent : SizeType.AutoSize, i == 4 ? 100 : 0));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.Controls.Add(new Label { Text = spec.Hint, AutoSize = true, MaximumSize = new Size(480, 0), ForeColor = ColorTranslator.FromHtml(spec.Muted), Margin = new Padding(0, 0, 0, 18) }, 0, 0);
        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 6, Margin = Padding.Empty };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, spec.LabelWidth)); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 6; i++) fields.RowStyles.Add(new RowStyle(SizeType.Absolute, spec.RowHeight));
        var wifiRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
        wifiRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); wifiRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 56));
        wifiRow.Controls.Add(ssid, 0, 0); detect.AutoSize = false; detect.Size = new Size(44, 28); detect.TextAlign = ContentAlignment.MiddleCenter; detect.Anchor = AnchorStyles.Right; detect.Margin = new Padding(8, 0, 0, 0); wifiRow.Controls.Add(detect, 1, 0);
        Field(fields, spec.AdapterLabel, adapter, 0); Field(fields, spec.SsidLabel, wifiRow, 1);
        Field(fields, spec.AddressLabel, address, 2); Field(fields, spec.SubnetLabel, subnet, 3); Field(fields, spec.GatewayLabel, gateway, 4); Field(fields, spec.DnsLabel, dns, 5);
        root.Controls.Add(fields, 0, 1);
        root.Controls.Add(new Panel { Height = 1, Dock = DockStyle.Top, BackColor = Color.FromArgb(64, 64, 64), Margin = new Padding(0, 20, 0, 16) }, 0, 2);
        var states = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, RowCount = 2, Margin = new Padding(0, 0, 0, 12) };
        states.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, spec.LabelWidth)); states.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); states.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        states.Controls.Add(StateLabel("설정"), 0, 0); states.Controls.Add(installState, 1, 0);
        states.Controls.Add(StateLabel("자동 전환"), 0, 1); states.Controls.Add(switchState, 1, 1); states.Controls.Add(toggle, 2, 1);
        foreach (Control control in states.Controls) control.Margin = new Padding(0, 0, 0, 8);
        root.Controls.Add(states, 0, 3); status.ForeColor = ColorTranslator.FromHtml(spec.Muted); root.Controls.Add(status, 0, 4);
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 4, Margin = new Padding(0, 12, 0, 0) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.Controls.Add(more, 0, 0); footer.Controls.Add(close, 2, 0); footer.Controls.Add(save, 3, 0); root.Controls.Add(footer, 0, 5);
        Controls.Add(root);
        foreach (var box in new[] { ssid, address, subnet, gateway, dns })
        {
            box.Dock = DockStyle.Fill; box.AutoSize = false; box.Margin = new Padding(0, 4, 0, 4);
            box.BorderStyle = BorderStyle.FixedSingle; box.BackColor = ColorTranslator.FromHtml(spec.Surface); box.ForeColor = ForeColor;
            box.TextChanged += (_, _) => Changed();
        }
        adapter.BackColor = ColorTranslator.FromHtml(spec.Surface); adapter.ForeColor = ForeColor; adapter.SelectedIndexChanged += (_, _) => Changed();
        foreach (var link in new[] { detect, toggle }) { link.LinkColor = ColorTranslator.FromHtml(spec.Accent); link.ActiveLinkColor = link.LinkColor; link.DisabledLinkColor = Color.Gray; }
        foreach (var button in new[] { more, close, save })
        {
            button.MinimumSize = new Size(80, 34); button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderSize = 0;
            button.BackColor = ColorTranslator.FromHtml(spec.Surface); button.ForeColor = ForeColor; button.Margin = new Padding(0, 0, 8, 0);
        }
        save.BackColor = ColorTranslator.FromHtml(spec.Accent); save.ForeColor = Color.FromArgb(24, 24, 24); save.Margin = Padding.Empty;
        ssid.AccessibleName = spec.SsidLabel; address.AccessibleName = spec.AddressLabel; subnet.AccessibleName = spec.SubnetLabel; gateway.AccessibleName = spec.GatewayLabel; dns.AccessibleName = spec.DnsLabel; adapter.AccessibleName = spec.AdapterLabel;
        ssid.TabIndex = 1; address.TabIndex = 2; subnet.TabIndex = 3; gateway.TabIndex = 4; dns.TabIndex = 5; adapter.TabIndex = 0;
        dns.PlaceholderText = "쉼표로 구분";
        more.Click += (_, _) => menu.Show(more, new Point(0, more.Height)); close.Click += (_, _) => Close();
        save.Click += async (_, _) => await Run("설정을 저장하는 중…", Save);
        detect.LinkClicked += async (_, _) =>
        {
            if (dirty && !ConfirmDialog.Show(this, "현재 설정 가져오기", "입력한 값 대신 현재 연결의 설정을 가져올까요?", "가져오기")) return;
            await Run("현재 설정을 읽는 중…", Detect);
        };
        toggle.LinkClicked += async (_, _) => await ChangeSwitching();
        BuildMenu();
        FormClosing += (_, e) =>
        {
            if (busy) { e.Cancel = true; return; }
            if (!preview && dirty && !ConfirmDialog.Show(this, "저장하지 않고 닫기", "입력한 변경 사항은 저장되지 않습니다.", "닫기")) e.Cancel = true;
        };
        Shown += async (_, _) => { if (!preview) await Run("현재 설정을 읽는 중…", Initialize); };
        RefreshState();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (OperatingSystem.IsWindows()) { var dark = 1; _ = DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int)); }
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private void Field(TableLayoutPanel panel, string name, Control control, int row)
    {
        panel.Controls.Add(new Label { Text = name, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Padding = new Padding(0, 0, 12, 0), Margin = Padding.Empty }, 0, row);
        control.Dock = DockStyle.Fill; control.Margin = new Padding(0, 4, 0, 4); panel.Controls.Add(control, 1, row);
    }
    private Label StateLabel(string name) => new() { Text = name, AutoSize = true, ForeColor = ColorTranslator.FromHtml(spec.Muted) };
    private void Changed() { if (!filling) { dirty = true; RefreshState(); } }

    private async Task Initialize()
    {
        try { PopulateAdapters(); }
        catch (Exception ex) { lastError = ex; status.Text = SetupMessages.Short(ex); }
        if (SetupOperations.Installed)
        {
            saved = SetupOperations.LoadInstalled(); Fill(saved);
            startUncertain = saved.Mode == "enforce" && SetupOperations.ServiceIsRunning() != true;
            status.Text = startUncertain ? "자동 전환 서비스 상태를 확인하지 못했습니다. ‘저장하고 시작’으로 다시 시도하세요." : "저장된 설정을 불러왔습니다.";
        }
        else await Detect();
    }

    private void PopulateAdapters()
    {
        var list = operations!.GetAdapters();
        filling = true;
        try { adapter.Items.Clear(); foreach (var item in list) adapter.Items.Add(new AdapterItem(item.Id, item.Description)); if (adapter.Items.Count == 1) adapter.SelectedIndex = 0; }
        finally { filling = false; }
    }

    private async Task Detect()
    {
        try
        {
            var config = await operations!.Capture(); Fill(config); dirty = true;
            status.Text = "현재 설정을 가져왔습니다. 확인 후 저장하세요.";
        }
        catch (Exception ex)
        {
            lastError = ex;
            // Import manual IPv4 fields independently of the SSID lookup, but never freeze a DHCP lease.
            if (adapter.SelectedItem is AdapterItem selected)
            {
                try
                {
                    var snapshot = await operations!.ReadAdapterSettings(selected.Id);
                    if (!snapshot.DhcpEnabled && !snapshot.DnsAutomatic && snapshot.AddressesReady && snapshot.Addresses.Length == 1 && snapshot.PrefixLengths.Length == 1 && snapshot.Gateways.Length == 1)
                    {
                        if (string.IsNullOrWhiteSpace(address.Text)) address.Text = snapshot.Addresses[0];
                        if (string.IsNullOrWhiteSpace(subnet.Text)) subnet.Text = ManualProfileInput.ToSubnetMask(snapshot.PrefixLengths[0]);
                        if (string.IsNullOrWhiteSpace(gateway.Text)) gateway.Text = snapshot.Gateways[0];
                        if (string.IsNullOrWhiteSpace(dns.Text)) dns.Text = string.Join(", ", snapshot.DnsServers);
                    }
                }
                catch { /* Manual entry remains available; diagnostic uses the original capture failure. */ }
            }
            status.Text = "자동 감지 실패. 회사 Wi-Fi와 IP를 직접 입력해 주세요.";
        }
    }

    private void Fill(SwitcherConfig config)
    {
        // Editing multi-profile configurations in this compact form would discard unseen data.
        if (config.Profiles.Count != 1 || config.Profiles[0].Mode != "static" || config.Profiles[0].Ssids.Length != 1)
            { editorCompatible = false; throw new SafeException("editor_complex_config"); }
        editorCompatible = true;
        filling = true;
        try
        {
            var index = adapter.Items.Cast<AdapterItem>().ToList().FindIndex(a => a.Id == config.AdapterId);
            adapter.SelectedIndex = index;
            var profile = config.Profiles[0]; ssid.Text = profile.Ssids[0]; address.Text = profile.Address ?? "";
            subnet.Text = ManualProfileInput.ToSubnetMask(profile.PrefixLength!.Value); gateway.Text = profile.Gateway ?? ""; dns.Text = string.Join(", ", profile.DnsServers);
            dirty = false;
        }
        finally { filling = false; }
    }

    private SwitcherConfig ReadForm()
    {
        if (!editorCompatible) throw new SafeException("editor_complex_config");
        if (adapter.SelectedItem is not AdapterItem selected) throw new SafeException("select_wifi_adapter");
        var config = ManualProfileInput.Create(selected.Id, ssid.Text, address.Text, subnet.Text, gateway.Text, dns.Text);
        return saved is null ? config : config with { Fallback = saved.Fallback, PollSeconds = saved.PollSeconds,
            Profiles = [config.Profiles[0] with { Id = saved.Profiles[0].Id }] };
    }

    private async Task Save()
    {
        var config = ReadForm();
        var installed = SetupOperations.Installed;
        var message = (installed ? "입력한 설정을 저장합니다.\n" : "프로그램을 설치하고 입력한 설정을 저장합니다.\n설치: Program Files / 설정·백업: ProgramData\n\n")
            + "회사 Wi-Fi에서는 고정 IP를 사용합니다.\n"
            + (config.Fallback == "dhcp" ? "다른 Wi-Fi에서는 IP와 DNS를 자동으로 받습니다.\n" : "다른 Wi-Fi에서는 현재 설정을 유지합니다.\n")
            + "\n감지를 확인하면 자동 전환을 시작합니다. 연결이 잠시 끊길 수 있습니다.";
        if (!ConfirmDialog.Show(this, "저장하고 시작", message, "저장하고 시작")) return;
        var result = await SetupStartFlow.Run(async () =>
        {
            if (installed) await operations!.Save(config); else await operations!.Install(config, manualEntry: true);
            saved = SetupOperations.LoadInstalled(); Fill(saved);
            status.Text = "저장했습니다. Wi-Fi 감지를 확인하는 중…";
        }, () => operations!.Enable(saved!), () => operations!.Pause());
        startUncertain = result.StopError is not null;
        saved = SetupOperations.LoadInstalled(); Fill(saved);
        if (result.Started)
        {
            status.Text = "자동 전환을 시작했습니다. 창을 닫아도 동작합니다.";
        }
        else
        {
            lastError = result.StopError ?? result.StartError;
            status.Text = startUncertain
                ? "설정은 저장됐지만 시작·중지 상태를 확인하지 못했습니다. ‘더 보기’에서 오류를 확인하세요."
                : "설정은 저장했습니다. " + SetupMessages.StartFailure(result.StartError!) + "\n현재 IP는 유지됩니다. 필요하면 ‘더 보기’에서 복구하세요.";
        }
    }

    private async Task ChangeSwitching()
    {
        if (saved is null || (saved.Mode != "enforce" && !startUncertain)) return;
        await Run("자동 전환을 끄는 중…", async () =>
        {
            await operations!.Pause(); startUncertain = false;
            saved = SetupOperations.LoadInstalled();
            // Stopping must not discard edits that have not been saved yet.
            status.Text = "자동 전환을 껐습니다. 다시 시작하려면 ‘저장하고 시작’을 누르세요.";
        });
    }

    private void BuildMenu()
    {
        detailsItem.Click += (_, _) => { if (lastError is not null) MessageBox.Show(this, SetupMessages.For(lastError), "오류 자세히", MessageBoxButtons.OK, MessageBoxIcon.Information); };
        copyItem.Click += (_, _) => { try { Clipboard.SetText("Wi-Fi Switcher 0.2.0-alpha.3\n" + SetupMessages.Code(lastError)); status.Text = "오류 코드를 복사했습니다. 네트워크 값은 포함하지 않습니다."; } catch { status.Text = "클립보드에 복사하지 못했습니다."; } };
        dhcpItem.Click += async (_, _) => await Recover(false); restoreItem.Click += async (_, _) => await Recover(true);
        removeItem.Click += async (_, _) =>
        {
            if (!ConfirmDialog.Show(this, "앱 제거", "프로그램·설정·백업을 삭제합니다.\n현재 IP는 유지됩니다. 필요한 복구를 먼저 진행하세요.", "제거", destructive: true)) return;
            await Run("앱을 제거하는 중…", async () => { await operations!.Uninstall(); saved = null; startUncertain = false; dirty = true; status.Text = "앱과 저장된 설정을 제거했습니다."; });
        };
        var location = new ToolStripMenuItem("Windows 위치 설정");
        location.Click += (_, _) => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:privacy-location") { UseShellExecute = true }); } catch { status.Text = "Windows 설정 → 개인 정보 및 보안 → 위치를 여세요."; } };
        var licenses = new ToolStripMenuItem("라이선스");
        licenses.Click += (_, _) =>
        {
            if (payload is null) return;
            using var viewer = new Form { Text = "라이선스", Width = 700, Height = 520, StartPosition = FormStartPosition.CenterParent };
            viewer.Controls.Add(new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill,
                Text = string.Join(Environment.NewLine + Environment.NewLine, new[] { "LICENSE", "DOTNET-LICENSE.txt", "THIRD-PARTY-NOTICES.txt", "WINDOWS-SDK-NOTICE.txt", "DESKTOP-LICENSE.txt" }.Select(n => File.ReadAllText(Path.Combine(payload.Package, n)))) });
            viewer.ShowDialog(this);
        };
        menu.Items.AddRange([detailsItem, copyItem, location, new ToolStripSeparator(), dhcpItem, restoreItem, removeItem, new ToolStripSeparator(), licenses]);
    }

    private async Task Recover(bool original)
    {
        if (!ConfirmDialog.Show(this, original ? "최초 백업 복원" : "DHCP로 복구", original
            ? "최초 백업 당시의 Wi-Fi에 연결되어 있어야 합니다.\n자동 전환을 끄고 백업을 복원합니다. 연결이 끊길 수 있습니다."
            : "자동 전환을 끄고 IP·DNS를 자동 설정으로 바꿉니다.\n회사 고정 IP 망에서는 연결이 끊길 수 있습니다.", "복구", destructive: true)) return;
        await Run("네트워크를 복구하는 중…", async () => { await operations!.Recover(original); startUncertain = false; saved = SetupOperations.LoadInstalled(); Fill(saved); status.Text = "복구 명령을 완료했습니다. 연결 상태를 확인하세요."; });
    }

    private async Task Run(string message, Func<Task> action)
    {
        if (busy || preview) return;
        busy = true; status.Text = message; lastError = null; RefreshState(); UseWaitCursor = true;
        try { await action(); }
        catch (Exception ex)
        {
            lastError = ex; status.Text = SetupMessages.Short(ex);
            if (ex is ManualInputException input)
            {
                busy = false; RefreshState();
                Control field = input.Field switch { "ssid" => ssid, "address" => address, "subnetMask" => subnet, "gateway" => gateway, "dns" => dns, _ => adapter };
                field.Focus();
            }
        }
        finally { busy = false; UseWaitCursor = false; RefreshState(); }
    }

    private void RefreshState()
    {
        var installed = saved is not null;
        installState.Text = installed ? (dirty ? "저장되지 않은 변경" : "저장됨") : "설치 전";
        switchState.Text = startUncertain ? "확인 필요" : saved?.Mode == "enforce" ? "켜짐" : "꺼짐";
        toggle.Text = "끄기"; toggle.Visible = saved?.Mode == "enforce" || startUncertain; toggle.Enabled = !busy;
        save.Enabled = !busy && editorCompatible && adapter.SelectedItem is not null && (dirty || !installed || saved?.Mode != "enforce" || startUncertain);
        detect.Enabled = !busy && editorCompatible; more.Enabled = close.Enabled = !busy;
        foreach (var control in new Control[] { adapter, ssid, address, subnet, gateway, dns }) control.Enabled = !busy && editorCompatible;
        adapter.Enabled = !busy && editorCompatible && saved is null && adapter.Items.Count > 1;
        detailsItem.Enabled = copyItem.Enabled = lastError is not null;
        dhcpItem.Enabled = restoreItem.Enabled = installed;
        removeItem.Enabled = !preview && SetupOperations.Installed;
    }

    internal void LoadPreview(bool stored = false, bool blocked = false)
    {
        filling = true;
        adapter.Items.Add(new AdapterItem(Guid.Parse("11111111-1111-4111-8111-111111111111"), "Wi-Fi")); adapter.SelectedIndex = 0;
        filling = false;
        var config = ManualProfileInput.Create(((AdapterItem)adapter.SelectedItem!).Id, "EXEM", "192.0.2.10", "255.255.255.0", "192.0.2.1", "192.0.2.53, 198.51.100.53");
        if (stored) saved = config with { Mode = blocked ? "observe" : "enforce" };
        Fill(config); dirty = !stored;
        status.Text = blocked ? "설정은 저장했습니다. Wi-Fi를 확인하지 못해 자동 전환을 시작하지 못했습니다." : stored ? "자동 전환을 시작했습니다. 창을 닫아도 동작합니다." : "자동 감지 실패. 회사 Wi-Fi와 IP를 직접 입력해 주세요.";
        RefreshState();
    }

    private sealed record AdapterItem(Guid Id, string Description) { public override string ToString() => Description; }
}
