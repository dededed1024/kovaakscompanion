using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using KovaaksCompanion.Core.Library;
using KovaaksCompanion.Core.Trajectory;

namespace KovaaksCompanion.App;

/// <summary>The card, table, tile and bar builders the scenario sheet, the session popup and the main statistics share.</summary>
static class StatBlocks
{
    internal static Brush DimB => (Brush)Application.Current.FindResource("Dim");
    internal static Brush FgB => (Brush)Application.Current.FindResource("Fg");

    internal static Brush Solid(string hex)
    {
        var b = new SolidColorBrush(Theme.Ink((Color)ColorConverter.ConvertFromString(hex)));
        b.Freeze();
        return b;
    }

    internal static string Dur(TimeSpan t) => t.TotalHours >= 1 ? L.F("{0}h {1}m", (int)t.TotalHours, t.Minutes) : L.F("{0}m {1}s", t.Minutes, t.Seconds);

    /// <summary>Inner padding of every stats card.</summary>
    internal static readonly Thickness CardPad = new(20, 16, 20, 14);
    /// <summary>The one gap between stacked cards and between grid tiles.</summary>
    internal const double Gap = 12;
    /// <summary>Space above a stats card that follows another element.</summary>
    internal static readonly Thickness CardMargin = new(0, Gap, 0, 0);

    /// <summary>The card surface every stats block sits on.</summary>
    internal static Border CardBox(UIElement child, Thickness? pad = null, Thickness? margin = null) =>
        new() { Style = (Style)Application.Current.FindResource("SheetCard"), Padding = pad ?? CardPad, Margin = margin ?? default, Child = child };

    /// <summary>A card title (15 SemiBold) with an optional dimmed note at its right end.</summary>
    internal static Grid Head(string? title, string? note = null, Thickness? margin = null)
    {
        var head = new Grid { Margin = margin ?? new Thickness(4, 0, 4, 10) };
        if (title is not null) head.Children.Add(new TextBlock { Text = L.T(title), Foreground = FgB, FontSize = 15, FontWeight = FontWeights.SemiBold });
        if (note is not null) head.Children.Add(new TextBlock { Text = note, Foreground = DimB, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center });
        return head;
    }

    /// <summary>A card with an optional title; <paramref name="note"/> sits dimmed at the title's right end.</summary>
    internal static Border Section(string? title, UIElement body, Thickness margin, string? note = null)
    {
        var sp = new StackPanel();
        if (title is not null) sp.Children.Add(Head(title, note));
        sp.Children.Add(body);
        return CardBox(sp, CardPad, margin);
    }

