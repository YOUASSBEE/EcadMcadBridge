using EcadMcadBridge.Core.Model;
using PCB;

namespace EcadMcadBridge.Altium.Adapter;

/// <summary>
/// Reads every contour exposed by the board-outline geometric polygon. Analytic line/arc
/// segments are matched to those contours; unmatched contours get an explicitly diagnosed
/// geometric fallback instead of being dropped.
/// </summary>
internal static class AltiumOutlineReader
{
    private const string OutlineId = "altium:board-outline";

    public static BoardOutline Read(
        IPCB_Board board,
        List<string> warnings,
        AltiumAdapterDiagnostics diagnostics,
        IReadOnlyList<AltiumRegionInfo> regions)
    {
        try
        {
            var outline = board.GetState_BoardOutline();
            if (outline is null)
            {
                warnings.Add("Board outline API returned null.");
                return new BoardOutline { Id = OutlineId, Completeness = OutlineCompleteness.Incomplete };
            }

            var geometricContours = ReadGeometricContours(outline, warnings, diagnostics);
            if (geometricContours.Count == 0)
            {
                warnings.Add("Board outline geometric polygon returned no usable contours.");
                return new BoardOutline { Id = OutlineId, Completeness = OutlineCompleteness.Incomplete };
            }

            var analyticLoops = ReadAnalyticLoops(outline, geometricContours, warnings, diagnostics);
            var usedLoops = new bool[analyticLoops.Count];
            var contours = new List<Contour>(geometricContours.Count);

            foreach (var geometric in geometricContours)
            {
                var loopIndex = FindMatchingLoop(geometric, analyticLoops, usedLoops, warnings, diagnostics);
                if (loopIndex >= 0)
                {
                    usedLoops[loopIndex] = true;
                    contours.Add(MakeAnalyticContour(geometric, analyticLoops[loopIndex]));
                    diagnostics.ExactOutlineContours++;
                }
                else if (geometric.Points.Count >= 2)
                {
                    contours.Add(MakeGeometricContour(geometric));
                    diagnostics.FallbackOutlineContours++;
                    warnings.Add(
                        $"Board outline contour {geometric.Index} ({geometric.Role}) has no matched analytic loop; " +
                        "geometric fallback uses line segments and may approximate/lose arcs.");
                }
                else
                {
                    diagnostics.OmittedOutlineContours++;
                    warnings.Add(
                        $"Board outline contour {geometric.Index} ({geometric.Role}) has fewer than two points and was omitted.");
                }
            }

            var unmatched = usedLoops.Count(used => !used);
            if (unmatched > 0)
            {
                diagnostics.UnmatchedAnalyticLoops += unmatched;
                warnings.Add($"{unmatched} analytic board-outline loop(s) could not be matched to geometric contours and were not exported separately.");
            }

            contours.AddRange(ReadBoardCutoutContours(outline, regions, geometricContours, warnings, diagnostics));

            var completeness = DeriveCompleteness(diagnostics);
            return new BoardOutline
            {
                Id = OutlineId,
                Completeness = completeness,
                Contours = contours,
            };
        }
        catch (Exception ex)
        {
            diagnostics.OutlineLoopErrors++;
            warnings.Add("Board outline read failed: " + ex.GetType().Name + ".");
            return new BoardOutline
            {
                Id = OutlineId,
                Completeness = OutlineCompleteness.Incomplete,
            };
        }
    }

