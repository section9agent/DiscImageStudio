using System.IO;
using System.Text.Json;
using DiscImageStudio.Core.Calibration;
using DiscImageStudio.ImageIO;

/// <summary>Calibration assists (fork addition), checked against the real example records in
/// docs/examples: the automatic rim/hole finder must land on the hand-placed rectification points,
/// and edge tracing on a rectified photo must follow the hand-traced band edges.</summary>
internal static class CalibrationAssistTests
{
    private const string Examples = "docs/examples/verbatim-azo-20261104";

    internal static void Run()
    {
        string root = FindRepositoryRoot();
        string directory = Path.Combine(Path.GetTempPath(), $"calibration-assist-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            foreach (string record in Directory.GetFiles(Path.Combine(root, Examples), "*.calibration.json").Order())
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(record));
                JsonElement photo = document.RootElement.GetProperty("photo");
                string png = Path.Combine(directory, Path.GetFileNameWithoutExtension(record) + ".png");
                File.WriteAllBytes(png, Convert.FromBase64String(photo.GetProperty("imagePngBase64").GetString()!));
                BgraRaster raster = RasterCodec.LoadBgra32(png);
                CalibrationPoint[] outer = Points(photo.GetProperty("outerBoundary"));
                CalibrationPoint[] hole = Points(photo.GetProperty("holeBoundary"));
                string name = Path.GetFileName(record);

                CalibrationPhotoEdgeDetection found = CalibrationPhotoEdgeDetector.Detect(raster.Pixels, raster.Width,
                    raster.Height, raster.Width * 4, 64);
                Require(found.OuterFound && found.HoleFound, $"{name}: rim and hole are found");
                double outerError = outer.Max(p => DistanceToLoop(found.Outer, p));
                double holeError = hole.Max(p => DistanceToLoop(found.Hole, p));
                Require(outerError < 8 && holeError < 6,
                    $"{name}: detected edges lie on the hand-placed points (rim {outerError:0.0} px, hole {holeError:0.0} px)");

                if (photo.TryGetProperty("boundarySegments", out JsonElement segments) && segments.GetArrayLength() > 0)
                {
                    TestEdgeTracing(name, raster, outer, hole, segments);
                }
            }

