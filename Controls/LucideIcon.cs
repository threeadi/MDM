using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Mdm.Controls;

public class LucideIcon : Control
{
    public static readonly DependencyProperty KindProperty =
        DependencyProperty.Register(nameof(Kind), typeof(string), typeof(LucideIcon),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeThicknessProperty =
        DependencyProperty.Register(nameof(StrokeThickness), typeof(double), typeof(LucideIcon),
            new FrameworkPropertyMetadata(2.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillBrushProperty =
        DependencyProperty.Register(nameof(FillBrush), typeof(Brush), typeof(LucideIcon),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public string Kind
    {
        get => (string)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public Brush? FillBrush
    {
        get => (Brush?)GetValue(FillBrushProperty);
        set => SetValue(FillBrushProperty, value);
    }

    private static readonly Dictionary<string, Geometry> Geometries = new(StringComparer.OrdinalIgnoreCase);

    static LucideIcon()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(LucideIcon),
            new FrameworkPropertyMetadata(typeof(LucideIcon)));

        RegisterIcons();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        if (ActualWidth <= 0 || ActualHeight <= 0) return;

        string kind = Kind?.Trim() ?? string.Empty;
        if (!Geometries.TryGetValue(kind, out var geom) || geom == null) return;

        double scaleX = ActualWidth / 24.0;
        double scaleY = ActualHeight / 24.0;

        dc.PushTransform(new ScaleTransform(scaleX, scaleY));

        var pen = new Pen(Foreground ?? Brushes.White, StrokeThickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        pen.Freeze();

        Brush? fill = FillBrush;
        if (fill == null && kind.Equals("play", StringComparison.OrdinalIgnoreCase))
        {
            fill = Foreground ?? Brushes.White;
        }

        dc.DrawGeometry(fill, pen, geom);
        dc.Pop();
    }

    private static void RegisterIcons()
    {
        {
            var group = new GeometryGroup();
            group.Children.Add(Geometry.Parse("M4 14a1 1 0 0 1-.78-1.63l9.9-10.2a.5.5 0 0 1 .86.46l-1.92 6.02A1 1 0 0 0 13 10h7a1 1 0 0 1 .78 1.63l-9.9 10.2a.5.5 0 0 1-.86-.46l1.92-6.02A1 1 0 0 0 11 14z"));
            group.Freeze();
            Geometries["zap"] = group;
        }
        {
            var group = new GeometryGroup();
            group.Children.Add(Geometry.Parse("M5 12h14"));
            group.Children.Add(Geometry.Parse("M12 5v14"));
            group.Freeze();
            Geometries["plus"] = group;
        }
        {
            var group = new GeometryGroup();
            group.Children.Add(Geometry.Parse("M5 5a2 2 0 0 1 3.008-1.728l11.997 6.998a2 2 0 0 1 .003 3.458l-12 7A2 2 0 0 1 5 19z"));
            group.Freeze();
            Geometries["play"] = group;
        }
        {
            var group = new GeometryGroup();
            group.Children.Add(new RectangleGeometry(new Rect(14, 3, 5, 18), 1, 1));
            group.Children.Add(new RectangleGeometry(new Rect(5, 3, 5, 18), 1, 1));
            group.Freeze();
            Geometries["pause"] = group;
        }
        {
            var group = new GeometryGroup();
            group.Children.Add(Geometry.Parse("M18 6 7 17l-5-5"));
            group.Children.Add(Geometry.Parse("m22 10-7.5 7.5L13 16"));
            group.Freeze();
            Geometries["check-check"] = group;
        }
        {
            var group = new GeometryGroup();
            group.Children.Add(Geometry.Parse("m21 21-4.34-4.34"));
            group.Children.Add(new EllipseGeometry(new Point(11, 11), 8, 8));
            group.Freeze();
            Geometries["search"] = group;
        }
        {
            var group = new GeometryGroup();
            group.Children.Add(Geometry.Parse("M9.671 4.136a2.34 2.34 0 0 1 4.659 0 2.34 2.34 0 0 0 3.319 1.915 2.34 2.34 0 0 1 2.33 4.033 2.34 2.34 0 0 0 0 3.831 2.34 2.34 0 0 1-2.33 4.033 2.34 2.34 0 0 0-3.319 1.915 2.34 2.34 0 0 1-4.659 0 2.34 2.34 0 0 0-3.32-1.915 2.34 2.34 0 0 1-2.33-4.033 2.34 2.34 0 0 0 0-3.831A2.34 2.34 0 0 1 6.35 6.051a2.34 2.34 0 0 0 3.319-1.915"));
            group.Children.Add(new EllipseGeometry(new Point(12, 12), 3, 3));
            group.Freeze();
            Geometries["settings"] = group;
        }
        {
            var group = new GeometryGroup();
            group.Children.Add(Geometry.Parse("M2 5h20"));
            group.Children.Add(Geometry.Parse("M6 12h12"));
            group.Children.Add(Geometry.Parse("M9 19h6"));
            group.Freeze();
            Geometries["list-filter"] = group;
        }
        {
            var group = new GeometryGroup();
            group.Children.Add(new EllipseGeometry(new Point(12, 12), 10, 10));
            group.Children.Add(Geometry.Parse("M12 8v8"));
            group.Children.Add(Geometry.Parse("m8 12 4 4 4-4"));
            group.Freeze();
            Geometries["circle-arrow-down"] = group;
        }
        {
            var group = new GeometryGroup();
            group.Children.Add(new EllipseGeometry(new Point(12, 12), 10, 10));
            group.Children.Add(new LineGeometry(new Point(10, 15), new Point(10, 9)));
            group.Children.Add(new LineGeometry(new Point(14, 15), new Point(14, 9)));
            group.Freeze();
            Geometries["circle-pause"] = group;
        }
        {
            var group = new GeometryGroup();
            group.Children.Add(Geometry.Parse("M21.801 10A10 10 0 1 1 17 3.335"));
            group.Children.Add(Geometry.Parse("m9 11 3 3L22 4"));
            group.Freeze();
            Geometries["circle-check-big"] = group;
        }
        {
            var group = new GeometryGroup();
            group.Children.Add(new EllipseGeometry(new Point(12, 12), 10, 10));
            group.Children.Add(new LineGeometry(new Point(12, 8), new Point(12, 12)));
            group.Children.Add(new LineGeometry(new Point(12, 16), new Point(12.01, 16)));
            group.Freeze();
            Geometries["circle-alert"] = group;
        }
        {
            var group = new GeometryGroup();
            group.Children.Add(Geometry.Parse("M14.536 21.686a.5.5 0 0 0 .937-.024l6.5-19a.496.496 0 0 0-.635-.635l-19 6.5a.5.5 0 0 0-.024.937l7.93 3.18a2 2 0 0 1 1.112 1.11z"));
            group.Children.Add(Geometry.Parse("m21.854 2.147-10.94 10.939"));
            group.Freeze();
            Geometries["send"] = group;
        }
        {
            var group = new GeometryGroup();
            group.Children.Add(Geometry.Parse("M12 20v2"));
            group.Children.Add(Geometry.Parse("M12 2v2"));
            group.Children.Add(Geometry.Parse("M17 20v2"));
            group.Children.Add(Geometry.Parse("M17 2v2"));
            group.Children.Add(Geometry.Parse("M2 12h2"));
            group.Children.Add(Geometry.Parse("M2 17h2"));
            group.Children.Add(Geometry.Parse("M2 7h2"));
            group.Children.Add(Geometry.Parse("M20 12h2"));
            group.Children.Add(Geometry.Parse("M20 17h2"));
            group.Children.Add(Geometry.Parse("M20 7h2"));
            group.Children.Add(Geometry.Parse("M7 20v2"));
            group.Children.Add(Geometry.Parse("M7 2v2"));
            group.Children.Add(new RectangleGeometry(new Rect(4, 4, 16, 16), 2, 2));
            group.Children.Add(new RectangleGeometry(new Rect(8, 8, 8, 8), 1, 1));
            group.Freeze();
            Geometries["cpu"] = group;
        }
        {
            var group = new GeometryGroup();
            group.Children.Add(Geometry.Parse("M6 22a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h8a2.4 2.4 0 0 1 1.704.706l3.588 3.588A2.4 2.4 0 0 1 20 8v12a2 2 0 0 1-2 2z"));
            group.Children.Add(Geometry.Parse("M14 2v5a1 1 0 0 0 1 1h5"));
            group.Children.Add(Geometry.Parse("M10 9H8"));
            group.Children.Add(Geometry.Parse("M16 13H8"));
            group.Children.Add(Geometry.Parse("M16 17H8"));
            group.Freeze();
            Geometries["file-text"] = group;
        }
        {
            var group = new GeometryGroup();
            group.Children.Add(Geometry.Parse("m16 13 5.223 3.482a.5.5 0 0 0 .777-.416V7.87a.5.5 0 0 0-.752-.432L16 10.5"));
            group.Children.Add(new RectangleGeometry(new Rect(2, 6, 14, 12), 2, 2));
            group.Freeze();
            Geometries["video"] = group;
        }
        {
            var group = new GeometryGroup();
            group.Children.Add(Geometry.Parse("M9 18V5l12-2v13"));
            group.Children.Add(new EllipseGeometry(new Point(6, 18), 3, 3));
            group.Children.Add(new EllipseGeometry(new Point(18, 16), 3, 3));
            group.Freeze();
            Geometries["music"] = group;
        }
        {
            var group = new GeometryGroup();
            group.Children.Add(new RectangleGeometry(new Rect(2, 3, 20, 5), 1, 1));
            group.Children.Add(Geometry.Parse("M4 8v11a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8"));
            group.Children.Add(Geometry.Parse("M10 12h4"));
            group.Freeze();
            Geometries["archive"] = group;
        }
        {
            var group = new GeometryGroup();
            group.Children.Add(Geometry.Parse("M10 11v6"));
            group.Children.Add(Geometry.Parse("M14 11v6"));
            group.Children.Add(Geometry.Parse("M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6"));
            group.Children.Add(Geometry.Parse("M3 6h18"));
            group.Children.Add(Geometry.Parse("M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"));
            group.Freeze();
            Geometries["trash-2"] = group;
        }
        {
            var group = new GeometryGroup();
            group.Children.Add(Geometry.Parse("m6 14 1.5-2.9A2 2 0 0 1 9.24 10H20a2 2 0 0 1 1.94 2.5l-1.54 6a2 2 0 0 1-1.95 1.5H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h3.9a2 2 0 0 1 1.69.9l.81 1.2a2 2 0 0 0 1.67.9H18a2 2 0 0 1 2 2v2"));
            group.Freeze();
            Geometries["folder-open"] = group;
        }
        {
            var group = new GeometryGroup();
            group.Children.Add(Geometry.Parse("M20 20a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.9a2 2 0 0 1-1.69-.9L9.6 3.9A2 2 0 0 0 7.93 3H4a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2Z"));
            group.Freeze();
            Geometries["folder"] = group;
        }
        {
            var group = new GeometryGroup();
            group.Children.Add(Geometry.Parse("M15 3h6v6"));
            group.Children.Add(Geometry.Parse("M10 14 21 3"));
            group.Children.Add(Geometry.Parse("M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6"));
            group.Freeze();
            Geometries["external-link"] = group;
        }
        {
            var group = new GeometryGroup();
            group.Children.Add(Geometry.Parse("M3 12a9 9 0 0 1 9-9 9.75 9.75 0 0 1 6.74 2.74L21 8"));
            group.Children.Add(Geometry.Parse("M21 3v5h-5"));
            group.Children.Add(Geometry.Parse("M21 12a9 9 0 0 1-9 9 9.75 9.75 0 0 1-6.74-2.74L3 16"));
            group.Children.Add(Geometry.Parse("M8 16H3v5"));
            group.Freeze();
            Geometries["refresh-cw"] = group;
        }
        {
            var group = new GeometryGroup();
            group.Children.Add(new RectangleGeometry(new Rect(8, 8, 14, 14), 2, 2));
            group.Children.Add(Geometry.Parse("M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2"));
            group.Freeze();
            Geometries["copy"] = group;
        }
        {
            var group = new GeometryGroup();
            group.Children.Add(new LineGeometry(new Point(4, 9), new Point(20, 9)));
            group.Children.Add(new LineGeometry(new Point(4, 15), new Point(20, 15)));
            group.Children.Add(new LineGeometry(new Point(10, 3), new Point(8, 21)));
            group.Children.Add(new LineGeometry(new Point(16, 3), new Point(14, 21)));
            group.Freeze();
            Geometries["hash"] = group;
        }
        {
            var group = new GeometryGroup();
            group.Children.Add(Geometry.Parse("M20 6 9 17l-5-5"));
            group.Freeze();
            Geometries["check"] = group;
        }
        {
            var group = new GeometryGroup();
            group.Children.Add(Geometry.Parse("M18 6 6 18"));
            group.Children.Add(Geometry.Parse("m6 6 12 12"));
            group.Freeze();
            Geometries["x"] = group;
        }
        {
            var group = new GeometryGroup();
            group.Children.Add(Geometry.Parse("M16.247 7.761a6 6 0 0 1 0 8.478"));
            group.Children.Add(Geometry.Parse("M19.075 4.933a10 10 0 0 1 0 14.134"));
            group.Children.Add(Geometry.Parse("M4.925 19.067a10 10 0 0 1 0-14.134"));
            group.Children.Add(Geometry.Parse("M7.753 16.239a6 6 0 0 1 0-8.478"));
            group.Children.Add(new EllipseGeometry(new Point(12, 12), 2, 2));
            group.Freeze();
            Geometries["radio"] = group;
        }
        {
            var group = new GeometryGroup();
            group.Children.Add(Geometry.Parse("M12 5v14"));
            group.Children.Add(Geometry.Parse("m19 12-7 7-7-7"));
            group.Freeze();
            Geometries["arrow-down"] = group;
        }
        {
            var group = new GeometryGroup();
            group.Children.Add(Geometry.Parse("m5 12 7-7 7 7"));
            group.Children.Add(Geometry.Parse("M12 19V5"));
            group.Freeze();
            Geometries["arrow-up"] = group;
        }
        {
            var group = new GeometryGroup();
            group.Children.Add(new EllipseGeometry(new Point(12, 12), 10, 10));
            group.Children.Add(Geometry.Parse("M12 6v6l4 2"));
            group.Freeze();
            Geometries["clock"] = group;
        }
        {
            var group = new GeometryGroup();
            group.Children.Add(Geometry.Parse("M10 16h.01"));
            group.Children.Add(Geometry.Parse("M2.212 11.577a2 2 0 0 0-.212.896V18a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-5.527a2 2 0 0 0-.212-.896L18.55 5.11A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11z"));
            group.Children.Add(Geometry.Parse("M21.946 12.013H2.054"));
            group.Children.Add(Geometry.Parse("M6 16h.01"));
            group.Freeze();
            Geometries["hard-drive"] = group;
        }
        {
            var group = new GeometryGroup();
            group.Children.Add(new EllipseGeometry(new Point(12, 12), 10, 10));
            group.Children.Add(Geometry.Parse("M12 16v-4"));
            group.Children.Add(Geometry.Parse("M12 8h.01"));
            group.Freeze();
            Geometries["info"] = group;
        }
        {
            var group = new GeometryGroup();
            group.Children.Add(Geometry.Parse("M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z"));
            group.Children.Add(Geometry.Parse("m9 12 2 2 4-4"));
            group.Freeze();
            Geometries["shield-check"] = group;
        }
        {
            var group = new GeometryGroup();
            group.Children.Add(Geometry.Parse("M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z"));
            group.Children.Add(Geometry.Parse("M12 8v4"));
            group.Children.Add(Geometry.Parse("M12 16h.01"));
            group.Freeze();
            Geometries["shield-alert"] = group;
        }
        {
            var group = new GeometryGroup();
            group.Children.Add(Geometry.Parse("M19 21v-2a4 4 0 0 0-4-4H9a4 4 0 0 0-4 4v2"));
            group.Children.Add(new EllipseGeometry(new Point(12, 7), 4, 4));
            group.Freeze();
            Geometries["user"] = group;
        }
        {
            var group = new GeometryGroup();
            group.Children.Add(Geometry.Parse("M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2"));
            group.Children.Add(Geometry.Parse("M16 3.128a4 4 0 0 1 0 7.744"));
            group.Children.Add(Geometry.Parse("M22 21v-2a4 4 0 0 0-3-3.87"));
            group.Children.Add(new EllipseGeometry(new Point(9, 7), 4, 4));
            group.Freeze();
            Geometries["users"] = group;
        }
        {
            var group = new GeometryGroup();
            group.Children.Add(Geometry.Parse("M12 8V4H8"));
            group.Children.Add(new RectangleGeometry(new Rect(4, 8, 16, 12), 2, 2));
            group.Children.Add(Geometry.Parse("M2 14h2"));
            group.Children.Add(Geometry.Parse("M20 14h2"));
            group.Children.Add(Geometry.Parse("M15 13v2"));
            group.Children.Add(Geometry.Parse("M9 13v2"));
            group.Freeze();
            Geometries["bot"] = group;
        }
        {
            var group = new GeometryGroup();
            group.Children.Add(Geometry.Parse("M17 3a2 2 0 0 1 2 2v15a1 1 0 0 1-1.496.868l-4.512-2.578a2 2 0 0 0-1.984 0l-4.512 2.578A1 1 0 0 1 5 20V5a2 2 0 0 1 2-2z"));
            group.Freeze();
            Geometries["bookmark"] = group;
        }
        {
            var group = new GeometryGroup();
            group.Children.Add(new RectangleGeometry(new Rect(2, 3, 20, 14), 2, 2));
            group.Children.Add(new LineGeometry(new Point(8, 21), new Point(16, 21)));
            group.Children.Add(new LineGeometry(new Point(12, 17), new Point(12, 21)));
            group.Freeze();
            Geometries["monitor"] = group;
        }
        {
            var group = new GeometryGroup();
            group.Children.Add(new EllipseGeometry(new Point(12, 12), 10, 10));
            group.Children.Add(Geometry.Parse("M12 2a14.5 14.5 0 0 0 0 20 14.5 14.5 0 0 0 0-20"));
            group.Children.Add(Geometry.Parse("M2 12h20"));
            group.Freeze();
            Geometries["globe"] = group;
        }
        {
            var group = new GeometryGroup();
            group.Children.Add(Geometry.Parse("M12.83 2.18a2 2 0 0 0-1.66 0L2.6 6.08a1 1 0 0 0 0 1.83l8.58 3.91a2 2 0 0 0 1.66 0l8.58-3.9a1 1 0 0 0 0-1.83z"));
            group.Children.Add(Geometry.Parse("M2 12a1 1 0 0 0 .58.91l8.6 3.91a2 2 0 0 0 1.65 0l8.58-3.9A1 1 0 0 0 22 12"));
            group.Children.Add(Geometry.Parse("M2 17a1 1 0 0 0 .58.91l8.6 3.91a2 2 0 0 0 1.65 0l8.58-3.9A1 1 0 0 0 22 17"));
            group.Freeze();
            Geometries["layers"] = group;
        }
        {
            var group = new GeometryGroup();
            group.Children.Add(Geometry.Parse("M10 5H3"));
            group.Children.Add(Geometry.Parse("M12 19H3"));
            group.Children.Add(Geometry.Parse("M14 3v4"));
            group.Children.Add(Geometry.Parse("M16 17v4"));
            group.Children.Add(Geometry.Parse("M21 12h-9"));
            group.Children.Add(Geometry.Parse("M21 19h-5"));
            group.Children.Add(Geometry.Parse("M21 5h-7"));
            group.Children.Add(Geometry.Parse("M8 10v4"));
            group.Children.Add(Geometry.Parse("M8 12H3"));
            group.Freeze();
            Geometries["sliders-horizontal"] = group;
        }
        {
            var group = new GeometryGroup();
            group.Children.Add(Geometry.Parse("M12 3q1 4 4 6.5t3 5.5a1 1 0 0 1-14 0 5 5 0 0 1 1-3 1 1 0 0 0 5 0c0-2-1.5-3-1.5-5q0-2 2.5-4"));
            group.Freeze();
            Geometries["flame"] = group;
        }
        {
            var group = new GeometryGroup();
            group.Children.Add(Geometry.Parse("M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8"));
            group.Children.Add(Geometry.Parse("M3 3v5h5"));
            group.Freeze();
            Geometries["rotate-ccw"] = group;
        }
        {
            var group = new GeometryGroup();
            group.Children.Add(Geometry.Parse("M5 12h14"));
            group.Freeze();
            Geometries["minus"] = group;
        }
        {
            var group = new GeometryGroup();
            group.Children.Add(Geometry.Parse("m16 11 2 2 4-4"));
            group.Children.Add(Geometry.Parse("M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2"));
            group.Children.Add(new EllipseGeometry(new Point(9, 7), 4, 4));
            group.Freeze();
            Geometries["user-check"] = group;
        }
        {
            var group = new GeometryGroup();
            group.Children.Add(Geometry.Parse("M2.062 12.348a1 1 0 0 1 0-.696 10.75 10.75 0 0 1 19.876 0 1 1 0 0 1 0 .696 10.75 10.75 0 0 1-19.876 0"));
            group.Children.Add(new EllipseGeometry(new Point(12, 12), 3, 3));
            group.Freeze();
            Geometries["eye"] = group;
        }
        {
            var group = new GeometryGroup();
            group.Children.Add(Geometry.Parse("M10.733 5.076a10.744 10.744 0 0 1 11.205 6.575 1 1 0 0 1 0 .696 10.747 10.747 0 0 1-1.444 2.49"));
            group.Children.Add(Geometry.Parse("M14.084 14.158a3 3 0 0 1-4.242-4.242"));
            group.Children.Add(Geometry.Parse("M17.479 17.499a10.75 10.75 0 0 1-15.417-5.151 1 1 0 0 1 0-.696 10.75 10.75 0 0 1 4.446-5.143"));
            group.Children.Add(Geometry.Parse("m2 2 20 20"));
            group.Freeze();
            Geometries["eye-off"] = group;
        }
        {
            var group = new GeometryGroup();
            group.Children.Add(Geometry.Parse("M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"));
            group.Children.Add(Geometry.Parse("m7 10 5 5 5-5"));
            group.Children.Add(Geometry.Parse("M12 15V3"));
            group.Freeze();
            Geometries["download"] = group;
        }
        {
            var group = new GeometryGroup();
            group.Children.Add(Geometry.Parse("M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z"));
            group.Freeze();
            Geometries["message-square"] = group;
        }
        {
            var group = new GeometryGroup();
            group.Children.Add(Geometry.Parse("m6 9 6 6 6-6"));
            group.Freeze();
            Geometries["chevron-down"] = group;
        }
        {
            var group = new GeometryGroup();
            group.Children.Add(Geometry.Parse("M22 12h-4l-3 9L9 3l-3 9H2"));
            group.Freeze();
            Geometries["activity"] = group;
        }
        {
            var group = new GeometryGroup();
            group.Children.Add(Geometry.Parse("M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z"));
            group.Freeze();
            Geometries["shield"] = group;
        }
        {
            var group = new GeometryGroup();
            group.Children.Add(new RectangleGeometry(new Rect(3, 3, 18, 18), 2, 2));
            group.Freeze();
            Geometries["square"] = group;
        }
        {
            var group = new GeometryGroup();
            group.Children.Add(Geometry.Parse("m9 11 3 3L22 4"));
            group.Children.Add(Geometry.Parse("M21 12v7a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11"));
            group.Freeze();
            Geometries["check-square"] = group;
        }

        // Aliases matching my-own-idm
        Geometries["sliders"] = Geometries["sliders-horizontal"];
        Geometries["arrow-down-circle"] = Geometries["circle-arrow-down"];
        Geometries["pause-circle"] = Geometries["circle-pause"];
        Geometries["check-circle-2"] = Geometries["circle-check-big"];
        Geometries["alert-circle"] = Geometries["circle-alert"];
        Geometries["telegram"] = Geometries["send"];
        Geometries["program"] = Geometries["cpu"];
        Geometries["dokumen"] = Geometries["file-text"];
        Geometries["kompresi"] = Geometries["archive"];
    }
}
