using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace DesktopMusicWidget;

public partial class SecondWindow : Window
{
    private readonly MainWindow _main;
    private readonly DispatcherTimer _uiTimer = new();
    private bool _syncing;
    private bool _seeking;
    private bool _returning;

    public SecondWindow(MainWindow main)
    {
        InitializeComponent();
        _main = main;
        DataContext = main;
        ExpandedPlaylist.ItemsSource = main.Tracks;
        VolumeSlider.Value = main.CurrentVolume;

        _uiTimer.Interval = TimeSpan.FromMilliseconds(150);
        _uiTimer.Tick += (_, _) => RefreshPlayerState();
        _uiTimer.Start();
        RefreshPlayerState();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_main.CurrentIndex >= 0 && _main.CurrentIndex < _main.Tracks.Count)
            ExpandedPlaylist.SelectedIndex = _main.CurrentIndex;
        RefreshPlayerState();
    }

    private void RefreshPlayerState()
    {
        int index = _main.CurrentIndex;
        _syncing = true;

        if (index >= 0 && index < _main.Tracks.Count)
        {
            TrackItem track = _main.Tracks[index];
            TrackTitle.Text = track.Title;
            TrackArtist.Text = string.IsNullOrWhiteSpace(track.Artist) ? "Неизвестный исполнитель" : track.Artist;
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

        bool playing = _main.IsPlaying;
        PlayButton.Content = playing ? "Ⅱ" : "▶";
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
        RemainingLabel.Text = "-" + MainWindow.FormatTrackTime(duration - position);
        _syncing = false;
    }

    private void ExpandedPlaylist_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || ExpandedPlaylist.SelectedIndex < 0)
            return;

        _main.PlayFromExpandedView(ExpandedPlaylist.SelectedIndex);
        RefreshPlayerState();
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e) => _main.TogglePlaybackFromExpandedView();
    private void Previous_Click(object sender, RoutedEventArgs e) => _main.PreviousFromExpandedView();
    private void Next_Click(object sender, RoutedEventArgs e) => _main.NextFromExpandedView();
    private void Direction_Click(object sender, RoutedEventArgs e) => _main.ToggleDirectionFromExpandedView();
    private void Repeat_Click(object sender, RoutedEventArgs e) => _main.ToggleRepeatFromExpandedView();

    private void Position_MouseDown(object sender, MouseButtonEventArgs e) => _seeking = true;

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
        RemainingLabel.Text = "-" + MainWindow.FormatTrackTime(_main.CurrentDuration - position);
    }

    private void Volume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_syncing)
            _main.SetVolumeFromExpandedView(VolumeSlider.Value);
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(this,
            "Раздел настроек будет расширяться отдельно от интерфейсов. Параметры уже хранятся в отдельной модели настроек.",
            "Настройки виджета", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void SwitchToCompact_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed &&
            e.OriginalSource is not Button &&
            e.OriginalSource is not Slider &&
            e.OriginalSource is not ListBox &&
            e.OriginalSource is not ScrollBar)
        {
            try { DragMove(); } catch (InvalidOperationException) { }
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_returning)
            return;

        _returning = true;
        _uiTimer.Stop();
        _main.ReturnFromExpandedView(this);
    }
}
