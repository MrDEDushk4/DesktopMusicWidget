using System.Windows.Interop;
using System.Runtime.InteropServices;

using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Globalization;
using System.Linq;
using Microsoft.Win32;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DesktopMusicWidget;

public class TrackItem : INotifyPropertyChanged
{
    private bool _isPlaying;
    private string _title = "";
    private string _artist = "Локальный файл";
    private string _album = "";
    private string _durationText = "--:--";
    private ImageSource? _albumArt;

    public string FilePath { get; set; } = "";

    public string Title
    {
        get => _title;
        set => SetField(ref _title, value);
    }

    public string Artist
    {
        get => _artist;
        set => SetField(ref _artist, value);
    }

    public string Album
    {
        get => _album;
        set => SetField(ref _album, value);
    }

    public string DurationText
    {
        get => _durationText;
        set => SetField(ref _durationText, value);
    }

    public ImageSource? AlbumArt
    {
        get => _albumArt;
        set => SetField(ref _albumArt, value);
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        set => SetField(ref _isPlaying, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value,
        [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
            return;

        field = value;
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }
}

public class WidgetPosition
{
    public double Left { get; set; }
    public double Top { get; set; }
}

public partial class MainWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WM_WINDOWPOSCHANGING = 0x0046;
    private const int WM_SYSCOMMAND = 0x0112;
    private const int SC_MOVE = 0xF010;
    private const int VK_RSHIFT = 0xA1;
    private const int SC_MINIMIZE = 0xF020;
    private const int SW_SHOWNOACTIVATE = 4;
    private const uint SWP_HIDEWINDOW = 0x0080;
    private const uint GW_HWNDPREV = 3;
    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DesktopMusicWidget", "position.json");

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPOS
    {
        public IntPtr hwnd;
        public IntPtr hwndInsertAfter;
        public int x, y, cx, cy;
        public uint flags;
    }

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    private IntPtr _hwnd = IntPtr.Zero;
    private bool _isMoveMode;
    private bool _isClosing;
    private bool _liftedAboveDesktop;
    private readonly DispatcherTimer _desktopGuardTimer = new(DispatcherPriority.Background);

    private readonly MediaPlayer _player = new();
    private readonly DispatcherTimer _timer = new();
    private readonly DispatcherTimer _playlistScrollTimer = new();
    private ScrollViewer? _playlistScrollViewer;
    private double _scrollStart;
    private double _scrollTarget;
    private DateTime _scrollAnimationStart;

    private bool _isSeeking;
    private bool _isPlaying;
    private int _currentIndex = -1;
    private TimeSpan _duration = TimeSpan.Zero;
    private bool _reversePlayback;
    private bool _repeatCurrentTrack;
    private bool _isLoadingVolume;
    private string SettingsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DesktopMusicWidget");
    private string VolumeSettingsPath => Path.Combine(SettingsDirectory, "settings.txt");
    private const string VolumeRegistryPath = @"Software\DesktopMusicWidget";
    private const string VolumeRegistryValue = "Volume";
    private string PlaylistSettingsPath => Path.Combine(SettingsDirectory, "playlist.json");

    public ObservableCollection<TrackItem> Tracks { get; } = new();

    public MainWindow()
    {
        InitializeComponent();

        PlaylistList.ItemsSource = Tracks;
        PlaylistList.Loaded += (_, _) =>
            _playlistScrollViewer = FindVisualChild<ScrollViewer>(PlaylistList);
        _playlistScrollTimer.Interval = TimeSpan.FromMilliseconds(16);
        _playlistScrollTimer.Tick += PlaylistScrollTimer_Tick;
        LoadPlaylist();

        _isLoadingVolume = true;
        VolumeSlider.Value = LoadVolume();
        _isLoadingVolume = false;
        _player.Volume = VolumeSlider.Value;

        _player.MediaOpened += Player_MediaOpened;
        _player.MediaEnded += Player_MediaEnded;

        _timer.Interval = TimeSpan.FromMilliseconds(250);
        _timer.Tick += Timer_Tick;
        _timer.Start();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        int exStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
        SetWindowLong(_hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);

        // Win+D protection: poll the desktop state (see DesktopGuardTimer_Tick).
        _desktopGuardTimer.Interval = TimeSpan.FromMilliseconds(100);
        _desktopGuardTimer.Tick += DesktopGuardTimer_Tick;
        _desktopGuardTimer.Start();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_SYSCOMMAND)
        {
            long cmd = wParam.ToInt64() & 0xFFF0;

            if (!_isMoveMode && cmd == SC_MOVE)
            {
                handled = true;
                return IntPtr.Zero;
            }

            // Show Desktop must not minimize the widget.
            if (cmd == SC_MINIMIZE && !_isClosing)
            {
                handled = true;
                return IntPtr.Zero;
            }
        }
        else if (msg == WM_WINDOWPOSCHANGING && !_isClosing)
        {
            // Strip the "hide window" flag so the shell can't hide the widget.
            var pos = Marshal.PtrToStructure<WINDOWPOS>(lParam);
            if ((pos.flags & SWP_HIDEWINDOW) != 0)
            {
                pos.flags &= ~SWP_HIDEWINDOW;
                Marshal.StructureToPtr(pos, lParam, false);
            }
        }
        return IntPtr.Zero;
    }

