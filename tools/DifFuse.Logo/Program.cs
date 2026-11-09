using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DifFuse.Logo;

/// <summary>
/// Draws the DifFuse mark and writes the app icon (<c>DifFuse.ico</c>), a PNG per icon size, a magnified
/// check of the small sizes, and the mark as a XAML <c>DrawingImage</c> fragment for Themes/Brand.xaml.
/// </summary>
/// <remarks>
/// The mark is the top of an archtop guitar: an f-hole (the f of DifFuse, the opening sound diffuses out
/// through), six strings of graduated gauge that spread from nut width to bridge width, and the bridge,
/// in the amp's amber, level with the f-hole's nicks, which is where luthiers place it. Everything is drawn
/// on a 64-unit grid. Sizes of 24 px and below use a hinted version: three one-pixel strings on pixel
/// centres and a two-pixel bridge.
/// Usage: <c>dotnet run --project tools/DifFuse.Logo -- [output folder]</c>
/// </remarks>
internal static class Program
{
    private static readonly Color Enamel = Color.FromRgb(0x0A, 0x12, 0x30);
    private static readonly Color Porcelain = Color.FromRgb(0xF3, 0xF1, 0xEA);
    private static readonly Color Amber = Color.FromRgb(0xFF, 0xC2, 0x0E);

    private static readonly int[] IconSizes = [16, 20, 24, 32, 40, 48, 64, 256];

    // Strings: centred at x 46.5, 2.5 apart at the top edge (towards the nut), 4.3 apart at the bottom (bridge).
    private const double StringCenter = 46.5, NutGap = 2.5, BridgeGap = 4.3, BridgeY = 32.1;
    private static readonly double[] Gauges = [2.3, 2.0, 1.7, 1.45, 1.2, 1.0];

    [STAThread]
    private static void Main(string[] args)
    {
        string outDir = args.Length > 0 ? args[0] : "logo-out";
        Directory.CreateDirectory(outDir);

        var frames = new List<(int Size, byte[] Png)>();
        foreach (int size in IconSizes)
        {
            var png = PngBytes(Render(size, size, dc =>
            {
                dc.PushTransform(new ScaleTransform(size / 64.0, size / 64.0));
                DrawMark(dc, size <= 24 ? size : 0);
                dc.Pop();
            }));
            File.WriteAllBytes(Path.Combine(outDir, $"icon-{size}.png"), png);
            frames.Add((size, png));
        }
        WriteIco(Path.Combine(outDir, "DifFuse.ico"), frames);
        Save(SmallSizeCheck(frames), Path.Combine(outDir, "small-sizes.png"));
        File.WriteAllText(Path.Combine(outDir, "DifFuseMark.xaml"), MarkXaml());
        Console.WriteLine($"Wrote DifFuse.ico, icon PNGs, small-sizes.png and DifFuseMark.xaml to {Path.GetFullPath(outDir)}");
    }

    private static double StringX(int i, double y) =>
        StringCenter + (i - 2.5) * (NutGap + (BridgeGap - NutGap) * ((y + 4) / 72));

    /// <summary>Draws the mark on a 64-unit grid. <paramref name="hintPixels"/> &gt; 0 draws the hinted small version.</summary>
    private static void DrawMark(DrawingContext dc, int hintPixels)
    {
        var tile = new RectangleGeometry(new Rect(0, 0, 64, 64), 14, 14);
        dc.DrawGeometry(Brush(Enamel), null, tile);
        dc.PushClip(tile);
        bool small = hintPixels > 0;
        var fhole = FHole(small);
        fhole.Transform = new TranslateTransform(-2, 0);
        dc.DrawGeometry(Brush(Porcelain), null, fhole);

        if (small)
        {
            double u = 64.0 / hintPixels;
            int[] cols = [(int)Math.Round(hintPixels * 0.62), (int)Math.Round(hintPixels * 0.75), (int)Math.Round(hintPixels * 0.88)];
            int row = (int)Math.Round(hintPixels * 0.47);
            dc.DrawRectangle(Brush(Amber), null, new Rect((cols[0] - 1) * u, row * u, (cols[2] - cols[0] + 3) * u, 2 * u));
            foreach (int c in cols) dc.DrawRectangle(Brush(Porcelain), null, new Rect(c * u, -1, u, 66));
        }
        else
        {
            double left = StringX(0, BridgeY) - 3.5, right = StringX(5, BridgeY) + 3.5;
            dc.DrawRoundedRectangle(Brush(Amber), null, new Rect(left, BridgeY - 2.6, right - left, 5.2), 2.6, 2.6);
            for (int i = 0; i < 6; i++)
            {
                var pen = new Pen(Brush(Porcelain), Gauges[i]) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                dc.DrawLine(pen, new Point(StringX(i, -4), -4), new Point(StringX(i, 68), 68));
            }
        }
        dc.Pop();
    }

