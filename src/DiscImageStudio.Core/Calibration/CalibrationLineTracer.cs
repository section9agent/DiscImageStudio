namespace DiscImageStudio.Core.Calibration;

/// <summary>What the tracer follows on a rectified calibration photo.</summary>
public enum CalibrationTraceFeature
{
    /// <summary>A thin line, lighter or darker than its surroundings (fine cross arms).</summary>
    Line,

    /// <summary>The colour edge between two regions (boundary-stage band edges).</summary>
    Edge,
}

/// <summary>Snap-to-line tracing on a rectified photo (fork addition). From one click it finds the
/// feature's centre and direction, then walks both ways along it, re-centring on the feature at each
/// step. It stops where the feature fades, leaves the disc, or where a second comparable line
/// appears beside it (a crossing): there the user clicks again past the crossing, so a trace
/// never silently jumps onto a neighbouring ring. All coordinates are millimetres on the disc,
/// with the rectified image covering [-canvasRadius, canvasRadius] on both axes.</summary>
public static class CalibrationLineTracer
{
    private const double StepMm = 0.4;
    private const double LineSearchHalfMm = 0.5;
    private const double EdgeSearchHalfMm = 0.35;
    private const double ProbeWidthMm = 0.25;

    public static IReadOnlyList<CalibrationPoint> Trace(byte[] bgra, int width, int height, int stride,
        double canvasRadiusMm, CalibrationPoint startMm, CalibrationTraceFeature feature,
        double minRadiusMm, double maxRadiusMm, double spacingMm = 1.0, double maxLengthMm = 80,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bgra);
        ArgumentNullException.ThrowIfNull(startMm);
        if (width < 16 || height < 16 || stride < width * 4 || bgra.Length < (long)stride * height)
            throw new ArgumentException("Pixel buffer is too small.", nameof(bgra));
        Image image = new(bgra, width, height, stride, canvasRadiusMm);
        if (Initial(image, startMm, feature) is not { } start) return [];
        List<CalibrationPoint>[] halves = [[], []];
        for (int half = 0; half < 2; half++)
        {
            double dx = start.Dx * (half == 0 ? 1 : -1), dy = start.Dy * (half == 0 ? 1 : -1);
            double px = start.X, py = start.Y, running = start.Strength, length = 0;
            int misses = 0;
            while (length < maxLengthMm)
            {
                cancellationToken.ThrowIfCancellationRequested();
                double qx = px + (dx * StepMm), qy = py + (dy * StepMm);
                double nx = -dy, ny = dx;
                double searchHalf = feature == CalibrationTraceFeature.Edge ? EdgeSearchHalfMm : LineSearchHalfMm;
                Profile profile = Measure(image, qx, qy, nx, ny, searchHalf, 0.025, feature);
                Peak? peak = profile.Best(0.3 * running);
                if (peak is { } found && profile.Ambiguous(found, 0.3 * running)) break;
                if (peak is null)
                {
                    if (++misses > 5) break;
                    px = qx;
                    py = qy;
                    length += StepMm;
                    continue;
                }

                misses = 0;
                running = (0.85 * running) + (0.15 * peak.Value.Strength);
                double newX = qx + (nx * peak.Value.Offset), newY = qy + (ny * peak.Value.Offset);
                double ex = newX - px, ey = newY - py, el = Math.Sqrt((ex * ex) + (ey * ey));
                if (el > 0)
                {
                    dx = (0.5 * dx) + (0.5 * ex / el);
                    dy = (0.5 * dy) + (0.5 * ey / el);
                    double dl = Math.Sqrt((dx * dx) + (dy * dy));
                    dx /= dl;
                    dy /= dl;
                }

                px = newX;
                py = newY;
                length += StepMm;
                double radius = Math.Sqrt((px * px) + (py * py));
                if (radius < minRadiusMm || radius > maxRadiusMm) break;
                halves[half].Add(new CalibrationPoint(px, py));
            }
        }

