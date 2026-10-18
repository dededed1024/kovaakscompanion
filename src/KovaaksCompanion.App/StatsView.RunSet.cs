using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using KovaaksCompanion.Core.Library;
using KovaaksCompanion.Core.Replays;
using KovaaksCompanion.Core.Trajectory;
using static KovaaksCompanion.App.StatBlocks;

namespace KovaaksCompanion.App;

/// <summary>Statistics over a set of runs of any scenarios: one session, or every run of the account. The scenario sheet's statistics, minus what only makes sense for one scenario's score.</summary>
public partial class StatsView
{
    /// <summary>Which half of <see cref="RunSetPanel"/> to build: the figures, the charts, or both.</summary>
    enum RunSetPart { All, Stats, Charts }

    const string NoValue = "–";

    /// <summary>
    /// The statistics panel of <paramref name="runs"/>: tiles, comparison, a chart of one figure over the runs, the scenarios, time of day, sensitivity and mouse speed.
    /// <paramref name="focus"/> is compared with <paramref name="against"/> (a session with every run; the last 30 days with every run); both may be null for no comparison.
    /// </summary>
    UIElement RunSetPanel(IReadOnlyList<RunRecord> runs, IReadOnlySet<RunRecord> pbs, (string Label, IReadOnlyList<RunRecord> Runs)? focus, (string Label, IReadOnlyList<RunRecord> Runs)? against, bool byKind = false, bool timeOfDay = false, RunSetPart part = RunSetPart.All, IReadOnlyList<Advice>? advice = null, bool runChart = true, bool speedByKind = false)
    {
        var root = new StackPanel();
        var sum = RunSetInsights.Summarize(runs, pbs);
        if (sum == null) return root;
        var stats = part != RunSetPart.Charts;
        var charts = part != RunSetPart.Stats;

        if (stats)
        {
            var tiles = new List<(string Label, UIElement Content)>
            {
                (L.T("Time"), BigValue(sum.TimePlayed.Ticks > 0 ? Dur(sum.TimePlayed) : NoValue, FgB)),
                (L.T("Runs"), BigValue($"{sum.Runs}", FgB)),
                (L.T("PB"), BigValue($"{sum.PersonalBests}", FgB, sum.PersonalBests > 0 ? (Brush)FindResource("Green") : null)),
                (L.T("Accuracy"), BigValue(sum.Accuracy is { } a ? a.ToString("P1") : NoValue, FgB)),
                (L.T("Kills"), BigValue(sum.TotalKills > 0 ? $"{sum.TotalKills:N0}" : NoValue, FgB)),
                (L.T("TTK"), BigValue(sum.AvgTtk is { } t ? $"{t:0.00}s" : NoValue, FgB)),
                (L.T("Overshots / kill"), BigValue(sum.OverShots is { } o ? o.ToString("0.00") : NoValue, FgB)),
            };
            var tileRow = TileRow(tiles);
            ((FrameworkElement)tileRow).Margin = new Thickness(0, 0, -Gap, -Gap);
            root.Children.Add(tileRow);
            if (focus is { Runs.Count: > 0 } f && against is { Runs.Count: > 0 } g && !ReferenceEquals(f.Runs, g.Runs)) root.Children.Add(RunSetCompare(f, g));
            if (advice is { Count: > 0 }) root.Children.Add(AdviceSection(advice, CardMargin));
        }
        if (charts && runChart && RunSetChart(runs) is { } chart) root.Children.Add(chart);
        if (stats)
        {
            if ((byKind ? RunSetKinds(runs, pbs) : RunSetScenarios(runs, pbs)) is { } lines) root.Children.Add(lines);
            if (timeOfDay && TimeOfDay(runs) is { } hours) root.Children.Add(hours);
        }
        if (charts)
        {
            if (RunSetSensitivityChart(runs) is { } sensChart) root.Children.Add(sensChart);
            var speedHost = new Border();
            root.Children.Add(speedHost);
            _ = FillRunSetSpeedAsync(speedHost, focus ?? ("", runs), against, speedByKind);
        }
        // The first element sits at the top of its parent: no gap above it.
        if (root.Children.Count > 0 && root.Children[0] is FrameworkElement first)
            first.Margin = new Thickness(first.Margin.Left, 0, first.Margin.Right, first.Margin.Bottom);
        return root;
    }

