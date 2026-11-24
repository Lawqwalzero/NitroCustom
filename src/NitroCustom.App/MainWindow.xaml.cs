using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using NitroCustom.Core.Hardware;
using NitroCustom.Core.Ipc;

namespace NitroCustom.App;

/// <summary>Item of the effect list (icon + name).</summary>
public sealed record EffectItem(LightingEffect Effect, string Name, Geometry Icon);

public partial class MainWindow : Window
{
    private static readonly string[] Presets = ["#FF2A2A", "#FF7A00", "#FFD400", "#00E060", "#00C8FF", "#2050FF", "#A040FF", "#FFFFFF"];
    private static readonly IEasingFunction Settle = new CubicEase { EasingMode = EasingMode.EaseOut };

    private readonly ServiceConnection _service;
    private readonly DispatcherTimer _sendDelay;
    private readonly DispatcherTimer _statsTimer;
    private readonly SystemStats _stats = new();
    private readonly Border[] _zones = new Border[4];
    private readonly SolidColorBrush[] _zoneBrushes = new SolidColorBrush[4];
    private IReadOnlyDictionary<ThermalProfile, string> _modeKeyColors = new Dictionary<ThermalProfile, string>();
    private KeyboardLighting _lighting = new();
    private ThermalProfile? _shownProfile;
    private string? _previewSignature;
    private int _selectedZone;
    private bool _updating;
    private bool _placed;

    public MainWindow(ServiceConnection service)
    {
        _service = service;
        InitializeComponent();

        _sendDelay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _sendDelay.Tick += async (_, _) => { _sendDelay.Stop(); await SendLightingAsync(); };
        _statsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _statsTimer.Tick += UpdateStats;

        BuildPreview();
        BuildSwatches();
        foreach (var e in Texts.Effects) EffectBox.Items.Add(new EffectItem(e, Texts.Name(e), (Geometry)FindResource(Texts.IconKey(e))));

        _service.ConnectionChanged += OnConnectionChanged;
        _service.StateReceived += OnState;
        _service.ProfileChanged += p => SelectProfile(p, animate: true);
        _service.LightingChanged += l => { _modeKeyColors = l.ModeKeyColors; ColorProfileDots(); ShowLighting(l.Keyboard); };
        OnConnectionChanged(_service.IsConnected);
        ShowLighting(_lighting);
    }

    // ---------- window ----------

    /// <summary>Shows the window centred on the work area (first time) and brings it to the front.</summary>
    public void ShowNearTray()
    {
        if (!_placed)
        {
            var area = SystemParameters.WorkArea;
            Left = area.Left + (area.Width - Width) / 2;
            Top = area.Top + (area.Height - Height) / 2;
            _placed = true;
        }
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        _statsTimer.Start();
    }

    /// <summary>Hotkey behaviour: open when hidden or in the background, hide when already in front.</summary>
    public void ToggleFromHotkey()
    {
        if (IsVisible && IsActive && WindowState != WindowState.Minimized) Hide();
        else ShowNearTray();
    }

