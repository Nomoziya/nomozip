using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Nomozip.DesignPreview;

public partial class MainWindow : Window
{
    private CancellationTokenSource? demoCancellation;
    private bool paused, requestedMotion = true, previewRequested = true, sortAscending = false;
    private Entry? preview;
    private string? sortProperty;
    public bool IsRunning { get; private set; }
    public bool IsPaused => paused;
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
    public static readonly DependencyProperty TaskOutputWidthProperty = DependencyProperty.Register(nameof(TaskOutputWidth), typeof(GridLength), typeof(MainWindow), new PropertyMetadata(new GridLength(220)));
    public GridLength TaskOutputWidth { get => (GridLength)GetValue(TaskOutputWidthProperty); set => SetValue(TaskOutputWidthProperty, value); }
    public MainWindow()
    {
        InitializeComponent(); PauseButton.IsEnabled = CancelButton.IsEnabled = false; DataContext = Model; ShowPreview(); ApplyMotion(true); ShowTasks(false);
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
        value = Math.Clamp(value, 0, 100); Progress.Value = value; TaskTitle.Text = state;
        ProcessedBytes = (long)Math.Round(TotalBytes * value / 100);
        TaskDetail.Text = $"{Entry.FormatSize(ProcessedBytes)} / {Entry.FormatSize(TotalBytes)}";
    }
    public async Task RunDemoAsync(bool checkedOnly = false)
    {
        if (IsRunning) return;
        var rows = Model.Entries.Where(row => !row.Directory && (!checkedOnly || row.Checked)).ToArray(); if (rows.Length == 0) return;
        TotalBytes = rows.Sum(row => row.Bytes.LongLength); IsRunning = true; paused = false; demoCancellation = new(); var token = demoCancellation.Token;
        PauseButton.Content = "暂停"; PauseButton.IsEnabled = CancelButton.IsEnabled = true; TaskBadge.Visibility = Visibility.Visible;
        SetTaskTone("Accent"); SetPose(0, "正在解压"); ShowTasks(true); TaskFilterStatus.Text = "进行中 1";
        long count = 0;
        try
        {
            using var zip = new ZipArchive(new MemoryStream(Model.ArchiveBytes), ZipArchiveMode.Read);
            foreach (var row in rows)
            {
                TaskCurrentFile.Text = "正在处理：" + row.Path; TaskCurrentFile.ToolTip = row.Path;
                using var input = zip.GetEntry(row.Path)!.Open(); using var output = new MemoryStream(); var buffer = new byte[65536]; int read;
                while ((read = await input.ReadAsync(buffer, token)) > 0)
                {
                    while (paused) await Task.Delay(40, token);
                    await output.WriteAsync(buffer.AsMemory(0, read), token); count += read;
                    SetPose(100d * count / TotalBytes, "正在解压"); await Task.Delay(80, token);
                }
                if (!output.ToArray().SequenceEqual(row.Bytes)) throw new InvalidDataException("Sample extraction bytes differ.");
            }
            SetPose(100, "已完成"); SetTaskTone("Success"); TaskCurrentFile.Text = "已校验所有示例文件字节，解压完成。";
        }
        catch (OperationCanceledException) { Progress.SnapToValue(); TaskTitle.Text = "已取消"; SetTaskTone("Muted"); TaskCurrentFile.Text = "演示已取消，未写入磁盘。"; }
        finally { PauseButton.IsEnabled = CancelButton.IsEnabled = false; IsRunning = false; demoCancellation.Dispose(); demoCancellation = null; paused = false; PauseButton.Content = "暂停"; TaskBadge.Visibility = Visibility.Collapsed; TaskFilterStatus.Text = "已结束 1"; }
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
        var compact = ActualWidth < 1050; var narrowPreview = compact && previewRequested; TaskOutputWidth = new GridLength(compact ? 0 : 220);
        RailColumn.Width = new GridLength(compact ? 0 : 186); NavigationRail.Visibility = compact ? Visibility.Collapsed : Visibility.Visible; CompactNavigation.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        SearchColumn.Width = new GridLength(compact ? 230 : 286);
        PreviewPane.Visibility = previewRequested ? Visibility.Visible : Visibility.Collapsed; PreviewColumn.Width = new GridLength(previewRequested ? (compact ? 300 : 350) : 0);
        SizeWidth = new GridLength(narrowPreview ? 94 : 103); TypeWidth = new GridLength(narrowPreview ? 0 : 113); DateWidth = new GridLength(compact ? 0 : 171);
        SizeHeaderColumn.Width = SizeWidth; TypeHeaderColumn.Width = TypeWidth; DateHeaderColumn.Width = DateWidth;
        MoreButton.Visibility = narrowPreview ? Visibility.Collapsed : Visibility.Visible;
    }
    private async void OnStartDemo(object sender, RoutedEventArgs e) => await RunDemoAsync(ReferenceEquals(sender, ExtractChecked));
    public void TogglePause()
    {
        if (!IsRunning) return; paused = !paused; PauseButton.Content = paused ? "继续" : "暂停";
        if (paused) Progress.SnapToValue(); TaskTitle.Text = paused ? "已暂停" : "正在解压"; TaskFilterStatus.Text = paused ? "已暂停 1" : "进行中 1"; SetTaskTone(paused ? "Muted" : "Accent");
    }
    private void SetTaskTone(string resource) { Progress.SetResourceReference(ForegroundProperty, resource); TaskRing.SetResourceReference(ForegroundProperty, resource); }
    private void OnPause(object sender, RoutedEventArgs e) => TogglePause();
    private void OnCancel(object sender, RoutedEventArgs e) => demoCancellation?.Cancel();
    private void OnMotionToggle(object sender, RoutedEventArgs e) => ApplyMotion(!requestedMotion);
    private void OnTheme(object sender, RoutedEventArgs e) { if (Application.Current is App app) app.ApplyTheme(!app.Dark); }
    private void OnMinimize(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    private void OnMaximize(object sender, RoutedEventArgs e) { if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this); else SystemCommands.MaximizeWindow(this); }
    private void OnClose(object sender, RoutedEventArgs e) => Close();
    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => UpdateResponsive();
    private void OnCompactNavigation(object sender, RoutedEventArgs e) { CompactNavigation.ContextMenu.PlacementTarget = CompactNavigation; CompactNavigation.ContextMenu.IsOpen = true; }
    private void OnMore(object sender, RoutedEventArgs e) { MoreButton.ContextMenu.PlacementTarget = MoreButton; MoreButton.ContextMenu.IsOpen = true; }
    private void OnHome(object sender, RoutedEventArgs e) { ShowTasks(false); Model.Navigate(); ShowPreview(); }
    private void OnDoubleClick(object sender, MouseButtonEventArgs e) { if (Model.CurrentEntry is { Directory: true } row) { Model.Navigate(row); ClosePreview(); } else ShowPreview(); }
    private void OnPreview(object sender, RoutedEventArgs e) => ShowPreview();
    private void OnClosePreview(object sender, RoutedEventArgs e) => ClosePreview();
    private void OnTogglePreview(object sender, RoutedEventArgs e) { if (previewRequested) ClosePreview(); else ShowPreview(); }
    public void SortBy(string property)
    {
        sortAscending = sortProperty != property || !sortAscending; sortProperty = property;
        Model.Sort(property, sortAscending ? ListSortDirection.Ascending : ListSortDirection.Descending);
        NameSortArrow.Visibility = property == nameof(Entry.Name) ? Visibility.Visible : Visibility.Collapsed;
        SizeSortArrow.Visibility = property == "Bytes.Length" ? Visibility.Visible : Visibility.Collapsed;
        NameSortArrow.Kind = SizeSortArrow.Kind = sortAscending ? "ChevronUp" : "ChevronDown";
    }
    private void OnSortName(object sender, RoutedEventArgs e) => SortBy(nameof(Entry.Name));
    private void OnSortSize(object sender, RoutedEventArgs e) => SortBy("Bytes.Length");
    public void ShowTasks(bool show)
    {
        ExpandedWorkspace.Visibility = Visibility.Collapsed;
        ArchiveWorkspace.Visibility = BrowserFooter.Visibility = ArchiveHeading.Visibility = SearchPane.Visibility = Breadcrumbs.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        TaskWorkspace.Visibility = TaskHeading.Visibility = TaskFilters.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        TaskCard.Visibility = TaskDetails.Visibility = TotalBytes > 0 ? Visibility.Visible : Visibility.Collapsed;
        TaskEmpty.Visibility = TotalBytes > 0 ? Visibility.Collapsed : Visibility.Visible;
        TaskFilterCount.Text = TotalBytes > 0 ? "全部 1" : "全部 0";
        FilesIndicator.Visibility = show ? Visibility.Collapsed : Visibility.Visible; TasksIndicator.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        FilesNavigation.SetResourceReference(BackgroundProperty, show ? "Canvas" : "Selected"); FilesNavigation.SetResourceReference(ForegroundProperty, show ? "Text" : "NavText"); FilesNavLabel.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, show ? "Text" : "NavText");
        TasksNavigation.SetResourceReference(BackgroundProperty, show ? "Selected" : "Canvas"); TasksNavigation.SetResourceReference(ForegroundProperty, show ? "NavText" : "Text"); TasksNavLabel.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, show ? "NavText" : "Text");
    }
    private void OnTask(object sender, RoutedEventArgs e) => ShowTasks(true);
    private void OnReturnToFiles(object sender, RoutedEventArgs e) => ShowTasks(false);
    private void OnOpenImage(object sender, RoutedEventArgs e)
    {
        if (preview is not { IsImage: true, Thumbnail: { } photo }) return;
        new Window { Owner = this, Title = preview.Name + " · 示例图片", Width = 900, Height = 650, Background = (Brush)Application.Current.Resources["Canvas"], Content = new Image { Source = photo, Stretch = Stretch.Uniform, Margin = new Thickness(20) }, WindowStartupLocation = WindowStartupLocation.CenterOwner }.ShowDialog();
    }
    private void OnNotice(object sender, RoutedEventArgs e) => MessageBox.Show(this, $"{(sender as FrameworkElement)?.Tag ?? "任务页面"}将在方向确认后接入。\n当前是参考图复现原型；文件列表、预览与解压动画使用内存 ZIP 样本。", "nomozip · 参考图复现", MessageBoxButton.OK, MessageBoxImage.Information);
}
