using System.Windows;
using System.Windows.Controls;
using DAW.Audio;
using DAW.Audio.Effects;

namespace DAW.MVVM.Views;

/// <summary>
/// Template selector for effect parameter UI.
/// </summary>
public class EffectTemplateSelector : DataTemplateSelector
{
    public static EffectTemplateSelector Instance { get; } = new();
    
    public override DataTemplate? SelectTemplate(object? item, DependencyObject container)
    {
        if (item is null || container is not FrameworkElement element)
            return null;
        
        var templateKey = item switch
        {
            EqualizerEffect => "EQTemplate",
            CompressorEffect => "CompressorTemplate",
            ReverbEffect => "ReverbTemplate",
            DelayEffect => "DelayTemplate",
            GainEffect => "GainTemplate",
            _ => null
        };
        
        if (templateKey is null) return null;
        
        // Walk up the tree to find resources
        var current = element;
        while (current != null)
        {
            if (current.TryFindResource(templateKey) is DataTemplate template)
                return template;
            current = current.Parent as FrameworkElement;
        }
        
        return null;
    }
}

/// <summary>
/// Effect rack control for managing track effects.
/// </summary>
public partial class EffectRackControl : UserControl
{
    public EffectRackControl()
    {
        InitializeComponent();
    }
    
    private EffectChain? EffectChain => DataContext as EffectChain;
    
    private void AddEffect_Click(object sender, RoutedEventArgs e)
    {
        if (EffectChain is null) return;
        
        var selectedItem = EffectTypeCombo.SelectedItem as ComboBoxItem;
        if (selectedItem?.Tag is not string effectType) return;
        
        var effect = EffectFactory.Create(effectType);
        if (effect != null)
        {
            EffectChain.AddEffect(effect);
        }
    }
    
    private void RemoveEffect_Click(object sender, RoutedEventArgs e)
    {
        if (EffectChain is null) return;
        
        if (sender is Button button && button.Tag is AudioEffect effect)
        {
            EffectChain.RemoveEffect(effect);
        }
    }
}
