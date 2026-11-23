namespace DiscImageStudio.Core.Calibration;

/// <summary>Result of <see cref="CalibrationPhotoEdgeDetector.Detect"/>: suggested points on the
/// disc's physical outer edge and on the centre hole's cut edge, in source-photo pixels.
/// <see cref="OuterFound"/>/<see cref="HoleFound"/> say which contours were actually detected;
/// when the hole is not found its points are a guess scaled from the rim.</summary>
public sealed record CalibrationPhotoEdgeDetection(
    bool OuterFound,
    bool HoleFound,
    IReadOnlyList<CalibrationPoint> Outer,
    IReadOnlyList<CalibrationPoint> Hole,
    double OuterSupport,
    double HoleSupport,
    string Message);

/// <summary>Finds the disc rim and centre hole in a calibration photo so the user only has to
/// check the 16 rectification points instead of placing them (fork addition).
/// <list type="number">
/// <item>On a copy scaled to 640 px, every strong edge votes along its gradient for the centre
/// of the concentric rings (tracks, bands and the rim all agree).</item>
/// <item>Rays from that centre collect the outermost strong edges; a RANSAC ellipse over them is
/// the rim, then refined to the outer side of the rim bevel at full resolution.</item>
/// <item>In a full-resolution crop of the hub, the hub rings vote for the projected disc centre;
/// thin dark rings around it are histogrammed by radius and the innermost strong one (the
/// hole's cut edge, inside the stacking ring) is fitted.</item>
/// </list>
/// Deterministic: the RANSAC sampler uses a fixed seed.</summary>
public static class CalibrationPhotoEdgeDetector
{
    private const int CoarseMaxSide = 640;
    private const int CoarseRays = 180;
    private const int RimRays = 360;
    private const int HoleRays = 240;
    private const double HoleRatioMin = 0.09;
    private const double HoleRatioMax = 0.16;

    /// <param name="bgra">32-bit BGRA pixels (top-down).</param>
    public static CalibrationPhotoEdgeDetection Detect(byte[] bgra, int width, int height, int stride,
        int pointsPerContour = 8, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bgra);
        if (width < 64 || height < 64) return Fail(width, height, pointsPerContour, "The photo is too small to find the disc edges.");
        if (stride < width * 4 || bgra.Length < (long)stride * height) throw new ArgumentException("Pixel buffer is too small.", nameof(bgra));
        if (pointsPerContour is < 5 or > 64) throw new ArgumentOutOfRangeException(nameof(pointsPerContour));
        Photo photo = new(bgra, width, height, stride);
        Random random = new(12345);

        // 1. Coarse rim on a downscaled copy.
        double scale = Math.Max(1.0, Math.Max(width, height) / (double)CoarseMaxSide);
        Small small = Small.From(photo, scale);
        cancellationToken.ThrowIfCancellationRequested();
        (float[] gx, float[] gy, float[] mag) = Gradients(small.Channels, small.Width, small.Height);
        (double cx, double cy) = VoteCenter(gx, gy, mag, small.Width, small.Height,
            0.05 * Math.Min(small.Width, small.Height), 0.55 * Math.Max(small.Width, small.Height), 0.92, cancellationToken);
        double floor = 0.35 * Percentile(mag, 0.97, positiveOnly: true);
        double maxRadius = Math.Sqrt((double)small.Width * small.Width + (double)small.Height * small.Height) / 2;
        List<Peak>[] candidates = RayPeaks(gx, gy, small.Width, small.Height, cx, cy,
            _ => 0.25 * Math.Min(small.Width, small.Height), _ => maxRadius, CoarseRays, keep: 2, outermost: true, floor);
        Ellipse? coarse = Ransac(candidates, cx, cy, 2.5, 1500, random, preferSmaller: false, cancellationToken)?.Ellipse;
        if (coarse is null)
            return Fail(width, height, pointsPerContour, "Couldn't find the disc's outer edge. Drag the points onto it.");
        Ellipse rimSmall = RefineCoarse(gx, gy, small.Width, small.Height, coarse.Value);
        cancellationToken.ThrowIfCancellationRequested();

