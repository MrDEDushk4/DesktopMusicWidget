using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace DesktopMusicWidget;

public partial class SecondWindow : Window
{
    private const double PlayerWidth = 250;
    private const double PlayerHeight = 190;
    private const double PlaylistWidth = 260;
    private const double Overlap = 10;
    private const double PlaylistPanelWidth = PlaylistWidth + Overlap;
    private const double OpenWindowWidth = PlaylistWidth + PlayerWidth;
    private const double PlaylistStart = -PlaylistPanelWidth;
    private const double PlayerOpenLeft = PlaylistWidth;

    private readonly MainWindow _main;
    private readonly DispatcherTimer _uiTimer = new();

    private double _compactLeft;
    private bool _syncing;
    private bool _seeking;
    private bool _returning;
    private bool _playlistVisible;
    private bool _animating;

    public SecondWindow(MainWindow main)
    {
        _main = main ?? throw new ArgumentNullException(nameof(main));
        _syncing = true;

        InitializeComponent();

        DataContext = main;
        ExpandedPlaylist.ItemsSource = main.Tracks;
        VolumeSlider.Value = main.CurrentVolume;

        _uiTimer.Interval = TimeSpan.FromMilliseconds(150);
        _uiTimer.Tick += (_, _) => RefreshPlayerState();

        _syncing = false;
        _uiTimer.Start();
        RefreshPlayerState();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _compactLeft = Left;

        Width = PlayerWidth;
        Height = PlayerHeight;

        RootCanvas.Width = OpenWindowWidth;
        RootCanvas.Height = PlayerHeight;

        Canvas.SetLeft(PlayerPanel, 0);
        Canvas.SetLeft(PlaylistPanel, PlaylistStart);
        PlaylistPanel.Visibility = Visibility.Collapsed;

        if (_main.CurrentIndex >= 0 && _main.CurrentIndex < _main.Tracks.Count)
            ExpandedPlaylist.SelectedIndex = _main.CurrentIndex;

        RefreshPlayerState();
    }

    private void RefreshPlayerState()
    {
        if (!IsLoaded)
            return;

        int index = _main.CurrentIndex;
        _syncing = true;

        if (index >= 0 && index < _main.Tracks.Count)
        {
            TrackItem track = _main.Tracks[index];

            TrackTitle.Text = track.Title;

            string artist = string.IsNullOrWhiteSpace(track.Artist)
                ? "Неизвестный исполнитель"
                : track.Artist;

            string album = string.IsNullOrWhiteSpace(track.Album)
                ? "Без альбома"
                : track.Album;

            TrackArtist.Text = artist + " · " + album;

            CoverImage.Source = track.AlbumArt;
            BackdropImage.Source = track.AlbumArt;

            if (ExpandedPlaylist.SelectedIndex != index)
                ExpandedPlaylist.SelectedIndex = index;
        }
        else
        {
            TrackTitle.Text = "Выберите композицию";
            TrackArtist.Text = "Плейлист пока пуст";
            CoverImage.Source = null;
            BackdropImage.Source = null;

            if (ExpandedPlaylist.SelectedIndex != -1)
                ExpandedPlaylist.SelectedIndex = -1;
        }

        PlayButton.Content = _main.IsPlaying ? "Ⅱ" : "▶";
        DirectionButton.Opacity = _main.ReversePlayback ? 1.0 : 0.62;
        RepeatButton.Opacity = _main.RepeatCurrentTrack ? 1.0 : 0.62;

        TimeSpan duration = _main.CurrentDuration;
        TimeSpan position = _main.SharedPlayer.Position;

        if (!_seeking)
        {
            PositionSlider.Maximum = Math.Max(1, duration.TotalSeconds);
            PositionSlider.Value = Math.Clamp(
                position.TotalSeconds, 0, PositionSlider.Maximum);
        }

        ElapsedLabel.Text = MainWindow.FormatTrackTime(position);
        RemainingLabel.Text = "-" + MainWindow.FormatTrackTime(
            duration > position ? duration - position : TimeSpan.Zero);

        _syncing = false;
    }

    private void TogglePlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (_animating)
            return;

