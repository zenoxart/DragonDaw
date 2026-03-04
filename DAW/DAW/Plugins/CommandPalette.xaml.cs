using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using DAW.Models;

namespace DAW.Plugins;

/// <summary>
/// VS Code-style command palette for searching and opening plugins.
/// </summary>
public partial class CommandPalette : Window
{
    private readonly ObservableCollection<PluginDefinition> _results = [];
    private Track? _targetTrack;
    
    public PluginDefinition? SelectedPlugin { get; private set; }

    public CommandPalette(Track? targetTrack = null)
    {
        _targetTrack = targetTrack;
        
        // Add converter for usage count visibility
        Resources.Add("CountToVis", new CountToVisibilityConverter());
        
        InitializeComponent();
        
        ResultsList.ItemsSource = _results;
        
        Loaded += (s, e) =>
        {
            SearchBox.Focus();
            RefreshResults();
        };
        
        Deactivated += (s, e) => Close();
    }

    private void RefreshResults()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("RefreshResults called");
            _results.Clear();
            
            var query = SearchBox.Text;
            System.Diagnostics.Debug.WriteLine($"Search query: '{query}'");
            
            var results = PluginManager.Instance.Search(query);
            System.Diagnostics.Debug.WriteLine($"Found {results.Count()} results");
            
            foreach (var plugin in results)
            {
                _results.Add(plugin);
            }
            
            if (_results.Count > 0)
            {
                ResultsList.SelectedIndex = 0;
                System.Diagnostics.Debug.WriteLine($"Selected first result: {_results[0].Name}");
            }
            
            ResultCount.Text = $"{_results.Count} plugins";
            System.Diagnostics.Debug.WriteLine($"Results updated: {_results.Count} plugins");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Exception in RefreshResults: {ex}");
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshResults();
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"SearchBox key pressed: {e.Key}");
            
            switch (e.Key)
            {
                case Key.Down:
                    if (ResultsList.Items.Count > 0)
                    {
                        ResultsList.SelectedIndex = Math.Min(ResultsList.SelectedIndex + 1, ResultsList.Items.Count - 1);
                        ResultsList.ScrollIntoView(ResultsList.SelectedItem);
                    }
                    e.Handled = true;
                    break;
                    
                case Key.Up:
                    if (ResultsList.Items.Count > 0)
                    {
                        ResultsList.SelectedIndex = Math.Max(ResultsList.SelectedIndex - 1, 0);
                        ResultsList.ScrollIntoView(ResultsList.SelectedItem);
                    }
                    e.Handled = true;
                    break;
                    
                case Key.Enter:
                    System.Diagnostics.Debug.WriteLine("Enter key pressed, calling OpenSelectedPlugin");
                    OpenSelectedPlugin();
                    e.Handled = true;
                    break;
                    
                case Key.Escape:
                    System.Diagnostics.Debug.WriteLine("Escape key pressed, closing dialog");
                    DialogResult = false;
                    Close();
                    e.Handled = true;
                    break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Exception in SearchBox_PreviewKeyDown: {ex}");
        }
    }

    private void ResultsList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OpenSelectedPlugin();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        OpenSelectedPlugin();
    }

    private void OpenSelectedPlugin()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("OpenSelectedPlugin called");
            
            if (ResultsList.SelectedItem is PluginDefinition plugin)
            {
                System.Diagnostics.Debug.WriteLine($"Selected plugin: {plugin.Name}");
                SelectedPlugin = plugin;
                DialogResult = true;
                System.Diagnostics.Debug.WriteLine("DialogResult set to true, closing dialog");
                Close();
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("No plugin selected in ResultsList");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Exception in OpenSelectedPlugin: {ex}");
            DialogResult = false;
            Close();
        }
    }

    /// <summary>
    /// Shows the command palette and returns the selected plugin (if any).
    /// </summary>
    public static PluginDefinition? Show(Window owner, Track? targetTrack = null)
    {
        var palette = new CommandPalette(targetTrack)
        {
            Owner = owner
        };
        palette.ShowDialog();
        return palette.SelectedPlugin;
    }
}

/// <summary>
/// Converts usage count to visibility (hidden if 0).
/// </summary>
public class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int count && count > 0)
            return Visibility.Visible;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