        // 2. Rim at full resolution (outer side of the bevel).
        Ellipse rimEstimate = rimSmall.Scaled(scale);
        (Ellipse? rim, double outerSupport) = RefineRimFull(photo, rimEstimate, cancellationToken);
        if (rim is null || outerSupport < 0.45 || !InsideImage(rim.Value, width, height))
        {
            Ellipse guess = rim ?? rimEstimate;
            return new CalibrationPhotoEdgeDetection(false, false,
                guess.Points(pointsPerContour), guess.ScaledAboutCenter(7.5 / 60).Points(pointsPerContour),
                outerSupport, 0,
                "The disc's outer edge isn't fully visible or is too faint. Photograph the whole disc against a plain background, or drag the points yourself.");
        }

        // 3. Centre hole.
        (Ellipse? hole, double holeSupport) = DetectHole(photo, rim.Value, random, cancellationToken);
        if (hole is null || holeSupport < 0.4)
        {
            return new CalibrationPhotoEdgeDetection(true, false, rim.Value.Points(pointsPerContour),
                rim.Value.ScaledAboutCenter(7.5 / 60).Points(pointsPerContour), outerSupport, holeSupport,
                "Found the outer edge. Couldn't find the centre hole's edge: drag the inner points onto the thin edge of the hole.");
        }