    /// <summary>
    /// The f-hole: a tapered slot along an integral-shaped curve, a round eye at each end (the lower one larger,
    /// as on a real f-hole) and the nicks at the waist drawn as one crossbar.
    /// </summary>
    private static Geometry FHole(bool small)
    {
        Point p0 = new(33, 11), c1 = new(19, 12.5), c2 = new(31, 51.5), p3 = new(17, 53);
        double endWidth = small ? 5 : 2.6, waistWidth = small ? 8 : 6.2;
        const int samples = 90;
        var left = new List<Point>();
        var right = new List<Point>();
        for (int k = 0; k <= samples; k++)
        {
            double t = (double)k / samples, v = 1 - t;
            var pt = new Point(v * v * v * p0.X + 3 * v * v * t * c1.X + 3 * v * t * t * c2.X + t * t * t * p3.X,
                               v * v * v * p0.Y + 3 * v * v * t * c1.Y + 3 * v * t * t * c2.Y + t * t * t * p3.Y);
            var d = new Vector(3 * v * v * (c1.X - p0.X) + 6 * v * t * (c2.X - c1.X) + 3 * t * t * (p3.X - c2.X),
                               3 * v * v * (c1.Y - p0.Y) + 6 * v * t * (c2.Y - c1.Y) + 3 * t * t * (p3.Y - c2.Y));
            d.Normalize();
            var normal = new Vector(-d.Y, d.X);
            double half = (endWidth + (waistWidth - endWidth) * Math.Pow(Math.Sin(Math.PI * t), 0.9)) / 2;
            left.Add(pt + normal * half);
            right.Add(pt - normal * half);
        }
        right.Reverse();
        var outline = new PathFigure { StartPoint = left[0], IsClosed = true, IsFilled = true };
        outline.Segments.Add(new PolyLineSegment(left.Skip(1).Concat(right), true));

        Geometry shape = new PathGeometry([outline]);
        shape = Geometry.Combine(shape, new EllipseGeometry(p0, small ? 5.4 : 4.4, small ? 5.4 : 4.4), GeometryCombineMode.Union, null);
        shape = Geometry.Combine(shape, new EllipseGeometry(p3, small ? 5.8 : 4.9, small ? 5.8 : 4.9), GeometryCombineMode.Union, null);
        var nicks = new RectangleGeometry(new Rect(18.5, small ? 29.9 : 30.4, 13, small ? 4.4 : 3.4), 1.2, 1.2);
        return Geometry.Combine(shape, nicks, GeometryCombineMode.Union, null);
    }