    private static string GetWindowClass(IntPtr hWnd)
    {
        var sb = new System.Text.StringBuilder(64);
        return GetClassName(hWnd, sb, sb.Capacity) > 0 ? sb.ToString() : string.Empty;
    }

    // Progman / WorkerW is the desktop (wallpaper + icons) layer.
    private static bool IsDesktopWindow(IntPtr hWnd)
    {
        string cls = GetWindowClass(hWnd);
        return cls == "Progman" || cls == "WorkerW";
    }

    // Taskbar and desktop: focus moving there does not mean the user went back to an app.
    private static bool IsShellWindow(IntPtr hWnd)
    {
        string cls = GetWindowClass(hWnd);
        return cls == "Progman" || cls == "WorkerW" ||
               cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd";
    }

    // True when the desktop layer sits above the widget in the z-order.
    // Normally it is at the very bottom; Win+D (Show Desktop) raises it above everything.
    private bool IsDesktopAboveWidget()
    {
        IntPtr h = GetWindow(_hwnd, GW_HWNDPREV);
        int guard = 0;
        while (h != IntPtr.Zero && guard++ < 2000)
        {
            if (IsDesktopWindow(h)) return true;
            h = GetWindow(h, GW_HWNDPREV);
        }
        return false;
    }

    private void EnsureWidgetShown()
    {
        if (IsIconic(_hwnd) || !IsWindowVisible(_hwnd))
        {
            ShowWindow(_hwnd, SW_SHOWNOACTIVATE);
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
        }
    }

