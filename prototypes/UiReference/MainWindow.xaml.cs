using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Nomozip.DesignPreview;

public partial class MainWindow : Window
{
    private CancellationTokenSource? demoCancellation;
    private bool paused, requestedMotion = true, previewRequested = true, sortAscending = false;
    private Entry? preview;
    public bool IsRunning { get; private set; }
    public bool MotionEnabled => Progress.MotionEnabled;
    public long ProcessedBytes { get; private set; }
    public long TotalBytes { get; private set; }
    public ArchiveModel Model { get; } = new();
    public static readonly DependencyProperty SizeWidthProperty = DependencyProperty.Register(nameof(SizeWidth), typeof(GridLength), typeof(MainWindow), new PropertyMetadata(new GridLength(103)));
    public static readonly DependencyProperty TypeWidthProperty = DependencyProperty.Register(nameof(TypeWidth), typeof(GridLength), typeof(MainWindow), new PropertyMetadata(new GridLength(113)));
    public static readonly DependencyProperty DateWidthProperty = DependencyProperty.Register(nameof(DateWidth), typeof(GridLength), typeof(MainWindow), new PropertyMetadata(new GridLength(171)));
    public GridLength SizeWidth { get => (GridLength)GetValue(SizeWidthProperty); set => SetValue(SizeWidthProperty, value); }
    public GridLength TypeWidth { get => (GridLength)GetValue(TypeWidthProperty); set => SetValue(TypeWidthProperty, value); }
    public GridLength DateWidth { get => (GridLength)GetValue(DateWidthProperty); set => SetValue(DateWidthProperty, value); }
    public MainWindow()
    {
        InitializeComponent(); PauseButton.IsEnabled = CancelButton.IsEnabled = false; DataContext = Model; ShowPreview(); ApplyMotion(true);
        SystemParameters.StaticPropertyChanged += OnSystemSettingsChanged;
        Closed += (_, _) => { demoCancellation?.Cancel(); SystemParameters.StaticPropertyChanged -= OnSystemSettingsChanged; };
    }
    private void OnSystemSettingsChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName == nameof(SystemParameters.ClientAreaAnimation)) ApplyMotion(requestedMotion); }
    public void ApplyMotion(bool enabled, bool isolatedSimulation = false)
    {
        if (isolatedSimulation && Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true") throw new InvalidOperationException("Motion preference simulation requires isolated CI.");
        requestedMotion = enabled; Progress.MotionEnabled = enabled && (isolatedSimulation || SystemParameters.ClientAreaAnimation);
        MotionMenu.Header = Progress.MotionEnabled ? "动态效果：开" : "动态效果：关"; MotionMenu.IsChecked = Progress.MotionEnabled;
    }
    public void SetPose(double value, string state)
    {
        value = Math.Clamp(value, 0, 100); Progress.Value = value; PercentLabel.Text = $"{value:0}%"; TaskTitle.Text = state;
        ProcessedBytes = (long)Math.Round(TotalBytes * value / 100);
        TaskDetail.Text = $"图片备份.zip · {Entry.FormatSize(ProcessedBytes)} / {Entry.FormatSize(TotalBytes)} · 仅处理内存样本";
    }
    public async Task RunDemoAsync(bool checkedOnly = false)
    {
        if (IsRunning) return;
        var rows = Model.Entries.Where(row => !row.Directory && (!checkedOnly || row.Checked)).ToArray(); if (rows.Length == 0) return;
        TotalBytes = rows.Sum(row => row.Bytes.LongLength); IsRunning = true; paused = false; demoCancellation = new(); var token = demoCancellation.Token;
        PauseButton.Content = "暂停"; PauseButton.IsEnabled = CancelButton.IsEnabled = true; TaskCard.Visibility = Visibility.Visible; TaskBadge.Visibility = Visibility.Visible; SetPose(0, "正在解压");
        if (MotionEnabled) TaskCard.BeginAnimation(OpacityProperty, new DoubleAnimation(.65, 1, TimeSpan.FromMilliseconds(170)) { FillBehavior = FillBehavior.Stop });
        long count = 0;
        try
        {
            using var zip = new ZipArchive(new MemoryStream(Model.ArchiveBytes), ZipArchiveMode.Read);
            foreach (var row in rows)
            {
                using var input = zip.GetEntry(row.Path)!.Open(); using var output = new MemoryStream(); var buffer = new byte[65536]; int read;
                while ((read = await input.ReadAsync(buffer, token)) > 0)
                {
                    while (paused) await Task.Delay(40, token);
                    await output.WriteAsync(buffer.AsMemory(0, read), token); count += read;
                    SetPose(100d * count / TotalBytes, "正在解压"); await Task.Delay(80, token);
                }
                if (!output.ToArray().SequenceEqual(row.Bytes)) throw new InvalidDataException("Sample extraction bytes differ.");
            }
            SetPose(100, "演示完成");
        }
        catch (OperationCanceledException) { TaskTitle.Text = "演示已取消"; }
        finally { PauseButton.IsEnabled = CancelButton.IsEnabled = false; IsRunning = false; demoCancellation.Dispose(); demoCancellation = null; paused = false; PauseButton.Content = "暂停"; TaskBadge.Visibility = Visibility.Collapsed; }
    }
    public void ShowPreview()
    {
        var row = Model.CurrentEntry; if (row is null || row.Directory) return; preview = row; previewRequested = true;
        PreviewName.Text = row.Name; PreviewName.ToolTip = row.Path; PreviewSize.Text = row.SizeLabel; PreviewDate.Text = row.Modified;
        PreviewImage.Source = row.Thumbnail; ImageFrame.Visibility = row.IsImage ? Visibility.Visible : Visibility.Collapsed;
        PreviewDimensions.Text = row.Thumbnail is { } image ? $"{image.PixelWidth} × {image.PixelHeight}" : "—";
        PreviewKind.Text = row.IsImage ? "图片预览" : "文件预览";
        PreviewText.Visibility = row.IsImage ? Visibility.Collapsed : Visibility.Visible;
        PreviewText.Text = row.Name.EndsWith(".txt") || row.Name.EndsWith(".csv") ? Encoding.UTF8.GetString(row.Bytes) : "此类文件保留元数据预览，正式版可交给系统关联程序打开。";
        UpdateResponsive();
    }
    public void ClosePreview() { previewRequested = false; UpdateResponsive(); }
    private void UpdateResponsive()
    {
        if (!IsInitialized || Client is null) return;
        var compact = ActualWidth < 1050; var narrowPreview = compact && previewRequested;
        RailColumn.Width = new GridLength(compact ? 0 : 186); NavigationRail.Visibility = compact ? Visibility.Collapsed : Visibility.Visible; CompactNavigation.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        SearchColumn.Width = new GridLength(compact ? 230 : 286);
        PreviewPane.Visibility = previewRequested ? Visibility.Visible : Visibility.Collapsed; PreviewColumn.Width = new GridLength(previewRequested ? (compact ? 300 : 350) : 0);
        SizeWidth = new GridLength(narrowPreview ? 94 : 103); TypeWidth = new GridLength(narrowPreview ? 0 : 113); DateWidth = new GridLength(compact ? 0 : 171);
        SizeHeaderColumn.Width = SizeWidth; TypeHeaderColumn.Width = TypeWidth; DateHeaderColumn.Width = DateWidth;
        MoreButton.Visibility = narrowPreview ? Visibility.Collapsed : Visibility.Visible;
    }
    private async void OnStartDemo(object sender, RoutedEventArgs e) => await RunDemoAsync(ReferenceEquals(sender, ExtractChecked));
    private void OnPause(object sender, RoutedEventArgs e) { if (!IsRunning) return; paused = !paused; PauseButton.Content = paused ? "继续" : "暂停"; TaskTitle.Text = paused ? "演示已暂停" : "正在解压"; }
    private void OnCancel(object sender, RoutedEventArgs e) => demoCancellation?.Cancel();
    private void OnMotionToggle(object sender, RoutedEventArgs e) => ApplyMotion(!requestedMotion);
    private void OnTheme(object sender, RoutedEventArgs e) { if (Application.Current is App app) app.ApplyTheme(!app.Dark); }
    private void OnMinimize(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    private void OnMaximize(object sender, RoutedEventArgs e) { if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this); else SystemCommands.MaximizeWindow(this); }
    private void OnClose(object sender, RoutedEventArgs e) => Close();
    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => UpdateResponsive();
    private void OnCompactNavigation(object sender, RoutedEventArgs e) { CompactNavigation.ContextMenu.PlacementTarget = CompactNavigation; CompactNavigation.ContextMenu.IsOpen = true; }
    private void OnMore(object sender, RoutedEventArgs e) { MoreButton.ContextMenu.PlacementTarget = MoreButton; MoreButton.ContextMenu.IsOpen = true; }
    private void OnHome(object sender, RoutedEventArgs e) { Model.Navigate(); ShowPreview(); }
    private void OnDoubleClick(object sender, MouseButtonEventArgs e) { if (Model.CurrentEntry is { Directory: true } row) { Model.Navigate(row); ClosePreview(); } else ShowPreview(); }
    private void OnPreview(object sender, RoutedEventArgs e) => ShowPreview();
    private void OnClosePreview(object sender, RoutedEventArgs e) => ClosePreview();
    private void OnTogglePreview(object sender, RoutedEventArgs e) { if (previewRequested) ClosePreview(); else ShowPreview(); }
    private void OnSortName(object sender, RoutedEventArgs e) { sortAscending = !sortAscending; Model.Sort(nameof(Entry.Name), sortAscending ? ListSortDirection.Ascending : ListSortDirection.Descending); }
    private void OnSortSize(object sender, RoutedEventArgs e) { sortAscending = !sortAscending; Model.Sort("Bytes.Length", sortAscending ? ListSortDirection.Ascending : ListSortDirection.Descending); }
    private void OnTask(object sender, RoutedEventArgs e) { if (TotalBytes > 0) TaskCard.Visibility = Visibility.Visible; else OnNotice(sender, e); }
    private void OnHideTask(object sender, RoutedEventArgs e) => TaskCard.Visibility = Visibility.Collapsed;
    private void OnOpenImage(object sender, RoutedEventArgs e)
    {
        if (preview is not { IsImage: true, Thumbnail: { } photo }) return;
        new Window { Owner = this, Title = preview.Name + " · 示例图片", Width = 900, Height = 650, Background = (Brush)Application.Current.Resources["Canvas"], Content = new Image { Source = photo, Stretch = Stretch.Uniform, Margin = new Thickness(20) }, WindowStartupLocation = WindowStartupLocation.CenterOwner }.ShowDialog();
    }
    private void OnNotice(object sender, RoutedEventArgs e) => MessageBox.Show(this, $"{(sender as FrameworkElement)?.Tag ?? "任务页面"}将在方向确认后接入。\n当前是参考图复现原型；文件列表、预览与解压动画使用内存 ZIP 样本。", "nomozip · 参考图复现", MessageBoxButton.OK, MessageBoxImage.Information);
}