        return new CalibrationPhotoEdgeDetection(true, true, rim.Value.Points(pointsPerContour),
            hole.Value.Points(pointsPerContour), outerSupport, holeSupport,
            "Found the outer edge and the centre hole. Check that the points sit on them, then rectify.");
    }

    private static CalibrationPhotoEdgeDetection Fail(int width, int height, int count, string message)
    {
        Ellipse guess = new(width / 2.0, height / 2.0, width * 0.43, height * 0.43, 0);
        return new CalibrationPhotoEdgeDetection(false, false, guess.Points(count),
            guess.ScaledAboutCenter(0.125).Points(count), 0, 0, message);
    }

    private static bool InsideImage(Ellipse rim, int width, int height)
        => rim.Points(72).All(p => p.X >= 1 && p.Y >= 1 && p.X <= width - 2 && p.Y <= height - 2);

    // ---------------------------------------------------------------- image access

    private sealed class Photo(byte[] bgra, int width, int height, int stride)
    {
        public int Width { get; } = width;
        public int Height { get; } = height;

        public double Channel(double x, double y, int channel)
        {
            x = Math.Clamp(x, 0, Width - 1.001);
            y = Math.Clamp(y, 0, Height - 1.001);
            int x0 = (int)x, y0 = (int)y;
            double fx = x - x0, fy = y - y0;
            int i00 = (y0 * stride) + (x0 * 4) + channel;
            int i10 = i00 + 4, i01 = i00 + stride, i11 = i01 + 4;
            return (bgra[i00] * (1 - fx) * (1 - fy)) + (bgra[i10] * fx * (1 - fy))
                   + (bgra[i01] * (1 - fx) * fy) + (bgra[i11] * fx * fy);
        }

        public float Gray(int x, int y)
        {
            int i = (y * stride) + (x * 4);
            return (bgra[i] + bgra[i + 1] + bgra[i + 2]) / 3f;
        }
    }

    private sealed class Small
    {
        public required int Width { get; init; }
        public required int Height { get; init; }
        public required float[][] Channels { get; init; }

        public static Small From(Photo photo, double scale)
        {
            int width = Math.Max(1, (int)Math.Round(photo.Width / scale));
            int height = Math.Max(1, (int)Math.Round(photo.Height / scale));
            float[][] channels = [new float[width * height], new float[width * height], new float[width * height]];
            int step = Math.Max(1, (int)Math.Floor(scale));
            Parallel.For(0, height, y =>
            {
                for (int x = 0; x < width; x++)
                {
                    int sx0 = (int)(x * scale), sy0 = (int)(y * scale);
                    for (int c = 0; c < 3; c++)
                    {
                        double sum = 0;
                        int count = 0;
                        for (int dy = 0; dy < step; dy++)
                        for (int dx = 0; dx < step; dx++)
                        {
                            sum += photo.Channel(Math.Min(sx0 + dx, photo.Width - 1), Math.Min(sy0 + dy, photo.Height - 1), c);
                            count++;
                        }

                        channels[c][(y * width) + x] = (float)(sum / count);
                    }
                }
            });
            return new Small { Width = width, Height = height, Channels = channels };
        }
    }

    private static float[] Blur(float[] source, int width, int height, int passes)
    {
        float[] a = (float[])source.Clone();
        float[] b = new float[a.Length];
        for (int pass = 0; pass < passes; pass++)
        {
            for (int y = 0; y < height; y++)
            {
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    float l = a[row + Math.Max(0, x - 1)], r = a[row + Math.Min(width - 1, x + 1)];
                    b[row + x] = (l + (2 * a[row + x]) + r) / 4;
                }
            }

            for (int y = 0; y < height; y++)
            {
                int up = Math.Max(0, y - 1) * width, down = Math.Min(height - 1, y + 1) * width, row = y * width;
                for (int x = 0; x < width; x++) a[row + x] = (b[up + x] + (2 * b[row + x]) + b[down + x]) / 4;
            }
        }

        return a;
    }

    /// <summary>Sobel gradient of the channel with the strongest response at each pixel.</summary>
    private static (float[] Gx, float[] Gy, float[] Mag) Gradients(float[][] channels, int width, int height)
    {
        float[] gx = new float[width * height], gy = new float[width * height], mag = new float[width * height];
        foreach (float[] raw in channels)
        {
            float[] a = Blur(raw, width, height, 2);
            for (int y = 2; y < height - 2; y++)
            for (int x = 2; x < width - 2; x++)
            {
                int i = (y * width) + x;
                float sx = ((a[i + 1] - a[i - 1]) * 2) + (a[i - width + 1] - a[i - width - 1]) + (a[i + width + 1] - a[i + width - 1]);
                float sy = ((a[i + width] - a[i - width]) * 2) + (a[i + width + 1] - a[i - width + 1]) + (a[i + width - 1] - a[i - width - 1]);
                float m = (sx * sx) + (sy * sy);
                if (m > mag[i])
                {
                    mag[i] = m;
                    gx[i] = sx;
                    gy[i] = sy;
                }
            }
        }

        for (int i = 0; i < mag.Length; i++) mag[i] = MathF.Sqrt(mag[i]);
        return (gx, gy, mag);
    }

    private static double Percentile(float[] values, double fraction, bool positiveOnly = false)
    {
        float[] sorted = positiveOnly ? values.Where(v => v > 0).ToArray() : (float[])values.Clone();
        if (sorted.Length == 0) return 0;
        Array.Sort(sorted);
        return sorted[Math.Clamp((int)(fraction * (sorted.Length - 1)), 0, sorted.Length - 1)];
    }

    /// <summary>Strong edges vote along their gradient line; concentric rings meet at the centre.</summary>
    private static (double X, double Y) VoteCenter(float[] gx, float[] gy, float[] mag, int width, int height,
        double minDistance, double maxDistance, double percentile, CancellationToken cancellationToken)
    {
        double threshold = Percentile(mag, percentile);
        float[] accumulator = new float[width * height];
        for (int i = 0; i < mag.Length; i++)
        {
            if (mag[i] <= threshold || mag[i] == 0) continue;
            if ((i & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            int x = i % width, y = i / width;
            double ux = gx[i] / mag[i], uy = gy[i] / mag[i];
            for (double d = minDistance; d < maxDistance; d += 1)
            {
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    int px = (int)Math.Round(x + (sign * d * ux)), py = (int)Math.Round(y + (sign * d * uy));
                    if ((uint)px < (uint)width && (uint)py < (uint)height) accumulator[(py * width) + px] += 1;
                }
            }
        }

        float[] smooth = Blur(accumulator, width, height, 3);
        int best = 0;
        for (int i = 1; i < smooth.Length; i++) if (smooth[i] > smooth[best]) best = i;
        return (best % width, best / width);
    }

    private static double Sample(float[] image, int width, int height, double x, double y)
    {
        x = Math.Clamp(x, 0, width - 1.001);
        y = Math.Clamp(y, 0, height - 1.001);
        int x0 = (int)x, y0 = (int)y;
        double fx = x - x0, fy = y - y0;
        int i = (y0 * width) + x0;
        return (image[i] * (1 - fx) * (1 - fy)) + (image[i + 1] * fx * (1 - fy))
               + (image[i + width] * (1 - fx) * fy) + (image[i + width + 1] * fx * fy);
    }

    private readonly record struct Peak(double X, double Y, double Strength, double Radius);

    private static List<Peak>[] RayPeaks(float[] gx, float[] gy, int width, int height, double cx, double cy,
        Func<int, double> from, Func<int, double> to, int rays, int keep, bool outermost, double floor)
    {
        List<Peak>[] result = new List<Peak>[rays];
        for (int k = 0; k < rays; k++)
        {
            double t = 2 * Math.PI * k / rays, c = Math.Cos(t), s = Math.Sin(t);
            List<(double R, double G, double X, double Y)> profile = [];
            for (double r = Math.Max(2, from(k)); r < to(k); r += 0.5)
            {
                double x = cx + (r * c), y = cy + (r * s);
                bool inside = x > 2 && y > 2 && x < width - 3 && y < height - 3;
                double g = inside ? Math.Abs((Sample(gx, width, height, x, y) * c) + (Sample(gy, width, height, x, y) * s)) : 0;
                profile.Add((r, g, x, y));
            }

            List<Peak> peaks = [];
            for (int i = 1; i < profile.Count - 1; i++)
            {
                if (profile[i].G >= profile[i - 1].G && profile[i].G > profile[i + 1].G && profile[i].G > floor)
                    peaks.Add(new Peak(profile[i].X, profile[i].Y, profile[i].G, profile[i].R));
            }

            result[k] = (outermost ? peaks.OrderByDescending(p => p.Radius) : peaks.OrderByDescending(p => p.Strength))
                .Take(keep).ToList();
        }

        return result;
    }

    private sealed record RansacResult(Ellipse Ellipse, double Support, List<Peak> Inliers);

    private static RansacResult? Ransac(List<Peak>[] candidates, double cx, double cy, double tolerance, int iterations,
        Random random, bool preferSmaller, CancellationToken cancellationToken)
    {
        int rays = candidates.Length;
        int[] nonEmpty = Enumerable.Range(0, rays).Where(k => candidates[k].Count > 0).ToArray();
        if (nonEmpty.Length < 12) return null;
        RansacResult? best = null;
        double bestMean = 0;
        CalibrationPoint[] sample = new CalibrationPoint[5];
        for (int iteration = 0; iteration < iterations; iteration++)
        {
            if ((iteration & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
            HashSet<int> chosen = [];
            while (chosen.Count < 5) chosen.Add(nonEmpty[random.Next(nonEmpty.Length)]);
            int n = 0;
            foreach (int k in chosen)
            {
                Peak p = candidates[k][random.Next(candidates[k].Count)];
                sample[n++] = new CalibrationPoint(p.X, p.Y);
            }

            if (Ellipse.Fit(sample) is not { } ellipse || ellipse.AxisRatio < 0.6) continue;
            double score = 0;
            List<Peak> inliers = [];
            bool valid = true;
            for (int k = 0; k < rays; k++)
            {
                double? r = ellipse.RayRadius(cx, cy, 2 * Math.PI * k / rays);
                if (r is null) { valid = false; break; }
                Peak? hit = null;
                foreach (Peak p in candidates[k])
                {
                    if (Math.Abs(p.Radius - r.Value) < tolerance && (hit is null || p.Strength > hit.Value.Strength)) hit = p;
                }

                if (hit is { } h)
                {
                    score++;
                    inliers.Add(h);
                }
            }

            if (!valid) continue;
            double mean = (ellipse.A + ellipse.B) / 2;
            bool better = best is null || score > best.Support
                || (score == best.Support && (preferSmaller ? mean < bestMean : mean > bestMean));
            if (better)
            {
                best = new RansacResult(ellipse, score, inliers);
                bestMean = mean;
            }
        }

        return best is null ? null : best with { Support = best.Support / rays };
    }

    private static Ellipse RefineCoarse(float[] gx, float[] gy, int width, int height, Ellipse ellipse)
    {
        double window = 6;
        for (int round = 0; round < 3; round++)
        {
            List<(CalibrationPoint P, double G)> points = [];
            for (int k = 0; k < RimRays; k++)
            {
                double t = 2 * Math.PI * k / RimRays, c = Math.Cos(t), s = Math.Sin(t);
                if (ellipse.RayRadius(ellipse.X, ellipse.Y, t) is not { } r) continue;
                List<(double R, double G)> profile = [];
                bool inside = true;
                for (double rr = r - window; rr < r + window; rr += 0.25)
                {
                    double x = ellipse.X + (rr * c), y = ellipse.Y + (rr * s);
                    if (x < 2 || y < 2 || x > width - 3 || y > height - 3) { inside = false; break; }
                    profile.Add((rr, Math.Abs((Sample(gx, width, height, x, y) * c) + (Sample(gy, width, height, x, y) * s))));
                }

                if (!inside || profile.Count < 3) continue;
                int top = 0;
                for (int i = 1; i < profile.Count; i++) if (profile[i].G > profile[top].G) top = i;
                int chosen = top;
                for (int i = profile.Count - 2; i > 0; i--)
                {
                    if (profile[i].G >= profile[i - 1].G && profile[i].G > profile[i + 1].G && profile[i].G > 0.5 * profile[top].G)
                    {
                        chosen = i;
                        break;
                    }
                }

                if (chosen <= 0 || chosen >= profile.Count - 1 || profile[chosen].G <= 0) continue;
                double radius = profile[chosen].R + (0.25 * Vertex(profile[chosen - 1].G, profile[chosen].G, profile[chosen + 1].G));
                points.Add((new CalibrationPoint(ellipse.X + (radius * c), ellipse.Y + (radius * s)), profile[chosen].G));
            }

            if (points.Count < 20) return ellipse;
            double cut = points.Select(p => p.G).OrderBy(g => g).ElementAt(points.Count / 5);
            List<CalibrationPoint> kept = points.Where(p => p.G > cut).Select(p => p.P).ToList();
            ellipse = RobustFit(kept, ellipse, 1.0, 2.5, rounds: 3) ?? ellipse;
            window = Math.Max(2, window / 2);
        }

        return ellipse;
    }

    private static (Ellipse? Ellipse, double Support) RefineRimFull(Photo photo, Ellipse estimate,
        CancellationToken cancellationToken)
    {
        double scale = Math.Max(1.0, Math.Max(photo.Width, photo.Height) / (double)CoarseMaxSide);
        double window = Math.Max(8, 3 * scale);
        List<CalibrationPoint> points = [];
        for (int k = 0; k < RimRays; k++)
        {
            if ((k & 31) == 0) cancellationToken.ThrowIfCancellationRequested();
            double t = 2 * Math.PI * k / RimRays, c = Math.Cos(t), s = Math.Sin(t);
            if (estimate.RayRadius(estimate.X, estimate.Y, t) is not { } r) continue;
            List<(double R, double G)> profile = [];
            bool inside = true;
            for (double rr = r - window; rr < r + (2.5 * window); rr += 0.5)
            {
                double x = estimate.X + (rr * c), y = estimate.Y + (rr * s);
                if (x < 3 || y < 3 || x > photo.Width - 4 || y > photo.Height - 4) { inside = false; break; }
                double sum = 0;
                for (int channel = 0; channel < 3; channel++)
                {
                    double d = photo.Channel(x + (1.5 * c), y + (1.5 * s), channel) - photo.Channel(x - (1.5 * c), y - (1.5 * s), channel);
                    sum += d * d;
                }

                profile.Add((rr, Math.Sqrt(sum)));
            }

            if (!inside || profile.Count < 3) continue;
            double top = profile.Max(p => p.G);
            if (top < 12) continue;
            int chosen = -1;
            for (int i = profile.Count - 2; i > 0; i--)
            {
                if (profile[i].G >= profile[i - 1].G && profile[i].G > profile[i + 1].G && profile[i].G > 0.35 * top)
                {
                    chosen = i;
                    break;
                }
            }

            if (chosen < 0) continue;
            double radius = profile[chosen].R + (0.5 * Vertex(profile[chosen - 1].G, profile[chosen].G, profile[chosen + 1].G));
            points.Add(new CalibrationPoint(estimate.X + (radius * c), estimate.Y + (radius * s)));
        }

        if (points.Count < 30) return (null, 0);
        Ellipse? fitted = RobustFit(points, estimate, 1.5, 3.0, rounds: 4, out int kept);
        return (fitted, kept / (double)RimRays);
    }

    private static (Ellipse? Ellipse, double Support) DetectHole(Photo photo, Ellipse rim, Random random,
        CancellationToken cancellationToken)
    {
        double rimRadius = (rim.A + rim.B) / 2;
        int half = (int)(0.22 * rimRadius);
        int x0 = Math.Max(0, (int)rim.X - half), y0 = Math.Max(0, (int)rim.Y - half);
        int x1 = Math.Min(photo.Width, (int)rim.X + half), y1 = Math.Min(photo.Height, (int)rim.Y + half);
        int width = x1 - x0, height = y1 - y0;
        if (width < 32 || height < 32) return (null, 0);
        float[] gray = new float[width * height];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
            gray[(y * width) + x] = photo.Gray(x0 + x, y0 + y);
        float[] crop = Blur(gray, width, height, 1);
        cancellationToken.ThrowIfCancellationRequested();

        (float[] gx, float[] gy, float[] mag) = Gradients([crop, crop, crop], width, height);
        (double cx, double cy) = VoteCenter(gx, gy, mag, width, height, 0.06 * rimRadius, 0.20 * rimRadius, 0.90,
            cancellationToken);

        double d = Math.Max(2, 0.004 * rimRadius);
        const int Bins = 80;
        double[] histogram = new double[Bins];
        List<Peak>[] candidates = new List<Peak>[HoleRays];
        for (int k = 0; k < HoleRays; k++)
        {
            double t = 2 * Math.PI * k / HoleRays, c = Math.Cos(t), s = Math.Sin(t);
            List<(double R, double V, double X, double Y)> profile = [];
            for (double r = 0.095 * rimRadius; r < 0.155 * rimRadius; r += 0.5)
            {
                double x = cx + (r * c), y = cy + (r * s);
                double value = Sample(crop, width, height, x, y);
                double outside = Sample(crop, width, height, cx + ((r + d) * c), cy + ((r + d) * s));
                double inside = Sample(crop, width, height, cx + ((r - d) * c), cy + ((r - d) * s));
                profile.Add((r, ((outside + inside) / 2) - value, x, y));
            }

            List<Peak> peaks = [];
            for (int i = 1; i < profile.Count - 1; i++)
            {
                if (profile[i].V >= profile[i - 1].V && profile[i].V > profile[i + 1].V && profile[i].V > 3)
                    peaks.Add(new Peak(profile[i].X, profile[i].Y, profile[i].V, profile[i].R));
            }

            candidates[k] = peaks.OrderByDescending(p => p.Strength).Take(3).ToList();
            foreach (Peak p in candidates[k])
            {
                int bin = (int)(((p.Radius / rimRadius) - HoleRatioMin) / (HoleRatioMax - HoleRatioMin) * Bins);
                if ((uint)bin < Bins) histogram[bin] += p.Strength;
            }
        }

        // The hole's cut edge and the stacking ring beside it are both thin rings; the hole
        // is always the innermost strong one.
        double[] smooth = new double[Bins];
        double[] kernel = [1, 2, 3, 2, 1];
        for (int i = 0; i < Bins; i++)
        for (int j = -2; j <= 2; j++)
            if ((uint)(i + j) < Bins) smooth[i] += kernel[j + 2] * histogram[i + j];
        double maximum = smooth.Max();
        if (maximum <= 0) return (null, 0);
        int ring = -1;
        for (int i = 1; i < Bins - 1; i++)
        {
            if (smooth[i] >= smooth[i - 1] && smooth[i] > smooth[i + 1] && smooth[i] > 0.5 * maximum)
            {
                ring = i;
                break;
            }
        }

        if (ring < 0) return (null, 0);
        double ratio = HoleRatioMin + ((ring + 0.5) / Bins * (HoleRatioMax - HoleRatioMin));
        for (int k = 0; k < HoleRays; k++)
            candidates[k] = candidates[k].Where(p => Math.Abs((p.Radius / rimRadius) - ratio) < 0.012).ToList();
        RansacResult? best = Ransac(candidates, cx, cy, Math.Max(1.5, 0.004 * rimRadius), 2000, random,
            preferSmaller: true, cancellationToken);
        if (best is null || best.Inliers.Count < 6) return (null, 0);
        Ellipse? hole = Ellipse.Fit(best.Inliers.Select(p => new CalibrationPoint(p.X + x0, p.Y + y0)).ToArray());
        return (hole, best.Support);
    }

    private static double Vertex(double left, double centre, double right)
    {
        double denominator = left - (2 * centre) + right;
        return denominator == 0 ? 0 : 0.5 * (left - right) / denominator;
    }

    private static Ellipse? RobustFit(List<CalibrationPoint> points, Ellipse fallback, double minimumTolerance,
        double medianMultiple, int rounds) => RobustFit(points, fallback, minimumTolerance, medianMultiple, rounds, out _);

    private static Ellipse? RobustFit(List<CalibrationPoint> points, Ellipse fallback, double minimumTolerance,
        double medianMultiple, int rounds, out int kept)
    {
        List<CalibrationPoint> current = points;
        Ellipse? ellipse = null;
        kept = 0;
        for (int round = 0; round < rounds; round++)
        {
            if (current.Count < 6) break;
            ellipse = Ellipse.Fit(current);
            if (ellipse is null) return null;
            Ellipse e = ellipse.Value;
            double[] residual = current.Select(p => e.RadialResidual(p)).ToArray();
            double[] sorted = residual.OrderBy(r => r).ToArray();
            double limit = Math.Max(minimumTolerance, medianMultiple * sorted[sorted.Length / 2]);
            List<CalibrationPoint> next = current.Where((_, i) => residual[i] < limit).ToList();
            kept = next.Count;
            if (next.Count == current.Count) break;
            current = next;
        }

        return ellipse ?? fallback;
    }

    // ---------------------------------------------------------------- ellipse

    /// <summary>Ellipse with centre (X, Y), semi-axes A ≥ B and rotation Theta of the A axis.</summary>
    private readonly record struct Ellipse(double X, double Y, double A, double B, double Theta)
    {
        public double AxisRatio => B / A;

        public Ellipse Scaled(double factor) => new(X * factor, Y * factor, A * factor, B * factor, Theta);

        public Ellipse ScaledAboutCenter(double factor) => new(X, Y, A * factor, B * factor, Theta);

        public IReadOnlyList<CalibrationPoint> Points(int count)
        {
            CalibrationPoint[] points = new CalibrationPoint[count];
            for (int i = 0; i < count; i++)
            {
                // Start at the top of the photo and go clockwise, like the default contour.
                double angle = (-Math.PI / 2) + (2 * Math.PI * i / count);
                double r = RayRadius(X, Y, angle) ?? A;
                points[i] = new CalibrationPoint(X + (r * Math.Cos(angle)), Y + (r * Math.Sin(angle)));
            }

            return points;
        }

        /// <summary>Distance from (cx, cy) along angle t to the boundary (cx, cy inside).</summary>
        public double? RayRadius(double cx, double cy, double t)
        {
            double cos = Math.Cos(Theta), sin = Math.Sin(Theta);
            double dx = Math.Cos(t), dy = Math.Sin(t);
            double du = (dx * cos) + (dy * sin), dv = (-dx * sin) + (dy * cos);
            double ox = cx - X, oy = cy - Y;
            double ou = (ox * cos) + (oy * sin), ov = (-ox * sin) + (oy * cos);
            double a = (du * du / (A * A)) + (dv * dv / (B * B));
            double b = 2 * ((ou * du / (A * A)) + (ov * dv / (B * B)));
            double c = (ou * ou / (A * A)) + (ov * ov / (B * B)) - 1;
            double discriminant = (b * b) - (4 * a * c);
            if (discriminant < 0 || a <= 0) return null;
            return (-b + Math.Sqrt(discriminant)) / (2 * a);
        }

        public double RadialResidual(CalibrationPoint p)
        {
            double t = Math.Atan2(p.Y - Y, p.X - X);
            return RayRadius(X, Y, t) is { } r ? Math.Abs(Math.Sqrt(((p.X - X) * (p.X - X)) + ((p.Y - Y) * (p.Y - Y))) - r) : double.MaxValue;
        }

        /// <summary>Algebraic least-squares conic through the points (normalised for
        /// conditioning); null unless it is a real ellipse.</summary>
        public static Ellipse? Fit(IReadOnlyList<CalibrationPoint> points)
        {
            if (points.Count < 5) return null;
            double mx = points.Average(p => p.X), my = points.Average(p => p.Y);
            double spread = Math.Sqrt(points.Average(p => ((p.X - mx) * (p.X - mx)) + ((p.Y - my) * (p.Y - my))));
            if (!(spread > 0)) return null;
            double[,] scatter = new double[6, 6];
            double[] row = new double[6];
            foreach (CalibrationPoint p in points)
            {
                double x = (p.X - mx) / spread, y = (p.Y - my) / spread;
                row[0] = x * x; row[1] = x * y; row[2] = y * y; row[3] = x; row[4] = y; row[5] = 1;
                for (int i = 0; i < 6; i++)
                for (int j = 0; j < 6; j++)
                    scatter[i, j] += row[i] * row[j];
            }

            double[] conic = SmallestEigenvector(scatter);
            double A = conic[0], B = conic[1], C = conic[2], D = conic[3], E = conic[4], F = conic[5];
            double determinant = (B * B) - (4 * A * C);
            if (!(determinant < 0)) return null;
            double x0 = ((2 * C * D) - (B * E)) / determinant;
            double y0 = ((2 * A * E) - (B * D)) / determinant;
            double value = (A * x0 * x0) + (B * x0 * y0) + (C * y0 * y0) + (D * x0) + (E * y0) + F;
            // Eigen-decomposition of [[A, B/2], [B/2, C]].
            double trace = A + C, difference = A - C;
            double root = Math.Sqrt((difference * difference) + (B * B));
            double l1 = (trace + root) / 2, l2 = (trace - root) / 2;
            if (value * l1 >= 0 || value * l2 >= 0) return null;
            double axis1 = Math.Sqrt(-value / l1), axis2 = Math.Sqrt(-value / l2);
            double theta1 = 0.5 * Math.Atan2(B, difference); // direction of eigenvalue l1
            (double major, double minor, double theta) = axis1 >= axis2
                ? (axis1, axis2, theta1)
                : (axis2, axis1, theta1 + (Math.PI / 2));
            if (!double.IsFinite(major) || !double.IsFinite(minor) || minor <= 0) return null;
            return new Ellipse((x0 * spread) + mx, (y0 * spread) + my, major * spread, minor * spread, theta);
        }

        private static double[] SmallestEigenvector(double[,] matrix)
        {
            const int N = 6;
            double[,] a = (double[,])matrix.Clone();
            double[,] v = new double[N, N];
            for (int i = 0; i < N; i++) v[i, i] = 1;
            for (int sweep = 0; sweep < 100; sweep++)
            {
                double off = 0;
                for (int p = 0; p < N; p++)
                for (int q = p + 1; q < N; q++)
                    off += a[p, q] * a[p, q];
                if (off < 1e-24) break;
                for (int p = 0; p < N; p++)
                for (int q = p + 1; q < N; q++)
                {
                    if (Math.Abs(a[p, q]) < 1e-300) continue;
                    double phi = 0.5 * Math.Atan2(2 * a[p, q], a[q, q] - a[p, p]);
                    double c = Math.Cos(phi), s = Math.Sin(phi);
                    for (int k = 0; k < N; k++)
                    {
                        double akp = a[k, p], akq = a[k, q];
                        a[k, p] = (c * akp) - (s * akq);
                        a[k, q] = (s * akp) + (c * akq);
                    }

                    for (int k = 0; k < N; k++)
                    {
                        double apk = a[p, k], aqk = a[q, k];
                        a[p, k] = (c * apk) - (s * aqk);
                        a[q, k] = (s * apk) + (c * aqk);
                    }

                    for (int k = 0; k < N; k++)
                    {
                        double vkp = v[k, p], vkq = v[k, q];
                        v[k, p] = (c * vkp) - (s * vkq);
                        v[k, q] = (s * vkp) + (c * vkq);
                    }
                }
            }

            int smallest = 0;
            for (int i = 1; i < N; i++) if (a[i, i] < a[smallest, smallest]) smallest = i;
            double[] vector = new double[N];
            for (int i = 0; i < N; i++) vector[i] = v[i, smallest];
            return vector;
        }
    }
}
