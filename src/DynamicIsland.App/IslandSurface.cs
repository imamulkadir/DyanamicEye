using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using DynamicIsland.Core.Shell;

namespace DynamicIsland.App;

/// <summary>
/// The visible island, drawn as a classic display notch: flat top flush with the screen edge, small concave
/// "shoulders" where it meets the edge, straight sides and large rounded bottom corners. Original geometry (no assets).
/// Only the drawn shape is hit-testable; everything around it is transparent and click-through.
/// <para>Exposes UI Automation ExpandCollapse (expand/collapse) and Toggle (pin) for screen-reader and keyboard users;
/// automated smoke tests use the same patterns.</para>
/// </summary>
public sealed class IslandSurface : Decorator
{
    public const string AutomationIdentifier = "IslandSurface";

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(IslandSurface),
        new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Radius of the concave shoulders at the top corners, in DIPs.</summary>
    public static readonly DependencyProperty ShoulderRadiusProperty = DependencyProperty.Register(
        nameof(ShoulderRadius), typeof(double), typeof(IslandSurface),
        new FrameworkPropertyMetadata(6.0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsArrange));

    /// <summary>Radius of the rounded bottom corners, in DIPs (clamped to the shape).</summary>
    public static readonly DependencyProperty BottomRadiusProperty = DependencyProperty.Register(
        nameof(BottomRadius), typeof(double), typeof(IslandSurface),
        new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush Fill
    {
        get => (Brush)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public double ShoulderRadius
    {
        get => (double)GetValue(ShoulderRadiusProperty);
        set => SetValue(ShoulderRadiusProperty, value);
    }

    public double BottomRadius
    {
        get => (double)GetValue(BottomRadiusProperty);
        set => SetValue(BottomRadiusProperty, value);
    }

    public IIslandStateMachine? StateMachine { get; set; }

    /// <summary>Notch outline for a total size (shoulders included) — public for tests and documentation.</summary>
    public static Geometry CreateNotchGeometry(double width, double height, double shoulder, double bottomRadius)
    {
        var s = Math.Clamp(shoulder, 0, Math.Min(width / 4, height / 2));
        var bodyWidth = width - (2 * s);
        var r = Math.Clamp(bottomRadius, 0, Math.Min(bodyWidth / 2, height - s));

        var figure = new PathFigure { StartPoint = new Point(0, 0), IsClosed = true, IsFilled = true };
        figure.Segments.Add(new LineSegment(new Point(width, 0), isStroked: false));
        figure.Segments.Add(new ArcSegment(new Point(width - s, s), new Size(s, s), 0, isLargeArc: false, SweepDirection.Counterclockwise, isStroked: false));
        figure.Segments.Add(new LineSegment(new Point(width - s, height - r), isStroked: false));
        figure.Segments.Add(new ArcSegment(new Point(width - s - r, height), new Size(r, r), 0, isLargeArc: false, SweepDirection.Clockwise, isStroked: false));
        figure.Segments.Add(new LineSegment(new Point(s + r, height), isStroked: false));
        figure.Segments.Add(new ArcSegment(new Point(s, height - r), new Size(r, r), 0, isLargeArc: false, SweepDirection.Clockwise, isStroked: false));
        figure.Segments.Add(new LineSegment(new Point(s, s), isStroked: false));
        figure.Segments.Add(new ArcSegment(new Point(0, 0), new Size(s, s), 0, isLargeArc: false, SweepDirection.Counterclockwise, isStroked: false));

        var geometry = new PathGeometry([figure]);
        geometry.Freeze();
        return geometry;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (ActualWidth > 0 && ActualHeight > 0)
        {
            drawingContext.DrawGeometry(Fill, null, CreateNotchGeometry(ActualWidth, ActualHeight, ShoulderRadius, BottomRadius));
        }
    }

    /// <summary>Content lives inside the notch body, between the shoulders.</summary>
    protected override Size ArrangeOverride(Size arrangeSize)
    {
        var inset = Math.Min(ShoulderRadius, arrangeSize.Width / 4);
        Child?.Arrange(new Rect(inset, 0, Math.Max(0, arrangeSize.Width - (2 * inset)), arrangeSize.Height));
        return arrangeSize;
    }

    protected override Size MeasureOverride(Size constraint)
    {
        var inset = Math.Min(ShoulderRadius, double.IsInfinity(constraint.Width) ? ShoulderRadius : constraint.Width / 4);
        Child?.Measure(new Size(Math.Max(0, constraint.Width - (2 * inset)), constraint.Height));
        return new Size(double.IsInfinity(constraint.Width) ? 0 : constraint.Width, double.IsInfinity(constraint.Height) ? 0 : constraint.Height);
    }

    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters)
    {
        var geometry = CreateNotchGeometry(ActualWidth, ActualHeight, ShoulderRadius, BottomRadius);
        return geometry.FillContains(hitTestParameters.HitPoint) ? new PointHitTestResult(this, hitTestParameters.HitPoint) : null;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new IslandSurfacePeer(this);

    private sealed class IslandSurfacePeer(IslandSurface owner) : FrameworkElementAutomationPeer(owner), IExpandCollapseProvider, IToggleProvider
    {
        private IIslandStateMachine? Machine => owner.StateMachine;

        public ExpandCollapseState ExpandCollapseState =>
            Machine?.State is IslandState.Expanded or IslandState.Pinned ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

        public ToggleState ToggleState => Machine?.State == IslandState.Pinned ? ToggleState.On : ToggleState.Off;

        public void Expand()
        {
            if (ExpandCollapseState == ExpandCollapseState.Collapsed)
            {
                Machine?.Fire(IslandInput.Click);
            }
        }

        public void Collapse() => Machine?.Fire(IslandInput.Escape);

        public void Toggle() => Machine?.Fire(IslandInput.TogglePin);

        public override object? GetPattern(PatternInterface patternInterface) =>
            patternInterface is PatternInterface.ExpandCollapse or PatternInterface.Toggle ? this : base.GetPattern(patternInterface);

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;

        protected override string GetAutomationIdCore() => AutomationIdentifier;

        protected override string GetClassNameCore() => nameof(IslandSurface);

        protected override bool IsKeyboardFocusableCore() => true;
    }
}