    private void SetTopmostNoActivate(bool topmost)
    {
        SetWindowPos(_hwnd, topmost ? HWND_TOPMOST : HWND_NOTOPMOST, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    private void DesktopGuardTimer_Tick(object? sender, EventArgs e)
    {
        if (_hwnd == IntPtr.Zero || !IsLoaded || _isClosing)
            return;

        // 1) If Win+D (or anything else) minimized/hid the widget, bring it back.
        EnsureWidgetShown();

        IntPtr fg = GetForegroundWindow();

        if (!_liftedAboveDesktop)
        {
            // 2) Show Desktop raised the desktop layer above the widget: lift the widget over it.
            if (IsDesktopAboveWidget())
            {
                SetTopmostNoActivate(true);
                _liftedAboveDesktop = true;
            }
        }
        else if (fg != IntPtr.Zero && fg != _hwnd && !IsShellWindow(fg))
        {
            // 3) A regular app became active (windows were restored): go back to the normal z-order.
            SetTopmostNoActivate(false);
            _liftedAboveDesktop = false;
        }
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);

        // The visibility timer handles Win+D, including cases where Windows
        // hides the HWND without changing WindowState.
        if (WindowState == WindowState.Minimized && _hwnd != IntPtr.Zero)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_hwnd == IntPtr.Zero || !IsLoaded)
                    return;

                ShowWindow(_hwnd, SW_SHOWNOACTIVATE);
                WindowState = WindowState.Normal;
                SendToBottom();
            }), DispatcherPriority.Background);
        }
    }

    // Do not force HWND_BOTTOM: on Windows 11 with live wallpapers this can
    // place the widget behind the wallpaper surface, making it visible but
    // unable to receive mouse input. Keep the window in the normal z-order.
    private void SendToBottom()
    {
        // Intentionally left empty.
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var saved = LoadPosition();
        if (saved != null && IsPositionVisible(saved.Left, saved.Top))
        {
            Left = saved.Left;
            Top = saved.Top;
        }
        else
        {
            var area = SystemParameters.WorkArea;
            Left = area.Right - ActualWidth - 10;
            Top = area.Top + (area.Height - ActualHeight) / 2;
        }
        SendToBottom();
    }

    private static WidgetPosition? LoadPosition()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return null;
            return JsonSerializer.Deserialize<WidgetPosition>(File.ReadAllText(SettingsPath));
        }
        catch { return null; }
    }

    private void SavePosition()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new WidgetPosition { Left = Left, Top = Top }));
        }
        catch { }
    }

    private bool IsPositionVisible(double left, double top)
    {
        if (double.IsNaN(left) || double.IsInfinity(left) || double.IsNaN(top) || double.IsInfinity(top))
            return false;
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        return screen.Contains(new Rect(left, top, ActualWidth, ActualHeight));
    }

    private static bool IsRightShiftDown() => (GetAsyncKeyState(VK_RSHIFT) & 0x8000) != 0;

    private static void PlayNotificationSound()
    {
        try
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "Media", "Windows Notify System Generic.wav");
            if (File.Exists(path)) { new System.Media.SoundPlayer(path).Play(); return; }
        }
        catch { }
        System.Media.SystemSounds.Asterisk.Play();
    }

    private void EnterMoveMode()
    {
        _isMoveMode = true;
        Cursor = Cursors.SizeAll;
        PlayNotificationSound();
        Activate();
        Focus();
    }

    private void ExitMoveMode()
    {
        _isMoveMode = false;
        ClearValue(CursorProperty);
        SavePosition();
        SendToBottom();
    }

    private static bool IsEmptyArea(DependencyObject? current)
    {
        while (current != null)
        {
            if (current is Button || current is Slider || current is ListBox || current is ScrollBar)
                return false;
            current = VisualTreeHelper.GetParent(current);
        }
        return true;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsEmptyArea(e.OriginalSource as DependencyObject)) return;
        if (_isMoveMode)
        {
            try { DragMove(); } catch (InvalidOperationException) { }
            return;
        }
        if (e.ClickCount == 3 && IsRightShiftDown()) EnterMoveMode();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_isMoveMode && e.Key == Key.Enter && IsRightShiftDown())
        {
            ExitMoveMode();
            e.Handled = true;
        }
    }




    private void PlaylistList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        _playlistScrollViewer ??= FindVisualChild<ScrollViewer>(PlaylistList);
        if (_playlistScrollViewer == null) return;

        if (!_playlistScrollTimer.IsEnabled)
            _scrollStart = _playlistScrollViewer.VerticalOffset;
        else
            _scrollStart = _playlistScrollViewer.VerticalOffset;

        double baseOffset = _playlistScrollTimer.IsEnabled
            ? _scrollTarget
            : _playlistScrollViewer.VerticalOffset;
        _scrollTarget = Math.Clamp(baseOffset - e.Delta * 0.65, 0,
            Math.Max(0, _playlistScrollViewer.ScrollableHeight));
        _scrollAnimationStart = DateTime.UtcNow;
        _playlistScrollTimer.Stop();
        _playlistScrollTimer.Start();
        e.Handled = true;
    }

    private void PlaylistScrollTimer_Tick(object? sender, EventArgs e)
    {
        if (_playlistScrollViewer == null)
        {
            _playlistScrollTimer.Stop();
            return;
        }

        double t = Math.Clamp((DateTime.UtcNow - _scrollAnimationStart).TotalMilliseconds / 220.0, 0, 1);
        double eased = 1 - Math.Pow(1 - t, 3);
        double offset = _scrollStart + (_scrollTarget - _scrollStart) * eased;
        _playlistScrollViewer.ScrollToVerticalOffset(offset);
        if (t >= 1)
        {
            _playlistScrollViewer.ScrollToVerticalOffset(_scrollTarget);
            _scrollStart = _scrollTarget;
            _playlistScrollTimer.Stop();
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T result) return result;
            var nested = FindVisualChild<T>(child);
            if (nested != null) return nested;
        }
        return null;
    }

    private void Playlist_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;

        e.Handled = true;
    }

    private void Playlist_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return;

        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files)
            return;

        string[] supportedExtensions =
        {
            ".mp3", ".wav", ".wma", ".aac", ".m4a"
        };

        bool playlistChanged = false;
        foreach (string file in files)
        {
            if (!File.Exists(file))
                continue;

            string extension = Path.GetExtension(file);

            if (!supportedExtensions.Contains(
                extension, StringComparer.OrdinalIgnoreCase))
                continue;

            if (Tracks.Any(track =>
                string.Equals(
                    track.FilePath,
                    file,
                    StringComparison.OrdinalIgnoreCase)))
                continue;

            Tracks.Add(ReadTrackMetadata(file));
            playlistChanged = true;
        }

        if (playlistChanged)
            SavePlaylist();

        e.Handled = true;

        if (PlaylistList.SelectedIndex < 0 && Tracks.Count > 0)
            PlaylistList.SelectedIndex = 0;
    }

    private TrackItem ReadTrackMetadata(string file)
    {
        var track = new TrackItem
        {
            FilePath = file,
            Title = Path.GetFileNameWithoutExtension(file),
            Artist = "Неизвестный исполнитель",
            Album = "",
            DurationText = "--:--"
        };

        try
        {
            using var tagFile = TagLib.File.Create(file);

            if (!string.IsNullOrWhiteSpace(tagFile.Tag.Title))
                track.Title = tagFile.Tag.Title;

            if (tagFile.Tag.Performers != null &&
                tagFile.Tag.Performers.Length > 0)
            {
                track.Artist = string.Join(", ", tagFile.Tag.Performers);
            }

            if (!string.IsNullOrWhiteSpace(tagFile.Tag.Album))
                track.Album = tagFile.Tag.Album;

            if (tagFile.Properties.Duration > TimeSpan.Zero)
                track.DurationText = FormatTime(tagFile.Properties.Duration);

            var picture = tagFile.Tag.Pictures.FirstOrDefault();

            if (picture != null && picture.Data.Count > 0)
            {
                byte[] imageBytes = picture.Data.Data;

                using var stream = new MemoryStream(imageBytes);

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();

                track.AlbumArt = bitmap;
            }
        }
        catch
        {
            // Если метаданные не читаются, используем имя файла.
        }

        return track;
    }

    private void PlaylistList_SelectionChanged(
        object sender, SelectionChangedEventArgs e)
    {
        int index = PlaylistList.SelectedIndex;

        if (index >= 0 && index < Tracks.Count)
            PlayTrack(index);
    }

    private void PlayTrack(int index)
    {
        if (index < 0 || index >= Tracks.Count)
            return;

        try
        {
            // Убираем эффект с предыдущего трека.
            if (_currentIndex >= 0 && _currentIndex < Tracks.Count)
                Tracks[_currentIndex].IsPlaying = false;

            _isPlaying = false;
            _currentIndex = index;
            _duration = TimeSpan.Zero;

            ProgressSlider.Value = 0;
            ProgressSlider.Maximum = 100;

            ElapsedText.Text = "0:00";
            RemainingText.Text = "-0:00";

            _player.Stop();
            _player.Open(new Uri(Tracks[index].FilePath));
            _player.Volume = VolumeSlider.Value;
            _player.Play();

            _isPlaying = true;
            Tracks[index].IsPlaying = true;

            SetPlayPauseImage(true);
        }
        catch (Exception ex)
        {
            _isPlaying = false;
            Tracks[index].IsPlaying = false;
            SetPlayPauseImage(false);

            MessageBox.Show(
                this,
                "Не удалось открыть файл:\n" + ex.Message,
                "Ошибка воспроизведения",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void SetPlayPauseImage(bool playing)
    {
        string imagePath = playing
            ? "pack://application:,,,/Images/pause.png"
            : "pack://application:,,,/Images/play.png";

        PlayPauseImage.Source = new BitmapImage(new Uri(imagePath));
    }

    private void Player_MediaOpened(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (!_player.NaturalDuration.HasTimeSpan)
                return;

            _duration = _player.NaturalDuration.TimeSpan;

            if (_currentIndex >= 0 && _currentIndex < Tracks.Count)
            {
                Tracks[_currentIndex].DurationText = FormatTime(_duration);
            }

            ProgressSlider.Maximum = Math.Max(1, _duration.TotalSeconds);
            RemainingText.Text = "-" + FormatTime(_duration);
        });
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_currentIndex < 0)
        {
            if (Tracks.Count > 0)
                PlayIndex(0);

            return;
        }

        if (_isPlaying)
        {
            _player.Pause();
            _isPlaying = false;
            Tracks[_currentIndex].IsPlaying = false;

            SetPlayPauseImage(false);
        }
        else
        {
            _player.Play();
            _isPlaying = true;
            Tracks[_currentIndex].IsPlaying = true;

            SetPlayPauseImage(true);
        }
    }

    private void PlayIndex(int index)
    {
        if (index < 0 || index >= Tracks.Count)
            return;

        if (PlaylistList.SelectedIndex == index)
        {
            PlayTrack(index);
        }
        else
        {
            PlaylistList.SelectedIndex = index;
        }
    }

    private void ReversePlayback_Click(object sender, RoutedEventArgs e)
    {
        _reversePlayback = !_reversePlayback;
        // Кнопка переключает направление автоматического перехода между треками.
        if (sender is Button button)
            button.Opacity = _reversePlayback ? 1.0 : 0.65;
    }

    private void Loop_Click(object sender, RoutedEventArgs e)
    {
        _repeatCurrentTrack = !_repeatCurrentTrack;
        LoopButton.Opacity = _repeatCurrentTrack ? 1.0 : 0.65;
        LoopButton.ToolTip = _repeatCurrentTrack
            ? "Повтор текущего трека включён"
            : "Повторять текущий трек";
    }

    private void LoadPlaylist()
    {
        try
        {
            if (!File.Exists(PlaylistSettingsPath))
                return;

            var savedPaths = JsonSerializer.Deserialize<List<string>>(
                File.ReadAllText(PlaylistSettingsPath));
            if (savedPaths == null)
                return;

            foreach (string path in savedPaths)
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    continue;

                if (Tracks.Any(track => string.Equals(track.FilePath, path,
                    StringComparison.OrdinalIgnoreCase)))
                    continue;

                Tracks.Add(ReadTrackMetadata(path));
            }
        }
        catch
        {
            // Повреждённый файл настроек не должен мешать запуску приложения.
        }
    }

    private void SavePlaylist()
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            string json = JsonSerializer.Serialize(
                Tracks.Select(track => track.FilePath).ToList(),
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(PlaylistSettingsPath, json);
        }
        catch
        {
            // Ошибка сохранения плейлиста не должна останавливать воспроизведение.
        }
    }

    private double LoadVolume()
    {
        // Registry is the primary store; the file is kept as a backup.
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(VolumeRegistryPath);
            object? stored = key?.GetValue(VolumeRegistryValue);
            if (stored != null && double.TryParse(Convert.ToString(stored, CultureInfo.InvariantCulture),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out double registryVolume))
                return Math.Clamp(registryVolume, 0.0, 1.0);
        }
        catch
        {
            // Fall back to the settings file if the registry is unavailable.
        }

        try
        {
            if (File.Exists(VolumeSettingsPath) &&
                double.TryParse(File.ReadAllText(VolumeSettingsPath),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out double volume))
                return Math.Clamp(volume, 0.0, 1.0);
        }
        catch
        {
            // Use the default volume if both stores are unavailable.
        }

        return 0.7;
    }

    private void SaveVolume()
    {
        string value = VolumeSlider.Value.ToString("R", CultureInfo.InvariantCulture);
        bool saved = false;

        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(VolumeRegistryPath, true);
            key.SetValue(VolumeRegistryValue, value, RegistryValueKind.String);
            saved = true;
        }
        catch
        {
            // Also write the file backup below.
        }

        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            File.WriteAllText(VolumeSettingsPath, value);
            saved = true;
        }
        catch
        {
            // Do not interrupt playback if the backup cannot be written.
        }

        if (!saved)
            System.Diagnostics.Debug.WriteLine("Could not persist DesktopMusicWidget volume settings.");
    }

    private void Previous_Click(object sender, RoutedEventArgs e)
    {
        if (Tracks.Count == 0)
            return;

        if (_currentIndex >= 0 && _player.Position.TotalSeconds > 3)
        {
            _player.Position = TimeSpan.Zero;
            return;
        }

        int direction = _reversePlayback ? 1 : -1;
        int previous = (_currentIndex + direction + Tracks.Count) % Tracks.Count;
        PlayIndex(previous);
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (Tracks.Count == 0)
            return;

        int direction = _reversePlayback ? -1 : 1;
        int next = (_currentIndex + direction + Tracks.Count) % Tracks.Count;
        PlayIndex(next);
    }

    private void Player_MediaEnded(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (_currentIndex >= 0 && _currentIndex < Tracks.Count)
                Tracks[_currentIndex].IsPlaying = false;

            _isPlaying = false;
            SetPlayPauseImage(false);

            if (Tracks.Count == 0)
                return;

            if (_repeatCurrentTrack && _currentIndex >= 0 && _currentIndex < Tracks.Count)
            {
                PlayIndex(_currentIndex);
                return;
            }

            int direction = _reversePlayback ? -1 : 1;
            int next = (_currentIndex + direction + Tracks.Count) % Tracks.Count;
            PlayIndex(next);
        });
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (_isSeeking || _currentIndex < 0 || _duration <= TimeSpan.Zero)
            return;

        TimeSpan position = _player.Position;

        ProgressSlider.Value = Math.Min(
            position.TotalSeconds,
            ProgressSlider.Maximum);

        ElapsedText.Text = FormatTime(position);

        TimeSpan remaining = _duration - position;
        RemainingText.Text = "-" + FormatTime(remaining);
    }

    private void Progress_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _isSeeking = true;
    }

    private void Progress_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_duration > TimeSpan.Zero)
        {
            double seconds = Math.Clamp(
                ProgressSlider.Value,
                0,
                _duration.TotalSeconds);

            _player.Position = TimeSpan.FromSeconds(seconds);
        }

        _isSeeking = false;
    }

    private void Progress_ValueChanged(
        object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isSeeking || _duration <= TimeSpan.Zero)
            return;

        TimeSpan position = TimeSpan.FromSeconds(
            Math.Clamp(e.NewValue, 0, _duration.TotalSeconds));

        ElapsedText.Text = FormatTime(position);
        RemainingText.Text = "-" + FormatTime(_duration - position);
    }

    private void Volume_ValueChanged(
        object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_player != null)
            _player.Volume = VolumeSlider.Value;

        // Do not write settings on every slider tick; persist when the user releases it.
    }

    private void DeleteTrack_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if (sender is not FrameworkElement { DataContext: TrackItem track })
            return;

        int index = Tracks.IndexOf(track);

        if (index < 0)
            return;

        if (index == _currentIndex)
        {
            // Удаляем играющий трек: останавливаем воспроизведение.
            track.IsPlaying = false;
            StopPlayback();
            Tracks.RemoveAt(index);
        }
        else
        {
            Tracks.RemoveAt(index);

            // Индекс текущего трека сдвинулся, если удалили трек выше него.
            if (index < _currentIndex)
                _currentIndex--;
        }

        SavePlaylist();
    }

    private void StopPlayback()
    {
        _player.Stop();
        _player.Close();

        _isPlaying = false;
        _currentIndex = -1;
        _duration = TimeSpan.Zero;

        ProgressSlider.Maximum = 100;
        ProgressSlider.Value = 0;

        ElapsedText.Text = "0:00";
        RemainingText.Text = "-0:00";

        SetPlayPauseImage(false);
    }

    private bool _isVolumeDragging;

    private void Volume_PreviewMouseLeftButtonDown(
        object sender, MouseButtonEventArgs e)
    {
        _isVolumeDragging = true;
        VolumeSlider.CaptureMouse();
        UpdateVolumeFromMouse(e);

        e.Handled = true;
    }

    private void Volume_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_isVolumeDragging)
            UpdateVolumeFromMouse(e);
    }

    private void Volume_PreviewMouseLeftButtonUp(
        object sender, MouseButtonEventArgs e)
    {
        if (!_isVolumeDragging)
            return;

        _isVolumeDragging = false;
        VolumeSlider.ReleaseMouseCapture();

        if (!_isLoadingVolume)
            SaveVolume();

        e.Handled = true;
    }

    private void Volume_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        const double step = 0.05;

        VolumeSlider.Value = Math.Clamp(
            VolumeSlider.Value + (e.Delta > 0 ? step : -step),
            VolumeSlider.Minimum,
            VolumeSlider.Maximum);

        e.Handled = true;
    }

    private void UpdateVolumeFromMouse(MouseEventArgs e)
    {
        if (VolumeSlider.Template.FindName("PART_Track", VolumeSlider)
            is not Track track)
            return;

        double value = track.ValueFromPoint(e.GetPosition(track));

        if (double.IsNaN(value) || double.IsInfinity(value))
            return;

        VolumeSlider.Value = Math.Clamp(
            value,
            VolumeSlider.Minimum,
            VolumeSlider.Maximum);
    }

    private static string FormatTime(TimeSpan time)
    {
        if (time < TimeSpan.Zero)
            time = TimeSpan.Zero;

        return time.TotalHours >= 1
            ? time.ToString(@"h\:mm\:ss")
            : time.ToString(@"m\:ss");
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _isClosing = true;
        _desktopGuardTimer.Stop();
        SaveVolume();
        SavePlaylist();
        _timer.Stop();
        _player.Close();

        base.OnClosing(e);
    }
}
