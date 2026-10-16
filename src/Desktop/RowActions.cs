using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace ArchivePassword.Desktop;

/// <summary>
/// Shared right-click behavior for the list pages. Right-clicking a row selects it first, so every menu command
/// acts on what the pointer is over; only commands that can run are shown, which keeps menus short instead of
/// listing disabled rows. Double-click/Enter and Delete are keyboard-reachable equivalents of the menu's main items.
/// </summary>
public static class RowActions
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(RowActions), new PropertyMetadata(false, OnEnabledChanged));
    public static readonly DependencyProperty OpenCommandProperty = DependencyProperty.RegisterAttached("OpenCommand", typeof(ICommand), typeof(RowActions));
    public static readonly DependencyProperty DeleteCommandProperty = DependencyProperty.RegisterAttached("DeleteCommand", typeof(ICommand), typeof(RowActions));
    public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);
    public static ICommand? GetOpenCommand(DependencyObject element) => (ICommand?)element.GetValue(OpenCommandProperty);
    public static void SetOpenCommand(DependencyObject element, ICommand? value) => element.SetValue(OpenCommandProperty, value);
    public static ICommand? GetDeleteCommand(DependencyObject element) => (ICommand?)element.GetValue(DeleteCommandProperty);
    public static void SetDeleteCommand(DependencyObject element, ICommand? value) => element.SetValue(DeleteCommandProperty, value);

    private static void OnEnabledChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args)
    {
        if (owner is not DataGrid grid) return;
        grid.PreviewMouseRightButtonDown -= OnRightButtonDown;
        grid.ContextMenuOpening -= OnContextMenuOpening;
        grid.MouseDoubleClick -= OnDoubleClick;
        grid.PreviewKeyDown -= OnKeyDown;
        if (args.NewValue is not true) return;
        grid.PreviewMouseRightButtonDown += OnRightButtonDown;
        grid.ContextMenuOpening += OnContextMenuOpening;
        grid.MouseDoubleClick += OnDoubleClick;
        grid.PreviewKeyDown += OnKeyDown;
    }

    private static T? Ancestor<T>(DependencyObject? node) where T : DependencyObject
    {
        for (; node is not null; node = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
            if (node is T match) return match;
        return null;
    }

    private static void OnRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid || Ancestor<DataGridColumnHeader>(e.OriginalSource as DependencyObject) is not null) return;
        var row = Ancestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row is null) grid.SelectedItem = null;
        else if (!row.IsSelected) grid.SelectedItem = row.Item;
        grid.Focus();
    }

    private static void OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        if (Ancestor<DataGridColumnHeader>(e.OriginalSource as DependencyObject) is not null) { e.Handled = true; return; }
        if (grid.ContextMenu is not { } menu) return;
        menu.Opened -= OnMenuOpened;
        menu.Opened += OnMenuOpened;
    }

    private static void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid || e.ChangedButton != MouseButton.Left || Ancestor<DataGridRow>(e.OriginalSource as DependencyObject) is null) return;
        if (Run(GetOpenCommand(grid))) e.Handled = true;
    }

    private static void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not DataGrid grid || Keyboard.Modifiers != ModifierKeys.None) return;
        var command = e.Key switch { Key.Enter => GetOpenCommand(grid), Key.Delete => GetDeleteCommand(grid), _ => null };
        if (command is not null && grid.SelectedItem is not null && Run(command)) e.Handled = true;
    }

    private static bool Run(ICommand? command)
    {
        if (command is null || !command.CanExecute(null)) return false;
        command.Execute(null);
        return true;
    }

    private static bool Available(MenuItem item) => item.Command is null ? item.IsEnabled : item.Command.CanExecute(item.CommandParameter);

    /// <summary>Hides commands that cannot run now and any separator that no longer divides two visible groups.</summary>
    internal static void Tidy(ContextMenu menu)
    {
        CommandManager.InvalidateRequerySuggested();
        var pending = default(Separator);
        var anyVisible = false;
        foreach (var item in menu.Items)
        {
            if (item is MenuItem command)
            {
                var visible = Available(command);
                command.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                if (!visible) continue;
                if (pending is not null) pending.Visibility = Visibility.Visible;
                pending = null; anyVisible = true;
            }
            else if (item is Separator separator)
            {
                separator.Visibility = Visibility.Collapsed;
                if (anyVisible) pending = separator;
            }
        }
        menu.UpdateLayout();
    }

    private static void OnMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu) return;
        Tidy(menu);
        // Nothing applies to the pointer position (for example, a disabled page): do not leave an empty popup.
        if (!menu.Items.OfType<MenuItem>().Any(item => item.Visibility == Visibility.Visible)) menu.IsOpen = false;
    }
}
