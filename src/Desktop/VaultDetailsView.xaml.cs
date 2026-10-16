using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using ArchivePassword.Core;

namespace ArchivePassword.Desktop;
public partial class VaultDetailsView : UserControl
{
    private MainViewModel? model;
    private SavedPassword? original;
    public VaultDetailsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => { if (model is not null) model.PropertyChanged -= OnModelChanged; model = DataContext as MainViewModel; if (model is not null) model.PropertyChanged += OnModelChanged; LoadSelection(); };
        IsVisibleChanged += (_, _) => { if (IsVisible) LoadSelection(); else Clear(); };
        Unloaded += (_, _) => { Clear(); if (model is not null) model.PropertyChanged -= OnModelChanged; };
        Loaded += (_, _) => { if (model is not null) { model.PropertyChanged -= OnModelChanged; model.PropertyChanged += OnModelChanged; } LoadSelection(); };
        foreach (var control in new Control[] {NameBox,SecretBox,VisibleSecret,GroupBox,NotesBox,SourceBox,MatchBox})
            System.Windows.Automation.AutomationProperties.SetName(control, control.Name);
    }
    private void OnModelChanged(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName is nameof(MainViewModel.SelectedPassword) or nameof(MainViewModel.VaultLocked) or nameof(MainViewModel.Page)) LoadSelection(); }
    private void Clear() { original=null; SecretBox.Clear(); VisibleSecret.Clear(); ShowSecret.IsChecked=false; NameBox.Clear(); GroupBox.Clear(); NotesBox.Clear(); SourceBox.Clear(); MatchBox.Clear(); Heading.Text=UiText.Get("Redesign.VaultDetails");Error.Text=""; SaveButton.IsEnabled=false; }
    private void LoadSelection()
    {
        Clear();
        if (!IsVisible || model is not {Page:1,VaultLocked:false,SelectedPassword:{} selected}) return;
        original=selected; Heading.Text=selected.Name; NameBox.Text=selected.Name; SecretBox.Password=selected.Secret; GroupBox.Text=selected.Group; NotesBox.Text=selected.Notes; SourceBox.Text=selected.Source; MatchBox.Text=selected.MatchText; AutoTryBox.IsChecked=selected.AutoTry; SaveButton.IsEnabled=true;
    }
    private void OnVisibilityChanged(object sender, RoutedEventArgs e)
    {
        if (SecretBox is null || VisibleSecret is null) return;
        if (ShowSecret.IsChecked==true) { VisibleSecret.Text=SecretBox.Password; SecretBox.Visibility=Visibility.Collapsed; VisibleSecret.Visibility=Visibility.Visible; }
        else { SecretBox.Password=VisibleSecret.Text; VisibleSecret.Clear(); SecretBox.Visibility=Visibility.Visible; VisibleSecret.Visibility=Visibility.Collapsed; }
    }
    private async void OnSave(object sender,RoutedEventArgs e)
    {
        if (original is null || model is null) return;
        var secret=ShowSecret.IsChecked==true?VisibleSecret.Text:SecretBox.Password;
        if (secret.Length==0) { Error.Text=UiText.Get("T_c682f190cbd4"); return; }
        var edited=original with {Name=string.IsNullOrWhiteSpace(NameBox.Text)?UiText.Get("T_218e758854ee"):NameBox.Text.Trim(),Secret=secret,Group=GroupBox.Text.Trim(),Notes=NotesBox.Text.Trim(),Source=SourceBox.Text.Trim(),MatchText=MatchBox.Text.Trim(),AutoTry=AutoTryBox.IsChecked==true};
        await model.SaveVaultDetailsAsync(edited); LoadSelection();
    }
    private void OnCancel(object sender,RoutedEventArgs e)=>LoadSelection();
}
