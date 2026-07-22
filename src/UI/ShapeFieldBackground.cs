using System.Windows;
using System.Windows.Media;

namespace UI;

/// <summary>
/// The drifting shape field behind the setup wizard: four large translucent shapes
/// (triangle / square / hexagon / circle) that bounce around inside the frame and
/// rotate slowly.
///
/// Deliberately NOT the ambient video. Setup runs before the dashboard exists, and
/// this is cheap enough to run on a cold boot on modest hardware — one composition
/// callback driving four geometries, no decoding.
///
/// Why one of each shape and one of each colour rather than random draws: with only
/// four on screen, random selection regularly produced three circles in the same hue,
/// which read as a mistake. Fixing the cast guarantees variety every time.
///
/// Rendered via DrawingVisual rather than four Shape elements because these move
/// every frame — going through the layout system 60 times a second for decorative
/// geometry is wasted work.
/// </summary>
public sealed class ShapeFieldBackground : FrameworkElement
{
    /// <summary>Sides per shape; 0 means a circle.</summary>
    private static readonly int[] Forms = { 3, 4, 6, 0 };

    /// <summary>
    /// Pulled from DashboardTheme so the field never introduces a colour that isn't
    /// already part of the console's palette: Meadow Green (the default accent) leads,
    /// then Emerald, Ocean Blue and Violet.
    /// </summary>
    private static readonly Color[] Hues =
    {
        Color.FromRgb(0xA8, 0xE0, 0x3B),
        Color.FromRgb(0x3B, 0xE0, 0x9A),
        Color.FromRgb(0x2E, 0xC8, 0xFF),
        Color.FromRgb(0x8F, 0x6B, 0xFF),
    };

    private sealed class Shape
    {
        public double X, Y;           // position, 0-1 of the frame
        public double Vx, Vy;         // velocity, fraction of frame per second
        public double Radius;         // fraction of the frame's LARGER edge
        public double Rotation;
        public double Spin;           // radians per second
        public int Sides;
        public Color Hue;
        public double Alpha;
        public double Fade = 1;       // drops to 0 during Scatter()
    }

