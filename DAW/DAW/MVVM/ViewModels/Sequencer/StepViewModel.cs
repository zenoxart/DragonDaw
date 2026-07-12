using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DAW.Commands;
using DAW.MVVM.Models.Sequencer;

namespace DAW.MVVM.ViewModels.Sequencer;

/// <summary>
/// ViewModel for a single step in the channel rack.
/// Thin wrapper that raises rendering-related notifications.
/// </summary>
public class StepViewModel : INotifyPropertyChanged
{
    public StepModel Model { get; }

    public StepViewModel(StepModel model)
    {
        Model = model;
        model.PropertyChanged += (_, e) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(e.PropertyName));
    }

    public bool IsActive
    {
        get => Model.IsActive;
        set => Model.IsActive = value;
    }

    public float Velocity
    {
        get => Model.Velocity;
        set => Model.Velocity = value;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
