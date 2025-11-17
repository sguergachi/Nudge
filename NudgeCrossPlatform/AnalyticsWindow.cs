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
        private TimeFilter _currentFilter = TimeFilter.Today;
        private AnalyticsData? _data;
        private ScrollViewer? _scrollViewer;
        private StackPanel? _contentPanel;
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

        public AnalyticsWindow()
        {
            InitializeWindow();
            LoadDataAndDisplay();
        }

        private void InitializeWindow()
        {
            Width = 420;
            Height = 580;
            CanResize = false;
            ShowInTaskbar = false;
            SystemDecorations = SystemDecorations.None;
            Title = "Nudge Analytics";
            Background = Brushes.Transparent;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };

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
                Height = 548, // Window height (580) minus margins (32)
                BoxShadow = new BoxShadows(
                    new BoxShadow
                    {
                        Blur = 16,
                        Spread = 0,
                        OffsetX = 0,
                        OffsetY = 2,
                        Color = Color.FromArgb(25, 0, 0, 0)
                    }
                )
            };

            // Use Grid instead of StackPanel to properly constrain ScrollViewer
            var mainGrid = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*") // Header is auto, content fills remaining
            };

            // Header Section with close button
            var header = CreateHeader();
            Grid.SetRow(header, 0);
            mainGrid.Children.Add(header);

            // Scrollable Content
            _contentPanel = new StackPanel
            {
                Spacing = 12,
                Margin = new Thickness(16, 12, 16, 16)
            };

            _scrollViewer = new ScrollViewer
            {
                Content = _contentPanel,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                // No MaxHeight - let it fill available space from Grid
            };

            Grid.SetRow(_scrollViewer, 1);
            mainGrid.Children.Add(_scrollViewer);

            mainContainer.Child = mainGrid;
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
            button.Click += (s, e) => Close();
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

            // Summary Section
            _contentPanel.Children.Add(CreateSummarySection());

            // App Usage Section
            if (_data.AppUsage.Any())
            {
                _contentPanel.Children.Add(CreateSection("chart", "Most Used Apps", CreateAppUsageView()));
            }

            // Hourly Productivity Section
            if (_data.HourlyProductivity.Any())
            {
                _contentPanel.Children.Add(CreateSection("calendar", "Productivity by Hour", CreateHourlyProductivityView()));
            }

            // Empty State
            if (!_data.AppUsage.Any() && !_data.HourlyProductivity.Any())
            {
                _contentPanel.Children.Add(CreateEmptyState());
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
                "Activity"
            );
            Grid.SetColumn(activityPanel, 0);

            // Productive Time
            var productivePanel = CreateStatCard(
                "star",
                (_data?.ProductivePercentage ?? 0).ToString("F0") + "%",
                "Productive"
            );
            Grid.SetColumn(productivePanel, 1);

            // Apps Used
            var appsPanel = CreateStatCard(
                "apps",
                (_data?.AppUsage.Count ?? 0).ToString(),
                "Apps"
            );
            Grid.SetColumn(appsPanel, 2);

            grid.Children.Add(activityPanel);
            grid.Children.Add(productivePanel);
            grid.Children.Add(appsPanel);

            border.Child = grid;
            return border;
        }

        private StackPanel CreateStatCard(string iconType, string value, string label)
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

            return panel;
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

            // Load activity log for app usage (minute-by-minute data)
            if (File.Exists(activityLogPath))
            {
                try
                {
                    var lines = File.ReadAllLines(activityLogPath);
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
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Analytics] Error loading activity log: {ex.Message}");
                }
            }

            // Load harvest data for productivity stats
            if (File.Exists(harvestPath))
            {
                try
                {
                    var lines = File.ReadAllLines(harvestPath);
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
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Analytics] Error loading harvest data: {ex.Message}");
                }
            }

            return data;
        }
    }
}