    /// <summary>A coloured dot with a label: the toggle chip of a chart series. A click calls <paramref name="click"/>; an unselected chip is dimmed.</summary>
    internal static Border Chip(string text, Brush dot, bool on, Action click)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new System.Windows.Shapes.Ellipse { Width = 9, Height = 9, Fill = dot, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock { Text = text, Foreground = on ? FgB : DimB, FontSize = 12.5, Margin = new Thickness(8, 0, 0, 0) });
        var chip = new Border { Style = (Style)Application.Current.FindResource("Chip"), Cursor = System.Windows.Input.Cursors.Hand, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 4, 4), CornerRadius = new CornerRadius(8), Opacity = on ? 1 : 0.55, Child = row };
        chip.MouseLeftButtonUp += (_, _) => click();
        return chip;
    }

    /// <summary>A card title with the chart's range bar at its right end.</summary>
    internal static Grid ChartHead(string title, UIElement rangeBar)
    {
        var head = Head(title, null, new Thickness(4, 0, 0, 8));
        head.Children.Add(rangeBar);
        return head;
    }

    /// <summary>A dot-and-label line with its figures on the right and a bar below; the row of one group (a sensitivity) in a card.</summary>
    internal static StackPanel GroupRow(Brush dot, string label, string runs, IReadOnlyList<(string Text, Brush Fg)> values, UIElement bar, Action? click = null)
    {
        var head = new Grid { Margin = new Thickness(4, 8, 4, 5), Background = Brushes.Transparent };
        if (click != null) { head.Cursor = System.Windows.Input.Cursors.Hand; head.MouseLeftButtonUp += (_, _) => click(); }
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Fill = dot });
        left.Children.Add(new TextBlock { Text = label, Foreground = DimB, FontSize = 13 });
        head.Children.Add(left);
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        right.Children.Add(new TextBlock { Text = runs, Foreground = DimB, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) });
        foreach (var (text, fg) in values) right.Children.Add(new TextBlock { Text = text, Foreground = fg, FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        head.Children.Add(right);
        ((FrameworkElement)bar).Margin = new Thickness(4, 0, 4, 2);
        var row = new StackPanel();
        row.Children.Add(head);
        row.Children.Add(bar);
        return row;
    }

    /// <summary>The advice card: one paragraph per piece of advice, its figures and the **marked** keywords of the translation in bold and the bright text colour, the rest dimmed.</summary>
    internal static Border AdviceSection(IReadOnlyList<Advice> advice, Thickness margin)
    {
        var body = new StackPanel();
        foreach (var a in advice)
        {
            var tb = new TextBlock { Foreground = DimB, FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 0, 4, 8) };
            // The figures are bracketed by control characters, so the formatted text can be cut back into plain and highlighted parts.
            var marked = Regex.Replace(L.T(a.Format), @"\{\d+[^}]*\}", "\u0001$0\u0002");
            var text = string.Format(L.Culture, marked, a.Args);
            var figure = false;
            foreach (var part in text.Split('\u0001', '\u0002'))
            {
                // Split drops the markers, so the parts alternate plain, figure, plain; "**" toggles keywords inside each part.
                var keyword = false;
                foreach (var seg in part.Split("**"))
                {
                    if (seg.Length > 0) tb.Inlines.Add(new System.Windows.Documents.Run(seg) { Foreground = figure || keyword ? FgB : DimB, FontWeight = figure || keyword ? FontWeights.SemiBold : FontWeights.Normal });
                    keyword = !keyword;
                }
                figure = !figure;
            }
            body.Children.Add(tb);
        }
        return Section("Advice", body, margin);
    }

    /// <summary>A label column and one value column per head, with a hairline above each row.</summary>
    internal static UIElement Table(IReadOnlyList<string> heads, IReadOnlyList<(string Label, IReadOnlyList<(string Text, Brush? Tone)> Cells)> rows, Action<int>? click = null, double labelWeight = 1.6)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWeight, GridUnitType.Star) });
        foreach (var _ in heads) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var r = 0; r <= rows.Count; r++) g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        void Put(int row, int col, string text, Brush fg, double size, bool bold, bool right)
        {
            var t = new TextBlock
            {
                Text = text, Foreground = fg, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, Margin = new Thickness(4, row == 0 ? 0 : 7, 4, 7),
                HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetRow(t, row); Grid.SetColumn(t, col);
            g.Children.Add(t);
        }
        if (heads.Any(h => h.Length > 0)) for (var c = 0; c < heads.Count; c++) Put(0, c + 1, heads[c], DimB, 11.5, false, true);
        for (var r = 0; r < rows.Count; r++)
        {
            if (click != null)
            {
                var index = r;
                var hit = new Border { Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand, CornerRadius = new CornerRadius(6) };
                hit.MouseEnter += (_, _) => hit.Background = (Brush)Application.Current.FindResource("Wash");
                hit.MouseLeave += (_, _) => hit.Background = Brushes.Transparent;
                hit.MouseLeftButtonUp += (_, _) => click(index);
                Grid.SetRow(hit, r + 1); Grid.SetColumnSpan(hit, heads.Count + 1);
                g.Children.Add(hit);
            }
            var line = new Border { BorderBrush = (Brush)Application.Current.FindResource("EdgeFaint"), BorderThickness = new Thickness(0, 1, 0, 0), IsHitTestVisible = false };
            Grid.SetRow(line, r + 1); Grid.SetColumnSpan(line, heads.Count + 1);
            g.Children.Add(line);
            Put(r + 1, 0, rows[r].Label, DimB, 13, false, false);
            for (var c = 0; c < rows[r].Cells.Count; c++) Put(r + 1, c + 1, rows[r].Cells[c].Text, rows[r].Cells[c].Tone ?? FgB, 13, true, true);
        }
        return g;
    }

    /// <summary>A thin bar filled to <paramref name="frac"/> of its width; an optional tick marks a second value.</summary>
    internal static UIElement MeterBar(double frac, Brush fill, double height = 5, double? tick = null, Brush? tickBrush = null)
    {
        var g = new Grid { Height = height };
        g.Children.Add(new Border { CornerRadius = new CornerRadius(height / 2), Background = Solid("#14FFFFFF") });
        var inner = new Grid();
        frac = double.IsNaN(frac) ? 0 : Math.Clamp(frac, 0, 1);
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(frac, 1e-6), GridUnitType.Star) });
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1 - frac, 1e-6), GridUnitType.Star) });
        inner.Children.Add(new Border { CornerRadius = new CornerRadius(height / 2), Background = fill });
        g.Children.Add(inner);
        if (tick is { } t)
        {
            t = double.IsNaN(t) ? 0 : Math.Clamp(t, 0, 1);
            var m = new Grid();
            m.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(t, 1e-6), GridUnitType.Star) });
            m.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1 - t, 1e-6), GridUnitType.Star) });
            m.Children.Add(new Border { Width = 2, HorizontalAlignment = HorizontalAlignment.Right, Background = tickBrush ?? Brushes.White, Margin = new Thickness(0, -2, -1, -2) });
            g.Children.Add(m);
        }
        return g;
    }

    /// <summary>The colour with less saturation and more lightness, same hue.</summary>
    internal static Color Washed(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        var l = (max + min) / 2;
        var sat = d == 0 ? 0 : d / (1 - Math.Abs(2 * l - 1));
        var h = d == 0 ? 0 : max == r ? ((g - b) / d % 6 + 6) % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        sat = Math.Min(1, sat * 0.9); l = 0.86;
        var chroma = (1 - Math.Abs(2 * l - 1)) * sat;
        var x = chroma * (1 - Math.Abs(h % 2 - 1));
        var m = l - chroma / 2;
        var (r1, g1, b1) = (int)h switch { 0 => (chroma, x, 0.0), 1 => (x, chroma, 0.0), 2 => (0.0, chroma, x), 3 => (0.0, x, chroma), 4 => (x, 0.0, chroma), _ => (chroma, 0.0, x) };
        byte B(double v) => (byte)Math.Round(Math.Clamp(v + m, 0, 1) * 255);
        return Color.FromRgb(B(r1), B(g1), B(b1));
    }

    /// <summary>The best bar with the average bar drawn over it, so the part past the average shows how far the best reaches. When both are the same only the best bar is drawn.</summary>
    internal static UIElement OverlapBar(double avg, double best, Brush avgFill, Brush bestFill, double height = 6)
    {
        var g = new Grid { Height = height };
        g.Children.Add(new Border { CornerRadius = new CornerRadius(height / 2), Background = Solid("#14FFFFFF") });
        UIElement Fill(double frac, Brush b)
        {
            frac = double.IsNaN(frac) ? 0 : Math.Clamp(frac, 0, 1);
            var inner = new Grid();
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(frac, 1e-6), GridUnitType.Star) });
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1 - frac, 1e-6), GridUnitType.Star) });
            inner.Children.Add(new Border { CornerRadius = new CornerRadius(height / 2), Background = b });
            return inner;
        }
        g.Children.Add(Fill(best, bestFill));
        if (Math.Abs(avg - best) > 1e-6) g.Children.Add(Fill(avg, avgFill));
        return g;
    }

    /// <summary>Wraps a grid of cards: as many columns (up to <paramref name="max"/>) as fit at <paramref name="minCell"/> px each. The cards carry a <see cref="Gap"/> right and bottom margin; the grid's own right margin takes the last one back.</summary>
    internal static void Responsive(UniformGrid grid, int max, double minCell)
    {
        grid.Margin = new Thickness(0, 0, -Gap, 0);
        grid.SizeChanged += (_, e) =>
        {
            var cols = Math.Clamp((int)(e.NewSize.Width / minCell), 1, Math.Max(1, max));
            if (grid.Columns != cols) grid.Columns = cols;
        };
    }

    /// <summary>A row of stat cards (label over a big value), wrapping to as many columns as fit.</summary>
    internal static UIElement TileRow(List<(string Label, UIElement Content)> tiles)
    {
        var grid = new UniformGrid { Columns = tiles.Count };
        Responsive(grid, tiles.Count, 124);
        for (var i = 0; i < tiles.Count; i++)
        {
            var body = new StackPanel();
            body.Children.Add(new TextBlock { Text = tiles[i].Label, Foreground = DimB, FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis });
            body.Children.Add(tiles[i].Content);
            grid.Children.Add(CardBox(body, new Thickness(14, 10, 14, 12), new Thickness(0, 0, Gap, Gap)));
        }
        return grid;
    }

    internal static TextBlock BigValue(string text, Brush fg, Brush? tone = null) =>
        new() { Text = text, Foreground = tone ?? fg, FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };

    /// <summary>The brush's color with a quarter of it mixed toward grey, so it stays recognisable but is easier on the eyes.</summary>
    static Brush Soft(Brush b)
    {
        if (b is not SolidColorBrush { Color: var c }) return b;
        byte Mix(byte v) => (byte)Math.Round(v * 0.75 + 128 * 0.25);
        var soft = new SolidColorBrush(Color.FromRgb(Mix(c.R), Mix(c.G), Mix(c.B)));
        soft.Freeze();
        return soft;
    }

    /// <summary>
    /// Mouse-speed figures as bars, one block per unit separated by lines (bars of a block share one scale). Each figure shows one bar per
    /// line: the average figure of that line's plays.
    /// </summary>
    internal static UIElement? SpeedCard(IReadOnlyList<(Brush Brush, string Label, IReadOnlyList<RunSpeed> Runs)> lines, AimKind? kind = null)
    {
        const string Speed = "{0:0.0} cm/s", Ms = "{0:0} ms", Pct = "{0:0}%";
        (string Label, Func<RunSpeed, double?> Get, string Format)[] speed =
        [
            ("Average speed", r => r.ToCmPerSec(r.Mean), Speed), ("Max speed", r => r.ToCmPerSec(r.Max), Speed),
            ("Firing speed", r => r.FireSpeed is { } f ? r.ToCmPerSec(f) : null, Speed), ("Non-firing speed", r => r.IdleSpeed is { } i ? r.ToCmPerSec(i) : null, Speed),
            ("Peak flick speed", r => r.FlickPeak is { } k ? r.ToCmPerSec(k) : null, Speed),
        ];
        (string Label, Func<RunSpeed, double?> Get, string Format)[] time =
        [
            ("Flick time", r => r.FlickTime * 1000, Ms), ("Reaction time", r => r.Reaction * 1000, Ms), ("Time between kills", r => r.KillInterval * 1000, Ms), ("Click delay after stop", r => r.StopToClick * 1000, Ms),
        ];
        (string Label, Func<RunSpeed, double?> Get, string Format)[] ratio =
        [
            ("Path efficiency", r => r.Efficiency * 100, Pct), ("Firing time", r => r.HoldRatio * 100, Pct), ("Speed variation", r => r.SpeedCv * 100, Pct), ("Ballistic ratio", r => r.BallisticRatio * 100, Pct),
        ];
        (string Label, Func<RunSpeed, double?> Get, string Format)[] other =
        [
            ("Flick distance", r => r.FlickDistance, "{0:0.0}°"), ("Direction changes", r => r.ReversalsPerSec, "{0:0.0}/s"), ("Jerk", r => r.Jerk / 1000, "{0:0.0} k°/s³"), ("Acceleration variation", r => r.AccelStd is { } a ? r.ToCmPerSec(a) : null, "{0:0} cm/s²"),
        ];
        string Show(double v, string format) => string.Format(System.Globalization.CultureInfo.InvariantCulture, format, v);

        var host = new StackPanel();
        var head = Head("Mouse speed", null, new Thickness(4, 0, 4, 4));
        var legend = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        void Key(string text, Brush brush)
        {
            legend.Children.Add(new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Fill = brush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(legend.Children.Count == 0 ? 0 : 12, 0, 5, 0) });
            legend.Children.Add(new TextBlock { Text = text, Foreground = DimB, FontSize = 12 });
        }
        lines = lines.Select(l => (Soft(l.Brush), l.Label, l.Runs)).ToList(); // many bars at once: softer than the colors of the single-figure charts
        foreach (var line in lines) Key(line.Label, line.Brush);
        head.Children.Add(legend);
        host.Children.Add(head);

        const double AxisT = 1.5, Tail = 132, TailY = 12, CapMin = 0.2, CapMax = 0.95, BarH = 8, Exaggerate = 2.5, SlotH = 13, RowPad = 6, LabelW = 124, NumW = 84;
        (string Name, (string Label, Func<RunSpeed, double?> Get, string Format)[] Specs)[] groups =
        [
            ("General", [speed[0], speed[1], speed[2], speed[3], time[2], ratio[1], other[1]]),
            ("Tracking", [ratio[2], other[2], other[3]]),
            ("Flick", [speed[4], time[0], time[3], time[1], ratio[0], ratio[3], other[0]]),
        ];
        // The group of the scenario's own kind comes right after the general figures.
        var own = kind switch { AimKind.Tracking => "Tracking", AimKind.Flick or AimKind.Switching => "Flick", _ => null };
        if (own != null) groups = [groups[0], .. groups.Skip(1).OrderBy(g => g.Name == own ? 0 : 1)];

        void Group(string name, (string Label, Func<RunSpeed, double?> Get, string Format)[] specs)
        {
            var rows = specs.Select(sp => (Spec: sp, Values: lines.Select(l => { var xs = l.Runs.Select(sp.Get).OfType<double>().ToList(); return xs.Count == 0 ? (double?)null : xs.Average(); }).ToList()))
                .Where(x => x.Values.Any(v => v != null)).ToList();
            if (rows.Count == 0) return;
            host.Children.Add(new TextBlock { Text = L.T(name), Foreground = FgB, FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, host.Children.Count > 1 ? 12 : 2, 0, 3) });
            host.Children.Add(new Border { Height = 1, Background = Solid("#26FFFFFF"), Margin = new Thickness(0, 0, 0, 2) });

            // Horizontal bars: one row per figure (label left, bars of the lines stacked), each row scaled to its own largest value.
            var chart = new Grid();
            chart.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LabelW) });
            chart.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(NumW) });
            chart.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (var r = 0; r < rows.Count; r++) chart.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            chart.RowDefinitions.Add(new RowDefinition { Height = new GridLength(TailY) });
            var axisColor = (Color)ColorConverter.ConvertFromString("#66FFFFFF");
            var axisClear = Color.FromArgb(0, 255, 255, 255);
            LinearGradientBrush Fade(Color from, Color to, bool horizontal) => new(from, to, horizontal ? 0 : 90);

            for (var r = 0; r < rows.Count; r++)
            {
                var (spec, values) = rows[r];
                // Length = (value / largest)^Exaggerate: a small gap shows a little, a big gap shows a lot, all bars of a row shift left together by the shortest one's share, so the longest sits at CapMin of the width for close values, gaps intact, and moves out to CapMax as the spread grows.
                var hi = values.OfType<double>().DefaultIfEmpty(0).Max();
                var lo = values.OfType<double>().DefaultIfEmpty(0).Min();
                var low = hi > 0 ? Math.Pow(Math.Max(0, lo) / hi, Exaggerate) : 1;
                var shift = 1 - (CapMin + (CapMax - CapMin) * (1 - low));
                var label = new TextBlock { Text = L.T(spec.Label), Foreground = DimB, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, RowPad, 8, RowPad) };
                Grid.SetRow(label, r);
                chart.Children.Add(label);
                // Row separator = an x axis: solid across the plot, a short fixed tail fading out to the left under the numbers.
                var sep = new Border { Height = AxisT, Background = new SolidColorBrush(axisColor), VerticalAlignment = VerticalAlignment.Bottom };
                Grid.SetRow(sep, r);
                Grid.SetColumn(sep, 2);
                chart.Children.Add(sep);
                var sepTail = new Border { Height = AxisT, Width = Tail, Background = Fade(axisClear, axisColor, true), VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Right };
                Grid.SetRow(sepTail, r);
                Grid.SetColumnSpan(sepTail, 2); // spans label + number columns so the tail is not clipped to the number column
                chart.Children.Add(sepTail);

                var nums = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, RowPad, 8, RowPad) };
                var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, RowPad, 0, RowPad) };
                for (var i = 0; i < lines.Count; i++)
                {
                    if (values[i] is not { } y) continue;
                    var frac = hi > 0 ? Math.Max(0.01, Math.Pow(Math.Max(0, y) / hi, Exaggerate) - shift) : 0;
                    nums.Children.Add(new TextBlock { Text = Show(y, spec.Format), Foreground = FgB, FontSize = 12, Height = SlotH, TextAlignment = TextAlignment.Right, HorizontalAlignment = HorizontalAlignment.Right, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, LineHeight = SlotH });
                    var bar = new Grid { Height = SlotH };
                    bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(frac, 0.0001), GridUnitType.Star) });
                    bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1 - frac, 0.0001), GridUnitType.Star) });
                    bar.Children.Add(new Border { Height = BarH, Background = lines[i].Brush, CornerRadius = new CornerRadius(0, 2, 2, 0), MinWidth = 2, VerticalAlignment = VerticalAlignment.Center });
                    stack.Children.Add(bar);
                }
                Grid.SetColumn(nums, 1);
                Grid.SetRow(nums, r);
                chart.Children.Add(nums);
                Grid.SetColumn(stack, 2);
                Grid.SetRow(stack, r);
                chart.Children.Add(stack);
            }
            // Vertical guides at 25 / 50 / 75% of the bar length, behind the bars (added first so they draw underneath).
            var guides = new Grid();
            for (var c = 0; c < 4; c++)
            {
                guides.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                if (c == 0) continue;
                var guide = new Border { Width = 1, Background = Solid("#17FFFFFF"), HorizontalAlignment = HorizontalAlignment.Left };
                Grid.SetColumn(guide, c);
                guides.Children.Add(guide);
            }
            Grid.SetColumn(guides, 2);
            Grid.SetRowSpan(guides, rows.Count);
            chart.Children.Insert(0, guides);
            var yAxis = new Border { Width = AxisT, Background = new SolidColorBrush(axisColor), HorizontalAlignment = HorizontalAlignment.Left };
            Grid.SetColumn(yAxis, 2);
            Grid.SetRowSpan(yAxis, rows.Count);
            chart.Children.Add(yAxis);
            var yTail = new Border { Width = AxisT, Background = Fade(axisColor, axisClear, false), HorizontalAlignment = HorizontalAlignment.Left };
            Grid.SetColumn(yTail, 2);
            Grid.SetRow(yTail, rows.Count);
            chart.Children.Add(yTail);
            host.Children.Add(chart);
        }
        foreach (var (name, specs) in groups) Group(name, specs);
        return CardBox(host, new Thickness(CardPad.Left, CardPad.Top, CardPad.Right, 8), CardMargin);
    }

    /// <summary>Runs by the part of the day they started in.</summary>
    internal static UIElement? TimeOfDay(IReadOnlyList<RunRecord> runs)
    {
        var blocks = RunSetInsights.ByTimeOfDay(runs);
        if (blocks.Count < 2) return null;
        var chart = new BarChart { Height = 150 };
        chart.Set(blocks.Select(b => new BarItem($"{b.Hour:00}", b.Accuracy ?? 0, L.F("{0} runs", b.Runs) + (b.Accuracy is { } a ? " · " + a.ToString("P1") : ""))).ToList(), "0.0%");
        return Section("Time of day", chart, CardMargin, L.T("Accuracy"));
    }
}