    private void UpdateStats(object? sender, EventArgs e)
    {
        if (!IsVisible) { _statsTimer.Stop(); return; }
        CpuLoad.Value = _stats.CpuPercent();
        RamLoad.Value = SystemStats.RamPercent();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Closing only hides; the app exits from the tray menu.
        e.Cancel = true;
        Hide();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Hide_Click(object sender, RoutedEventArgs e) => Hide();

    // ---------- service state ----------

    private void OnConnectionChanged(bool connected)
    {
        ShowStatus(connected ? null : Core.Loc.T("Служба NitroCustom не запущена. Режимы, датчики и подсветка недоступны, переподключаюсь...", "The NitroCustom service is not running. Modes, sensors and lighting are unavailable, reconnecting..."));
        UpdateControlsEnabled();
        if (!connected) UpdateSensors([]);
    }

    /// <summary>Set by the service: on a laptop model that was not verified, writes stay off until the user allows them.</summary>
    private ModelDto? _model;

    private bool WritesBlocked => _model is { WritesAllowed: false };

    private void UpdateControlsEnabled()
    {
        var enabled = _service.IsConnected && !WritesBlocked;
        ProfilePanel.IsEnabled = enabled;
        LightingPanel.IsEnabled = enabled;
    }

    private void ApplyModel(ModelDto? model)
    {
        _model = model;
        UpdateControlsEnabled();
        ShowStatus(null);
    }

    private async void ModelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_model is not { Verified: false } model) return;
        var allow = !model.WritesAllowed;
        if (allow)
        {
            var verified = string.Join(", ", SupportedModels.Verified);
            var answer = MessageBox.Show(this,
                Core.Loc.T(
                    $"NitroCustom проверен только на: {verified}. На модели «{model.Model}» смена режимов и подсветки пишет в прошивку ноутбука, и результат непредсказуем. Разрешить запись?",
                    $"NitroCustom has only been verified on: {verified}. On “{model.Model}” changing modes and lighting writes to the laptop firmware and the result is unpredictable. Allow writing?"),
                "NitroCustom", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) return;
        }
        try { ApplyModel(await _service.RequestAsync<ModelDto>(IpcCommands.SetUnverifiedConsent, new ConsentArgs(allow))); }
        catch (Exception ex) { ShowStatus(ex.Message); }
    }

    private void OnState(StateDto state)
    {
        ApplyModel(state.Model);
        _modeKeyColors = state.Lighting.ModeKeyColors;
        ProfilePanel.Children.Clear();
        foreach (var p in Texts.ProfileOrder.Where(state.SupportedProfiles.Contains))
        {
            var button = new RadioButton
            {
                Content = Texts.Title(p),
                ToolTip = Texts.Long(p),
                Tag = p,
                GroupName = "Profile",
                Style = (Style)FindResource("ProfileRow"),
            };
            Glyph.SetData(button, (Geometry)FindResource(Texts.IconKey(p)));
            button.Click += Profile_Click;
            ProfilePanel.Children.Add(button);
        }
        ColorProfileDots();
        _shownProfile = null;
        // Rows need a layout pass before the highlight can be placed.
        Dispatcher.BeginInvoke(() => SelectProfile(state.Profile, animate: false), DispatcherPriority.Loaded);
        UpdateSensors(state.Sensors);
        ShowLighting(state.Lighting.Keyboard);
    }

    private void ColorProfileDots()
    {
        foreach (var b in ProfilePanel.Children.OfType<RadioButton>())
            b.Background = _modeKeyColors.TryGetValue((ThermalProfile)b.Tag, out var hex) ? Brush(hex) : Brushes.Transparent;
    }

    private void SelectProfile(ThermalProfile profile, bool animate)
    {
        RadioButton? selected = null;
        foreach (var b in ProfilePanel.Children.OfType<RadioButton>())
        {
            var on = (ThermalProfile)b.Tag == profile;
            b.IsChecked = on;
            if (on) selected = b;
        }
        MoveHighlight(selected, animate);
        if (_shownProfile == profile) return;
        _shownProfile = profile;
        ModeName.Text = Texts.Title(profile);
        if (animate) PlayModeChange(profile);
        else EmblemGlow.Color = GlowColor(profile);
    }

    private void MoveHighlight(RadioButton? row, bool animate)
    {
        if (row is null || !row.IsLoaded) { ProfileHighlight.Opacity = 0; return; }
        var y = row.TranslatePoint(new Point(0, 0), ProfilePanel).Y;
        if (animate && ProfileHighlight.Opacity > 0)
            HighlightShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y, TimeSpan.FromMilliseconds(280)) { EasingFunction = Settle });
        else
        {
            HighlightShift.BeginAnimation(TranslateTransform.YProperty, null);
            HighlightShift.Y = y;
        }
        ProfileHighlight.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)));
    }

    /// <summary>Mode change: name slides up, ember sweep across the stripes, emblem pulses in the mode-key colour.</summary>
    private void PlayModeChange(ThermalProfile profile)
    {
        ModeName.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));
        ModeNameShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = Settle });

        Sweep.BeginAnimation(OpacityProperty, new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromMilliseconds(700),
            KeyFrames = { new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.15)), new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.75)), new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)) },
        });
        SweepShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-90, 220, TimeSpan.FromMilliseconds(700)) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });

        var pulse = new DoubleAnimation(1, 1.07, TimeSpan.FromMilliseconds(170)) { AutoReverse = true, EasingFunction = new SineEase() };
        EmblemScale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
        EmblemScale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
        EmblemGlow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty,
            new DoubleAnimation(0.45, 0.95, TimeSpan.FromMilliseconds(220)) { AutoReverse = true });
        EmblemGlow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.ColorProperty,
            new ColorAnimation(GlowColor(profile), TimeSpan.FromMilliseconds(450)) { EasingFunction = Settle });
    }

    /// <summary>Emblem glow follows the mode-key colour (white "Quiet" is warmed up so the glow stays visible).</summary>
    private Color GlowColor(ThermalProfile profile)
    {
        if (!_modeKeyColors.TryGetValue(profile, out var hex)) return Color.FromRgb(0xFF, 0x4A, 0x12);
        var c = Rgb.Parse(hex);
        return c is { R: > 0xF0, G: > 0xF0, B: > 0xF0 } ? Color.FromRgb(0xFF, 0xD8, 0xB0) : Color.FromRgb(c.R, c.G, c.B);
    }

    private async void Profile_Click(object sender, RoutedEventArgs e)
    {
        var profile = (ThermalProfile)((RadioButton)sender).Tag;
        SelectProfile(profile, animate: true); // optimistic, confirmed by the service event
        try
        {
            var result = await _service.RequestAsync<ProfileDto>(IpcCommands.SetProfile, new SetProfileArgs(profile));
            if (result is not null) SelectProfile(result.Profile, animate: true);
            ShowStatus(null);
        }
        catch (Exception ex)
        {
            ShowStatus(Core.Loc.T("Режим не переключился: ", "Could not switch the mode: ") + ex.Message);
        }
    }

    public void UpdateSensors(IReadOnlyList<SensorDto> sensors)
    {
        double? Get(Sensor s) => sensors.FirstOrDefault(x => x.Sensor == s)?.Value;
        CpuTempGauge.Value = Get(Sensor.CpuTemperature);
        GpuTempGauge.Value = Get(Sensor.GpuTemperature);
        SysTempGauge.Value = Get(Sensor.SystemTemperature);
        CpuFanGauge.Value = Get(Sensor.CpuFanSpeed);
        GpuFanGauge.Value = Get(Sensor.GpuFanSpeed);
    }

    /// <summary>An error message, or (with null) the standing notice about an unverified laptop model, if any.</summary>
    private void ShowStatus(string? text)
    {
        if (text is null && _model is { Verified: false } model)
        {
            StatusText.Text = model.WritesAllowed
                ? Core.Loc.T($"Модель «{model.Model}» не проверена. Запись в прошивку разрешена на ваш риск.", $"“{model.Model}” has not been verified. Writing is enabled at your own risk.")
                : Core.Loc.T($"Модель «{model.Model}» не проверена. Режимы и подсветка доступны только для чтения.", $"“{model.Model}” has not been verified. Modes and lighting are read-only.");
            ModelButton.Content = model.WritesAllowed ? Core.Loc.T("Запретить", "Disable") : Core.Loc.T("Разрешить", "Allow");
            ModelButton.Visibility = Visibility.Visible;
            StatusBar.Visibility = Visibility.Visible;
            return;
        }
        ModelButton.Visibility = Visibility.Collapsed;
        StatusText.Text = text ?? "";
        StatusBar.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
    }

    // ---------- lighting ----------

    private void BuildPreview()
    {
        for (var i = 0; i < 4; i++)
        {
            _zoneBrushes[i] = new SolidColorBrush(Colors.Black);
            var zone = new Border
            {
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(i == 0 ? 0 : 2, 0, i == 3 ? 0 : 2, 0),
                BorderThickness = new Thickness(1.5),
                Background = _zoneBrushes[i],
                Tag = i,
                ToolTip = Core.Loc.T($"Зона {i + 1}", $"Zone {i + 1}"),
            };
            zone.MouseLeftButtonDown += Zone_Click;
            Grid.SetColumn(zone, i);
            Preview.Children.Add(zone);
            _zones[i] = zone;
        }
    }

    private void BuildSwatches()
    {
        foreach (var hex in Presets)
        {
            var swatch = new RadioButton
            {
                Style = (Style)FindResource("Swatch"),
                Background = Brush(hex),
                Tag = hex,
                GroupName = "Swatch",
                Width = 28,
                Height = 28,
                Margin = new Thickness(0, 0, 3, 0),
                ToolTip = hex,
            };
            swatch.Click += (_, _) => PickColor(hex);
            SwatchPanel.Children.Add(swatch);
        }
    }

    private void ShowLighting(KeyboardLighting l)
    {
        _updating = true;
        _lighting = l;
        LightOn.IsChecked = l.Enabled;
        EffectBox.SelectedItem = EffectBox.Items.OfType<EffectItem>().FirstOrDefault(i => i.Effect == l.Effect);
        BrightnessSlider.Value = l.Brightness;
        SpeedSlider.Value = l.Speed;
        DirLeft.IsChecked = l.Direction != LightingDirection.Right;
        DirRight.IsChecked = l.Direction == LightingDirection.Right;
        _updating = false;
        RefreshLightingView();
    }

    /// <summary>Reads the controls into <see cref="_lighting"/>.</summary>
    private KeyboardLighting ReadControls() => _lighting with
    {
        Enabled = LightOn.IsChecked == true,
        Effect = EffectBox.SelectedItem is EffectItem item ? item.Effect : LightingEffect.Static,
        Brightness = (byte)BrightnessSlider.Value,
        Speed = (byte)SpeedSlider.Value,
        Direction = DirRight.IsChecked == true ? LightingDirection.Right : LightingDirection.Left,
    };

    private void RefreshLightingView()
    {
        var l = _lighting;
        var direct = l.Effect == LightingEffect.Direct;
        ColorRow.Visibility = Texts.UsesColor(l.Effect) ? Visibility.Visible : Visibility.Collapsed;
        ColorLabel.Text = direct ? Core.Loc.T($"Цвет зоны {_selectedZone + 1}", $"Zone {_selectedZone + 1} colour") : Core.Loc.T("Цвет", "Colour");
        SpeedRow.Visibility = DirectionRow.Visibility = Texts.IsAnimated(l.Effect) ? Visibility.Visible : Visibility.Collapsed;

        var current = direct ? l.ZoneColors[_selectedZone] : l.Color;
        if (!HexBox.IsKeyboardFocused) HexBox.Text = current;
        foreach (var s in SwatchPanel.Children.OfType<RadioButton>())
            s.IsChecked = string.Equals((string)s.Tag, current, StringComparison.OrdinalIgnoreCase);

        for (var i = 0; i < 4; i++)
        {
            _zones[i].BorderBrush = direct && i == _selectedZone ? (Brush)FindResource("Text") : Brushes.Transparent;
            _zones[i].Cursor = direct ? Cursors.Hand : null;
        }
        Preview.BeginAnimation(OpacityProperty, new DoubleAnimation(l.Enabled ? 0.35 + 0.65 * l.Brightness / 100.0 : 0.08, TimeSpan.FromMilliseconds(200)));
        AnimatePreview(l);
    }

    /// <summary>Live preview of the keyboard effect on the four zones.</summary>
    private void AnimatePreview(KeyboardLighting l)
    {
        var signature = $"{l.Effect}|{l.Color}|{string.Join(",", l.ZoneColors)}|{l.Speed}|{l.Direction}";
        if (signature == _previewSignature) return;
        _previewSignature = signature;

        var period = TimeSpan.FromSeconds(Math.Max(0.6, 4.2 - 0.4 * l.Speed));
        var color = ToColor(l.Color);
        Color[] rainbow = [Color.FromRgb(0xFF, 0x2A, 0x2A), Color.FromRgb(0xFF, 0xD4, 0x00), Color.FromRgb(0x00, 0xE0, 0x60), Color.FromRgb(0x00, 0xC8, 0xFF), Color.FromRgb(0x20, 0x50, 0xFF), Color.FromRgb(0xA0, 0x40, 0xFF)];

        for (var i = 0; i < 4; i++)
        {
            var zone = _zones[i];
            var brush = _zoneBrushes[i];
            zone.BeginAnimation(OpacityProperty, null);
            brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            zone.Opacity = 1;
            // Phase along the keyboard in the chosen direction.
            var phase = (l.Direction == LightingDirection.Right ? i : 3 - i) / 4.0;

            switch (l.Effect)
            {
                case LightingEffect.Static:
                    FadeTo(brush, color);
                    break;
                case LightingEffect.Direct:
                    FadeTo(brush, ToColor(l.ZoneColors[i]));
                    break;
                case LightingEffect.Breathing:
                    brush.Color = color;
                    zone.BeginAnimation(OpacityProperty, Loop(new DoubleAnimation(1, 0.12, period) { AutoReverse = true, EasingFunction = new SineEase() }, TimeSpan.Zero));
                    break;
                case LightingEffect.Wave or LightingEffect.Shifting:
                    brush.BeginAnimation(SolidColorBrush.ColorProperty, Loop(Cycle(rainbow, period * 1.5), -(period * 1.5) * phase));
                    break;
                case LightingEffect.Neon:
                    brush.BeginAnimation(SolidColorBrush.ColorProperty, Loop(Cycle(rainbow, period * 1.5), TimeSpan.Zero));
                    break;
                case LightingEffect.Meteor or LightingEffect.Zoom:
                    brush.Color = color;
                    var chase = new DoubleAnimationUsingKeyFrames { Duration = period };
                    chase.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0)));
                    chase.KeyFrames.Add(new EasingDoubleKeyFrame(0.1, KeyTime.FromPercent(0.45), new CubicEase { EasingMode = EasingMode.EaseOut }));
                    chase.KeyFrames.Add(new LinearDoubleKeyFrame(0.1, KeyTime.FromPercent(1)));
                    var offset = l.Effect == LightingEffect.Zoom ? Math.Abs(i - 1.5) / 4.0 : phase;
                    zone.BeginAnimation(OpacityProperty, Loop(chase, -period * (1 - offset)));
                    break;
                case LightingEffect.Twinkling:
                    brush.Color = color;
                    var twinkle = new DoubleAnimation(1, 0.15, period * (0.35 + 0.17 * ((i * 7) % 4))) { AutoReverse = true, EasingFunction = new SineEase() };
                    zone.BeginAnimation(OpacityProperty, Loop(twinkle, TimeSpan.FromMilliseconds(-170 * i)));
                    break;
            }
        }
    }

    private static void FadeTo(SolidColorBrush brush, Color color) =>
        brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(color, TimeSpan.FromMilliseconds(220)) { EasingFunction = Settle });

    private static ColorAnimationUsingKeyFrames Cycle(Color[] colors, TimeSpan period)
    {
        var a = new ColorAnimationUsingKeyFrames { Duration = period };
        for (var k = 0; k <= colors.Length; k++)
            a.KeyFrames.Add(new LinearColorKeyFrame(colors[k % colors.Length], KeyTime.FromPercent((double)k / colors.Length)));
        return a;
    }

    private static T Loop<T>(T timeline, TimeSpan begin) where T : Timeline
    {
        timeline.RepeatBehavior = RepeatBehavior.Forever;
        timeline.BeginTime = begin;
        return timeline;
    }

    private void Lighting_Changed(object sender, RoutedEventArgs e)
    {
        if (_updating || !IsInitialized) return;
        _lighting = ReadControls();
        RefreshLightingView();
        QueueSend();
    }

    private void Slider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) => Lighting_Changed(sender, e);

    private void Zone_Click(object sender, MouseButtonEventArgs e)
    {
        if (_lighting.Effect != LightingEffect.Direct) return;
        _selectedZone = (int)((Border)sender).Tag;
        RefreshLightingView();
    }

    private void PickColor(string hex)
    {
        if (_lighting.Effect == LightingEffect.Direct)
        {
            var zones = _lighting.ZoneColors.ToArray();
            zones[_selectedZone] = hex;
            _lighting = _lighting with { ZoneColors = zones };
        }
        else
        {
            _lighting = _lighting with { Color = hex };
        }
        RefreshLightingView();
        QueueSend();
    }

    private void HexBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) HexBox_Commit(sender, e);
    }

    private void HexBox_Commit(object sender, RoutedEventArgs e)
    {
        var text = HexBox.Text.Trim();
        if (!text.StartsWith('#')) text = "#" + text;
        try
        {
            PickColor(Rgb.Parse(text).ToString());
        }
        catch (FormatException)
        {
            RefreshLightingView(); // restore the last valid value
        }
    }

    private void QueueSend()
    {
        _sendDelay.Stop();
        _sendDelay.Start();
    }

    private async Task SendLightingAsync()
    {
        try
        {
            await _service.RequestAsync<LightingDto>(IpcCommands.SetLighting, _lighting);
            ShowStatus(null);
        }
        catch (Exception ex)
        {
            ShowStatus(Core.Loc.T("Подсветка не применилась: ", "Could not apply lighting: ") + ex.Message);
        }
    }

    private static Color ToColor(string hex)
    {
        var c = Rgb.Parse(hex);
        return Color.FromRgb(c.R, c.G, c.B);
    }

    private static SolidColorBrush Brush(string hex)
    {
        var brush = new SolidColorBrush(ToColor(hex));
        brush.Freeze();
        return brush;
    }
}