    /// <summary>Per-run figures of the focus set beside the same figures of the comparison set.</summary>
    UIElement RunSetCompare((string Label, IReadOnlyList<RunRecord> Runs) focus, (string Label, IReadOnlyList<RunRecord> Runs) against)
    {
        double? Mean(IReadOnlyList<RunRecord> rs, Func<RunRecord, double?> get) => rs.Select(get).OfType<double>().ToList() is { Count: > 0 } xs ? xs.Average() : null;
        var rows = new List<(string Label, IReadOnlyList<(string Text, Brush? Tone)> Cells)>();
        void Row(string label, Func<RunRecord, double?> get, string format, bool higherIsBetter = true)
        {
            var x = Mean(focus.Runs, get);
            var y = Mean(against.Runs, get);
            Brush? tone = x is { } xv && y is { } yv && Math.Abs(xv - yv) > 1e-9 ? (Brush)FindResource((xv > yv) == higherIsBetter ? "Green" : "Red") : null;
            rows.Add((L.T(label), [(x?.ToString(format) ?? NoValue, tone), (y?.ToString(format) ?? NoValue, null)]));
        }
        Row("Accuracy", r => r.Accuracy, "P1");
        Row("Kills", r => r.Kills, "0.#");
        Row("TTK", r => r.AvgTtk, "0.00", false);
        Row("Overshots", r => r.OverShots, "0.#", false);
        Row("Reloads", r => r.Reloads, "0.#", false);
        Row("Distance moved", r => r.Distance, "0.##");
        rows.Add((L.T("Run length"), [(Dur(TimeSpan.FromTicks((long)focus.Runs.Average(r => r.Duration.Ticks))), null), (Dur(TimeSpan.FromTicks((long)against.Runs.Average(r => r.Duration.Ticks))), null)]));
        var heads = new[] { focus.Label, against.Label };
        var half = (rows.Count + 1) / 2;
        var both = new Grid();
        both.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        both.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        both.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var left = Table(heads, rows.Take(half).ToList());
        var right = Table(heads, rows.Skip(half).ToList());
        Grid.SetColumn(right, 2);
        var divider = new Border { Width = 1, Margin = new Thickness(0, 4, 0, 4), HorizontalAlignment = HorizontalAlignment.Center, Background = (Brush)FindResource("EdgeFaint") };
        Grid.SetColumn(divider, 1);
        both.Children.Add(left); both.Children.Add(divider); both.Children.Add(right);
        return Section("Per run average", both, CardMargin);
    }

    /// <summary>One figure of every run over time; the chips pick the figure. Hidden when fewer than two runs have any.</summary>
    UIElement? RunSetChart(IReadOnlyList<RunRecord> runs)
    {
        var series = new (string Name, string Brush, string Format, Func<RunRecord, double?> Get)[]
        {
            ("Accuracy", "Green", "0.0%", r => r.Accuracy),
            ("Kills", "Orange", "0", r => r.Kills),
            ("TTK", "Purple", "0.00\"s\"", r => r.AvgTtk),
            ("Overshots", "Red", "0", r => r.OverShots),
            ("Reloads", "Mint", "0", r => r.Reloads),
            ("Distance moved", "Accent", "0.##", r => r.Distance),
        };
        var ordered = runs.OrderBy(r => r.End).ToList();
        var available = series.Where(s => ordered.Count(r => s.Get(r) != null) >= 2).ToList();
        if (available.Count == 0) return null;
        var chart = new ScoreChart { Cursor = Cursors.Arrow, Zoomable = true, Height = 220, AverageWindow = Math.Clamp(ordered.Count / 10, 3, 10) };
        var bar = new RangeBar { Width = 130, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, MinLen = ScoreChart.MinWindow };
        bar.Changed += (off, len) => chart.SetView(off, len);
        bar.Reset += chart.ResetView;
        void Sync()
        {
            bar.Visibility = chart.CanZoom ? Visibility.Visible : Visibility.Collapsed;
            var v = chart.View;
            bar.Set(chart.Total, v.Off, v.Len);
        }
        chart.ViewChanged += Sync;

        var chips = new WrapPanel { Margin = new Thickness(2, 0, 0, 6) };
        var shown = available[0].Name;
        void Show()
        {
            var s = available.First(x => x.Name == shown);
            chart.ScoreName = L.T(s.Name);
            chart.Set(ordered.Select(r => (r, v: s.Get(r))).Where(x => x.v != null).Select(x => (x.r.End, x.v!.Value)).ToList(), s.Format);
            Sync();
            chips.Children.Clear();
            foreach (var c in available)
            {
                var name = c.Name;
                chips.Children.Add(Chip(L.T(c.Name), (Brush)FindResource(c.Brush), name == shown, () => { shown = name; Show(); }));
            }
        }
        var body = new StackPanel();
        body.Children.Add(ChartHead("Run stats", bar));
        body.Children.Add(chips);
        body.Children.Add(chart);
        Show();
        return CardBox(body, CardPad, CardMargin);
    }

