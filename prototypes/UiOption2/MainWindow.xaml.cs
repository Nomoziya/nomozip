using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Nomozip.DesignPreview;

public partial class MainWindow : Window
{
    private CancellationTokenSource? demoCancellation;
    private bool paused, requestedMotion = true;
    public bool IsRunning { get; private set; }
    public bool MotionEnabled => Progress.MotionEnabled;
    public long ProcessedBytes { get; private set; }
    public long TotalBytes => Model.Entries.Sum(item => item.Bytes.LongLength);
    public ArchiveModel Model { get; } = new();
    public MainWindow()
    {
        InitializeComponent(); DataContext = Model; ApplyMotion(true); SetPose(0, "准备解压");
        SystemParameters.StaticPropertyChanged += OnSystemSettingsChanged;
        Closed += (_, _) => { demoCancellation?.Cancel(); SystemParameters.StaticPropertyChanged -= OnSystemSettingsChanged; };
    }
    private void OnSystemSettingsChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName == nameof(SystemParameters.ClientAreaAnimation)) ApplyMotion(requestedMotion); }
    public void ApplyMotion(bool enabled, bool isolatedSimulation = false)
    {
        if (isolatedSimulation && Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true") throw new InvalidOperationException("Motion preference simulation requires isolated CI.");
        requestedMotion = enabled; Progress.MotionEnabled = enabled && (isolatedSimulation || SystemParameters.ClientAreaAnimation);
        MotionToggle.Content = Progress.MotionEnabled ? "动态效果：开" : "动态效果：关";
        MotionToggle.ToolTip = SystemParameters.ClientAreaAnimation ? "关闭后，进度数字与状态仍正常更新" : "Windows 已关闭客户端动态效果";
    }
    public void SetPose(double value, string state)
    {
        value = Math.Clamp(value, 0, 100); Progress.Value = value; PercentLabel.Text = $"{value:0}%"; TaskTitle.Text = state;
        ProcessedBytes = (long)Math.Round(TotalBytes * value / 100);
        TaskDetail.Text = $"项目交付.zip · {Entry.FormatSize(ProcessedBytes)} / {Entry.FormatSize(TotalBytes)}";
    }
    public async Task RunDemoAsync()
    {
        if (IsRunning) return;
        IsRunning = true; paused = false; demoCancellation = new(); var token = demoCancellation.Token;
        PauseButton.Content = "暂停演示"; SetPose(0, "正在解压");
        if (MotionEnabled)
        {
            TaskCard.BeginAnimation(OpacityProperty, new DoubleAnimation(.65, 1, TimeSpan.FromMilliseconds(170)) { FillBehavior = FillBehavior.Stop });
        }
        long count = 0;
        try
        {
            using var zip = new ZipArchive(new MemoryStream(Model.ArchiveBytes), ZipArchiveMode.Read);
            foreach (var item in zip.Entries.Where(item => !item.FullName.EndsWith('/')))
            {
                using var input = item.Open(); using var output = new MemoryStream(); var buffer = new byte[64]; int read;
                while ((read = await input.ReadAsync(buffer, token)) > 0)
                {
                    while (paused) await Task.Delay(50, token);
                    await output.WriteAsync(buffer.AsMemory(0, read), token); count += read;
                    SetPose(100d * count / TotalBytes, "正在解压");
                    // Deliberate pacing makes the in-memory sample visible; it is never represented as product throughput.
                    await Task.Delay(220, token);
                }
                var expected = Model.Entries.Single(row => row.Name == item.FullName).Bytes;
                if (!output.ToArray().SequenceEqual(expected)) throw new InvalidDataException("Sample extraction bytes differ.");
            }
            SetPose(100, "演示完成");
        }
        catch (OperationCanceledException) { TaskTitle.Text = "演示已取消"; }
        finally { IsRunning = false; demoCancellation.Dispose(); demoCancellation = null; paused = false; PauseButton.Content = "暂停演示"; }
    }
    private async void OnStartDemo(object sender, RoutedEventArgs e) => await RunDemoAsync();
    private void OnPause(object sender, RoutedEventArgs e) { if (!IsRunning) return; paused = !paused; PauseButton.Content = paused ? "继续演示" : "暂停演示"; TaskTitle.Text = paused ? "演示已暂停" : "正在解压"; }
    private void OnCancel(object sender, RoutedEventArgs e) => demoCancellation?.Cancel();
    private void OnMotionToggle(object sender, RoutedEventArgs e) => ApplyMotion(!requestedMotion);
    private void OnTheme(object sender, RoutedEventArgs e) { if (Application.Current is App app) app.ApplyTheme(!app.Dark); }
    private void OnMinimize(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    private void OnMaximize(object sender, RoutedEventArgs e) { if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this); else SystemCommands.MaximizeWindow(this); }
    private void OnClose(object sender, RoutedEventArgs e) => Close();
    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!IsInitialized) return;
        var compact = ActualWidth < 1000; Model.SetCompact(compact);
        RailColumn.Width = new GridLength(compact ? 0 : 168); NavigationRail.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        CompactNavigation.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        BrandPanel.Margin = new Thickness(compact ? 56 : 20, 0, 0, 0);
    }
    private void OnCompactNavigation(object sender, RoutedEventArgs e) { CompactNavigation.ContextMenu.PlacementTarget = CompactNavigation; CompactNavigation.ContextMenu.IsOpen = true; }
    private void OnDoubleClick(object sender, MouseButtonEventArgs e) => ShowPreview();
    private void OnPreview(object sender, RoutedEventArgs e) => ShowPreview();
    private void ShowPreview()
    {
        if (Model.CurrentEntry is null || Model.CurrentEntry.Directory) return;
        Model.RequestPreview(); MessageBox.Show(this, Model.PreviewText, Model.PreviewName, MessageBoxButton.OK, MessageBoxImage.None);
    }
    private void OnNotice(object sender, RoutedEventArgs e) => MessageBox.Show(this, $"“{(sender as FrameworkElement)?.Tag}”会在方向确认后接入。\n此为独立视觉原型；解压按钮只演示内存 ZIP 样本，不写入磁盘或访问密码库。", "nomozip · 待选 2", MessageBoxButton.OK, MessageBoxImage.Information);
}