            TestWideLineSnap();
            Throws(() => CalibrationPhotoEdgeDetector.Detect(new byte[10], 100, 100, 400), "a short pixel buffer is rejected");
            CalibrationPhotoEdgeDetection blank = CalibrationPhotoEdgeDetector.Detect(new byte[200 * 200 * 4], 200, 200, 800);
            Require(!blank.OuterFound && blank.Outer.Count == 8 && blank.Hole.Count == 8,
                "a photo without a disc falls back to the default circle");
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
        }
    }

    private static void TestEdgeTracing(string name, BgraRaster raster, CalibrationPoint[] outer, CalibrationPoint[] hole,
        JsonElement segments)
    {
        CalibrationPhotoFitResult fit = CalibrationPhotoRectifier.Fit(outer, hole);
        Require(fit.Succeeded && fit.Transform is not null, $"{name}: the hand points rectify");
        const double CanvasRadius = 63;
        const int Size = 1600;
        byte[] rectified = Rectify(raster, fit.Transform!, CanvasRadius, Size);
        int hit = 0, total = 0;
        List<double> offLine = [];
        foreach (JsonElement segment in segments.EnumerateArray())
        {
            CalibrationPoint[] hand = Points(segment.GetProperty("points"));
            if (hand.Length < 3) continue;
            IReadOnlyList<CalibrationPoint> path = CalibrationLineTracer.Trace(rectified, Size, Size, Size * 4, CanvasRadius,
                hand[hand.Length / 2], CalibrationTraceFeature.Edge, 8, 59);
            total += hand.Length;
            if (path.Count < 2) continue;
            hit += hand.Count(p => DistanceToPath(path, p) < 0.3);
            offLine.AddRange(path.Select(p => DistanceToPath(hand, p)).Where(d => d < 1.0));
        }

        double coverage = hit / (double)total;
        offLine.Sort();
        double p90 = offLine.Count == 0 ? 0 : offLine[(int)(0.9 * (offLine.Count - 1))];
        Require(coverage > 0.3, $"{name}: one click per band edge covers {coverage:P0} of the hand-traced points");
        Require(p90 < 0.3, $"{name}: traced points stay on the hand-traced edges (p90 {p90:0.00} mm)");
    }

    /// <summary>Same mapping as the app's rectified photo: pixel centres over [-R, R] mm.</summary>
    private static byte[] Rectify(BgraRaster raster, CalibrationPhotoTransform transform, double radius, int size)
    {
        byte[] output = new byte[size * size * 4];
        double step = 2 * radius / size;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            CalibrationPoint source = transform.DiscToSource(new CalibrationPoint(((x + 0.5) * step) - radius, ((y + 0.5) * step) - radius));
            double sx = Math.Clamp(source.X, 0, raster.Width - 1.001), sy = Math.Clamp(source.Y, 0, raster.Height - 1.001);
            int x0 = (int)sx, y0 = (int)sy;
            double fx = sx - x0, fy = sy - y0;
            for (int c = 0; c < 4; c++)
            {
                int i = (((y0 * raster.Width) + x0) * 4) + c;
                double value = (raster.Pixels[i] * (1 - fx) * (1 - fy)) + (raster.Pixels[i + 4] * fx * (1 - fy))
                               + (raster.Pixels[i + (raster.Width * 4)] * (1 - fx) * fy)
                               + (raster.Pixels[i + (raster.Width * 4) + 4] * fx * fy);
                output[(((y * size) + x) * 4) + c] = (byte)Math.Round(value);
            }
        }

        return output;
    }

    private static CalibrationPoint[] Points(JsonElement array) => array.EnumerateArray()
        .Select(p => new CalibrationPoint(p.GetProperty("x").GetDouble(), p.GetProperty("y").GetDouble())).ToArray();

    private static double DistanceToLoop(IReadOnlyList<CalibrationPoint> loop, CalibrationPoint p)
        => DistanceToPath([.. loop, loop[0]], p);

    private static double DistanceToPath(IReadOnlyList<CalibrationPoint> path, CalibrationPoint p)
    {
        double best = double.MaxValue;
        for (int i = 0; i + 1 < path.Count; i++)
        {
            CalibrationPoint a = path[i], b = path[i + 1];
            double abx = b.X - a.X, aby = b.Y - a.Y, length = (abx * abx) + (aby * aby);
            double t = length == 0 ? 0 : Math.Clamp((((p.X - a.X) * abx) + ((p.Y - a.Y) * aby)) / length, 0, 1);
            double dx = p.X - (a.X + (t * abx)), dy = p.Y - (a.Y + (t * aby));
            best = Math.Min(best, Math.Sqrt((dx * dx) + (dy * dy)));
        }

        return best;
    }

    private static string FindRepositoryRoot()
    {
        foreach (string start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            for (DirectoryInfo? directory = new(start); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "DiscImageStudio.slnx"))) return directory.FullName;
            }
        }

        throw new InvalidOperationException("Calibration assist test: cannot locate DiscImageStudio.slnx.");
    }

    /// <summary>A rectified photo of the 6 mm wide round 3 stripe, pointing up from the hub.</summary>
    private static void TestWideLineSnap()
    {
        const int size = 1200;
        const double radius = 60, step = 2 * radius / size;
        byte[] pixels = new byte[size * size * 4];
        var noise = new Random(7);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                double mx = ((x + 0.5) * step) - radius, my = ((y + 0.5) * step) - radius;
                // A slight curl, as a burn with a small leftover error would show.
                double centre = 0.02 * (my * my) / 30;
                bool dark = my < -10 && Math.Abs(mx - centre) <= CalibrationTarget.SingleRadialLineWidthMm / 2;
                byte value = (byte)Math.Clamp((dark ? 70 : 190) + noise.Next(-12, 13), 0, 255);
                int i = ((y * size) + x) * 4;
                pixels[i] = pixels[i + 1] = pixels[i + 2] = value;
                pixels[i + 3] = 255;
            }

        foreach (double y in new[] { -15.0, -30, -45 })
            foreach (double offset in new[] { -2.2, 0.4, 2.5 })
            {
                double centre = 0.02 * (y * y) / 30;
                CalibrationPoint? snapped = CalibrationLineTracer.SnapBand(pixels, size, size, size * 4, radius,
                    new(centre + offset, y), CalibrationTarget.SingleRadialLineWidthMm);
                Require(snapped is { } p && Math.Abs(p.X - centre) < 0.2,
                    $"a click {offset:+0.0;-0.0} mm off the wide line at {-y} mm snaps to its centre");
            }

        Require(CalibrationLineTracer.SnapBand(pixels, size, size, size * 4, radius, new(12, -30),
            CalibrationTarget.SingleRadialLineWidthMm) is null, "a click beside the wide line is left where it is");
    }

    private static void Throws(Action action, string name)
    {
        try { action(); }
        catch (ArgumentException) { Console.WriteLine("PASS " + name); return; }
        throw new InvalidOperationException("Calibration assist test failed: expected an exception: " + name);
    }

    private static void Require(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("Calibration assist test failed: " + name);
        Console.WriteLine("PASS " + name);
    }
}