    /// <summary>The scenarios of the set, most played first: runs, best score, accuracy and time. Click opens the scenario.</summary>
    UIElement? RunSetScenarios(IReadOnlyList<RunRecord> runs, IReadOnlySet<RunRecord> pbs)
    {
        const int Shown = 12;
        var lines = RunSetInsights.ByScenario(runs, pbs);
        if (lines.Count == 0) return null;
        var shown = lines.Take(Shown).ToList();
        var rows = shown.Select(l => (l.Scenario, (IReadOnlyList<(string Text, Brush? Tone)>)
        [
            (l.PersonalBests > 0 ? $"{l.Runs} · {l.PersonalBests} PB" : $"{l.Runs}", null),
            (l.Best.ToString("0.#"), null),
            (l.Accuracy is { } a ? a.ToString("P1") : NoValue, null),
            (l.AvgTtk is { } t ? $"{t:0.00}s" : NoValue, null),
            (Dur(l.Time), null),
        ])).ToList();
        var host = Table([L.T("Runs"), L.T("Best"), L.T("Accuracy"), L.T("TTK"), L.T("Time")], rows, i => _host.ShowScenario(shown[i].Scenario, Mouse.GetPosition(this)), 2.4);
        return Section("Scenarios", host, CardMargin, lines.Count > Shown ? L.F("{0} of {1}", Shown, lines.Count) : null);
    }

    /// <summary>How far the best score of each played scenario of the kind reaches through that scenario's tiers (0 = nothing, 1 = top tier), averaged over the scenarios; null when none has tiers.</summary>
    double? KindLevel(IEnumerable<string> scenarios)
    {
        var levels = new List<double>();
        foreach (var n in scenarios)
        {
            var best = _book.For(n).Best;
            if (best <= 0 || TierBands(n) is not { Count: > 0 } bands) continue;
            var k = bands.Count(b => best >= b.Value);
            var floor = k > 0 ? bands[k - 1].Value : 0;
            var frac = k >= bands.Count ? 1 : bands[k].Value > floor ? (best - floor) / (bands[k].Value - floor) : 0;
            levels.Add(Math.Clamp((k + frac) / bands.Count, 0, 1));
        }
        return levels.Count > 0 ? levels.Average() : null;
    }

