using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Components;

namespace HVO.SkyMonitorV5.RPi.Components.Shared;

public partial class HistoryLineChart : ComponentBase
{
    private readonly List<HistoryDataPoint> _dataPoints = new();
    private readonly List<AxisTick> _yTicks = new();
    private readonly List<AxisTick> _xTicks = new();
    private readonly string _chartId = $"history-chart-{Guid.NewGuid():N}";

    private string _linePath = string.Empty;
    private double _plotLeft;
    private double _plotTop;
    private double _plotRight;
    private double _plotBottom;
    private double _plotWidth;
    private double _plotHeight;
    private double _yAxisTitleX;
    private double _yAxisTitleY;
    private double _xAxisTitleX;

    [Parameter]
    public IReadOnlyList<double>? Values { get; set; }

    [Parameter]
    public string DatasetLabel { get; set; } = "Series";

    [Parameter]
    public string Color { get; set; } = "#0d6efd";

    [Parameter]
    public string YAxisLabel { get; set; } = string.Empty;

    [Parameter]
    public string ValueSuffix { get; set; } = string.Empty;

    [Parameter]
    public string XAxisLabel { get; set; } = "Samples";

    [Parameter]
    public string EmptyMessage { get; set; } = "No telemetry samples yet.";

    protected IReadOnlyList<HistoryDataPoint> DataPoints => _dataPoints;

    protected bool HasData => _dataPoints.Count > 0;

    protected IReadOnlyList<AxisTick> YTicks => _yTicks;

    protected IReadOnlyList<AxisTick> XTicks => _xTicks;

    protected string LinePath => _linePath;

    protected double SvgWidth => Dimensions.SvgWidth;

    protected double SvgHeight => Dimensions.SvgHeight;

    protected double PlotLeft => _plotLeft;

    protected double PlotRight => _plotRight;

    protected double PlotTop => _plotTop;

    protected double PlotBottom => _plotBottom;

    protected string ChartTitleId => $"{_chartId}-title";

    protected string ChartDescriptionId => $"{_chartId}-description";

    protected double YAxisTitleX => _yAxisTitleX;

    protected double YAxisTitleY => _yAxisTitleY;

    protected double XAxisTitleX => _xAxisTitleX;

    protected string ChartColor => string.IsNullOrWhiteSpace(Color) ? "#0d6efd" : Color;

    protected string YAxisTransform => string.Format(CultureInfo.InvariantCulture, "rotate(-90, {0}, {1})", YAxisTitleX, YAxisTitleY);

    protected override void OnParametersSet()
    {
        _dataPoints.Clear();
        _yTicks.Clear();
        _xTicks.Clear();
        _linePath = string.Empty;

        if (Values is null || Values.Count == 0)
        {
            return;
        }

        for (var index = 0; index < Values.Count; index++)
        {
            var value = Values[index];
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                value = 0d;
            }

            var label = (index + 1).ToString(CultureInfo.CurrentCulture);
            _dataPoints.Add(new HistoryDataPoint(label, value));
        }