        if (_playlistVisible)
            ClosePlaylist();
        else
            OpenPlaylist();
    }

    private void OpenPlaylist()
    {
        if (_animating || _playlistVisible)
            return;

        _animating = true;
        _playlistVisible = true;
        PlaylistPanel.Visibility = Visibility.Visible;
        PlaylistToggleButton.ToolTip = "Закрыть плейлист";

        // Расширяем окно и переносим его левую границу вместе с плеером.
        // Экранная координата PlayerPanel остаётся прежней:
        // Window.Left + Canvas.Left(PlayerPanel) == _compactLeft.
        Width = OpenWindowWidth;
        Canvas.SetLeft(PlayerPanel, PlayerOpenLeft);
        Left = _compactLeft - PlaylistWidth;

        AnimateCanvas(PlaylistPanel, PlaylistStart, 0, () =>
        {
            _animating = false;
            Canvas.SetLeft(PlaylistPanel, 0);
        });
    }

    private void ClosePlaylist()
    {
        if (_animating || !_playlistVisible)
            return;

        _animating = true;
        PlaylistToggleButton.ToolTip = "Открыть плейлист";

        // Плеер остаётся неподвижным. Закрывается только панель плейлиста.
        AnimateCanvas(PlaylistPanel, 0, PlaylistStart, () =>
        {
            PlaylistPanel.Visibility = Visibility.Collapsed;

            // Одновременное изменение координат и размера выполняется
            // в одном UI-проходе: экранное положение плеера не меняется.
            Canvas.SetLeft(PlayerPanel, 0);
            Left = _compactLeft;
            Width = PlayerWidth;
            Canvas.SetLeft(PlaylistPanel, PlaylistStart);

            _playlistVisible = false;
            _animating = false;
        });
    }

    private static void AnimateCanvas(
        FrameworkElement element,
        double from,
        double to,
        Action? completed)
    {
        // Удаляем предыдущую анимацию, чтобы не накапливались clocks
        // и итоговое значение не зависело от старой анимации.
        element.BeginAnimation(Canvas.LeftProperty, null);
        Canvas.SetLeft(element, from);

        var animation = new DoubleAnimation(
            from,
            to,
            TimeSpan.FromMilliseconds(300))
        {
            EasingFunction = new CubicEase
            {
                EasingMode = EasingMode.EaseInOut
            },
            FillBehavior = FillBehavior.Stop
        };

        animation.Completed += (_, _) =>
        {
            element.BeginAnimation(Canvas.LeftProperty, null);
            Canvas.SetLeft(element, to);
            completed?.Invoke();
        };

        element.BeginAnimation(
            Canvas.LeftProperty,
            animation,
            HandoffBehavior.SnapshotAndReplace);
    }

    private void ExpandedPlaylist_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_syncing || ExpandedPlaylist.SelectedIndex < 0)
            return;

        _main.PlayFromExpandedView(ExpandedPlaylist.SelectedIndex);
        RefreshPlayerState();
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e) =>
        _main.TogglePlaybackFromExpandedView();

    private void Previous_Click(object sender, RoutedEventArgs e) =>
        _main.PreviousFromExpandedView();

    private void Next_Click(object sender, RoutedEventArgs e) =>
        _main.NextFromExpandedView();

    private void Direction_Click(object sender, RoutedEventArgs e) =>
        _main.ToggleDirectionFromExpandedView();

    private void Repeat_Click(object sender, RoutedEventArgs e) =>
        _main.ToggleRepeatFromExpandedView();

    private void Position_MouseDown(object sender, MouseButtonEventArgs e) =>
        _seeking = true;

    private void Position_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _main.SeekFromExpandedView(PositionSlider.Value);
        _seeking = false;
    }

    private void Position_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_seeking || _main.CurrentDuration <= TimeSpan.Zero)
            return;

        TimeSpan position = TimeSpan.FromSeconds(e.NewValue);
        ElapsedLabel.Text = MainWindow.FormatTrackTime(position);

        TimeSpan remaining = _main.CurrentDuration - position;
        RemainingLabel.Text = "-" + MainWindow.FormatTrackTime(
            remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
    }

    private void Volume_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        // Событие может сработать во время InitializeComponent,
        // пока элементы окна ещё не полностью инициализированы.
        if (_syncing || !IsInitialized || _main is null)
            return;

        _main.SetVolumeFromExpandedView(e.NewValue);
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            this,
            "Настройки виджета будут доступны здесь.",
            "Настройки",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void Window_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed ||
            IsInteractiveElement(e.OriginalSource as DependencyObject))
            return;

        try
        {
            DragMove();
            _compactLeft = _playlistVisible
                ? Left + PlaylistWidth
                : Left;
        }
        catch (InvalidOperationException)
        {
            // Окно могло потерять захват мыши во время перетаскивания.
        }
    }

    private static bool IsInteractiveElement(DependencyObject? element)
    {
        while (element != null)
        {
            if (element is Button or Slider or ListBox or ScrollBar or TextBox)
                return true;

            element = VisualTreeHelper.GetParent(element);
        }

        return false;
    }

    private void SwitchToCompact_Click(object sender, RoutedEventArgs e) =>
        Close();

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_returning)
            return;

        _returning = true;
        _uiTimer.Stop();
        _main.ReturnFromExpandedView(this);
    }
}