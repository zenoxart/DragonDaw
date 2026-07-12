using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DAW.Audio.Effects;
using static DAW.MVVM.Views.Controls.DragonUI;

namespace DAW.MVVM.Views.Controls;

/// <summary>
/// Utility Gain — Red Dragon Design.
///
/// Fixed layout (300 × 300 window):
///   Row 0  [HDR_H]   Header
///   Row 1  [LROW_H]  1 large knob: GAIN (centred)
///   Row 2  [BTN_H+PAD]  SOFT CLIP toggle
/// </summary>
public sealed class GainControl : FrameworkElement
{
    public static readonly DependencyProperty EffectProperty =
        DependencyProperty.Register(nameof(Effect), typeof(GainEffect), typeof(GainControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender,
                (d, _) => ((GainControl)d).InvalidateVisual()));

    public GainEffect? Effect
    { get => (GainEffect?)GetValue(EffectProperty); set => SetValue(EffectProperty, value); }

    private int    _dragKnob = -1;
    private double _dragY, _dragBase;
    private Point  _kCenter;
    private Rect   _clipBtn;

    protected override void OnRender(DrawingContext dc)
    {
        double W = ActualWidth, H = ActualHeight;
        if (W < 30 || H < 30) return;

        dc.DrawRoundedRectangle(BBg, PBorder, new Rect(0, 0, W, H), 6, 6);
        DrawScrew(dc, 8, 8); DrawScrew(dc, W - 8, 8);
        DrawScrew(dc, 8, H - 8); DrawScrew(dc, W - 8, H - 8);

        var fx = Effect;
        DrawHeader(dc, new Rect(0, 0, W, HDR_H), "GAIN", "Utility");

        // ── Large gain knob centred ──
        double r1top = HDR_H + PAD;
        double kcy   = KnobCY(r1top, KR_LG);
        _kCenter = new Point(W / 2, kcy);

        double gain = fx?.Gain ?? 0;
        double norm = Math.Clamp((gain + 24) / 48.0, 0, 1);
        DrawKnob(dc, W / 2, kcy, KR_LG, norm, CGold, "GAIN",
            $"{gain:+0.0;-0.0} dB", glow: true);

        // ── Soft-clip button ──
        double btnY = r1top + LROW_H + GAP;
        _clipBtn = new Rect(W / 2 - BTN_W / 2, btnY, BTN_W, BTN_H);
        DrawButton(dc, _clipBtn, "SOFT CLIP", fx?.SoftClip ?? false, 9);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var p = e.GetPosition(this);
        if (_clipBtn.Contains(p) && Effect != null)
        { Effect.SoftClip = !Effect.SoftClip; InvalidateVisual(); e.Handled = true; return; }
        var d = p - _kCenter;
        if (d.X * d.X + d.Y * d.Y <= (KR_LG + 14) * (KR_LG + 14))
        { _dragKnob = 0; _dragY = p.Y; _dragBase = Effect?.Gain ?? 0; CaptureMouse(); e.Handled = true; }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragKnob < 0 || Effect == null) return;
        bool sh = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        Effect.Gain = (float)Math.Clamp(_dragBase + (_dragY - e.GetPosition(this).Y) * 48.0 / (sh ? 600 : 200), -24, 24);
        InvalidateVisual(); e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    { if (_dragKnob >= 0) { _dragKnob = -1; ReleaseMouseCapture(); e.Handled = true; } }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        var d = e.GetPosition(this) - _kCenter;
        if (d.X * d.X + d.Y * d.Y <= (KR_LG + 14) * (KR_LG + 14) && Effect != null)
        { Effect.Gain = 0; InvalidateVisual(); e.Handled = true; }
    }

    protected override Size MeasureOverride(Size av)
        => new(Math.Max(double.IsInfinity(av.Width)  ? 300 : av.Width,  260),
               Math.Max(double.IsInfinity(av.Height) ? 320 : av.Height, 270));
}