        BuildChartGeometry();
    }

    protected sealed record HistoryDataPoint(string Label, double Value);

    protected sealed record AxisTick(double Coordinate, string Label);

    private void BuildChartGeometry()
    {
        _plotLeft = Dimensions.MarginLeft;
        _plotTop = Dimensions.MarginTop;
        _plotRight = Dimensions.SvgWidth - Dimensions.MarginRight;
        _plotBottom = Dimensions.SvgHeight - Dimensions.MarginBottom;
        _plotWidth = _plotRight - _plotLeft;
        _plotHeight = _plotBottom - _plotTop;

        _yAxisTitleX = _plotLeft - Dimensions.YAxisTitleOffset;
        _yAxisTitleY = _plotTop + (_plotHeight / 2);
        _xAxisTitleX = _plotLeft + (_plotWidth / 2);

        if (_plotWidth <= 0 || _plotHeight <= 0)
        {
            return;
        }

        var minValue = _dataPoints.Min(p => p.Value);
        var maxValue = _dataPoints.Max(p => p.Value);

        var niceScale = CalculateNiceScale(minValue, maxValue, Dimensions.MaxTickCount);
        var range = niceScale.NiceMax - niceScale.NiceMin;
        if (range <= double.Epsilon)
        {
            range = 1d;
        }

        for (var tickValue = niceScale.NiceMin;
             tickValue <= niceScale.NiceMax + (niceScale.TickSpacing / 2);
             tickValue += niceScale.TickSpacing)
        {
            var normalized = (tickValue - niceScale.NiceMin) / range;
            var y = _plotBottom - (normalized * _plotHeight);
            var label = string.IsNullOrWhiteSpace(ValueSuffix)
                ? tickValue.ToString("0.0", CultureInfo.CurrentCulture)
                : string.Format(CultureInfo.CurrentCulture, "{0:0.0}{1}", tickValue, ValueSuffix);
            _yTicks.Add(new AxisTick(y, label));
        }

        var pointCount = _dataPoints.Count;
        if (pointCount == 0)
        {
            return;
        }

        var interval = DetermineLabelInterval(pointCount);
        var builder = new StringBuilder();

        for (var index = 0; index < pointCount; index++)
        {
            var point = _dataPoints[index];
            var position = pointCount == 1
                ? _plotLeft + (_plotWidth / 2)
                : _plotLeft + (index * (_plotWidth / (pointCount - 1)));

            var normalized = (point.Value - niceScale.NiceMin) / range;
            var y = _plotBottom - (normalized * _plotHeight);

            if (index == 0)
            {
                builder.AppendFormat(CultureInfo.InvariantCulture, "M {0:F2} {1:F2}", position, y);
            }
            else
            {
                builder.AppendFormat(CultureInfo.InvariantCulture, " L {0:F2} {1:F2}", position, y);
            }

            if (pointCount == 1)
            {
                builder.AppendFormat(CultureInfo.InvariantCulture, " L {0:F2} {1:F2}", position + 1, y);
            }

            if ((index % interval == 0) || index == pointCount - 1)
            {
                _xTicks.Add(new AxisTick(position, point.Label));
            }
        }

        _linePath = builder.ToString();
    }

    private static int DetermineLabelInterval(int pointCount)
    {
        if (pointCount <= Dimensions.MaxXAxisLabels)
        {
            return 1;
        }

        var interval = (int)Math.Ceiling(pointCount / (double)Dimensions.MaxXAxisLabels);
        return Math.Max(interval, 1);
    }

    private static NiceScaleResult CalculateNiceScale(double min, double max, int maxTickCount)
    {
        if (double.IsNaN(min) || double.IsInfinity(min))
        {
            min = 0;
        }

        if (double.IsNaN(max) || double.IsInfinity(max))
        {
            max = 0;
        }

        if (Math.Abs(max - min) < double.Epsilon)
        {
            if (Math.Abs(max) < double.Epsilon)
            {
                max = 1;
            }
            else
            {
                min -= Math.Abs(max) * 0.1;
                max += Math.Abs(max) * 0.1;
            }
        }

        var range = NiceNumber(max - min, false);
        var spacing = NiceNumber(range / Math.Max(maxTickCount - 1, 1), true);
        var niceMin = Math.Floor(min / spacing) * spacing;
        var niceMax = Math.Ceiling(max / spacing) * spacing;

        if (Math.Abs(niceMin) < double.Epsilon && niceMin < 0)
        {
            niceMin = 0;
        }

        return new NiceScaleResult(niceMin, niceMax, spacing);
    }

    private static double NiceNumber(double value, bool round)
    {
        if (value <= 0)
        {
            return 1;
        }

        var exponent = Math.Floor(Math.Log10(value));
        var fraction = value / Math.Pow(10, exponent);
        double niceFraction;

        if (round)
        {
            niceFraction = fraction switch
            {
                < 1.5 => 1,
                < 3 => 2,
                < 7 => 5,
                _ => 10
            };
        }
        else
        {
            niceFraction = fraction switch
            {
                <= 1 => 1,
                <= 2 => 2,
                <= 5 => 5,
                _ => 10
            };
        }

        return niceFraction * Math.Pow(10, exponent);
    }

    private readonly record struct NiceScaleResult(double NiceMin, double NiceMax, double TickSpacing);

    private static class Dimensions
    {
        public const double SvgWidth = 400;
        public const double SvgHeight = 240;
        public const double MarginLeft = 76;
        public const double MarginRight = 20;
        public const double MarginTop = 32;
        public const double MarginBottom = 72;
        public const double YAxisTitleOffset = 44;
        public const int MaxTickCount = 5;
        public const int MaxXAxisLabels = 14;
    }

    protected MarkupString BuildSvgText(double x, double y, string? dy, string cssClass, string anchor, string content, string? transform = null)
    {
        var builder = new StringBuilder();
        builder.Append("<text");
        builder.AppendFormat(CultureInfo.InvariantCulture, " x=\"{0}\" y=\"{1}\"", x, y);

        if (!string.IsNullOrWhiteSpace(dy))
        {
            builder.AppendFormat(CultureInfo.InvariantCulture, " dy=\"{0}\"", dy);
        }

        if (!string.IsNullOrWhiteSpace(cssClass))
        {
            builder.AppendFormat(CultureInfo.InvariantCulture, " class=\"{0}\"", cssClass);
        }

        if (!string.IsNullOrWhiteSpace(anchor))
        {
            builder.AppendFormat(CultureInfo.InvariantCulture, " text-anchor=\"{0}\"", anchor);
        }

        if (!string.IsNullOrWhiteSpace(transform))
        {
            builder.AppendFormat(CultureInfo.InvariantCulture, " transform=\"{0}\"", transform);
        }

        builder.Append('>');
        builder.Append(HtmlEncoder.Default.Encode(content));
        builder.Append("</text>");

        return new MarkupString(builder.ToString());
    }
}
