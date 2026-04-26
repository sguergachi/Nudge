// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// Analytics Window - Productivity Insights Dashboard
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
//
// Features:
// - Shows most used applications
// - Displays hourly productivity patterns
// - Filter by Today / This Week
// - Fluent Design System UI matching CustomNotification style
//
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace NudgeTray
{
    public class AnalyticsWindow : Window
    {
        private enum DetailViewType
        {
            None,
            Activity,
            Apps,
            Productivity
        }

        private TimeFilter _currentFilter = TimeFilter.Today;
        private AnalyticsData? _data;
        private readonly AnalyticsData? _initialData;
        private Border? _contentViewport;
        private StackPanel? _contentPanel;
        private TranslateTransform? _contentTransform;
        private double _contentScrollOffset;
        private DetailViewType _activeDetailView;
        private Border? _todayTab;
        private Border? _weekTab;

        // Fluent Design System Colors - matching CustomNotification
        private static readonly Color BackgroundColor = Color.FromRgb(18, 18, 20);
        private static readonly Color SurfaceColor = Color.FromArgb(245, 18, 18, 20);
        private static readonly Color CardColor = Color.FromRgb(25, 25, 28);
        private static readonly Color PrimaryBlue = Color.FromRgb(88, 166, 255);
        private static readonly Color PrimaryBlueHover = Color.FromRgb(108, 186, 255);
        private static readonly Color TextPrimary = Color.FromRgb(240, 240, 245);
        private static readonly Color TextSecondary = Color.FromRgb(150, 150, 160);
        private static readonly Color TextTertiary = Color.FromRgb(120, 120, 130);
        private static readonly Color BorderColor = Color.FromArgb(40, 255, 255, 255);
        private static readonly Color ProgressBarBg = Color.FromRgb(35, 35, 40);
        private static readonly Color ProductiveGreen = Color.FromRgb(76, 175, 80);
        private static readonly Color UnproductiveRed = Color.FromRgb(244, 67, 54);

        public enum TimeFilter
        {
            Today,
            ThisWeek
        }

        public AnalyticsWindow(AnalyticsData? initialData = null)
        {
            _initialData = initialData;
            InitializeWindow();
            if (_initialData != null)
            {
                _data = _initialData;
                BuildUI();
            }
            else
            {
                LoadDataAndDisplay();
            }
        }

        private void InitializeWindow()
        {
            Width = 420;
            Height = 580;
            CanResize = false;
            ShowInTaskbar = false;
            WindowDecorations = WindowDecorations.None;
            Title = "Nudge Analytics";
            Background = Brushes.Transparent;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            Focusable = true;

            // Position near bottom-right (typical tray icon location)
            var screen = Screens.Primary;
            if (screen != null)
            {
                var workingArea = screen.WorkingArea;
                Position = new PixelPoint(
                    workingArea.Right - 420 - 20,
                    workingArea.Bottom - 580 - 20
                );
            }
            else
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Position = new PixelPoint(100, 100);
            }
        }

        private void LoadDataAndDisplay()
        {
            try
            {
                _data = AnalyticsData.LoadFromCSV(_currentFilter);
                BuildUI();
            }
            catch (Exception ex)
            {
                ShowError($"Failed to load data: {ex.Message}");
            }
        }

        private void BuildUI()
        {
            // Outer container with subtle shadow and rounded corners
            var mainContainer = new Border
            {
                Background = new SolidColorBrush(CardColor),
                CornerRadius = new CornerRadius(12),
                Margin = new Thickness(16), // Add margin for shadow room
                BorderBrush = new SolidColorBrush(BorderColor),
                BorderThickness = new Thickness(1),
                ClipToBounds = false,
                BoxShadow = new BoxShadows(
                    new BoxShadow
                    {
                        Blur = 16,
                        Spread = 0,
                        OffsetX = 0,
                        OffsetY = 2,
                        Color = Color.FromArgb(25, 0, 0, 0)
                    }
                ),
                Width = 388,
                Height = 548
            };

            // Main layout grid (outer) - will contain header and content area
            var outerGrid = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*"),
                Width = 388,
                Height = 548,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            // Header Section
            var header = CreateHeader();
            Grid.SetRow(header, 0);
            outerGrid.Children.Add(header);

            // Content area with scrolling - this MUST have constrained height
            // Use a border to constrain the scrollviewer area
            var contentArea = new Border
            {
                CornerRadius = new CornerRadius(0, 0, 12, 12),
                ClipToBounds = true,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            // Scrollable Content
            _contentPanel = new StackPanel
            {
                Spacing = 12,
                Margin = new Thickness(16, 12, 16, 16),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Top
            };

            _contentTransform = new TranslateTransform();
            _contentPanel.RenderTransform = _contentTransform;

            _contentViewport = new Border
            {
                Child = _contentPanel,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0))
            };

            _contentViewport.PointerWheelChanged += (s, e) =>
            {
                if (ApplyWheelScrollDelta(e.Delta.Y))
                {
                    e.Handled = true;
                }
            };

            contentArea.Child = _contentViewport;
            Grid.SetRow(contentArea, 1);
            outerGrid.Children.Add(contentArea);

            mainContainer.Child = outerGrid;
            Content = mainContainer;

            // Populate content
            RefreshContent();
        }

        private Border CreateHeader()
        {
            var headerStack = new StackPanel { Spacing = 0 };

            // Top bar with title and close button
            var topBar = new Border
            {
                Background = new SolidColorBrush(SurfaceColor),
                Padding = new Thickness(16, 14, 12, 14),
                CornerRadius = new CornerRadius(12, 12, 0, 0)
            };

            var topGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto")
            };

            var titleText = new TextBlock
            {
                Text = "Analytics",
                FontSize = 15,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(TextPrimary),
                VerticalAlignment = VerticalAlignment.Center
            };

            Grid.SetColumn(titleText, 0);

            // Close Button
            var closeButton = CreateCloseButton();
            Grid.SetColumn(closeButton, 1);

            topGrid.Children.Add(titleText);
            topGrid.Children.Add(closeButton);
            topBar.Child = topGrid;

            // Tabs bar
            var tabsBar = new Border
            {
                Background = new SolidColorBrush(SurfaceColor),
                BorderBrush = new SolidColorBrush(BorderColor),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(16, 0, 16, 0)
            };

            var tabsStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8
            };

            _todayTab = CreateTab("Today", true);
            _weekTab = CreateTab("This Week", false);

            tabsStack.Children.Add(_todayTab);
            tabsStack.Children.Add(_weekTab);
            tabsBar.Child = tabsStack;

            headerStack.Children.Add(topBar);
            headerStack.Children.Add(tabsBar);

            var headerBorder = new Border
            {
                Child = headerStack
            };

            return headerBorder;
        }

        private Border CreateTab(string label, bool isActive)
        {
            var border = new Border
            {
                Padding = new Thickness(12, 10, 12, 10),
                Cursor = new Cursor(StandardCursorType.Hand),
                BorderThickness = new Thickness(0, 0, 0, 2),
                BorderBrush = isActive ? new SolidColorBrush(PrimaryBlue) : Brushes.Transparent
            };

            var button = new Button
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = new Cursor(StandardCursorType.Hand),
                Padding = new Thickness(0),
                Content = new TextBlock
                {
                    Text = label,
                    FontSize = 11,
                    FontWeight = isActive ? FontWeight.SemiBold : FontWeight.Medium,
                    Foreground = new SolidColorBrush(isActive ? PrimaryBlue : TextSecondary)
                }
            };

            button.Click += (s, e) =>
            {
                var newFilter = label == "Today" ? TimeFilter.Today : TimeFilter.ThisWeek;
                if (newFilter != _currentFilter)
                {
                    _currentFilter = newFilter;
                    UpdateTabStyles();
                    // Reload data and refresh content only (don't rebuild entire UI)
                    _data = AnalyticsData.LoadFromCSV(_currentFilter);
                    RefreshContent();
                }
            };

            border.Child = button;

            // Hover effect for inactive tabs
            if (!isActive)
            {
                border.PointerEntered += (s, e) =>
                {
                    if (border.Child is Button btn && btn.Content is TextBlock tb)
                    {
                        tb.Foreground = new SolidColorBrush(TextPrimary);
                    }
                };
                border.PointerExited += (s, e) =>
                {
                    if (border.Child is Button btn && btn.Content is TextBlock tb)
                    {
                        tb.Foreground = new SolidColorBrush(TextSecondary);
                    }
                };
            }

            return border;
        }

        private void UpdateTabStyles()
        {
            if (_todayTab != null && _weekTab != null)
            {
                bool todayActive = _currentFilter == TimeFilter.Today;

                // Update Today tab
                _todayTab.BorderBrush = todayActive ? new SolidColorBrush(PrimaryBlue) : Brushes.Transparent;
                if (_todayTab.Child is Button todayBtn && todayBtn.Content is TextBlock todayTb)
                {
                    todayTb.FontWeight = todayActive ? FontWeight.SemiBold : FontWeight.Medium;
                    todayTb.Foreground = new SolidColorBrush(todayActive ? PrimaryBlue : TextSecondary);
                }

                // Update Week tab
                _weekTab.BorderBrush = !todayActive ? new SolidColorBrush(PrimaryBlue) : Brushes.Transparent;
                if (_weekTab.Child is Button weekBtn && weekBtn.Content is TextBlock weekTb)
                {
                    weekTb.FontWeight = !todayActive ? FontWeight.SemiBold : FontWeight.Medium;
                    weekTb.Foreground = new SolidColorBrush(!todayActive ? PrimaryBlue : TextSecondary);
                }
            }
        }


        private Border CreateCloseButton()
        {
            var border = new Border
            {
                Background = Brushes.Transparent,
                CornerRadius = new CornerRadius(6),
                Width = 32,
                Height = 32,
                Cursor = new Cursor(StandardCursorType.Hand)
            };

            var button = new Button
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = new Cursor(StandardCursorType.Hand),
                Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Width = 32,
                Height = 32
            };

            var closeIcon = new TextBlock
            {
                Text = "✕",
                FontSize = 16,
                FontWeight = FontWeight.Normal,
                Foreground = new SolidColorBrush(TextSecondary),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            button.Content = closeIcon;
            button.Click += (s, e) => Hide(); // Hide window instead of closing to prevent app shutdown
            border.Child = button;

            // Hover effects
            border.PointerEntered += (s, e) =>
            {
                border.Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));
                closeIcon.Foreground = new SolidColorBrush(TextPrimary);
            };
            border.PointerExited += (s, e) =>
            {
                border.Background = Brushes.Transparent;
                closeIcon.Foreground = new SolidColorBrush(TextSecondary);
            };

            return border;
        }


        private void RefreshContent()
        {
            if (_contentPanel == null || _data == null) return;

            _contentPanel.Children.Clear();

            Console.WriteLine($"[Analytics] Refreshing content - AppUsage: {_data.AppUsage.Count} apps, HourlyProductivity: {_data.HourlyProductivity.Count} hours");
            Console.WriteLine($"[Analytics] Total activity: {_data.TotalActivityMinutes}min, Productive: {_data.ProductiveMinutes}min");

            // Summary Section
            _contentPanel.Children.Add(CreateSummarySection());

            if (_activeDetailView != DetailViewType.None)
            {
                _contentPanel.Children.Add(CreateDetailSection());
                Dispatcher.UIThread.Post(ClampContentScrollOffset, DispatcherPriority.Background);
                return;
            }

            // App Usage Section
            if (_data.AppUsage.Any())
            {
                Console.WriteLine($"[Analytics] Adding 'Most Used Apps' section with {_data.AppUsage.Count} apps");
                _contentPanel.Children.Add(CreateSection("chart", "Most Used Apps", CreateAppUsageView()));
            }
            else
            {
                Console.WriteLine("[Analytics] Skipping 'Most Used Apps' section - no data");
            }

            // Hourly Productivity Section
            if (_data.HourlyProductivity.Any())
            {
                Console.WriteLine($"[Analytics] Adding 'Productivity by Hour' section with {_data.HourlyProductivity.Count} hours");
                _contentPanel.Children.Add(CreateSection("calendar", "Productivity by Hour", CreateHourlyProductivityView()));

                // Add visual timeline chart
                Console.WriteLine($"[Analytics] Adding 'Activity Timeline' chart");
                _contentPanel.Children.Add(CreateSection("chart", "Activity Timeline", CreateTimelineChart()));
            }
            else
            {
                Console.WriteLine("[Analytics] Skipping 'Productivity by Hour' section - no HARVEST.CSV data (respond to notifications to populate this)");
            }

            // Empty State
            if (!_data.AppUsage.Any() && !_data.HourlyProductivity.Any())
            {
                _contentPanel.Children.Add(CreateEmptyState());
            }

            Dispatcher.UIThread.Post(ClampContentScrollOffset, DispatcherPriority.Background);
        }

        internal bool HasScrollableOverflow()
        {
            return GetMaxContentScrollOffset() > 0;
        }

        internal double GetScrollOffsetY()
        {
            return _contentScrollOffset;
        }

        internal double GetScrollExtentHeight()
        {
            if (_contentPanel == null)
            {
                return 0;
            }

            return GetContentHeight();
        }

        internal double GetScrollViewportHeight()
        {
            return _contentViewport?.Bounds.Height ?? 0;
        }

        internal bool ApplyWheelScrollDelta(double deltaY)
        {
            if (_contentPanel == null || _contentTransform == null)
            {
                return false;
            }

            const double scrollStep = 96;
            double maxOffsetY = GetMaxContentScrollOffset();
            if (maxOffsetY <= 0)
            {
                return false;
            }

            double nextOffsetY = Math.Clamp(
                _contentScrollOffset - (deltaY * scrollStep),
                0,
                maxOffsetY
            );

            if (Math.Abs(nextOffsetY - _contentScrollOffset) < 0.1)
            {
                return false;
            }

            _contentScrollOffset = nextOffsetY;
            UpdateContentOffset();
            return true;
        }

        private double GetMaxContentScrollOffset()
        {
            if (_contentPanel == null || _contentViewport == null)
            {
                return 0;
            }

            double contentHeight = GetContentHeight();
            double viewportHeight = _contentViewport.Bounds.Height;
            return Math.Max(0, contentHeight - viewportHeight);
        }

        private double GetContentHeight()
        {
            if (_contentPanel == null)
            {
                return 0;
            }

            double childHeights = 0;
            for (int i = 0; i < _contentPanel.Children.Count; i++)
            {
                var child = _contentPanel.Children[i];
                childHeights += Math.Max(child.DesiredSize.Height, child.Bounds.Height);

                if (child is Control control)
                {
                    childHeights += control.Margin.Top + control.Margin.Bottom;
                }
            }

            if (_contentPanel.Children.Count > 1)
            {
                childHeights += _contentPanel.Spacing * (_contentPanel.Children.Count - 1);
            }

            double intrinsicHeight = Math.Max(
                Math.Max(_contentPanel.DesiredSize.Height, _contentPanel.Bounds.Height),
                childHeights
            );

            return intrinsicHeight + _contentPanel.Margin.Top + _contentPanel.Margin.Bottom;
        }

        private void ClampContentScrollOffset()
        {
            _contentScrollOffset = Math.Clamp(_contentScrollOffset, 0, GetMaxContentScrollOffset());
            UpdateContentOffset();
        }

        private void UpdateContentOffset()
        {
            if (_contentTransform != null)
            {
                _contentTransform.Y = -_contentScrollOffset;
            }
        }

        private Border CreateSummarySection()
        {
            var border = new Border
            {
                Background = new SolidColorBrush(SurfaceColor),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14),
                BorderBrush = new SolidColorBrush(BorderColor),
                BorderThickness = new Thickness(1)
            };

            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,*,*"),
                RowDefinitions = new RowDefinitions("Auto")
            };

            // Total Activity
            var activityPanel = CreateStatCard(
                "clock",
                FormatDuration(_data?.TotalActivityMinutes ?? 0),
                "Activity",
                DetailViewType.Activity
            );
            Grid.SetColumn(activityPanel, 0);

            // Productive Time
            var productivePanel = CreateStatCard(
                "star",
                (_data?.ProductivePercentage ?? 0).ToString("F0") + "%",
                "Productive",
                DetailViewType.Productivity
            );
            Grid.SetColumn(productivePanel, 1);

            // Apps Used
            var appsPanel = CreateStatCard(
                "apps",
                (_data?.AppUsage.Count ?? 0).ToString(),
                "Apps",
                DetailViewType.Apps
            );
            Grid.SetColumn(appsPanel, 2);

            grid.Children.Add(activityPanel);
            grid.Children.Add(productivePanel);
            grid.Children.Add(appsPanel);

            border.Child = grid;
            return border;
        }

        private Border CreateStatCard(string iconType, string value, string label, DetailViewType detailView)
        {
            var panel = new StackPanel
            {
                Spacing = 4,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            // Create icon container with SVG path
            var iconViewBox = new Viewbox
            {
                Width = 24,
                Height = 24,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 2)
            };

            var iconCanvas = new Canvas
            {
                Width = 24,
                Height = 24
            };

            var iconPath = new Avalonia.Controls.Shapes.Path
            {
                Fill = new SolidColorBrush(PrimaryBlue),
                Data = Geometry.Parse(GetIconPath(iconType))
            };

            iconCanvas.Children.Add(iconPath);
            iconViewBox.Child = iconCanvas;

            var valueText = new TextBlock
            {
                Text = value,
                FontSize = 19,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(TextPrimary),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var labelText = new TextBlock
            {
                Text = label,
                FontSize = 10,
                FontWeight = FontWeight.Normal,
                Foreground = new SolidColorBrush(TextSecondary),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            panel.Children.Add(iconViewBox);
            panel.Children.Add(valueText);
            panel.Children.Add(labelText);

            var border = new Border
            {
                Child = panel,
                Padding = new Thickness(8, 6, 8, 6),
                CornerRadius = new CornerRadius(8),
                Cursor = new Cursor(StandardCursorType.Hand)
            };

            border.PointerEntered += (s, e) =>
            {
                border.Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));
            };
            border.PointerExited += (s, e) =>
            {
                border.Background = Brushes.Transparent;
            };
            border.PointerPressed += (s, e) =>
            {
                _activeDetailView = detailView;
                _contentScrollOffset = 0;
                RefreshContent();
            };

            return border;
        }

        private string GetIconPath(string iconType)
        {
            // Material Design Icons SVG paths
            switch (iconType)
            {
                case "clock": // Clock/Time icon
                    return "M12,20A8,8 0 0,0 20,12A8,8 0 0,0 12,4A8,8 0 0,0 4,12A8,8 0 0,0 12,20M12,2A10,10 0 0,1 22,12A10,10 0 0,1 12,22C6.47,22 2,17.5 2,12A10,10 0 0,1 12,2M12.5,7V12.25L17,14.92L16.25,16.15L11,13V7H12.5Z";
                case "star": // Star/Achievement icon
                    return "M12,17.27L18.18,21L16.54,13.97L22,9.24L14.81,8.62L12,2L9.19,8.62L2,9.24L7.45,13.97L5.82,21L12,17.27Z";
                case "apps": // Apps/Application icon
                    return "M16,20H20V16H16M16,14H20V10H16M10,8H14V4H10M16,8H20V4H16M10,14H14V10H10M4,14H8V10H4M4,20H8V16H4M10,20H14V16H10M4,8H8V4H4V8Z";
                case "chart": // Chart/Stats icon
                    return "M22,21H2V3H4V19H6V17H10V19H12V16H16V19H18V17H22V21M18,14H22V16H18V14M12,6H16V14H12V6M6,10H10V15H6V10M4,8V11H2V8H4Z";
                case "calendar": // Calendar/Time icon
                    return "M19,19H5V8H19M16,1V3H8V1H6V3H5C3.89,3 3,3.89 3,5V19A2,2 0 0,0 5,21H19A2,2 0 0,0 21,19V5C21,3.89 20.1,3 19,3H18V1M17,12H12V17H17V12Z";
                default:
                    return "M12,2A10,10 0 0,1 22,12A10,10 0 0,1 12,22A10,10 0 0,1 2,12A10,10 0 0,1 12,2Z"; // Circle fallback
            }
        }

        private Border CreateSection(string iconType, string title, Control content)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(SurfaceColor),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14),
                BorderBrush = new SolidColorBrush(BorderColor),
                BorderThickness = new Thickness(1)
            };

            var stack = new StackPanel { Spacing = 10 };

            // Title with icon
            var titleStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(0, 0, 0, 4)
            };

            // Create SVG icon
            var iconViewBox = new Viewbox
            {
                Width = 16,
                Height = 16,
                VerticalAlignment = VerticalAlignment.Center
            };

            var iconCanvas = new Canvas
            {
                Width = 24,
                Height = 24
            };

            var iconPath = new Avalonia.Controls.Shapes.Path
            {
                Fill = new SolidColorBrush(TextSecondary),
                Data = Geometry.Parse(GetIconPath(iconType))
            };

            iconCanvas.Children.Add(iconPath);
            iconViewBox.Child = iconCanvas;

            var titleText = new TextBlock
            {
                Text = title,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(TextPrimary),
                VerticalAlignment = VerticalAlignment.Center
            };

            titleStack.Children.Add(iconViewBox);
            titleStack.Children.Add(titleText);

            stack.Children.Add(titleStack);
            stack.Children.Add(content);
            border.Child = stack;

            return border;
        }

        private Border CreateDetailSection()
        {
            string title;
            string iconType;
            Control content;

            switch (_activeDetailView)
            {
                case DetailViewType.Activity:
                    title = "Activity Details";
                    iconType = "clock";
                    content = CreateActivityDetailView();
                    break;
                case DetailViewType.Apps:
                    title = "App Usage Details";
                    iconType = "apps";
                    content = CreateAppsDetailView();
                    break;
                case DetailViewType.Productivity:
                    title = "Productivity Details";
                    iconType = "calendar";
                    content = CreateProductivityDetailView();
                    break;
                default:
                    title = "Details";
                    iconType = "chart";
                    content = new TextBlock { Text = "No detail selected." };
                    break;
            }

            var section = CreateSection(iconType, title, content);
            if (section.Child is StackPanel stack)
            {
                stack.Children.Insert(0, CreateDetailToolbar());
            }

            return section;
        }

        private Control CreateDetailToolbar()
        {
            var toolbar = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                Margin = new Thickness(0, 0, 0, 4)
            };

            var backButton = new Button
            {
                Content = new TextBlock
                {
                    Text = "← Back to overview",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(PrimaryBlue)
                },
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Left,
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            backButton.Click += (s, e) =>
            {
                _activeDetailView = DetailViewType.None;
                _contentScrollOffset = 0;
                RefreshContent();
            };

            toolbar.Children.Add(backButton);
            return toolbar;
        }

        private Control CreateActivityDetailView()
        {
            if (_data == null)
            {
                return new TextBlock { Text = "No activity data available." };
            }

            var rows = new List<(string, string)>
            {
                ("Filter", _currentFilter == TimeFilter.Today ? "Today" : "This Week"),
                ("Total Activity", FormatDuration(_data.TotalActivityMinutes)),
                ("Productive Time", FormatDuration(_data.ProductiveMinutes)),
                ("Unproductive Time", FormatDuration(_data.UnproductiveMinutes)),
                ("Productivity Rate", $"{_data.ProductivePercentage:F0}%"),
                ("Tracked Apps", _data.AppUsage.Count.ToString()),
                ("Tracked Hours", _data.HourlyProductivity.Count(h => h.Value.Total > 0).ToString())
            };

            return CreateTwoColumnTable("Metric", "Value", rows);
        }

        private Control CreateAppsDetailView()
        {
            if (_data == null || !_data.AppUsage.Any())
            {
                return new TextBlock
                {
                    Text = "No app usage data available.",
                    Foreground = new SolidColorBrush(TextSecondary)
                };
            }

            var rows = _data.AppUsage
                .OrderByDescending(a => a.Value)
                .Select(a =>
                {
                    double share = _data.TotalActivityMinutes > 0
                        ? (double)a.Value / _data.TotalActivityMinutes * 100
                        : 0;
                    return (a.Key, $"{FormatDuration(a.Value)} ({share:F0}%)");
                })
                .ToList();

            return CreateTwoColumnTable("App", "Time", rows);
        }

        private Control CreateProductivityDetailView()
        {
            if (_data == null || !_data.HourlyProductivity.Any(h => h.Value.Total > 0))
            {
                return new TextBlock
                {
                    Text = "No productivity data available.",
                    Foreground = new SolidColorBrush(TextSecondary)
                };
            }

            var table = new StackPanel { Spacing = 8 };
            table.Children.Add(CreateTableHeader("Hour", "Productive", "Unproductive", "Rate"));

            foreach (var entry in _data.HourlyProductivity
                         .Where(h => h.Value.Total > 0)
                         .OrderBy(h => h.Key))
            {
                table.Children.Add(CreateTableRow(
                    $"{entry.Key:D2}:00",
                    entry.Value.ProductiveCount.ToString(),
                    entry.Value.UnproductiveCount.ToString(),
                    $"{entry.Value.ProductivePercentage:F0}%"
                ));
            }

            return table;
        }

        private Control CreateTwoColumnTable(string firstHeader, string secondHeader, IEnumerable<(string First, string Second)> rows)
        {
            var table = new StackPanel { Spacing = 8 };
            table.Children.Add(CreateTableHeader(firstHeader, secondHeader));

            foreach (var row in rows)
            {
                table.Children.Add(CreateTableRow(row.First, row.Second));
            }

            return table;
        }

        private Grid CreateTableHeader(params string[] titles)
        {
            var grid = CreateTableGrid(titles.Length, true);

            for (int i = 0; i < titles.Length; i++)
            {
                var text = new TextBlock
                {
                    Text = titles[i],
                    FontSize = 10,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = new SolidColorBrush(TextSecondary)
                };
                Grid.SetColumn(text, i);
                grid.Children.Add(text);
            }

            return grid;
        }

        private Border CreateTableRow(params string[] values)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(BorderColor),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 8)
            };

            var grid = CreateTableGrid(values.Length, false);
            for (int i = 0; i < values.Length; i++)
            {
                var text = new TextBlock
                {
                    Text = values[i],
                    FontSize = 11,
                    FontWeight = i == values.Length - 1 ? FontWeight.SemiBold : FontWeight.Medium,
                    Foreground = new SolidColorBrush(TextPrimary),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    HorizontalAlignment = i == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right
                };
                Grid.SetColumn(text, i);
                grid.Children.Add(text);
            }

            border.Child = grid;
            return border;
        }

        private Grid CreateTableGrid(int columnCount, bool isHeader)
        {
            string definitions = string.Join(",", Enumerable.Repeat("*", columnCount));
            return new Grid
            {
                ColumnDefinitions = new ColumnDefinitions(definitions),
                Margin = isHeader ? new Thickness(2, 0, 2, 2) : default
            };
        }

        private StackPanel CreateAppUsageView()
        {
            var panel = new StackPanel { Spacing = 8 };

            if (_data == null) return panel;

            var topApps = _data.AppUsage.OrderByDescending(a => a.Value).Take(10);

            foreach (var app in topApps)
            {
                panel.Children.Add(CreateAppUsageBar(app.Key, app.Value, _data.TotalActivityMinutes));
            }

            return panel;
        }

        private Grid CreateAppUsageBar(string appName, int minutes, int totalMinutes)
        {
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                Margin = new Thickness(0, 0, 0, 0)
            };

            var leftStack = new StackPanel
            {
                Spacing = 5,
                VerticalAlignment = VerticalAlignment.Center
            };

            // App name
            var nameText = new TextBlock
            {
                Text = appName,
                FontSize = 11,
                FontWeight = FontWeight.Medium,
                Foreground = new SolidColorBrush(TextPrimary),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            // Progress bar
            double percentage = totalMinutes > 0 ? (double)minutes / totalMinutes * 100 : 0;
            var progressBorder = new Border
            {
                Height = 5,
                Background = new SolidColorBrush(ProgressBarBg),
                CornerRadius = new CornerRadius(2.5),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 3, 0, 0)
            };

            var progressFill = new Border
            {
                Width = Math.Max(percentage * 1.4, 0),
                MaxWidth = 140,
                Height = 5,
                Background = new SolidColorBrush(PrimaryBlue),
                CornerRadius = new CornerRadius(2.5),
                HorizontalAlignment = HorizontalAlignment.Left
            };

            progressBorder.Child = progressFill;

            leftStack.Children.Add(nameText);
            leftStack.Children.Add(progressBorder);

            Grid.SetColumn(leftStack, 0);

            // Time display
            var timeStack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0)
            };

            var timeText = new TextBlock
            {
                Text = FormatDuration(minutes),
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(TextPrimary),
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var percentText = new TextBlock
            {
                Text = percentage.ToString("F0") + "%",
                FontSize = 9,
                FontWeight = FontWeight.Normal,
                Foreground = new SolidColorBrush(TextSecondary),
                HorizontalAlignment = HorizontalAlignment.Right
            };

            timeStack.Children.Add(timeText);
            timeStack.Children.Add(percentText);

            Grid.SetColumn(timeStack, 1);

            grid.Children.Add(leftStack);
            grid.Children.Add(timeStack);

            return grid;
        }

        private StackPanel CreateHourlyProductivityView()
        {
            var panel = new StackPanel { Spacing = 8 };

            if (_data == null) return panel;

            // Only show hours with activity
            var activeHours = _data.HourlyProductivity
                .Where(h => h.Value.Total > 0)
                .OrderBy(h => h.Key);

            foreach (var hour in activeHours)
            {
                panel.Children.Add(CreateHourlyBar(hour.Key, hour.Value));
            }

            return panel;
        }

        private Canvas CreateTimelineChart()
        {
            if (_data == null || !_data.HourlyProductivity.Any())
                return new Canvas { Height = 200 };

            // Chart dimensions
            const int chartWidth = 360;
            const int chartHeight = 180;
            const int marginBottom = 30;
            const int marginLeft = 10;
            const int marginRight = 10;

            var canvas = new Canvas
            {
                Width = chartWidth,
                Height = chartHeight + marginBottom,
                Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255))
            };

            // Get all hours with activity, sorted
            var activeHours = _data.HourlyProductivity
                .Where(h => h.Value.Total > 0)
                .OrderBy(h => h.Key)
                .ToList();

            if (!activeHours.Any()) return canvas;

            // Find max value for scaling
            int maxValue = activeHours.Max(h => h.Value.Total);
            if (maxValue == 0) return canvas;

            // Calculate bar width and spacing
            int barCount = activeHours.Count;
            double availableWidth = chartWidth - marginLeft - marginRight;
            double barSpacing = 2;
            double barWidth = (availableWidth - (barSpacing * (barCount - 1))) / barCount;
            barWidth = Math.Max(barWidth, 4); // Minimum bar width

            // Draw bars
            double currentX = marginLeft;
            foreach (var hourData in activeHours)
            {
                int hour = hourData.Key;
                var stats = hourData.Value;

                // Calculate heights (inverted because canvas Y goes top-down)
                double productiveHeight = (double)stats.ProductiveCount / maxValue * chartHeight;
                double unproductiveHeight = (double)stats.UnproductiveCount / maxValue * chartHeight;
                double totalHeight = productiveHeight + unproductiveHeight;

                // Draw stacked bar (productive on bottom, unproductive on top)
                if (stats.ProductiveCount > 0)
                {
                    var productiveBar = new Border
                    {
                        Width = barWidth,
                        Height = productiveHeight,
                        Background = new SolidColorBrush(ProductiveGreen),
                        CornerRadius = new CornerRadius(0)
                    };
                    Canvas.SetLeft(productiveBar, currentX);
                    Canvas.SetTop(productiveBar, chartHeight - totalHeight);
                    canvas.Children.Add(productiveBar);
                }

                if (stats.UnproductiveCount > 0)
                {
                    var unproductiveBar = new Border
                    {
                        Width = barWidth,
                        Height = unproductiveHeight,
                        Background = new SolidColorBrush(UnproductiveRed),
                        CornerRadius = new CornerRadius(0)
                    };
                    Canvas.SetLeft(unproductiveBar, currentX);
                    Canvas.SetTop(unproductiveBar, chartHeight - totalHeight + productiveHeight);
                    canvas.Children.Add(unproductiveBar);
                }

                // Hour label below bar
                var hourLabel = new TextBlock
                {
                    Text = $"{hour:D2}",
                    FontSize = 8,
                    FontWeight = FontWeight.Normal,
                    Foreground = new SolidColorBrush(TextSecondary),
                    TextAlignment = TextAlignment.Center,
                    Width = barWidth
                };
                Canvas.SetLeft(hourLabel, currentX);
                Canvas.SetTop(hourLabel, chartHeight + 5);
                canvas.Children.Add(hourLabel);

                currentX += barWidth + barSpacing;
            }

            // Draw baseline
            var baseline = new Border
            {
                Width = chartWidth - marginLeft - marginRight,
                Height = 1,
                Background = new SolidColorBrush(BorderColor)
            };
            Canvas.SetLeft(baseline, marginLeft);
            Canvas.SetTop(baseline, chartHeight);
            canvas.Children.Add(baseline);

            // Add legend
            var legendStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 16
            };

            // Productive legend
            var productiveLegend = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6
            };
            var productiveBox = new Border
            {
                Width = 12,
                Height = 12,
                Background = new SolidColorBrush(ProductiveGreen),
                CornerRadius = new CornerRadius(2)
            };
            var productiveLabel = new TextBlock
            {
                Text = "Productive",
                FontSize = 9,
                Foreground = new SolidColorBrush(TextSecondary),
                VerticalAlignment = VerticalAlignment.Center
            };
            productiveLegend.Children.Add(productiveBox);
            productiveLegend.Children.Add(productiveLabel);

            // Unproductive legend
            var unproductiveLegend = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6
            };
            var unproductiveBox = new Border
            {
                Width = 12,
                Height = 12,
                Background = new SolidColorBrush(UnproductiveRed),
                CornerRadius = new CornerRadius(2)
            };
            var unproductiveLabel = new TextBlock
            {
                Text = "Unproductive",
                FontSize = 9,
                Foreground = new SolidColorBrush(TextSecondary),
                VerticalAlignment = VerticalAlignment.Center
            };
            unproductiveLegend.Children.Add(unproductiveBox);
            unproductiveLegend.Children.Add(unproductiveLabel);

            legendStack.Children.Add(productiveLegend);
            legendStack.Children.Add(unproductiveLegend);

            Canvas.SetLeft(legendStack, marginLeft);
            Canvas.SetTop(legendStack, chartHeight + 20);
            canvas.Children.Add(legendStack);

            return canvas;
        }

        private Grid CreateHourlyBar(int hour, ProductivityStats stats)
        {
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("40,*,55")
            };

            // Hour label
            var hourText = new TextBlock
            {
                Text = $"{hour:D2}:00",
                FontSize = 10,
                FontWeight = FontWeight.Medium,
                Foreground = new SolidColorBrush(TextSecondary),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(hourText, 0);

            // Productivity bar container
            var barContainer = new Border
            {
                Height = 20,
                Background = new SolidColorBrush(ProgressBarBg),
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(6, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
                ClipToBounds = true
            };

            var barGrid = new Grid();

            // Calculate percentages
            double productivePercentage = stats.Total > 0 ? (double)stats.ProductiveCount / stats.Total * 100 : 0;
            double unproductivePercentage = stats.Total > 0 ? (double)stats.UnproductiveCount / stats.Total * 100 : 0;

            // Productive portion (green)
            if (stats.ProductiveCount > 0)
            {
                var productiveBar = new Border
                {
                    Width = productivePercentage,
                    Height = 20,
                    Background = new SolidColorBrush(ProductiveGreen),
                    CornerRadius = new CornerRadius(3, 0, 0, 3),
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                barGrid.Children.Add(productiveBar);
            }

            // Unproductive portion (red)
            if (stats.UnproductiveCount > 0)
            {
                var unproductiveBar = new Border
                {
                    Width = unproductivePercentage,
                    Height = 20,
                    Background = new SolidColorBrush(UnproductiveRed),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(productivePercentage, 0, 0, 0)
                };

                if (stats.ProductiveCount == 0)
                {
                    unproductiveBar.CornerRadius = new CornerRadius(4, 0, 0, 4);
                }

                barGrid.Children.Add(unproductiveBar);
            }

            barContainer.Child = barGrid;
            Grid.SetColumn(barContainer, 1);

            // Stats text
            var statsStack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center
            };

            var statsText = new TextBlock
            {
                Text = $"{stats.ProductiveCount} / {stats.Total}",
                FontSize = 11,
                FontWeight = FontWeight.Medium,
                Foreground = new SolidColorBrush(TextPrimary),
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var percentageText = new TextBlock
            {
                Text = $"{productivePercentage:F0}%",
                FontSize = 9,
                FontWeight = FontWeight.Normal,
                Foreground = new SolidColorBrush(TextSecondary),
                HorizontalAlignment = HorizontalAlignment.Right
            };

            statsStack.Children.Add(statsText);
            statsStack.Children.Add(percentageText);

            Grid.SetColumn(statsStack, 2);

            grid.Children.Add(hourText);
            grid.Children.Add(barContainer);
            grid.Children.Add(statsStack);

            return grid;
        }

        private StackPanel CreateEmptyState()
        {
            var panel = new StackPanel
            {
                Spacing = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 80, 0, 0)
            };

            var iconText = new TextBlock
            {
                Text = "○",
                FontSize = 80,
                FontWeight = FontWeight.Light,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new SolidColorBrush(TextSecondary),
                Opacity = 0.5
            };

            var messageText = new TextBlock
            {
                Text = "No data available",
                FontSize = 16,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(TextPrimary),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var hintText = new TextBlock
            {
                Text = _currentFilter == TimeFilter.Today
                    ? "Keep using Nudge today to see your analytics"
                    : "No activity recorded this week yet",
                FontSize = 12,
                Foreground = new SolidColorBrush(TextTertiary),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            panel.Children.Add(iconText);
            panel.Children.Add(messageText);
            panel.Children.Add(hintText);

            return panel;
        }

        private void ShowError(string message)
        {
            var errorPanel = new StackPanel
            {
                Spacing = 16,
                Margin = new Thickness(40),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var iconText = new TextBlock
            {
                Text = "△",
                FontSize = 56,
                FontWeight = FontWeight.Light,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new SolidColorBrush(UnproductiveRed)
            };

            var errorText = new TextBlock
            {
                Text = message,
                FontSize = 14,
                FontWeight = FontWeight.Medium,
                Foreground = new SolidColorBrush(UnproductiveRed),
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                MaxWidth = 300
            };

            errorPanel.Children.Add(iconText);
            errorPanel.Children.Add(errorText);
            Content = errorPanel;
        }

        private string FormatDuration(int minutes)
        {
            if (minutes < 60)
                return $"{minutes}m";

            int hours = minutes / 60;
            int mins = minutes % 60;
            return mins > 0 ? $"{hours}h {mins}m" : $"{hours}h";
        }

        public static DateTime GetFilterStartDate(TimeFilter filter)
        {
            DateTime now = DateTime.Now;

            switch (filter)
            {
                case TimeFilter.Today:
                    return now.Date;

                case TimeFilter.ThisWeek:
                    int daysFromMonday = ((int)now.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
                    return now.Date.AddDays(-daysFromMonday);

                default:
                    return now.Date;
            }
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // Analytics Data Models and CSV Parsing
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    public class ProductivityStats
    {
        public int ProductiveCount { get; set; }
        public int UnproductiveCount { get; set; }
        public int Total => ProductiveCount + UnproductiveCount;
        public double ProductivePercentage => Total > 0 ? (double)ProductiveCount / Total * 100 : 0;
    }

    public class AnalyticsData
    {
        public Dictionary<string, int> AppUsage { get; set; } = new Dictionary<string, int>();
        public Dictionary<int, ProductivityStats> HourlyProductivity { get; set; } = new Dictionary<int, ProductivityStats>();
        public int TotalActivityMinutes { get; set; }
        public int ProductiveMinutes { get; set; }
        public int UnproductiveMinutes { get; set; }
        public double ProductivePercentage => TotalActivityMinutes > 0 ? (double)ProductiveMinutes / TotalActivityMinutes * 100 : 0;

        public static AnalyticsData LoadFromCSV(AnalyticsWindow.TimeFilter filter)
        {
            var data = new AnalyticsData();

            string homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string nudgeDir = Path.Combine(homeDir, ".nudge");
            string activityLogPath = Path.Combine(nudgeDir, "ACTIVITY_LOG.CSV");
            string harvestPath = Path.Combine(nudgeDir, "HARVEST.CSV");

            DateTime filterStartDate = AnalyticsWindow.GetFilterStartDate(filter);

            Console.WriteLine($"[Analytics] Loading data for {filter} (from {filterStartDate:yyyy-MM-dd HH:mm})");
            Console.WriteLine($"[Analytics] Activity log: {activityLogPath}");
            Console.WriteLine($"[Analytics] Harvest data: {harvestPath}");

            // Load activity log for app usage (minute-by-minute data)
            if (File.Exists(activityLogPath))
            {
                try
                {
                    var lines = File.ReadAllLines(activityLogPath);
                    Console.WriteLine($"[Analytics] Found {lines.Length} lines in ACTIVITY_LOG.CSV");
                    int processedLines = 0;
                    for (int i = 1; i < lines.Length; i++) // Skip header
                    {
                        var parts = lines[i].Split(',');
                        if (parts.Length >= 4)
                        {
                            // Format: timestamp,hour_of_day,day_of_week,app_name,foreground_app,idle_time
                            if (DateTime.TryParse(parts[0], out DateTime timestamp))
                            {
                                if (timestamp >= filterStartDate)
                                {
                                    string appName = parts[3];

                                    // Exclude Nudge itself from analytics
                                    if (appName.ToLower().Contains("nudge"))
                                        continue;

                                    if (!data.AppUsage.ContainsKey(appName))
                                        data.AppUsage[appName] = 0;

                                    data.AppUsage[appName] += 1; // 1 minute per entry
                                    data.TotalActivityMinutes += 1;
                                    processedLines++;
                                }
                            }
                        }
                    }
                    Console.WriteLine($"[Analytics] Processed {processedLines} activity log entries after filtering");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Analytics] Error loading activity log: {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine("[Analytics] ACTIVITY_LOG.CSV not found");
            }

            // Load harvest data for productivity stats
            if (File.Exists(harvestPath))
            {
                try
                {
                    var lines = File.ReadAllLines(harvestPath);
                    Console.WriteLine($"[Analytics] Found {lines.Length} lines in HARVEST.CSV");
                    int processedHarvest = 0;
                    for (int i = 1; i < lines.Length; i++) // Skip header
                    {
                        var parts = lines[i].Split(',');
                        if (parts.Length >= 8)
                        {
                            // Format: timestamp,hour_of_day,day_of_week,app_name,foreground_app,idle_time,time_last_request,productive
                            if (DateTime.TryParse(parts[0], out DateTime timestamp))
                            {
                                if (timestamp >= filterStartDate)
                                {
                                    string appName = parts[3];

                                    // Exclude Nudge itself from productivity stats
                                    if (appName.ToLower().Contains("nudge"))
                                        continue;

                                    if (int.TryParse(parts[1], out int hour))
                                    {
                                        if (!data.HourlyProductivity.ContainsKey(hour))
                                            data.HourlyProductivity[hour] = new ProductivityStats();

                                        bool productive = parts[7] == "1";

                                        if (productive)
                                        {
                                            data.HourlyProductivity[hour].ProductiveCount++;
                                            data.ProductiveMinutes += 1;
                                        }
                                        else
                                        {
                                            data.HourlyProductivity[hour].UnproductiveCount++;
                                            data.UnproductiveMinutes += 1;
                                        }
                                        processedHarvest++;
                                    }
                                }
                            }
                        }
                    }
                    Console.WriteLine($"[Analytics] Processed {processedHarvest} harvest entries after filtering");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Analytics] Error loading harvest data: {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine("[Analytics] HARVEST.CSV not found - respond to notifications to populate productivity data");
            }

            return data;
        }
    }
}