    /// <summary>The mark as a DrawingImage for Themes/Brand.xaml, using the app's brush resources.</summary>
    private static string MarkXaml()
    {
        var inv = CultureInfo.InvariantCulture;
        string N(double v) => Math.Round(v, 2).ToString("0.##", inv);
        string fhole = ((IFormattable)PathGeometry.CreateFromGeometry(FHole(false))).ToString("0.##", inv);
        double left = StringX(0, BridgeY) - 3.5, right = StringX(5, BridgeY) + 3.5;

        var sb = new StringBuilder();
        sb.AppendLine("<DrawingImage x:Key=\"DifFuseMark\">");
        sb.AppendLine("    <DrawingImage.Drawing>");
        sb.AppendLine("        <DrawingGroup>");
        sb.AppendLine("            <DrawingGroup.ClipGeometry>");
        sb.AppendLine("                <RectangleGeometry Rect=\"0,0,64,64\" RadiusX=\"14\" RadiusY=\"14\" />");
        sb.AppendLine("            </DrawingGroup.ClipGeometry>");
        sb.AppendLine("            <GeometryDrawing Brush=\"{StaticResource MarkTileBrush}\">");
        sb.AppendLine("                <GeometryDrawing.Geometry>");
        sb.AppendLine("                    <RectangleGeometry Rect=\"0,0,64,64\" RadiusX=\"14\" RadiusY=\"14\" />");
        sb.AppendLine("                </GeometryDrawing.Geometry>");
        sb.AppendLine("            </GeometryDrawing>");
        sb.AppendLine("            <!-- f-hole -->");
        sb.AppendLine("            <DrawingGroup>");
        sb.AppendLine("                <DrawingGroup.Transform>");
        sb.AppendLine("                    <TranslateTransform X=\"-2\" />");
        sb.AppendLine("                </DrawingGroup.Transform>");
        sb.AppendLine($"                <GeometryDrawing Brush=\"{{StaticResource TextPrimaryBrush}}\" Geometry=\"{fhole}\" />");
        sb.AppendLine("            </DrawingGroup>");
        sb.AppendLine("            <!-- bridge -->");
        sb.AppendLine("            <GeometryDrawing Brush=\"{StaticResource AmpBrush}\">");
        sb.AppendLine("                <GeometryDrawing.Geometry>");
        sb.AppendLine($"                    <RectangleGeometry Rect=\"{N(left)},{N(BridgeY - 2.6)},{N(right - left)},5.2\" RadiusX=\"2.6\" RadiusY=\"2.6\" />");
        sb.AppendLine("                </GeometryDrawing.Geometry>");
        sb.AppendLine("            </GeometryDrawing>");
        sb.AppendLine("            <!-- strings, low E to high E -->");
        for (int i = 0; i < 6; i++)
        {
            sb.AppendLine("            <GeometryDrawing>");
            sb.AppendLine("                <GeometryDrawing.Pen>");
            sb.AppendLine($"                    <Pen Brush=\"{{StaticResource TextPrimaryBrush}}\" Thickness=\"{N(Gauges[i])}\" StartLineCap=\"Round\" EndLineCap=\"Round\" />");
            sb.AppendLine("                </GeometryDrawing.Pen>");
            sb.AppendLine("                <GeometryDrawing.Geometry>");
            sb.AppendLine($"                    <LineGeometry StartPoint=\"{N(StringX(i, -4))},-4\" EndPoint=\"{N(StringX(i, 68))},68\" />");
            sb.AppendLine("                </GeometryDrawing.Geometry>");
            sb.AppendLine("            </GeometryDrawing>");
        }
        sb.AppendLine("        </DrawingGroup>");
        sb.AppendLine("    </DrawingImage.Drawing>");
        sb.AppendLine("</DrawingImage>");
        return sb.ToString();
    }

    /// <summary>The sizes up to 48 px, magnified and at real size on a dark and a light taskbar.</summary>
    private static BitmapSource SmallSizeCheck(List<(int Size, byte[] Png)> frames) => Render(1200, 560, dc =>
    {
        dc.DrawRectangle(Brush(Color.FromRgb(0x20, 0x20, 0x20)), null, new Rect(0, 0, 1200, 280));
        dc.DrawRectangle(Brush(Color.FromRgb(0xEE, 0xEE, 0xF0)), null, new Rect(0, 280, 1200, 280));
        double x = 20;
        foreach (var (size, png) in frames.Where(f => f.Size <= 48))
        {
            var img = Decode(png);
            double zoom = size <= 24 ? 6 : 4;
            dc.DrawImage(img, new Rect(x, 20, size * zoom, size * zoom));
            dc.DrawImage(img, new Rect(x, 236 - size, size, size));
            dc.DrawImage(img, new Rect(x, 300, size, size));
            x += size * zoom + 24;
        }
    });

    /// <summary>An .ico whose entries are PNG-compressed (supported since Windows Vista).</summary>
    private static void WriteIco(string path, List<(int Size, byte[] Png)> frames)
    {
        using var w = new BinaryWriter(File.Create(path));
        w.Write((ushort)0);
        w.Write((ushort)1);
        w.Write((ushort)frames.Count);
        int offset = 6 + 16 * frames.Count;
        foreach (var (size, png) in frames)
        {
            w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)0);
            w.Write((byte)0);
            w.Write((ushort)1);
            w.Write((ushort)32);
            w.Write(png.Length);
            w.Write(offset);
            offset += png.Length;
        }
        foreach (var (_, png) in frames) w.Write(png);
    }

    private static Brush Brush(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static BitmapSource Render(int w, int h, Action<DrawingContext> draw)
    {
        var dv = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.NearestNeighbor);
        using (var dc = dv.RenderOpen()) draw(dc);
        var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(dv);
        return bmp;
    }

    private static byte[] PngBytes(BitmapSource bmp)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    private static void Save(BitmapSource bmp, string path) => File.WriteAllBytes(path, PngBytes(bmp));

    private static BitmapSource Decode(byte[] png)
    {
        var img = new BitmapImage();
        img.BeginInit();
        img.StreamSource = new MemoryStream(png);
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.EndInit();
        img.Freeze();
        return img;
    }
}
