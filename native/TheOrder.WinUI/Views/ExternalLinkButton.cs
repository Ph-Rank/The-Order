using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using TheOrder.Application;

namespace TheOrder.WinUI.Views;

public sealed class ExternalLinkButton : HyperlinkButton
{
    public ExternalLinkButton()
    {
        Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["ExternalLinkButtonStyle"];
        AutomationProperties.SetHelpText(this, UiText.Get("在浏览器中打开"));
    }
}
