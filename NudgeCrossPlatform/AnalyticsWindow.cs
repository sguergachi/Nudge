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
        private Border? _filterButton;
        private TextBlock? _filterButtonText;

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
            Width = 480;
            Height = 640;
            CanResize = true;
            MinWidth = 400;
            MinHeight = 500;
            ShowInTaskbar = true;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            SystemDecorations = SystemDecorations.Full;
            Title = "Nudge Analytics";
            Background = new SolidColorBrush(BackgroundColor);
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
            var mainContainer = new Border
            {
                Background = new SolidColorBrush(BackgroundColor),
                Padding = new Thickness(0)
            };

            var mainStack = new StackPanel
            {
                Spacing = 0
            };

            // Header Section
            mainStack.Children.Add(CreateHeader());

            // Scrollable Content
            _contentPanel = new StackPanel
            {
                Spacing = 16,
                Margin = new Thickness(20, 16, 20, 20)
            };

            _scrollViewer = new ScrollViewer
            {
                Content = _contentPanel,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            mainStack.Children.Add(_scrollViewer);
            mainContainer.Child = mainStack;
            Content = mainContainer;

            // Populate content
            RefreshContent();
        }

        private Border CreateHeader()
        {
            var headerBorder = new Border
            {
                Background = new SolidColorBrush(SurfaceColor),
                BorderBrush = new SolidColorBrush(BorderColor),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(20, 20, 20, 16),
                BoxShadow = new BoxShadows(
                    new BoxShadow
                    {
                        Blur = 8,
                        Spread = 0,
                        OffsetX = 0,
                        OffsetY = 2,
                        Color = Color.FromArgb(40, 0, 0, 0)
                    }
                )
            };

            var headerGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto")
            };

            var titleStack = new StackPanel
            {
                Spacing = 4,
                VerticalAlignment = VerticalAlignment.Center
            };

            var titleText = new TextBlock
            {
                Text = "Productivity Analytics",
                FontSize = 18,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(TextPrimary)
            };

            var subtitleText = new TextBlock
            {
                Text = GetFilterSubtitle(),
                FontSize = 12,
                FontWeight = FontWeight.Normal,
                Foreground = new SolidColorBrush(TextSecondary)
            };

            titleStack.Children.Add(titleText);
            titleStack.Children.Add(subtitleText);

            Grid.SetColumn(titleStack, 0);

            // Filter Toggle Button
            _filterButton = CreateFilterButton();
            Grid.SetColumn(_filterButton, 1);

            headerGrid.Children.Add(titleStack);
            headerGrid.Children.Add(_filterButton);
            headerBorder.Child = headerGrid;

            return headerBorder;
        }

        private string GetFilterSubtitle()
        {
            if (_currentFilter == TimeFilter.Today)
            {
                return DateTime.Now.ToString("MMMM d, yyyy");
            }
            else
            {
                var startOfWeek = GetFilterStartDate(TimeFilter.ThisWeek);
                var endOfWeek = startOfWeek.AddDays(6);
                return $"{startOfWeek:MMM d} - {endOfWeek:MMM d}";
            }
        }

        private Border CreateFilterButton()
        {
            var border = new Border
            {
                Background = new SolidColorBrush(PrimaryBlue),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(12, 8, 12, 8),
                Cursor = new Cursor(StandardCursorType.Hand)
            };

            var button = new Button
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = new Cursor(StandardCursorType.Hand),
                Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };

            var stack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6
            };

            var icon = new TextBlock
            {
                Text = _currentFilter == TimeFilter.Today ? "📅" : "📆",
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center
            };

            _filterButtonText = new TextBlock
            {
                Text = _currentFilter == TimeFilter.Today ? "Today" : "This Week",
                FontSize = 12,
                FontWeight = FontWeight.Medium,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            };

            stack.Children.Add(icon);
            stack.Children.Add(_filterButtonText);
            button.Content = stack;
            button.Click += OnFilterButtonClick;
            border.Child = button;

            // Hover effects
            border.PointerEntered += (s, e) => border.Background = new SolidColorBrush(PrimaryBlueHover);
            border.PointerExited += (s, e) => border.Background = new SolidColorBrush(PrimaryBlue);

            return border;
        }

        private void OnFilterButtonClick(object? sender, RoutedEventArgs e)
        {
            // Toggle filter
            _currentFilter = _currentFilter == TimeFilter.Today ? TimeFilter.ThisWeek : TimeFilter.Today;

            // Reload data and rebuild UI
            LoadDataAndDisplay();
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
                _contentPanel.Children.Add(CreateSection("Most Used Applications", CreateAppUsageView()));
            }

            // Hourly Productivity Section
            if (_data.HourlyProductivity.Any())
            {
                _contentPanel.Children.Add(CreateSection("Productivity by Hour", CreateHourlyProductivityView()));
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
                Padding = new Thickness(20),
                BorderBrush = new SolidColorBrush(BorderColor),
                BorderThickness = new Thickness(1),
                ClipToBounds = false,
                BoxShadow = new BoxShadows(
                    new BoxShadow
                    {
                        Blur = 16,
                        Spread = 0,
                        OffsetX = 0,
                        OffsetY = 4,
                        Color = Color.FromArgb(30, 0, 0, 0)
                    }
                )
            };

            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,*,*"),
                RowDefinitions = new RowDefinitions("Auto")
            };

            // Total Activity
            var activityPanel = CreateStatCard(
                "🕐",
                FormatDuration(_data?.TotalActivityMinutes ?? 0),
                "Total Activity"
            );
            Grid.SetColumn(activityPanel, 0);

            // Productive Time
            var productivePanel = CreateStatCard(
                "✨",
                (_data?.ProductivePercentage ?? 0).ToString("F0") + "%",
                "Productive"
            );
            Grid.SetColumn(productivePanel, 1);

            // Apps Used
            var appsPanel = CreateStatCard(
                "💻",
                (_data?.AppUsage.Count ?? 0).ToString(),
                "Apps Used"
            );
            Grid.SetColumn(appsPanel, 2);

            grid.Children.Add(activityPanel);
            grid.Children.Add(productivePanel);
            grid.Children.Add(appsPanel);

            border.Child = grid;
            return border;
        }

        private StackPanel CreateStatCard(string icon, string value, string label)
        {
            var panel = new StackPanel
            {
                Spacing = 6,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var iconText = new TextBlock
            {
                Text = icon,
                FontSize = 32,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4)
            };

            var valueText = new TextBlock
            {
                Text = value,
                FontSize = 24,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(TextPrimary),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var labelText = new TextBlock
            {
                Text = label,
                FontSize = 11,
                FontWeight = FontWeight.Normal,
                Foreground = new SolidColorBrush(TextSecondary),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            panel.Children.Add(iconText);
            panel.Children.Add(valueText);
            panel.Children.Add(labelText);

            return panel;
        }

        private Border CreateSection(string title, Control content)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(SurfaceColor),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(20),
                BorderBrush = new SolidColorBrush(BorderColor),
                BorderThickness = new Thickness(1),
                ClipToBounds = false,
                BoxShadow = new BoxShadows(
                    new BoxShadow
                    {
                        Blur = 16,
                        Spread = 0,
                        OffsetX = 0,
                        OffsetY = 4,
                        Color = Color.FromArgb(30, 0, 0, 0)
                    }
                )
            };

            var stack = new StackPanel { Spacing = 16 };

            var titleText = new TextBlock
            {
                Text = title,
                FontSize = 14,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(TextPrimary)
            };

            stack.Children.Add(titleText);
            stack.Children.Add(content);
            border.Child = stack;

            return border;
        }

        private StackPanel CreateAppUsageView()
        {
            var panel = new StackPanel { Spacing = 10 };

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
                Spacing = 8,
                VerticalAlignment = VerticalAlignment.Center
            };

            // App name
            var nameText = new TextBlock
            {
                Text = appName,
                FontSize = 12,
                FontWeight = FontWeight.Medium,
                Foreground = new SolidColorBrush(TextPrimary),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            // Progress bar
            double percentage = totalMinutes > 0 ? (double)minutes / totalMinutes * 100 : 0;
            var progressBorder = new Border
            {
                Height = 6,
                Background = new SolidColorBrush(ProgressBarBg),
                CornerRadius = new CornerRadius(3),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0)
            };

            var progressFill = new Border
            {
                Width = Math.Max(percentage * 1.5, 0), // Scale for visual impact
                MaxWidth = 150,
                Height = 6,
                Background = new SolidColorBrush(PrimaryBlue),
                CornerRadius = new CornerRadius(3),
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
                Margin = new Thickness(12, 0, 0, 0)
            };

            var timeText = new TextBlock
            {
                Text = FormatDuration(minutes),
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(TextPrimary),
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var percentText = new TextBlock
            {
                Text = percentage.ToString("F0") + "%",
                FontSize = 10,
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
                ColumnDefinitions = new ColumnDefinitions("45,*,60")
            };

            // Hour label
            var hourText = new TextBlock
            {
                Text = $"{hour:D2}:00",
                FontSize = 11,
                FontWeight = FontWeight.Medium,
                Foreground = new SolidColorBrush(TextSecondary),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(hourText, 0);

            // Productivity bar container
            var barContainer = new Border
            {
                Height = 24,
                Background = new SolidColorBrush(ProgressBarBg),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(8, 0, 8, 0),
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
                    Height = 24,
                    Background = new SolidColorBrush(ProductiveGreen),
                    CornerRadius = new CornerRadius(4, 0, 0, 4),
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
                    Height = 24,
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
                Text = "📭",
                FontSize = 64,
                HorizontalAlignment = HorizontalAlignment.Center,
                Opacity = 0.6
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
                Text = "⚠️",
                FontSize = 48,
                HorizontalAlignment = HorizontalAlignment.Center
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

        private static DateTime GetFilterStartDate(TimeFilter filter)
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
