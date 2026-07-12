using DAW.MVVM.ViewModels.PianoRoll;
using System.Windows.Controls;

namespace DAW.MVVM.Views.PianoRoll;

public partial class PianoRollView : UserControl
{
    public PianoRollView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Synchronises the piano keyboard scroll offset with the note grid scroll.
    /// The KeyboardScroller mirrors the vertical offset of the NoteScroller so
    /// the key labels stay perfectly aligned with the note rows at all times.
    /// </summary>
    private void NoteScroller_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        var vm = ((DataContext as PianoRollViewModel));
        if (vm != null)
        {
            vm.ScrollX = e.HorizontalOffset;
            vm.ScrollY = e.VerticalOffset;
        }

        // Mirror the vertical scroll position onto the keyboard panel.
        // This is the critical fix: the keyboard lives in its own ScrollViewer
        // and must be scrolled in sync with the note canvas ScrollViewer.
        if (Math.Abs(e.VerticalChange) > 0.001)
        {
            KeyboardScroller.ScrollToVerticalOffset(e.VerticalOffset);
        }
    }
}
