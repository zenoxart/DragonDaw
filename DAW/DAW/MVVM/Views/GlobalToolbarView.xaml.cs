using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DAW.MVVM.ViewModels;

namespace DAW.MVVM.Views;

/// <summary>
/// Interaction logic for GlobalToolbarView.xaml
/// </summary>
public partial class GlobalToolbarView : UserControl
{
    public GlobalToolbarView()
    {
        InitializeComponent();
    }

    private GlobalToolbarViewModel? Vm => DataContext as GlobalToolbarViewModel;

    // ── BPM mouse-wheel: scroll anywhere on the BPM panel ────────────────────

    private void BpmControl_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var vm = Vm; if (vm == null) return;
        bool large = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        double delta = e.Delta > 0 ? 1 : -1;
        if (large) delta *= 5;
        vm.BPM = vm.BPM + delta;
        e.Handled = true;
    }

    // ── BPM text box: Up/Down arrows, Enter, Escape ───────────────────────────

    private double _bpmBeforeEdit;

    private void BpmTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb)
        {
            _bpmBeforeEdit = Vm?.BPM ?? 0;
            tb.Dispatcher.InvokeAsync(tb.SelectAll);
        }
    }

    private void BpmTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var vm = Vm; if (vm == null) return;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        switch (e.Key)
        {
            case Key.Up:
                vm.BPM = vm.BPM + (shift ? 5 : 1);
                e.Handled = true;
                break;

            case Key.Down:
                vm.BPM = vm.BPM - (shift ? 5 : 1);
                e.Handled = true;
                break;

            case Key.Enter:
                // Commit: parse and apply
                if (sender is TextBox tb && double.TryParse(
                        tb.Text.Replace(',', '.'),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double parsed))
                {
                    vm.BPM = parsed;
                }
                // Move focus away to trigger the binding
                Keyboard.ClearFocus();
                e.Handled = true;
                break;

            case Key.Escape:
                // Cancel: restore previous value
                if (sender is TextBox tbEsc)
                    tbEsc.Text = $"{_bpmBeforeEdit:F1}";
                Keyboard.ClearFocus();
                e.Handled = true;
                break;
        }
    }

    private void BpmTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        // Validate on focus loss — reject out-of-range values
        var vm = Vm; if (vm == null) return;
        if (sender is TextBox tb && double.TryParse(
                tb.Text.Replace(',', '.'),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out double parsed))
        {
            vm.BPM = parsed;
        }
        else if (sender is TextBox tbFail)
        {
            tbFail.Text = $"{vm.BPM:F1}"; // revert to last valid value
        }
    }
}