        List<CalibrationPoint> path = [];
        for (int i = halves[1].Count - 1; i >= 0; i--) path.Add(halves[1][i]);
        path.Add(new CalibrationPoint(start.X, start.Y));
        path.AddRange(halves[0]);
        return Resample(path, spacingMm);
    }

    /// <summary>Snaps one click onto the nearest feature centre without tracing; null when no
    /// clear feature is under the click.</summary>
    public static CalibrationPoint? Snap(byte[] bgra, int width, int height, int stride, double canvasRadiusMm,
        CalibrationPoint clickMm, CalibrationTraceFeature feature)
    {
        Image image = new(bgra, width, height, stride, canvasRadiusMm);
        return Initial(image, clickMm, feature) is { } start ? new CalibrationPoint(start.X, start.Y) : null;
    }

    private readonly record struct Start(double X, double Y, double Dx, double Dy, double Strength);

    /// <summary>The direction whose perpendicular profile has the sharpest, most consistent
    /// peak at the click, and the feature centre on it.</summary>
    private static Start? Initial(Image image, CalibrationPoint click, CalibrationTraceFeature feature)
    {
        Start? best = null;
        double bestScore = 0;
        for (int step = 0; step < 36; step++)
        {
            double angle = Math.PI * step / 36, dx = Math.Cos(angle), dy = Math.Sin(angle), nx = -dy, ny = dx;
            Profile profile = Measure(image, click.X, click.Y, nx, ny, 0.6, 0.05, feature);
            if (profile.Best(0) is not { } peak || Math.Abs(peak.Offset) > 0.35) continue;
            double score = peak.Strength;
            foreach (double along in new[] { -0.6, 0.6 })
            {
                double qx = click.X + (dx * along) + (nx * peak.Offset), qy = click.Y + (dy * along) + (ny * peak.Offset);
                Peak? again = Measure(image, qx, qy, nx, ny, 0.3, 0.05, feature).Best(0);
                score = again is { } a && Math.Abs(a.Offset) < 0.2 ? Math.Min(score, a.Strength) : 0;
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = new Start(click.X + (nx * peak.Offset), click.Y + (ny * peak.Offset), dx, dy, peak.Strength);
            }
        }

        return best;
    }

    private static Profile Measure(Image image, double x, double y, double nx, double ny, double half,
        double resolution, CalibrationTraceFeature feature)
    {
        int count = (int)Math.Round(2 * half / resolution) + 1;
        double[] offsets = new double[count], values = new double[count];
        for (int i = 0; i < count; i++)
        {
            double s = -half + (i * resolution);
            double px = x + (s * nx), py = y + (s * ny);
            offsets[i] = s;
            double ox = px + (ProbeWidthMm * nx), oy = py + (ProbeWidthMm * ny);
            double ix = px - (ProbeWidthMm * nx), iy = py - (ProbeWidthMm * ny);
            if (feature == CalibrationTraceFeature.Line)
            {
                values[i] = Math.Abs(((image.Gray(ox, oy) + image.Gray(ix, iy)) / 2) - image.Gray(px, py));
            }
            else
            {
                double sum = 0;
                for (int channel = 0; channel < 3; channel++)
                {
                    double d = image.Channel(ox, oy, channel) - image.Channel(ix, iy, channel);
                    sum += d * d;
                }

                values[i] = Math.Sqrt(sum);
            }
        }

        return new Profile(offsets, values);
    }

    private readonly record struct Peak(double Offset, double Strength);

    private sealed class Profile(double[] offsets, double[] values)
    {
        private IEnumerable<int> Peaks(double floor)
        {
            for (int i = 1; i < values.Length - 1; i++)
                if (values[i] >= values[i - 1] && values[i] > values[i + 1] && values[i] > floor) yield return i;
        }

        public Peak? Best(double floor)
        {
            int[] peaks = Peaks(floor).ToArray();
            if (peaks.Length == 0) return null;
            double strongest = peaks.Max(i => values[i]);
            int best = peaks.OrderBy(i => Math.Abs(offsets[i]) - (0.15 * values[i] / Math.Max(strongest, 1e-9))).First();
            double denominator = values[best - 1] - (2 * values[best]) + values[best + 1];
            double shift = denominator == 0 ? 0 : 0.5 * (values[best - 1] - values[best + 1]) / denominator;
            return new Peak(offsets[best] + (shift * (offsets[1] - offsets[0])), values[best]);
        }

        /// <summary>A comparable second line in the window: a crossing or a close neighbour.</summary>
        public bool Ambiguous(Peak chosen, double floor)
            => Peaks(floor).Any(i => Math.Abs(offsets[i] - chosen.Offset) > 0.12 && values[i] > 0.6 * chosen.Strength);
    }

    private static IReadOnlyList<CalibrationPoint> Resample(List<CalibrationPoint> path, double spacing)
    {
        if (path.Count < 2) return path;
        List<CalibrationPoint> result = [path[0]];
        double carried = 0;
        for (int i = 1; i < path.Count; i++)
        {
            CalibrationPoint a = path[i - 1], b = path[i];
            double segment = Math.Sqrt(((b.X - a.X) * (b.X - a.X)) + ((b.Y - a.Y) * (b.Y - a.Y)));
            while (carried + segment >= spacing && segment > 0)
            {
                double t = (spacing - carried) / segment;
                a = new CalibrationPoint(a.X + ((b.X - a.X) * t), a.Y + ((b.Y - a.Y) * t));
                result.Add(a);
                segment = Math.Sqrt(((b.X - a.X) * (b.X - a.X)) + ((b.Y - a.Y) * (b.Y - a.Y)));
                carried = 0;
            }

            carried += segment;
        }

        return result;
    }

    private sealed class Image(byte[] bgra, int width, int height, int stride, double canvasRadiusMm)
    {
        private readonly double _step = 2 * canvasRadiusMm / width;
        private readonly double _stepY = 2 * canvasRadiusMm / height;

        public double Channel(double xMm, double yMm, int channel)
        {
            double x = Math.Clamp(((xMm + canvasRadiusMm) / _step) - 0.5, 0, width - 1.001);
            double y = Math.Clamp(((yMm + canvasRadiusMm) / _stepY) - 0.5, 0, height - 1.001);
            int x0 = (int)x, y0 = (int)y;
            double fx = x - x0, fy = y - y0;
            int i = (y0 * stride) + (x0 * 4) + channel;
            return (bgra[i] * (1 - fx) * (1 - fy)) + (bgra[i + 4] * fx * (1 - fy))
                   + (bgra[i + stride] * (1 - fx) * fy) + (bgra[i + stride + 4] * fx * fy);
        }

        public double Gray(double xMm, double yMm)
            => (Channel(xMm, yMm, 0) + Channel(xMm, yMm, 1) + Channel(xMm, yMm, 2)) / 3;
    }
}
