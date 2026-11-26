using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace TheOrder.WinUI;

public sealed partial class MainWindow
{
    // Explicit Test fixture only. Reads the live provider, not XAML source labels.
    // Does not claim physical keyboard input or screen-reader speech coverage.
    private object AccessibilityEvidence()
    {
        var rows = new List<object>();
        var pending = new Stack<DependencyObject>(); pending.Push(Root);
        int visited = 0;
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            if (++visited > 8192) throw new InvalidDataException("Accessibility fixture tree bound.");
            if (node is FrameworkElement element && element.Visibility == Visibility.Visible &&
                element is ButtonBase or TextBox or ToggleSwitch or ListViewItem or NavigationViewItem or AutoSuggestBox)
            {
                var peer = FrameworkElementAutomationPeer.CreatePeerForElement(element);
                if (peer is not null)
                {
                    var rect = peer.GetBoundingRectangle();
                    rows.Add(new { id = AutomationProperties.GetAutomationId(element), name = peer.GetName(), role = peer.GetAutomationControlType().ToString(),
                        enabled = peer.IsEnabled(), keyboardFocusable = peer.IsKeyboardFocusable(), offscreen = peer.IsOffscreen(),
                        width = rect.Width, height = rect.Height,
                        invoke = peer.GetPattern(PatternInterface.Invoke) is not null,
                        selectionItem = peer.GetPattern(PatternInterface.SelectionItem) is not null,
                        toggle = peer.GetPattern(PatternInterface.Toggle) is not null });
                }
            }
            for (int i = VisualTreeHelper.GetChildrenCount(node) - 1; i >= 0; i--) pending.Push(VisualTreeHelper.GetChild(node, i));
        }
        return new { source = "Live XAML automation peers; no injected accessibility state", visited, controls = rows,
            page = ViewModel.Page.ToString(), theme = Root.ActualTheme.ToString(), dpi = Root.XamlRoot.RasterizationScale,
            material = materialPolicy?.Evidence() };
    }
}