    /// <summary>One card per aim kind (tracking, flick, switching) with its tier level as a bar; the kind with the highest level is marked, so it shows which one the player is strongest at.</summary>
    UIElement? RunSetKinds(IReadOnlyList<RunRecord> runs, IReadOnlySet<RunRecord> pbs)
    {
        var lines = RunSetInsights.ByKind(runs, pbs);
        if (lines.Count == 0) return null;
        var levels = lines.ToDictionary(l => l.Kind, l => KindLevel(runs.Where(r => RunSetInsights.KindOf(r) == l.Kind).Select(r => r.Scenario).Distinct(StringComparer.OrdinalIgnoreCase)));
        var ranked = levels.Where(p => p.Value != null).OrderByDescending(p => p.Value).ToList();
        AimKind? strongest = ranked.Count >= 2 && ranked[0].Value - ranked[1].Value > 0.005 ? ranked[0].Key : null;
        var green = (Brush)FindResource("Green");
        var grid = new UniformGrid { Columns = lines.Count };
        Responsive(grid, lines.Count, 240);
        foreach (var l in lines)
        {
            var top = l.Kind == strongest;
            var body = new StackPanel();
            var head = Head(l.Kind.ToString(), null, new Thickness(4, 0, 4, 0));
            if (top) head.Children.Add(new TextBlock { Text = L.T("Strongest"), Foreground = green, FontSize = 12, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center });
            body.Children.Add(head);
            if (levels[l.Kind] is { } lv)
            {
                body.Children.Add(new TextBlock { Text = L.T("Tier level"), Foreground = DimB, FontSize = 11.5, Margin = new Thickness(0, 10, 0, 0) });
                body.Children.Add(BigValue($"{lv:P0}", FgB, top ? green : null));
                var bar = MeterBar(lv, top ? green : (Brush)FindResource("Accent"));
                ((FrameworkElement)bar).Margin = new Thickness(0, 6, 0, 8);
                body.Children.Add(bar);
            }
            var rows = new List<(string Label, IReadOnlyList<(string Text, Brush? Tone)> Cells)>
            {
                (L.T("Runs"), [(l.PersonalBests > 0 ? $"{l.Runs} · {l.PersonalBests} PB" : $"{l.Runs}", null)]),
                (L.T("Scenarios"), [($"{l.Scenarios}", null)]),
                (L.T("Time"), [(Dur(l.Time), null)]),
                (L.T("Sensitivity"), [(l.Cm360 is { } cm ? $"{cm:0.##} cm/360" : NoValue, null)]),
                (L.T("Accuracy"), [(l.Accuracy is { } a ? a.ToString("P1") : NoValue, null)]),
                (L.T("TTK"), [(l.AvgTtk is { } t ? $"{t:0.00}s" : NoValue, null)]),
                (L.T("Overshots / kill"), [(l.OverShots is { } o ? o.ToString("0.00") : NoValue, null)]),
            };
            body.Children.Add(Table([""], rows));
            var card = Section(null, body, new Thickness(0, 0, Gap, Gap));
            if (top) card.BorderBrush = green;
            grid.Children.Add(card);
        }
        return new Border { Margin = CardMargin, Child = grid };
    }

    /// <summary>Accuracy of each run by the sensitivity it was played at, one dot per run; only the most played sensitivities are plotted, each in its own colour. Null with fewer than two such runs.</summary>
    UIElement? RunSetSensitivityChart(IReadOnlyList<RunRecord> runs)
    {
        var groups = RunSetInsights.BySensitivity(runs);
        if (groups.Count == 0) return null;
        int GroupOf(RunRecord r) => groups.FindIndex(g => Math.Abs(g.Cm360 - Math.Round(r.Cm360!.Value, 2)) < 1e-6);
        var dots = runs.Where(r => r.Cm360 is > 0 && r.Accuracy != null && GroupOf(r) >= 0).ToList();
        if (dots.Count < 2) return null;
        var chart = new ScatterChart { Height = 200, Margin = new Thickness(0, 0, 0, 8) };
        chart.Set(dots.Select(r => (r.Cm360!.Value, r.Accuracy!.Value, $"{r.Cm360!.Value:0.##} cm/360 · {r.Scenario} · {L.Stamp(r.End)}")).ToList(), "0.## \"cm\"", "0.0%", dots.Select(GroupOf).ToList());
        var body = new StackPanel();
        body.Children.Add(chart);
        for (var gi = 0; gi < groups.Count; gi++)
        {
            var g = groups[gi];
            var full = new SolidColorBrush(ScatterChart.GroupColors[gi % ScatterChart.GroupColors.Length]);
            body.Children.Add(GroupRow(full, $"{g.Cm360:0.##} cm/360", L.F("{0} runs", g.Runs), [(g.Accuracy is { } a ? a.ToString("P1") : NoValue, full)], MeterBar(Math.Clamp(g.Accuracy ?? 0, 0, 1), full)));
        }
        return Section("Accuracy by sensitivity", body, CardMargin);
    }

