using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
namespace TheOrder.WinUI.Views;
// Preserve the native template and automation. Animate neighbouring sections
// through the compositor, without measuring and animating layout on every frame.
public sealed class SmoothExpander : Expander
{
    private bool? motion;
    private bool secondaryStyled;
    internal bool ContentShown => GetTemplateChild("ExpanderContent") is FrameworkElement { Visibility: Visibility.Visible };
    public SmoothExpander()
    {
        DefaultStyleKey = typeof(Expander);
        Loaded += (_, _) => {
            ConfigureMotion();
            if (!secondaryStyled && Header is string title && title is ("资料语言与来源" or "资料与图片来源" or "资料来源" or "资料来源与对应关系" or "关联来源与关系说明" or "详细证据" or "对应证据" or "书目来源与原始字段" or "文件位置"))
            { secondaryStyled = true; SecondaryDisclosure.Apply(this); }
        };
        Expanding += (_, _) => ConfigurePeers();
        Collapsed += (_, _) => ConfigurePeers();
    }
    private void ConfigureMotion()
    {
        var enabled = InteractionMotion.Enabled; if (motion == enabled) return; motion = enabled;
        Transitions = enabled ? new TransitionCollection { new RepositionThemeTransition { IsStaggeringEnabled = false } } : null;
    }
    private void ConfigurePeers()
    {
        ConfigureMotion();
        if (Parent is Panel panel)
            foreach (var sibling in panel.Children.OfType<SmoothExpander>()) sibling.ConfigureMotion();
    }
}
