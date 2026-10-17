using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using ArchivePassword.Core;

namespace ArchivePassword.Desktop;

internal static class ExtractionOptionsVisuals
{
    internal static FrameworkElement BatchList(MainViewModel model,out ListCollectionView view)
    {
        view=new ListCollectionView(model.Tasks) {Filter=value=>!((ArchiveTaskRow)value).Completed};
        var grid=new Grid {Margin=new Thickness(0,0,0,16)};
        grid.RowDefinitions.Add(new RowDefinition());grid.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
        var table=new DataGrid {Name="BatchArchiveTable",ItemsSource=view,IsSynchronizedWithCurrentItem=false,AutoGenerateColumns=false,IsReadOnly=true,RowHeight=36,Height=Math.Clamp(view.Count*36+2,74,146),HeadersVisibility=DataGridHeadersVisibility.None,CanUserAddRows=false,BorderThickness=new Thickness(0)};
        System.Windows.Automation.AutomationProperties.SetName(table,UiText.Get("Extraction.BatchTitle"));
        var template=(DataTemplate)XamlReader.Parse("""
          <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:local="clr-namespace:ArchivePassword.Desktop;assembly=ArchivePassword">
            <DockPanel Margin="12,0,8,0">
              <Button DockPanel.Dock="Right" Tag="RemoveBatchArchive" Style="{DynamicResource SubtleButton}" Width="28" Height="28" MinHeight="28" Padding="6" Margin="8,0,0,0" FocusVisualStyle="{DynamicResource FocusStyle}">
                <Path Data="M1,1 L11,11 M11,1 L1,11" Stroke="{DynamicResource SecondaryBrush}" StrokeThickness="1.2" Width="12" Height="12"/>
              </Button>
              <local:FileKindIcon DockPanel.Dock="Left" FileName="{Binding Path}" Width="18" Height="24" Margin="0,0,12,0"/>
              <TextBlock Text="{Binding Name}" TextTrimming="CharacterEllipsis" VerticalAlignment="Center"/>
            </DockPanel>
          </DataTemplate>
          """);
        table.Columns.Add(new DataGridTemplateColumn {CellTemplate=template,Width=new DataGridLength(1,DataGridLengthUnitType.Star)});
        table.SelectionChanged+=(_,_)=>{if(table.SelectedItem is ArchiveTaskRow row)model.SelectedTask=row;};
        table.InputBindings.Add(new KeyBinding(model.RemoveFileCommand,Key.Delete,ModifierKeys.None));
        table.AddHandler(Button.ClickEvent,new RoutedEventHandler((sender,args)=>
        {
            if(args.OriginalSource is not Button {Tag:"RemoveBatchArchive",DataContext:ArchiveTaskRow row})return;
            model.SelectedTask=row;if(model.RemoveFileCommand.CanExecute(null))model.RemoveFileCommand.Execute(null);args.Handled=true;
        }));
        table.LoadingRow+=(_,args)=>
        {
            args.Row.Dispatcher.BeginInvoke(()=>
            {
                foreach(var button in DesktopVisualTree.Buttons(args.Row))if(button.Tag as string=="RemoveBatchArchive")System.Windows.Automation.AutomationProperties.SetName(button,UiText.Get("Extraction.RemoveArchive"));
            });
        };
        var frame=new Border {Child=table,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(6)};frame.SetResourceReference(Border.BorderBrushProperty,"DividerBrush");grid.Children.Add(frame);
        var tools=new DockPanel {Margin=new Thickness(0,12,0,0)};Grid.SetRow(tools,1);grid.Children.Add(tools);
        var count=new TextBlock {VerticalAlignment=VerticalAlignment.Center,Style=(Style)Application.Current.FindResource("Secondary")};DockPanel.SetDock(count,Dock.Right);tools.Children.Add(count);
        var actions=new WrapPanel();tools.Children.Add(actions);
        var add=new Button {Command=model.AddFilesCommand,Style=(Style)Application.Current.FindResource("PrimaryButton"),Margin=new Thickness(0,0,8,0)};
        var addContent=new StackPanel {Orientation=Orientation.Horizontal};
        var plus=new System.Windows.Shapes.Path {Data=System.Windows.Media.Geometry.Parse("M1,7 L13,7 M7,1 L7,13"),Width=14,Height=14,StrokeThickness=1.3,Margin=new Thickness(0,0,8,0),VerticalAlignment=VerticalAlignment.Center};plus.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty,"OnAccentBrush");addContent.Children.Add(plus);addContent.Children.Add(new TextBlock {Text=UiText.Get("Extraction.AddFiles")});add.Content=addContent;actions.Children.Add(add);
        var remove=new Button {Content=UiText.Get("T_a194128b2939"),Command=model.RemoveFileCommand};actions.Children.Add(remove);
        remove.SetBinding(UIElement.IsEnabledProperty,new Binding("SelectedItem") {Source=table,Converter=new NonNullBooleanConverter()});
        var current=view;
        void Update() {count.Text=UiText.Format("Extraction.BatchCount",current.Count);table.Height=Math.Clamp(current.Count*36+2,74,146);}
        ((INotifyCollectionChanged)view).CollectionChanged+=(_,_)=>Update();Update();
        return grid;
    }

    internal static (FrameworkElement View,Action Dispose) OutputPreview(MainViewModel model,bool batch)
    {
        var content=new StackPanel();if(batch)content.Children.Add(new TextBlock {Text=UiText.Get("Extraction.PreviewTitle"),Margin=new Thickness(0,0,0,6),FontSize=13});
        var paths=new StackPanel();content.Children.Add(new ScrollViewer {Content=paths,MaxHeight=100,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
        var hint=new TextBlock {Style=(Style)Application.Current.FindResource("Secondary"),Margin=new Thickness(0,6,0,0)};content.Children.Add(hint);
        var border=new Border {Child=content,Padding=new Thickness(batch?10:0),CornerRadius=new CornerRadius(4),BorderThickness=new Thickness(batch?1:0),ToolTip=UiText.Get("Extraction.PreviewEstimated")};
        border.SetResourceReference(Border.BackgroundProperty,batch?"CanvasBrush":"SurfaceBrush");border.SetResourceReference(Border.BorderBrushProperty,"DividerBrush");
        void Update()
        {
            paths.Children.Clear();var pending=false;var invalid=false;
            foreach(var row in batch?model.Tasks.Where(value=>!value.Completed):model.SelectedTask is {} selected?new[]{selected}:Array.Empty<ArchiveTaskRow>())
            {
                string path;
                try
                {
                    path=row.Checkpoint?.Destination ?? row.ShellOutputPath ?? ExtractionDirectoryPlanner.BaseDirectory(row.Path,model.OutputPath,model.DirectoryRules.Location);
                    if(row.Checkpoint is null && row.ShellOutputPath is null)
                    {
                        if(model.DirectoryRules.Layout==ExtractionLayout.Smart)pending=true;
                        else if(model.DirectoryRules.Layout==ExtractionLayout.ArchiveFolder)path=ExtractionDirectoryPlanner.Resolve(row.Path,path,model.DirectoryRules,[],DateOnly.FromDateTime(DateTime.Today));
                    }
                }
                catch(Exception exception) when(exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException) {invalid=true;path=UiText.Get("Settings.InvalidOutput");}
                var item=new DockPanel {Margin=new Thickness(0,0,0,2)};if(batch){var icon=new FileKindIcon {Width=16,Height=16,IsDirectory=true,Margin=new Thickness(0,0,8,0)};DockPanel.SetDock(icon,Dock.Left);item.Children.Add(icon);}item.Children.Add(new TextBlock {Text=batch?path:UiText.Format("Extraction.EstimatedPath",path),ToolTip=path,TextWrapping=TextWrapping.NoWrap,TextTrimming=TextTrimming.CharacterEllipsis,Style=(Style)Application.Current.FindResource("Secondary")});paths.Children.Add(item);
            }
            hint.Text=UiText.Get(invalid?"Settings.InvalidOutput":pending?"Extraction.PreviewPending":"Extraction.PreviewEstimated");
            hint.Visibility=invalid || pending?Visibility.Visible:Visibility.Collapsed;
        }
        PropertyChangedEventHandler changed=(_,_)=>Update();NotifyCollectionChangedEventHandler collection=(_,_)=>Update();
        var attached=false;var disposed=false;
        void Detach() {if(!attached)return;model.PropertyChanged-=changed;model.Tasks.CollectionChanged-=collection;attached=false;}
        border.Loaded+=(_,_)=>{if(attached || disposed)return;model.PropertyChanged+=changed;model.Tasks.CollectionChanged+=collection;attached=true;Update();};
        border.Unloaded+=(_,_)=>Detach();
        Update();return (border,()=>{disposed=true;Detach();});
    }

    private sealed class NonNullBooleanConverter:IValueConverter
    {
        public object Convert(object value,Type targetType,object parameter,System.Globalization.CultureInfo culture)=>value is not null;
        public object ConvertBack(object value,Type targetType,object parameter,System.Globalization.CultureInfo culture)=>throw new NotSupportedException();
    }
}

internal static class DesktopVisualTree
{
    internal static IEnumerable<Button> Buttons(DependencyObject root)
    {
        for(var index=0;index<System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);index++)
        {var child=System.Windows.Media.VisualTreeHelper.GetChild(root,index);if(child is Button button)yield return button;foreach(var nested in Buttons(child))yield return nested;}
    }
}
