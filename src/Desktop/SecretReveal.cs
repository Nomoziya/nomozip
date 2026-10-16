using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Automation;

namespace ArchivePassword.Desktop;

// Visible copies exist only while the user explicitly enables the reveal control.
internal static class SecretReveal
{
    private static readonly DependencyProperty VisibleFieldProperty=DependencyProperty.RegisterAttached("VisibleField",typeof(TextBox),typeof(SecretReveal));
    internal static void Configure(Window owner,ToggleButton toggle,params (PasswordBox Masked,TextBox Visible)[] fields)
    {
        foreach(var (masked,visible) in fields)
        {
            masked.SetValue(VisibleFieldProperty,visible);
            AutomationProperties.SetName(visible,AutomationProperties.GetName(masked));
            visible.SetBinding(FieldState.HasErrorProperty,new Binding {Source=masked,Path=new PropertyPath("(0)",FieldState.HasErrorProperty)});
            visible.TextChanged+=(_,_)=>{if(toggle.IsChecked==true && visible.IsVisible && masked.Password!=visible.Text)masked.Password=visible.Text;};
            masked.PasswordChanged+=(_,_)=>{if(toggle.IsChecked==true && visible.IsVisible && visible.Text!=masked.Password)visible.Text=masked.Password;};
        }
        void Apply()
        {
            foreach(var (masked,visible) in fields)
            {
                if(toggle.IsChecked==true){visible.Text=masked.Password;masked.Visibility=Visibility.Collapsed;visible.Visibility=Visibility.Visible;}
                else{masked.Visibility=Visibility.Visible;visible.Visibility=Visibility.Collapsed;visible.Clear();}
            }
        }
        toggle.Checked+=(_,_)=>Apply();toggle.Unchecked+=(_,_)=>Apply();
        owner.Closed+=(_,_)=>{toggle.IsChecked=false;foreach(var (_,visible) in fields)visible.Clear();};
    }
    internal static void Focus(PasswordBox field)
    {if(field.GetValue(VisibleFieldProperty) is TextBox {IsVisible:true} visible)visible.Focus();else field.Focus();}
}