    private static IReadOnlyList<Contour> ReadBoardCutoutContours(
        IPCB_BoardOutline outline,
        IReadOnlyList<AltiumRegionInfo> regions,
        IReadOnlyList<GeometricContour> geometricContours,
        List<string> warnings,
        AltiumAdapterDiagnostics diagnostics)
    {
        var cutoutRegions = regions
            .Where(region => region.Kind == TRegionKind.eRegionKind_BoardCutout)
            .ToArray();
        var contours = new List<Contour>();
        var clipped = false;
        var outerContours = geometricContours
            .Where(contour => contour.Role == ContourRole.Outer)
            .Select(contour => ToCoreContour(contour.Points))
            .ToArray();
        var cutoutSources = BuildCutoutSources(
            cutoutRegions,
            outerContours,
            warnings,
            diagnostics);
        var existingHoles = geometricContours
            .Where(contour => contour.Role == ContourRole.Hole && contour.Points.Count >= 3)
            .Select(contour => new CutoutExistingHole(contour.Index, contour.Points))
            .ToArray();

        try
        {
            var clip = outline.ClipWithPolyRegShapes(false, false);
            if (clip is null)
            {
                warnings.Add("ClipWithPolyRegShapes(false,false) returned null; Board Cutout regions cannot be clipped.");
            }
            else
            {
                clipped = true;
                var shapes = clip.GetCutouts();
                diagnostics.ClipCutoutsCount = shapes?.Count() ?? 0;
                diagnostics.ClipNewPrimsCount = clip.GetNewPrims()?.Count() ?? 0;
                diagnostics.ClipInoperablePrimsCount = clip.GetInoperablePrims()?.Count() ?? 0;

                if (shapes is not null)
                {
                    var shapeCount = shapes.Count();
                    for (var i = 0; i < shapeCount; i++)
                    {
                        var shape = shapes.Items(i);
                        if (shape is null)
                        {
                            diagnostics.ClipCutoutErrors++;
                            warnings.Add($"Clipped Board Cutout shape {i} is null.");
                            continue;
                        }

                        var loops = ReadShapeLoops(shape, i, warnings, diagnostics);
                        for (var loopIndex = 0; loopIndex < loops.Count; loopIndex++)
                        {
                            var loop = loops[loopIndex];
                            var analyticBounds = AnalyticBounds.GetAnalyticBounds(loop);
                            var sampledBounds = CutoutRegionMatcher.GetBounds(
                                CutoutRegionMatcher.SampleLoop(loop, CutoutRegionMatcher.ArcSamples));
                            var clipBounds = FormatBounds(sampledBounds);
                            var boundsText =
                                $"analyticBounds={FormatBounds(analyticBounds)} " +
                                $"sampledBounds={clipBounds} analyticBoundsExact={analyticBounds.IsExact} " +
                                $"boundsTolerance={CutoutRegionMatcher.BoundsToleranceIu}";
                            var match = CutoutRegionMatcher.Match(loop, cutoutSources);

                            if (match.Status == CutoutRegionMatchStatus.Ambiguous)
                            {
                                diagnostics.ClipAmbiguousCutoutRegions++;
                                warnings.Add(
                                    $"Clipped cutout shape {i} loop {loopIndex} is ambiguous: {match.Reason}; " +
                                    $"{boundsText}; candidates=" + FormatCandidates(match.Candidates) +
                                    "; it was not exported.");
                                continue;
                            }

                            if (match.Status == CutoutRegionMatchStatus.Unmatched || match.Source is null)
                            {
                                diagnostics.ClipUnmatchedCutoutRegions++;
                                warnings.Add(
                                    $"Clipped cutout shape {i} loop {loopIndex} is unmatched: {match.Reason}; " +
                                    $"{boundsText}; it was not exported.");
                                continue;
                            }

                            var owner = match.Source;
                            diagnostics.MatchedCutoutSources.Add(owner.Key);
                            diagnostics.ClipMatchedCutoutRegions++;
                            warnings.Add(
                                $"Clipped cutout shape {i} loop {loopIndex} matched Board Cutout region " +
                                $"{owner.RegionIndex} contour {owner.ContourIndex} " +
                                $"(uniqueId='{owner.RegionUniqueId}', regionBounds={FormatBounds(owner)}, " +
                                $"{boundsText}, {match.Reason}).");

                            var duplicate = CutoutDeduplicator.Evaluate(loop, existingHoles);
                            if (duplicate.Status == CutoutDuplicateStatus.Duplicate)
                            {
                                diagnostics.DuplicateCutoutContours++;
                                warnings.Add(
                                    $"Clipped cutout shape {i} loop {loopIndex} duplicates geometric Hole " +
                                    $"{duplicate.ExistingHoleIndex} ({boundsText}, {duplicate.Reason}); " +
                                    "it was not exported twice.");
                                continue;
                            }

                            if (duplicate.Status == CutoutDuplicateStatus.Partial)
                            {
                                warnings.Add(
                                    $"Clipped cutout shape {i} loop {loopIndex} has only a partial match with " +
                                    $"geometric Hole {duplicate.ExistingHoleIndex} ({boundsText}, " +
                                    $"{duplicate.Reason}); duplicate export was not suppressed.");
                            }

                            diagnostics.ClipCutoutContours++;
                            diagnostics.ExportedCutoutContours++;
                            contours.Add(new Contour
                            {
                                Id = "altium:contour:cutout:clip:" +
                                    diagnostics.ExportedCutoutContours.ToString(System.Globalization.CultureInfo.InvariantCulture),
                                Role = ContourRole.Hole,
                                Segments = loop.Select(ToCoreSegment).ToArray(),
                            });
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            diagnostics.ClipCutoutErrors++;
            warnings.Add(
                "ClipWithPolyRegShapes(false,false) failed: " + ex.GetType().Name +
                "; Board Cutout regions are not exported.");
        }

        if (cutoutRegions.Length > 0 && !clipped)
        {
            warnings.Add(
                $"{cutoutRegions.Length} IPCB_Region object(s) have TRegionKind.eRegionKind_BoardCutout, " +
                "but ClipWithPolyRegShapes(false,false) returned no clip result; " +
                "their contours are left as diagnostics and are not exported as outline holes.");
        }

        if (cutoutRegions.Length == 0 && diagnostics.ClipCutoutsCount == 0)
        {
            warnings.Add(
                "No IPCB_Region with TRegionKind.eRegionKind_BoardCutout was found and the outline clipper returned no cutouts.");
        }
        else if (diagnostics.ExportedCutoutContours == 0)
        {
            warnings.Add(
                $"Board Cutout evidence is present but no cutout contour was exported " +
                $"(regions={cutoutRegions.Length}, clipCutouts={diagnostics.ClipCutoutsCount}).");
        }
        else
        {
            warnings.Add(
                $"Exporting {diagnostics.ExportedCutoutContours} Board Cutout hole contour(s) " +
                $"(regions={cutoutRegions.Length}, clipCutouts={diagnostics.ClipCutoutsCount}, " +
                $"duplicates={diagnostics.DuplicateCutoutContours}).");
        }

        return contours;
    }

    private static IReadOnlyList<List<OutlineSegment>> ReadShapeLoops(
        IPCB_RegionShape shape,
        int shapeIndex,
        List<string> warnings,
        AltiumAdapterDiagnostics diagnostics)
    {
        var count = shape.GetState_ShapeSegmentCount();
        if (count < 2)
        {
            diagnostics.ClipCutoutErrors++;
            warnings.Add($"Clipped Board Cutout shape {shapeIndex} has {count} segment(s); a closed loop needs at least two.");
            return Array.Empty<List<OutlineSegment>>();
        }

        var raw = new List<RawSegment>(count);
        for (var i = 0; i < count; i++)
        {
            var segment = shape.GetState_ShapeSegment(i);
            if (segment is null)
            {
                diagnostics.ClipCutoutErrors++;
                warnings.Add($"Clipped Board Cutout shape {shapeIndex} segment {i} is null.");
                return Array.Empty<List<OutlineSegment>>();
            }

            raw.Add(new RawSegment(
                segment.Kind,
                segment.Vx,
                segment.Vy,
                segment.Cx,
                segment.Cy,
                segment.Radius,
                segment.Angle1,
                segment.Angle2));
        }

        var segments = BuildOutlineSegments(raw);
        var (loops, invalid) = BuildLoops(segments);
        if (invalid > 0)
        {
            diagnostics.ClipCutoutErrors += invalid;
            warnings.Add(
                $"Clipped Board Cutout shape {shapeIndex} has {invalid} open/invalid segment sequence(s); " +
                "only closed loops are exported.");
        }

        diagnostics.ClipCutoutSegments += count;
        return loops;
    }

    private static IReadOnlyList<CutoutSourceContour> BuildCutoutSources(
        IReadOnlyList<AltiumRegionInfo> cutoutRegions,
        IReadOnlyList<Contour> outerContours,
        List<string> warnings,
        AltiumAdapterDiagnostics diagnostics)
    {
        var result = new List<CutoutSourceContour>();
        foreach (var region in cutoutRegions)
        {
            for (var contourIndex = 0; contourIndex < region.Contours.Count; contourIndex++)
            {
                var contour = region.Contours[contourIndex];
                var role = BoardOutlineTopology.ClassifyRegionContour(
                    ToCoreContour(contour.Points),
                    outerContours);
                if (role == BoardCutoutRole.BoardShape)
                {
                    diagnostics.BoardCutoutShapeContours++;
                    warnings.Add(
                        $"Board Cutout region {region.Index} contour {contourIndex} " +
                        "(uniqueId='" + (region.UniqueId ?? string.Empty) + "') lies on the Outer contour; " +
                        "it is board-shape/layer-stack evidence, not a hole.");
                    continue;
                }

                if (role == BoardCutoutRole.Unrelated)
                {
                    diagnostics.BoardCutoutUnclassifiedContours++;
                    warnings.Add(
                        $"Board Cutout region {region.Index} contour {contourIndex} " +
                        "(uniqueId='" + (region.UniqueId ?? string.Empty) + "') is neither on an Outer " +
                        "contour nor inside the board; it was not exported.");
                    continue;
                }

                diagnostics.BoardCutoutHoles++;
                var bounds = CutoutRegionMatcher.GetBounds(
                    CutoutDeduplicator.EnsureClosed(contour.Points));
                result.Add(new CutoutSourceContour(
                    region.Index,
                    region.UniqueId,
                    region.Name,
                    contourIndex,
                    contour.IsHole,
                    contour.Points,
                    region.Area,
                    bounds.MinX,
                    bounds.MinY,
                    bounds.MaxX,
                    bounds.MaxY));
            }
        }

        return result;
    }

    private static string FormatCandidates(IReadOnlyList<CutoutSourceContour> candidates) =>
        string.Join(
            "; ",
            candidates.Select(candidate =>
                $"region={candidate.RegionIndex} contour={candidate.ContourIndex} " +
                $"uniqueId='{candidate.RegionUniqueId}' bounds={FormatBounds(candidate)}"));

    private static string FormatBounds(CutoutSourceContour source) =>
        FormatBounds((source.MinX, source.MinY, source.MaxX, source.MaxY));

    private static string FormatBounds((int MinX, int MinY, int MaxX, int MaxY) bounds) =>
        $"({bounds.MinX},{bounds.MinY})-({bounds.MaxX},{bounds.MaxY})";

    private static string FormatBounds(AnalyticBoundsResult bounds) =>
        FormatBounds((bounds.MinX, bounds.MinY, bounds.MaxX, bounds.MaxY))
        + (bounds.Error is null ? string.Empty : " (error: " + bounds.Error + ")");

    private static OutlineCompleteness DeriveCompleteness(AltiumAdapterDiagnostics diagnostics)
    {
        var incomplete =
            diagnostics.OutlineLoopErrors > 0
            || diagnostics.OmittedOutlineContours > 0
            || diagnostics.FallbackOutlineContours > 0
            || diagnostics.UnmatchedAnalyticLoops > 0
            || diagnostics.AmbiguousOutlineMatches > 0
            || diagnostics.PartialOutlineMatches > 0
            || diagnostics.ClipCutoutErrors > 0
            || diagnostics.ClipUnmatchedCutoutRegions > 0
            || diagnostics.ClipAmbiguousCutoutRegions > 0
            || diagnostics.BoardCutoutUnclassifiedContours > 0
            || diagnostics.BoardCutoutShapeContours + diagnostics.MatchedCutoutSources.Count
                != diagnostics.BoardCutoutContours;
        return incomplete ? OutlineCompleteness.Incomplete : OutlineCompleteness.Complete;
    }

    private static Contour ToCoreContour(IReadOnlyList<Point2D> points)
    {
        var segments = new List<ContourSegment>();
        for (var i = 0; i + 1 < points.Count; i++)
            segments.Add(new LineSegment { Start = points[i], End = points[i + 1] });
        if (points.Count > 2 && PointsEqual(points[0], points[^1]) == false)
            segments.Add(new LineSegment { Start = points[^1], End = points[0] });
        return new Contour
        {
            Id = "altium:topology:" + points.Count,
            Role = ContourRole.Outer,
            Segments = segments,
        };
    }

    private static IReadOnlyList<GeometricContour> ReadGeometricContours(
        IPCB_BoardOutline outline,
        List<string> warnings,
        AltiumAdapterDiagnostics diagnostics)
    {
        var geometric = outline.BoardOutline_GeometricPolygon();
        if (geometric is null)
        {
            warnings.Add("Board outline geometric polygon is unavailable.");
            return Array.Empty<GeometricContour>();
        }

        var result = new List<GeometricContour>();
        var count = geometric.GetState_Count();
        for (var i = 0; i < count; i++)
        {
            var contour = geometric.GetState_Contour(i);
            if (contour is null)
            {
                diagnostics.OutlineLoopErrors++;
                warnings.Add($"Board outline geometric contour {i} is null.");
                continue;
            }

            var points = new List<Point2D>();
            var pointCount = contour.GetState_Count();
            for (var p = 0; p < pointCount; p++)
                points.Add(Point2D.FromIu(contour.GetState_PointX(p), contour.GetState_PointY(p)));

            var role = geometric.GetState_IsHole(i) ? ContourRole.Hole : ContourRole.Outer;
            result.Add(new GeometricContour(i, role, points));
        }

        diagnostics.GeometricContourCount = result.Count;
        diagnostics.GeometricPointCount = result.Sum(contour => contour.Points.Count);
        diagnostics.GeometricHoleCount = result.Count(contour => contour.Role == ContourRole.Hole);
        return result;
    }

    private static IReadOnlyList<List<OutlineSegment>> ReadAnalyticLoops(
        IPCB_BoardOutline outline,
        IReadOnlyList<GeometricContour> geometricContours,
        List<string> warnings,
        AltiumAdapterDiagnostics diagnostics)
    {
        var polygon = outline as IPCB_Polygon;
        if (polygon is null)
        {
            warnings.Add("Board outline does not expose IPCB_Polygon segments; geometric fallback will be used.");
            return Array.Empty<List<OutlineSegment>>();
        }

        var count = polygon.GetState_PointCount();
        if (count < 2)
            return Array.Empty<List<OutlineSegment>>();

        var raw = new List<RawSegment>(count);
        for (var i = 0; i < count; i++)
        {
            var segment = polygon.GetState_Segments(i);
            if (segment is null)
            {
                diagnostics.OutlineLoopErrors++;
                warnings.Add($"Board outline segment {i} is null; analytic outline is incomplete.");
                return Array.Empty<List<OutlineSegment>>();
            }

            raw.Add(new RawSegment(
                segment.Kind,
                segment.Vx,
                segment.Vy,
                segment.Cx,
                segment.Cy,
                segment.Radius,
                segment.Angle1,
                segment.Angle2));
        }

        var segments = BuildOutlineSegments(raw, geometricContours);
        var (loops, invalid) = BuildLoops(segments);
        if (invalid > 0)
        {
            diagnostics.OutlineLoopErrors += invalid;
            warnings.Add($"{invalid} analytic board-outline loop(s) are not closed/continuous and were not used.");
        }

        return loops;
    }

    private static int FindMatchingLoop(
        GeometricContour geometric,
        IReadOnlyList<List<OutlineSegment>> loops,
        bool[] usedLoops,
        List<string> warnings,
        AltiumAdapterDiagnostics diagnostics)
    {
        var matches = OutlineContourMatcher.FindExactMatches(geometric.Points, loops, usedLoops);
        if (matches.Count == 1)
            return matches[0];

        if (matches.Count > 1)
        {
            diagnostics.AmbiguousOutlineMatches++;
            warnings.Add(
                $"Board outline contour {geometric.Index} ({geometric.Role}) has {matches.Count} exact analytic-loop matches; " +
                "geometric fallback was used rather than choosing arbitrarily.");
            return -1;
        }

        var partial = false;
        for (var i = 0; i < loops.Count; i++)
        {
            if (usedLoops[i])
                continue;
            if (OutlineContourMatcher.HasAnySharedPoint(geometric.Points, loops[i]))
            {
                partial = true;
                break;
            }
        }

        if (partial)
        {
            diagnostics.PartialOutlineMatches++;
            var explanation = string.Empty;
            for (var i = 0; i < loops.Count; i++)
            {
                if (usedLoops[i] || !OutlineContourMatcher.HasAnySharedPoint(geometric.Points, loops[i]))
                    continue;
                explanation = " " + OutlineContourMatcher.ExplainExactMatch(geometric.Points, loops[i]);
                break;
            }

            warnings.Add(
                $"Board outline contour {geometric.Index} ({geometric.Role}) has only a partial analytic-loop match; " +
                "exact matching was rejected and geometric fallback was used." + explanation);
        }

        return -1;
    }

    private static Contour MakeAnalyticContour(GeometricContour geometric, List<OutlineSegment> loop)
    {
        return new Contour
        {
            Id = "altium:contour:" + geometric.Index.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Role = geometric.Role,
            Segments = loop.Select(ToCoreSegment).ToArray(),
        };
    }

    private static Contour MakeGeometricContour(GeometricContour geometric)
    {
        var segments = new List<ContourSegment>(geometric.Points.Count);
        for (var p = 0; p < geometric.Points.Count; p++)
        {
            var next = (p + 1) % geometric.Points.Count;
            if (p == geometric.Points.Count - 1 && PointsEqual(geometric.Points[p], geometric.Points[0]))
                break;

            segments.Add(new LineSegment
            {
                Start = geometric.Points[p],
                End = geometric.Points[next],
            });
        }

        return new Contour
        {
            Id = "altium:contour:" + geometric.Index.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Role = geometric.Role,
            Segments = segments,
        };
    }

    private static (List<List<OutlineSegment>> Loops, int Invalid) BuildLoops(
        IReadOnlyList<OutlineSegment> segments)
    {
        var loops = new List<List<OutlineSegment>>();
        var current = new List<OutlineSegment>();
        var invalid = 0;

        foreach (var segment in segments)
        {
            if (current.Count > 0 && !PointsEqual(current[^1].End, segment.Start))
            {
                if (current.Count >= 2 && PointsEqual(current[^1].End, current[0].Start))
                    loops.Add(current);
                else
                    invalid++;
                current = new List<OutlineSegment>();
            }

            current.Add(segment);
            if (current.Count > 1 && PointsEqual(current[^1].End, current[0].Start))
            {
                loops.Add(current);
                current = new List<OutlineSegment>();
            }
        }

        if (current.Count > 0)
        {
            if (current.Count >= 2 && PointsEqual(current[^1].End, current[0].Start))
                loops.Add(current);
            else
                invalid++;
        }

        return (loops, invalid);
    }

    internal static List<OutlineSegment> BuildOutlineSegments(IReadOnlyList<RawSegment> raw) =>
        BuildOutlineSegments(raw, Array.Empty<GeometricContour>());

    internal static List<OutlineSegment> BuildOutlineSegments(
        IReadOnlyList<RawSegment> raw,
        IReadOnlyList<GeometricContour> references)
    {
        var segments = new List<OutlineSegment>(raw.Count);
        for (var i = 0; i < raw.Count; i++)
        {
            var current = raw[i];
            var next = raw[(i + 1) % raw.Count];
            var start = StartPoint(current);
            var end = current.Kind == TPolySegmentType.ePolySegmentArc
                ? Polar(current.Cx, current.Cy, current.Radius, current.Angle2)
                : next.Kind == TPolySegmentType.ePolySegmentArc
                    ? StartPoint(next)
                    : Point2D.FromIu(next.Vx, next.Vy);

            if (current.Kind == TPolySegmentType.ePolySegmentArc)
            {
                // Altium stores arc endpoints as angles but no sweep direction. The SDK's
                // geometric contour is authoritative: sample both candidate sweeps and keep
                // the one that lies on the package's own contour coordinates.
                var direction = ChooseArcDirection(current, references);
                segments.Add(new OutlineSegment(
                    start,
                    end,
                    Point2D.FromIu(current.Cx, current.Cy),
                    current.Radius,
                    direction));
            }
            else
            {
                segments.Add(new OutlineSegment(start, end, default, 0, null));
            }
        }

        return segments;
    }

    private static ArcSweepDirection ChooseArcDirection(
        RawSegment arc,
        IReadOnlyList<GeometricContour> references)
    {
        if (references.Count == 0)
            return ArcSweepDirection.CounterClockwise;

        var clockwise = IsArcOnReferences(arc, ArcSweepDirection.Clockwise, references);
        var counterClockwise = IsArcOnReferences(arc, ArcSweepDirection.CounterClockwise, references);
        if (clockwise && !counterClockwise)
            return ArcSweepDirection.Clockwise;
        return ArcSweepDirection.CounterClockwise;
    }

    private static bool IsArcOnReferences(
        RawSegment arc,
        ArcSweepDirection direction,
        IReadOnlyList<GeometricContour> references)
    {
        const int steps = 16;
        var toleranceIu = ArcReferenceTolerance(arc, references);
        var start = arc.Angle1;
        var delta = direction == ArcSweepDirection.Clockwise
            ? -PositiveModulo(start - arc.Angle2, 360.0)
            : PositiveModulo(arc.Angle2 - start, 360.0);
        for (var i = 0; i <= steps; i++)
        {
            var point = Polar(
                arc.Cx,
                arc.Cy,
                arc.Radius,
                start + ((delta * i) / steps));
            if (!references.Any(contour => IsPointOnPolyline(point, contour.Points, toleranceIu)))
                return false;
        }
        return true;
    }

    /// <summary>
    /// The SDK geometric contour is a polyline sampled from the same analytic arc. Its
    /// chords sit inside the true arc, so the comparison tolerance must include the
    /// measured chord sagitta; a fixed IU tolerance rejects valid large-radius arcs.
    /// </summary>
    private static double ArcReferenceTolerance(
        RawSegment arc,
        IReadOnlyList<GeometricContour> references)
    {
        const double minimumIu = 3.0;
        var tolerance = minimumIu;
        if (arc.Radius <= 0)
            return tolerance;

        foreach (var contour in references)
        {
            for (var i = 0; i + 1 < contour.Points.Count; i++)
            {
                if (!TryCircleAngle(contour.Points[i], arc, out var first)
                    || !TryCircleAngle(contour.Points[i + 1], arc, out var second))
                    continue;

                var gapDegrees = Math.Abs(PositiveModulo(second - first + 180.0, 360.0) - 180.0);
                if (gapDegrees <= 0 || gapDegrees >= 180)
                    continue;

                var sagitta = arc.Radius * (1.0 - Math.Cos(gapDegrees * Math.PI / 360.0));
                tolerance = Math.Max(tolerance, (sagitta * 1.25) + minimumIu);
            }
        }

        return tolerance;
    }

    private static bool TryCircleAngle(Point2D point, RawSegment arc, out double degrees)
    {
        degrees = 0;
        if (point.XIu is not int x || point.YIu is not int y || arc.Radius <= 0)
            return false;

        var dx = x - arc.Cx;
        var dy = y - arc.Cy;
        var distance = Math.Sqrt(((double)dx * dx) + ((double)dy * dy));
        if (Math.Abs(distance - arc.Radius) > Math.Max(10.0, arc.Radius * 0.01))
            return false;

        degrees = Math.Atan2(dy, dx) * 180.0 / Math.PI;
        return true;
    }

    private static bool IsPointOnPolyline(
        Point2D point,
        IReadOnlyList<Point2D> polyline,
        double tolerance)
    {
        if (point.XIu is not int px || point.YIu is not int py)
            return false;
        for (var i = 0; i + 1 < polyline.Count; i++)
        {
            var a = polyline[i];
            var b = polyline[i + 1];
            if (a.XIu is not int ax || a.YIu is not int ay
                || b.XIu is not int bx || b.YIu is not int by)
                continue;
            var dx = (double)bx - ax;
            var dy = (double)by - ay;
            var lengthSquared = (dx * dx) + (dy * dy);
            var t = lengthSquared <= 0
                ? 0
                : Math.Clamp(
                    ((((double)px - ax) * dx) + (((double)py - ay) * dy)) / lengthSquared,
                    0,
                    1);
            var ex = (double)px - (ax + (t * dx));
            var ey = (double)py - (ay + (t * dy));
            if (Math.Sqrt((ex * ex) + (ey * ey)) <= tolerance)
                return true;
        }
        return false;
    }

    private static double PositiveModulo(double value, double modulus)
    {
        var result = value % modulus;
        return result < 0 ? result + modulus : result;
    }

    private static Point2D StartPoint(RawSegment segment)
    {
        return segment.Kind == TPolySegmentType.ePolySegmentArc
            ? Polar(segment.Cx, segment.Cy, segment.Radius, segment.Angle1)
            : Point2D.FromIu(segment.Vx, segment.Vy);
    }

    private static Point2D Polar(int centreX, int centreY, int radius, double angleDegrees)
    {
        var radians = angleDegrees * Math.PI / 180.0;
        var x = (int)Math.Round(centreX + (radius * Math.Cos(radians)), MidpointRounding.AwayFromZero);
        var y = (int)Math.Round(centreY + (radius * Math.Sin(radians)), MidpointRounding.AwayFromZero);
        return Point2D.FromIu(x, y);
    }

    private static bool PointsEqual(Point2D left, Point2D right) =>
        left.XIu == right.XIu && left.YIu == right.YIu;

    private static ContourSegment ToCoreSegment(OutlineSegment segment)
    {
        if (segment.SweepDirection is null)
            return new LineSegment { Start = segment.Start, End = segment.End };

        return new ArcSegment
        {
            Start = segment.Start,
            End = segment.End,
            Center = segment.Center,
            Radius = LengthValue.FromIu(segment.Radius),
            SweepDirection = segment.SweepDirection,
        };
    }

    internal sealed record GeometricContour(
        int Index,
        ContourRole Role,
        IReadOnlyList<Point2D> Points);

    internal sealed record RawSegment(
        TPolySegmentType Kind,
        int Vx,
        int Vy,
        int Cx,
        int Cy,
        int Radius,
        double Angle1,
        double Angle2);

}
