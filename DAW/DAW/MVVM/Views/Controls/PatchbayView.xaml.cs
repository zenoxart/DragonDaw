using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using DAW.MVVM.Models.Mixer;

namespace DAW.MVVM.Views.Controls;

/// <summary>
/// Hardware-inspired patchbay view for visual audio routing.
/// </summary>
public partial class PatchbayView : UserControl
{
    public static readonly DependencyProperty ChannelsProperty =
        DependencyProperty.Register(nameof(Channels), typeof(IEnumerable<MixerChannel>),
            typeof(PatchbayView),
            new PropertyMetadata(null, OnChannelsChanged));

    private const double ChannelSpacing = 100.0;
    private const double SocketSize = 20.0;

    private Point? _dragStartPoint;
    private MixerChannel? _dragSourceChannel;
    private Path? _dragCablePath;
    private Canvas? _cableCanvas;
    private StackPanel? _patchbayContent;
    private readonly Dictionary<string, Path> _cables = new();

    public IEnumerable<MixerChannel>? Channels
    {
        get => (IEnumerable<MixerChannel>?)GetValue(ChannelsProperty);
        set => SetValue(ChannelsProperty, value);
    }

    public PatchbayView()
    {
        InitializeComponent();
        Loaded += PatchbayView_Loaded;
    }

    private void PatchbayView_Loaded(object sender, RoutedEventArgs e)
    {
        _patchbayContent = FindName("PatchbayContent") as StackPanel;
        RebuildPatchbay();
    }