    // ---- mouse speed ---------------------------------------------------------------------------------------------

    readonly Dictionary<Guid, RunSpeed?> _runSpeeds = [];

    /// <summary>The mouse speed of every run that has any: recorded replays (trajectory on disk, read once and cached) and deleted ones (speed kept in the archive). Off the UI thread.</summary>
    async Task<List<(string Scenario, DateTime End, RunSpeed Speed)>> AllSpeedsAsync()
    {
        var infos = _host.Replays.List().ToList();
        var store = _host.Settings.OpenStore();
        var archived = await Task.Run(() => store.ArchivedRuns().Where(r => r.Speed != null).ToList());
        var missing = infos.Where(i => !_runSpeeds.ContainsKey(i.Id)).ToList();
        if (missing.Count > 0)
        {
            var cache = _host.Settings.OpenSpeedCache();
            var archive = _host.Replays;
            var read = await Task.Run(() => { var r = missing.AsParallel().Select(i => (i.Id, Speed: cache.Get(archive, i.Id))).ToList(); cache.Save(); return r; });
            foreach (var (id, speed) in read) _runSpeeds[id] = speed;
        }
        return infos.Select(i => (i.Scenario, i.End, Speed: _runSpeeds.GetValueOrDefault(i.Id)))
            .Where(x => x.Speed != null).Select(x => (x.Scenario, x.End, x.Speed!))
            .Concat(archived.Select(r => (r.Scenario, r.End, r.Speed!))).ToList();
    }

    /// <summary>The speed of each run in <paramref name="runs"/> (its own archived figures, else the replay ending within the match tolerance).</summary>
    static List<RunSpeed> SpeedsOf(IEnumerable<RunRecord> runs, ILookup<string, (DateTime End, RunSpeed Speed)> all)
    {
        var result = new List<RunSpeed>();
        foreach (var r in runs)
        {
            if (r.Speed is { } own) { result.Add(own with { Cm360 = r.Cm360 }); continue; }
            RunSpeed? best = null;
            var gap = ReplayStore.RunMatchTolerance;
            foreach (var x in all[r.Scenario])
            {
                var d = (x.End - r.End).Duration();
                if (d < gap) { gap = d; best = x.Speed; }
            }
            if (best != null) result.Add(best with { Cm360 = r.Cm360 });
        }
        return result;
    }

    async Task FillRunSetSpeedAsync(Border host, (string Label, IReadOnlyList<RunRecord> Runs) focus, (string Label, IReadOnlyList<RunRecord> Runs)? against, bool byKind)
    {
        try
        {
            var all = (await AllSpeedsAsync()).ToLookup(x => x.Scenario, x => (x.End, x.Speed), StringComparer.OrdinalIgnoreCase);
            var lines = new List<(Brush Brush, string Label, IReadOnlyList<RunSpeed> Runs)>();
            var mine = SpeedsOf(focus.Runs, all);
            if (byKind)
            {
                // only the split by aim kind: one line per kind, no overall average and no comparison set
                string[] colors = ["Accent", "Orange", "Purple"];
                foreach (var g in focus.Runs.GroupBy(RunSetInsights.KindOf).OrderBy(g => g.Key))
                    if (SpeedsOf(g, all) is { Count: > 0 } ofKind) lines.Add(((Brush)FindResource(colors[(int)g.Key % colors.Length]), g.Key.ToString(), ofKind));
            }
            else if (mine.Count > 0) lines.Add(((Brush)FindResource("Accent"), focus.Label.Length > 0 ? focus.Label : L.T("Average"), mine));
            if (!byKind && against is { } a && !ReferenceEquals(a.Runs, focus.Runs) && SpeedsOf(a.Runs, all) is { Count: > 0 } other) lines.Add(((Brush)FindResource("Red"), a.Label, other));
            if (lines.Count > 0) host.Child = SpeedCard(lines);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Core.Diagnostics.ErrorReporter.Report("run set speed", e, Core.Diagnostics.ErrorSeverity.Low); }
    }
}