    private readonly Shape[] _shapes = new Shape[4];
    private readonly DrawingVisual _visual = new();
    private TimeSpan _lastRender;
    private bool _scattering;
    private bool _running;

    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index) => _visual;

    public ShapeFieldBackground()
    {
        IsHitTestVisible = false;
        AddVisualChild(_visual);
        Reset();

        Loaded += (_, _) => Start();
        Unloaded += (_, _) => Stop();
    }

    /// <summary>
    /// Puts every shape back to its starting position and velocity. Called on
    /// construction and whenever the wizard is reopened, so a second run through
    /// setup doesn't begin with shapes already scattered off-screen.
    /// </summary>
    public void Reset()
    {
        _scattering = false;

        // Fixed seed: the arrangement should be identical on every boot rather than
        // different each time. Consoles are recognisable; a splash that looks
        // different on every power-on reads as broken rather than lively.
        var rng = new Random(20260719);

        for (var i = 0; i < _shapes.Length; i++)
        {
            var angle = rng.NextDouble() * Math.PI * 2;
            var speed = 0.045 + rng.NextDouble() * 0.035;

            _shapes[i] = new Shape
            {
                X = 0.2 + rng.NextDouble() * 0.6,
                Y = 0.2 + rng.NextDouble() * 0.6,
                Vx = Math.Cos(angle) * speed,
                Vy = Math.Sin(angle) * speed,
                Radius = 0.07 + rng.NextDouble() * 0.05,
                Rotation = rng.NextDouble() * Math.PI * 2,
                Spin = (rng.NextDouble() - 0.5) * 0.34,
                Sides = Forms[i],
                Hue = Hues[i],
                Alpha = 0.14 + rng.NextDouble() * 0.10,
            };
        }
    }

    /// <summary>
    /// Sends the shapes accelerating outward, spinning up and fading — the setup
    /// wizard's exit into the dashboard. Bouncing is suspended so they actually
    /// leave the frame rather than being trapped by its walls.
    /// </summary>
    public void Scatter() => _scattering = true;

    public void Start()
    {
        if (_running)
        {
            return;
        }

        _running = true;
        _lastRender = TimeSpan.Zero;
        CompositionTarget.Rendering += OnRendering;
    }

    public void Stop()
    {
        if (!_running)
        {
            return;
        }

        _running = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (e is not RenderingEventArgs args)
        {
            return;
        }

        // CompositionTarget.Rendering can fire more than once for the same frame;
        // ignoring the repeat keeps the motion from advancing twice as fast.
        if (args.RenderingTime == _lastRender)
        {
            return;
        }

        var dt = _lastRender == TimeSpan.Zero
            ? 0
            : Math.Min((args.RenderingTime - _lastRender).TotalSeconds, 0.05);

        _lastRender = args.RenderingTime;

        if (dt > 0)
        {
            Advance(dt);
        }

        Draw();
    }

    private void Advance(double dt)
    {
        var w = ActualWidth;
        var h = ActualHeight;

        if (w <= 0 || h <= 0)
        {
            return;
        }

        foreach (var s in _shapes)
        {
            s.X += s.Vx * dt;
            s.Y += s.Vy * dt;
            s.Rotation += s.Spin * dt;

            if (_scattering)
            {
                s.Vx *= 1 + 2.6 * dt;
                s.Vy *= 1 + 2.6 * dt;
                s.Spin *= 1 + 1.8 * dt;
                s.Fade = Math.Max(0, s.Fade - dt * 1.5);
                continue;
            }

            // Bounce margins are per-axis: the frame isn't square, so a shape sized
            // as a fraction of the larger edge occupies a very different share of
            // the shorter one. A single margin would let shapes clip through the
            // top and bottom on anything other than a square frame.
            var radius = s.Radius * Math.Max(w, h);
            var mx = radius / w;
            var my = radius / h;

            // Clamping the position as well as flipping the velocity matters: a
            // shape that starts out of bounds — or one caught by a long stalled
            // frame — would otherwise stick to the edge and judder.
            if (s.X < mx) { s.X = mx; s.Vx = Math.Abs(s.Vx); }
            if (s.X > 1 - mx) { s.X = 1 - mx; s.Vx = -Math.Abs(s.Vx); }
            if (s.Y < my) { s.Y = my; s.Vy = Math.Abs(s.Vy); }
            if (s.Y > 1 - my) { s.Y = 1 - my; s.Vy = -Math.Abs(s.Vy); }
        }
    }

    private void Draw()
    {
        var w = ActualWidth;
        var h = ActualHeight;

        using var ctx = _visual.RenderOpen();

        if (w <= 0 || h <= 0)
        {
            return;
        }

        foreach (var s in _shapes)
        {
            if (s.Fade <= 0)
            {
                continue;
            }

            var cx = s.X * w;
            var cy = s.Y * h;
            var radius = s.Radius * Math.Max(w, h);
            var centre = new Point(cx, cy);

            var fill = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.5, 0.5),
                Center = new Point(0.5, 0.5),
                RadiusX = 0.5,
                RadiusY = 0.5,
                GradientStops =
                {
                    new GradientStop(WithAlpha(s.Hue, s.Alpha * s.Fade), 0),
                    new GradientStop(WithAlpha(s.Hue, 0), 1),
                },
            };
            fill.Freeze();

            // Without the edge stroke the rotation is invisible — at this size and
            // opacity the soft radial body alone reads as a static smudge.
            var stroke = new Pen(
                new SolidColorBrush(WithAlpha(s.Hue, Math.Min(1, s.Alpha * 1.9 * s.Fade))),
                1.25);
            stroke.Freeze();

            if (s.Sides == 0)
            {
                ctx.DrawEllipse(fill, stroke, centre, radius, radius);
                continue;
            }

            ctx.DrawGeometry(fill, stroke, BuildPolygon(centre, radius, s.Sides, s.Rotation));
        }
    }

    private static Geometry BuildPolygon(Point centre, double radius, int sides, double rotation)
    {
        var figure = new PathFigure { IsClosed = true, IsFilled = true };

        for (var i = 0; i < sides; i++)
        {
            var a = rotation + i / (double)sides * Math.PI * 2;
            var p = new Point(centre.X + Math.Cos(a) * radius, centre.Y + Math.Sin(a) * radius);

            if (i == 0)
            {
                figure.StartPoint = p;
            }
            else
            {
                figure.Segments.Add(new LineSegment(p, isStroked: true));
            }
        }

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        return geometry;
    }

    private static Color WithAlpha(Color c, double alpha) =>
        Color.FromArgb((byte)Math.Clamp(alpha * 255, 0, 255), c.R, c.G, c.B);
}