    private static void OnChannelsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PatchbayView view && view.IsLoaded)
        {
            view.RebuildPatchbay();
        }
    }

    private void RebuildPatchbay()
    {
        if (_patchbayContent == null) return;

        _patchbayContent.Children.Clear();
        _cables.Clear();

        if (Channels == null || !Channels.Any())
        {
            // Show empty state
            var emptyText = new TextBlock
            {
                Text = "Keine Channels verfügbar\nFügen Sie Tracks zum Projekt hinzu",
                Foreground = (Brush)FindResource("TextDim"),
                FontSize = 12,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 50, 0, 0)
            };
            _patchbayContent.Children.Add(emptyText);
            return;
        }

        var channelList = Channels.ToList();

        // Create main grid
        var mainGrid = new Grid();
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Headers
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(80) }); // Output sockets
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(120) }); // Cable area
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(80) }); // Input sockets

        // Create horizontal panel for channels
        var headersPanel = new StackPanel { Orientation = Orientation.Horizontal };
        var outputsPanel = new StackPanel { Orientation = Orientation.Horizontal };
        var inputsPanel = new StackPanel { Orientation = Orientation.Horizontal };

        // Create cable canvas
        _cableCanvas = new Canvas
        {
            Background = Brushes.Transparent,
            Height = 120
        };
        _cableCanvas.MouseMove += CableCanvas_MouseMove;
        _cableCanvas.MouseLeftButtonUp += CableCanvas_MouseLeftButtonUp;

        for (int i = 0; i < channelList.Count; i++)
        {
            var channel = channelList[i];

            // Add header
            headersPanel.Children.Add(CreateChannelHeader(channel, i));

            // Add output socket
            outputsPanel.Children.Add(CreateOutputSocket(channel, i));

            // Add input socket
            inputsPanel.Children.Add(CreateInputSocket(channel, i));
        }

        Grid.SetRow(headersPanel, 0);
        Grid.SetRow(outputsPanel, 1);
        Grid.SetRow(_cableCanvas, 2);
        Grid.SetRow(inputsPanel, 3);

        mainGrid.Children.Add(headersPanel);
        mainGrid.Children.Add(outputsPanel);
        mainGrid.Children.Add(_cableCanvas);
        mainGrid.Children.Add(inputsPanel);

        _patchbayContent.Children.Add(mainGrid);

        // Draw existing connections after layout is created
        Dispatcher.InvokeAsync(() => DrawAllCables(), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private FrameworkElement CreateChannelHeader(MixerChannel channel, int index)
    {
        var border = new Border
        {
            Width = ChannelSpacing,
            Padding = new Thickness(4),
            BorderThickness = new Thickness(0, 0, 1, 0),
            BorderBrush = (Brush)FindResource("BorderBrush")
        };

        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };

        var numberText = new TextBlock
        {
            Text = channel.ChannelNumber.ToString(),
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("TextPrimary"),
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var nameText = new TextBlock
        {
            Text = channel.Name,
            FontSize = 9,
            Foreground = (Brush)FindResource("TextSecondary"),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = ChannelSpacing - 8
        };

        var colorBar = new Rectangle
        {
            Height = 3,
            Fill = new SolidColorBrush(channel.Color),
            Margin = new Thickness(0, 4, 0, 0)
        };

        stack.Children.Add(numberText);
        stack.Children.Add(nameText);
        stack.Children.Add(colorBar);
        border.Child = stack;

        return border;
    }

    private FrameworkElement CreateOutputSocket(MixerChannel channel, int index)
    {
        var container = new Border
        {
            Width = ChannelSpacing,
            Height = 80,
            BorderThickness = new Thickness(0, 0, 1, 0),
            BorderBrush = (Brush)FindResource("BorderBrush")
        };

        var stack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var label = new TextBlock
        {
            Text = "OUT",
            FontSize = 8,
            Foreground = (Brush)FindResource("TextDim"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8)
        };

        var socket = new Ellipse
        {
            Width = SocketSize,
            Height = SocketSize,
            Fill = (Brush)FindResource("ControlBg"),
            Stroke = (Brush)FindResource("BorderBrush"),
            StrokeThickness = 2,
            Cursor = Cursors.Hand,
            Tag = new SocketData { Channel = channel, IsOutput = true, Index = index }
        };

        socket.MouseLeftButtonDown += OutputSocket_MouseLeftButtonDown;
        socket.MouseEnter += Socket_MouseEnter;
        socket.MouseLeave += Socket_MouseLeave;

        stack.Children.Add(label);
        stack.Children.Add(socket);
        container.Child = stack;

        return container;
    }

    private FrameworkElement CreateInputSocket(MixerChannel channel, int index)
    {
        var container = new Border
        {
            Width = ChannelSpacing,
            Height = 80,
            BorderThickness = new Thickness(0, 0, 1, 0),
            BorderBrush = (Brush)FindResource("BorderBrush")
        };

        var stack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var socket = new Ellipse
        {
            Width = SocketSize,
            Height = SocketSize,
            Fill = (Brush)FindResource("ControlBg"),
            Stroke = (Brush)FindResource("BorderBrush"),
            StrokeThickness = 2,
            Cursor = Cursors.Hand,
            Tag = new SocketData { Channel = channel, IsOutput = false, Index = index }
        };

        socket.MouseLeftButtonDown += InputSocket_MouseLeftButtonDown;
        socket.MouseEnter += Socket_MouseEnter;
        socket.MouseLeave += Socket_MouseLeave;

        var label = new TextBlock
        {
            Text = "IN",
            FontSize = 8,
            Foreground = (Brush)FindResource("TextDim"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0)
        };

        stack.Children.Add(socket);
        stack.Children.Add(label);
        container.Child = stack;

        return container;
    }

    private void OutputSocket_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Ellipse socket && socket.Tag is SocketData data && _cableCanvas != null)
        {
            _dragSourceChannel = data.Channel;
            _dragStartPoint = socket.TranslatePoint(new Point(SocketSize / 2, SocketSize / 2), _cableCanvas);

            // Create drag cable
            _dragCablePath = CreateCablePath(_dragStartPoint.Value, _dragStartPoint.Value, Colors.Gray, true);
            _cableCanvas.Children.Add(_dragCablePath);

            _cableCanvas.CaptureMouse();
            e.Handled = true;
        }
    }

    private void InputSocket_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Ellipse socket && socket.Tag is SocketData data && Channels != null)
        {
            // Check if clicking an input to remove existing connections
            var channelList = Channels.ToList();
            foreach (var sourceChannel in channelList)
            {
                if (sourceChannel.SendTargets.Contains(data.Channel.ChannelNumber))
                {
                    // Remove connection
                    sourceChannel.RemoveSend(data.Channel.ChannelNumber);
                    DrawAllCables();
                    e.Handled = true;
                    return;
                }
            }
        }
    }

    private void CableCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragCablePath != null && _dragStartPoint.HasValue && _cableCanvas != null)
        {
            var currentPoint = e.GetPosition(_cableCanvas);
            UpdateCablePath(_dragCablePath, _dragStartPoint.Value, currentPoint);
        }
    }

    private void CableCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragCablePath != null && _cableCanvas != null)
        {
            _cableCanvas.Children.Remove(_dragCablePath);
            _dragCablePath = null;
        }

        if (_dragSourceChannel != null && _dragStartPoint.HasValue && _cableCanvas != null)
        {
            // Find if we dropped on an input socket
            var dropPoint = e.GetPosition(_cableCanvas);
            var targetSocket = FindInputSocketAt(dropPoint);

            if (targetSocket != null && targetSocket.Tag is SocketData targetData)
            {
                // Create connection
                _dragSourceChannel.AddSend(targetData.Channel.ChannelNumber);
                DrawAllCables();
            }
        }

        _dragSourceChannel = null;
        _dragStartPoint = null;
        _cableCanvas?.ReleaseMouseCapture();
    }

    private Ellipse? FindInputSocketAt(Point point)
    {
        if (_patchbayContent == null) return null;

        // Find all input sockets
        var inputSockets = new List<Ellipse>();
        FindInputSocketsRecursive(_patchbayContent, inputSockets);

        foreach (var socket in inputSockets)
        {
            if (_cableCanvas == null) continue;

            try
            {
                var socketPoint = socket.TranslatePoint(new Point(0, 0), _cableCanvas);
                var socketBounds = new Rect(socketPoint, new Size(SocketSize, SocketSize));

                if (socketBounds.Contains(point))
                    return socket;
            }
            catch
            {
                // Ignore transform exceptions
            }
        }
        return null;
    }

    private void FindInputSocketsRecursive(DependencyObject parent, List<Ellipse> sockets)
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);

            if (child is Ellipse ellipse && ellipse.Tag is SocketData data && !data.IsOutput)
            {
                sockets.Add(ellipse);
            }

            FindInputSocketsRecursive(child, sockets);
        }
    }

    private void DrawAllCables()
    {
        if (_cableCanvas == null) return;

        _cableCanvas.Children.Clear();
        _cables.Clear();

        if (Channels == null) return;

        var channelList = Channels.ToList();

        foreach (var sourceChannel in channelList)
        {
            var sourceIndex = channelList.IndexOf(sourceChannel);

            foreach (var targetChannelNum in sourceChannel.SendTargets)
            {
                var targetChannel = channelList.FirstOrDefault(c => c.ChannelNumber == targetChannelNum);
                if (targetChannel == null) continue;

                var targetIndex = channelList.IndexOf(targetChannel);
                DrawCable(sourceIndex, targetIndex, sourceChannel.Color);
            }
        }
    }

    private void DrawCable(int sourceIndex, int targetIndex, Color color)
    {
        if (_cableCanvas == null) return;

        // Calculate socket positions
        double sourceX = sourceIndex * ChannelSpacing + ChannelSpacing / 2;
        double targetX = targetIndex * ChannelSpacing + ChannelSpacing / 2;
        double sourceY = 40; // Approximate output socket Y
        double targetY = 80; // Approximate input socket Y (relative to canvas which starts at row 2)

        var startPoint = new Point(sourceX, sourceY);
        var endPoint = new Point(targetX, targetY);

        var cable = CreateCablePath(startPoint, endPoint, color, false);
        cable.MouseLeftButtonDown += Cable_MouseLeftButtonDown;
        cable.Tag = new CableData { SourceIndex = sourceIndex, TargetIndex = targetIndex };

        _cableCanvas.Children.Add(cable);
        _cables[$"{sourceIndex}-{targetIndex}"] = cable;
    }

    private Path CreateCablePath(Point start, Point end, Color color, bool isDragging)
    {
        var geometry = new PathGeometry();
        var figure = new PathFigure { StartPoint = start };

        // Create smooth curve
        double controlOffset = Math.Abs(end.Y - start.Y) * 0.5;
        var control1 = new Point(start.X, start.Y + controlOffset);
        var control2 = new Point(end.X, end.Y - controlOffset);

        figure.Segments.Add(new BezierSegment(control1, control2, end, true));
        geometry.Figures.Add(figure);

        var path = new Path
        {
            Data = geometry,
            Stroke = new SolidColorBrush(isDragging ? Color.FromArgb(128, color.R, color.G, color.B) : color),
            StrokeThickness = isDragging ? 2 : 3,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Cursor = isDragging ? Cursors.Arrow : Cursors.Hand
        };

        if (!isDragging)
        {
            // Add glow effect
            path.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = color,
                BlurRadius = 8,
                ShadowDepth = 0,
                Opacity = 0.6
            };
        }

        return path;
    }

    private void UpdateCablePath(Path path, Point start, Point end)
    {
        var geometry = new PathGeometry();
        var figure = new PathFigure { StartPoint = start };

        double controlOffset = Math.Abs(end.Y - start.Y) * 0.5;
        var control1 = new Point(start.X, start.Y + controlOffset);
        var control2 = new Point(end.X, end.Y - controlOffset);

        figure.Segments.Add(new BezierSegment(control1, control2, end, true));
        geometry.Figures.Add(figure);

        path.Data = geometry;
    }

    private void Cable_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Path cable && cable.Tag is CableData data && Channels != null)
        {
            var channelList = Channels.ToList();
            if (data.SourceIndex >= 0 && data.SourceIndex < channelList.Count &&
                data.TargetIndex >= 0 && data.TargetIndex < channelList.Count)
            {
                var sourceChannel = channelList[data.SourceIndex];
                var targetChannel = channelList[data.TargetIndex];

                sourceChannel.RemoveSend(targetChannel.ChannelNumber);
                DrawAllCables();
                e.Handled = true;
            }
        }
    }

    private void Socket_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is Ellipse socket)
        {
            socket.StrokeThickness = 3;
            socket.Stroke = (Brush)FindResource("LapisAccentBrush");
        }
    }

    private void Socket_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is Ellipse socket)
        {
            socket.StrokeThickness = 2;
            socket.Stroke = (Brush)FindResource("BorderBrush");
        }
    }

    private void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        if (Channels == null) return;

        var result = MessageBox.Show(
            "Alle Routing-Verbindungen entfernen?",
            "Patchbay Reset",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            foreach (var channel in Channels)
            {
                channel.SendTargets.Clear();
            }
            DrawAllCables();
        }
    }

    private class SocketData
    {
        public MixerChannel Channel { get; set; } = null!;
        public bool IsOutput { get; set; }
        public int Index { get; set; }
    }

    private class CableData
    {
        public int SourceIndex { get; set; }
        public int TargetIndex { get; set; }
    }
}

