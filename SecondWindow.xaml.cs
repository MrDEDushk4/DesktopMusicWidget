using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace DesktopMusicWidget;

public partial class SecondWindow : Window
{
    private const double PlayerWidth = 234;
    private const double PlaylistWidth = 244;
    private const double Overlap = 10;
    private const double OpenWindowWidth = PlaylistWidth + PlayerWidth;
    private const double PlaylistStart = -(PlaylistWidth + Overlap);
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
        Height = 179;
        RootCanvas.Width = OpenWindowWidth;
        RootCanvas.Height = 179;
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
            PositionSlider.Value = Math.Clamp(position.TotalSeconds, 0, PositionSlider.Maximum);
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
        _animating = true;
        _playlistVisible = true;
        PlaylistPanel.Visibility = Visibility.Visible;
        PlaylistToggleButton.ToolTip = "Закрыть плейлист";

        AnimateDouble(this, LeftProperty, Left, _compactLeft - PlaylistWidth, null);
        AnimateDouble(this, WidthProperty, Width, OpenWindowWidth, null);
        AnimateCanvas(PlaylistPanel, PlaylistStart, 0, null);
        AnimateCanvas(PlayerPanel, 0, PlayerOpenLeft, () =>
        {
            _animating = false;
            Left = _compactLeft - PlaylistWidth;
            Width = OpenWindowWidth;
            Canvas.SetLeft(PlaylistPanel, 0);
            Canvas.SetLeft(PlayerPanel, PlayerOpenLeft);
        });
    }

    private void ClosePlaylist()
    {
        _animating = true;
        PlaylistToggleButton.ToolTip = "Открыть плейлист";

        AnimateDouble(this, LeftProperty, Left, _compactLeft, null);
        AnimateDouble(this, WidthProperty, Width, PlayerWidth, null);
        AnimateCanvas(PlaylistPanel, 0, PlaylistStart, null);
        AnimateCanvas(PlayerPanel, PlayerOpenLeft, 0, () =>
        {
            _playlistVisible = false;
            _animating = false;
            PlaylistPanel.Visibility = Visibility.Collapsed;

            Left = _compactLeft;
            Width = PlayerWidth;
            Canvas.SetLeft(PlaylistPanel, PlaylistStart);
            Canvas.SetLeft(PlayerPanel, 0);
        });
    }

    private static void AnimateDouble(DependencyObject target, DependencyProperty property,
        double from, double to, Action? completed)
    {
        var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(280))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };

        if (completed != null)
            animation.Completed += (_, _) => completed();

        target.BeginAnimation(property, animation);
    }

    private static void AnimateCanvas(FrameworkElement element, double from, double to,
        Action? completed)
    {
        var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(280))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };

        if (completed != null)
            animation.Completed += (_, _) => completed();

        element.BeginAnimation(Canvas.LeftProperty, animation);
    }

    private void ExpandedPlaylist_SelectionChanged(object sender, SelectionChangedEventArgs e)
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

    private void Position_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_seeking || _main.CurrentDuration <= TimeSpan.Zero)
            return;

        TimeSpan position = TimeSpan.FromSeconds(e.NewValue);
        ElapsedLabel.Text = MainWindow.FormatTrackTime(position);
        TimeSpan remaining = _main.CurrentDuration - position;
        RemainingLabel.Text = "-" + MainWindow.FormatTrackTime(
            remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
    }

    private void Volume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncing || _main is null || VolumeSlider is null)
            return;

        _main.SetVolumeFromExpandedView(e.NewValue);
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(this,
            "Настройки виджета будут доступны здесь.",
            "Настройки", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed ||
            IsInteractiveElement(e.OriginalSource as DependencyObject))
            return;

        try
        {
            DragMove();
            _compactLeft = _playlistVisible ? Left + PlaylistWidth : Left;
        }
        catch (InvalidOperationException)
        {
            // The window can lose the mouse capture during a drag.
        }
    }

    private static bool IsInteractiveElement(DependencyObject? element)
    {
        while (element != null)
        {
            if (element is Button or Slider or ListBox or ScrollBar or TextBox)
                return true;

            element = System.Windows.Media.VisualTreeHelper.GetParent(element);
        }

        return false;
    }

    private void SwitchToCompact_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_returning)
            return;

        _returning = true;
        _uiTimer.Stop();
        _main.ReturnFromExpandedView(this);
    }
}