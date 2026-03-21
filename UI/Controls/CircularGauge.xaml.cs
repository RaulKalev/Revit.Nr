using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Renumber.UI.Controls
{
    /// <summary>
    /// A self-contained circular gauge UserControl that renders a 270-degree arc
    /// with progress fill, state-based coloring, and center-aligned text readout.
    ///
    /// Usage:
    ///   <controls:CircularGauge Title="Load" Value="{Binding CurrentLoadmA}"
    ///                            MaxValue="{Binding MaxLoadmA}" UnitText="mA"/>
    ///
    /// Arc geometry:
    ///   The gauge sweeps 270 degrees, starting at 135 deg (bottom-left)
    ///   and ending at 405 deg (bottom-right). This leaves a 90-degree gap
    ///   at the bottom, producing the classic "dashboard gauge" look.
    ///
    /// Color states (based on Value/MaxValue ratio):
    ///   Normal:  ratio less than WarningThreshold (default 0.80) -> green
    ///   Warning: ratio >= WarningThreshold and less than ErrorThreshold -> amber
    ///   Error:   ratio >= ErrorThreshold (default 1.00) -> red
    /// </summary>
    public partial class CircularGauge : UserControl
    {
        // Arc parameters
        private const double CenterX = 80.0;
        private const double CenterY = 80.0;
        private const double Radius = 65.0;
        private const double StartAngleDeg = 135.0; // bottom-left
        private const double SweepAngleDeg = 270.0; // total sweep

        // ---- Dependency Properties ----

        /// <summary>Gauge title displayed above the value text.</summary>
        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(nameof(Title), typeof(string), typeof(CircularGauge),
                new PropertyMetadata("Gauge", OnGaugePropertyChanged));

        /// <summary>Current numeric value.</summary>
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(double), typeof(CircularGauge),
                new PropertyMetadata(0.0, OnGaugePropertyChanged));

        /// <summary>Maximum value (full-scale).</summary>
        public static readonly DependencyProperty MaxValueProperty =
            DependencyProperty.Register(nameof(MaxValue), typeof(double), typeof(CircularGauge),
                new PropertyMetadata(100.0, OnGaugePropertyChanged));

        /// <summary>Unit label (e.g., "mA", "addr").</summary>
        public static readonly DependencyProperty UnitTextProperty =
            DependencyProperty.Register(nameof(UnitText), typeof(string), typeof(CircularGauge),
                new PropertyMetadata("", OnGaugePropertyChanged));

        /// <summary>Ratio threshold at which the gauge enters warning state (default 0.80).</summary>
        public static readonly DependencyProperty WarningThresholdProperty =
            DependencyProperty.Register(nameof(WarningThreshold), typeof(double), typeof(CircularGauge),
                new PropertyMetadata(0.80, OnGaugePropertyChanged));

        /// <summary>Ratio threshold at which the gauge enters error state (default 1.00).</summary>
        public static readonly DependencyProperty ErrorThresholdProperty =
            DependencyProperty.Register(nameof(ErrorThreshold), typeof(double), typeof(CircularGauge),
                new PropertyMetadata(1.00, OnGaugePropertyChanged));

        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public double Value
        {
            get => (double)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public double MaxValue
        {
            get => (double)GetValue(MaxValueProperty);
            set => SetValue(MaxValueProperty, value);
        }

        public string UnitText
        {
            get => (string)GetValue(UnitTextProperty);
            set => SetValue(UnitTextProperty, value);
        }

        public double WarningThreshold
        {
            get => (double)GetValue(WarningThresholdProperty);
            set => SetValue(WarningThresholdProperty, value);
        }

        public double ErrorThreshold
        {
            get => (double)GetValue(ErrorThresholdProperty);
            set => SetValue(ErrorThresholdProperty, value);
        }

        // ---- Constructor ----

        public CircularGauge()
        {
            InitializeComponent();
            Loaded += (s, e) => UpdateGauge();
        }

        // ---- Property Change Handler ----

        private static void OnGaugePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CircularGauge gauge && gauge.IsLoaded)
            {
                gauge.UpdateGauge();
            }
        }

        // ---- Gauge Rendering ----

        /// <summary>
        /// Recomputes all arc geometry and text whenever Value, MaxValue, or thresholds change.
        /// </summary>
        private void UpdateGauge()
        {
            double max = MaxValue > 0 ? MaxValue : 1.0;
            double ratio = Value / max;
            double clampedRatio = Math.Min(Math.Max(ratio, 0.0), 1.5); // visual clamp at 150%

            // --- Update track arc (full 270-degree sweep) ---
            TrackArc.Data = CreateArcGeometry(1.0);

            // --- Update progress arc ---
            double progressRatio = Math.Min(clampedRatio, 1.0); // can't draw beyond full circle
            if (progressRatio > 0.001)
            {
                ProgressArc.Data = CreateArcGeometry(progressRatio);
                ProgressArc.Visibility = Visibility.Visible;
            }
            else
            {
                ProgressArc.Visibility = Visibility.Collapsed;
            }

            // --- Determine color state ---
            Brush stateBrush;
            if (ratio >= ErrorThreshold)
            {
                stateBrush = (Brush)FindResource("GaugeErrorBrush");
            }
            else if (ratio >= WarningThreshold)
            {
                stateBrush = (Brush)FindResource("GaugeWarningBrush");
            }
            else
            {
                stateBrush = (Brush)FindResource("GaugeNormalBrush");
            }
            ProgressArc.Stroke = stateBrush;

            // --- Update text ---
            TitleText.Text = Title ?? "";

            // Format value appropriately (show decimal for mA, integer for addresses)
            string valueStr;
            if (Value == Math.Floor(Value) && Value < 10000)
            {
                valueStr = $"{Value:N0} / {max:N0}";
            }
            else
            {
                valueStr = $"{Value:N1} / {max:N0}";
            }

            if (!string.IsNullOrEmpty(UnitText))
            {
                valueStr += $" {UnitText}";
            }
            ValueText.Text = valueStr;

            int percentInt = (int)Math.Round(ratio * 100);
            PercentText.Text = $"{percentInt}%";

            // Tint percentage text with state color when in warning/error
            if (ratio >= WarningThreshold)
            {
                PercentText.Foreground = stateBrush;
            }
            else
            {
                PercentText.Foreground = new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF));
            }
        }

        /// <summary>
        /// Creates an arc PathGeometry for the given ratio (0.0 to 1.0) of the total 270-degree sweep.
        ///
        /// The arc starts at 135 degrees (7 o'clock position) and sweeps clockwise.
        /// A ratio of 1.0 produces a full 270-degree arc ending at 405 degrees (5 o'clock).
        ///
        /// For arcs greater than 180 degrees, the WPF ArcSegment IsLargeArc flag is set to true.
        /// </summary>
        private static PathGeometry CreateArcGeometry(double ratio)
        {
            double sweepDeg = SweepAngleDeg * Math.Min(Math.Max(ratio, 0.0), 1.0);

            // Convert angles to radians
            double startRad = StartAngleDeg * Math.PI / 180.0;
            double endRad = (StartAngleDeg + sweepDeg) * Math.PI / 180.0;

            // Compute start and end points on the circle
            double startX = CenterX + Radius * Math.Cos(startRad);
            double startY = CenterY + Radius * Math.Sin(startRad);
            double endX = CenterX + Radius * Math.Cos(endRad);
            double endY = CenterY + Radius * Math.Sin(endRad);

            // WPF requires IsLargeArc when sweep exceeds 180 degrees
            bool isLargeArc = sweepDeg > 180.0;

            var figure = new PathFigure
            {
                StartPoint = new Point(startX, startY),
                IsClosed = false,
                IsFilled = false
            };

            figure.Segments.Add(new ArcSegment
            {
                Point = new Point(endX, endY),
                Size = new Size(Radius, Radius),
                SweepDirection = SweepDirection.Clockwise,
                IsLargeArc = isLargeArc
            });

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            return geometry;
        }
    }
}
