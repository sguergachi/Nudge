// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// Analytics Window - Productivity Insights Dashboard
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
//
// Features:
// - Shows most used applications
// - Displays hourly productivity patterns
// - Filter by Today / This Week
// - Clean, modern Fluent Design System UI
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
        private TextBlock? _filterButtonText;

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
            Width = 450;
            Height = 600;
            CanResize = true;
            MinWidth = 400;
            MinHeight = 500;
            ShowInTaskbar = true;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            SystemDecorations = SystemDecorations.Full;
            Title = "Nudge Analytics";
            Background = new SolidColorBrush(Color.FromRgb(18, 18, 20));

            // Prevent multiple instances
            Closing += (s, e) => { };
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
                Background = new SolidColorBrush(Color.FromRgb(18, 18, 20)),
                Padding = new Thickness(0)
            };

            var mainStack = new StackPanel
            {
                Spacing = 0
            };

            // Header Section
            var headerBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(25, 25, 28)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(20, 16, 20, 16)
            };

            var headerStack = new StackPanel
            {
                Spacing = 12
            };

            var titleText = new TextBlock
            {
                Text = "📊 Productivity Analytics",
                FontSize = 20,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(240, 240, 245))
            };

            // Filter Toggle Button
            var filterButton = CreateFilterButton();

            headerStack.Children.Add(titleText);
            headerStack.Children.Add(filterButton);
            headerBorder.Child = headerStack;
            mainStack.Children.Add(headerBorder);

            // Scrollable Content
            _contentPanel = new StackPanel
            {
                Spacing = 16,
                Margin = new Thickness(20, 20, 20, 20)
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

        private Border CreateFilterButton()
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(88, 166, 255)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(16, 8, 16, 8),
                Cursor = new Cursor(StandardCursorType.Hand),
                HorizontalAlignment = HorizontalAlignment.Left
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

            _filterButtonText = new TextBlock
            {
                Text = GetFilterButtonText(),
                FontSize = 13,
                FontWeight = FontWeight.Medium,
                Foreground = Brushes.White
            };

            button.Content = _filterButtonText;
            button.Click += OnFilterButtonClick;
            border.Child = button;

            // Hover effects
            border.PointerEntered += (s, e) => border.Background = new SolidColorBrush(Color.FromRgb(108, 186, 255));
            border.PointerExited += (s, e) => border.Background = new SolidColorBrush(Color.FromRgb(88, 166, 255));

            return border;
        }

        private string GetFilterButtonText()
        {
            return _currentFilter == TimeFilter.Today ? "📅 Today" : "📅 This Week";
        }

        private void OnFilterButtonClick(object? sender, RoutedEventArgs e)
        {
            // Toggle filter
            _currentFilter = _currentFilter == TimeFilter.Today ? TimeFilter.ThisWeek : TimeFilter.Today;

            // Update button text
            if (_filterButtonText != null)
            {
                _filterButtonText.Text = GetFilterButtonText();
            }

            // Reload data
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
                Background = new SolidColorBrush(Color.FromRgb(25, 25, 28)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),
                BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                BorderThickness = new Thickness(1)
            };

            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,*,*")
            };

            // Total Activity
            var activityPanel = CreateStatPanel(
                "Total Activity",
                FormatDuration(_data?.TotalActivityMinutes ?? 0),
                "🕐"
            );
            Grid.SetColumn(activityPanel, 0);

            // Productive Time
            var productivePanel = CreateStatPanel(
                "Productive",
                _data?.ProductivePercentage.ToString("F0") + "%",
                "✅"
            );
            Grid.SetColumn(productivePanel, 1);

            // Apps Used
            var appsPanel = CreateStatPanel(
                "Apps Used",
                (_data?.AppUsage.Count ?? 0).ToString(),
                "💻"
            );
            Grid.SetColumn(appsPanel, 2);

            grid.Children.Add(activityPanel);
            grid.Children.Add(productivePanel);
            grid.Children.Add(appsPanel);

            border.Child = grid;
            return border;
        }

        private StackPanel CreateStatPanel(string label, string value, string icon)
        {
            var panel = new StackPanel
            {
                Spacing = 4,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var iconText = new TextBlock
            {
                Text = icon,
                FontSize = 24,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var valueText = new TextBlock
            {
                Text = value,
                FontSize = 20,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(240, 240, 245)),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var labelText = new TextBlock
            {
                Text = label,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 160)),
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
                Background = new SolidColorBrush(Color.FromRgb(25, 25, 28)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),
                BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                BorderThickness = new Thickness(1)
            };

            var stack = new StackPanel { Spacing = 12 };

            var titleText = new TextBlock
            {
                Text = title,
                FontSize = 15,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(240, 240, 245)),
                Margin = new Thickness(0, 0, 0, 4)
            };

            stack.Children.Add(titleText);
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

        private Border CreateAppUsageBar(string appName, int minutes, int totalMinutes)
        {
            var container = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(30, 30, 35)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 8, 10, 8)
            };

            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto")
            };

            var leftStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                VerticalAlignment = VerticalAlignment.Center
            };

            // App name
            var nameText = new TextBlock
            {
                Text = appName,
                FontSize = 12,
                FontWeight = FontWeight.Medium,
                Foreground = new SolidColorBrush(Color.FromRgb(220, 220, 230)),
                VerticalAlignment = VerticalAlignment.Center
            };

            // Progress bar
            double percentage = totalMinutes > 0 ? (double)minutes / totalMinutes * 100 : 0;
            var progressBorder = new Border
            {
                Width = 100,
                Height = 6,
                Background = new SolidColorBrush(Color.FromRgb(45, 45, 50)),
                CornerRadius = new CornerRadius(3),
                VerticalAlignment = VerticalAlignment.Center
            };

            var progressFill = new Border
            {
                Width = percentage,
                Height = 6,
                Background = new SolidColorBrush(Color.FromRgb(88, 166, 255)),
                CornerRadius = new CornerRadius(3),
                HorizontalAlignment = HorizontalAlignment.Left
            };

            progressBorder.Child = progressFill;

            leftStack.Children.Add(nameText);
            leftStack.Children.Add(progressBorder);

            Grid.SetColumn(leftStack, 0);

            // Time display
            var timeText = new TextBlock
            {
                Text = FormatDuration(minutes),
                FontSize = 12,
                FontWeight = FontWeight.Medium,
                Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 160)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(timeText, 1);

            grid.Children.Add(leftStack);
            grid.Children.Add(timeText);
            container.Child = grid;

            return container;
        }

        private StackPanel CreateHourlyProductivityView()
        {
            var panel = new StackPanel { Spacing = 6 };

            if (_data == null) return panel;

            foreach (var hour in _data.HourlyProductivity.OrderBy(h => h.Key))
            {
                panel.Children.Add(CreateHourlyBar(hour.Key, hour.Value));
            }

            return panel;
        }

        private Border CreateHourlyBar(int hour, ProductivityStats stats)
        {
            var container = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(30, 30, 35)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 6, 10, 6)
            };

            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("50,*,Auto")
            };

            // Hour label
            var hourText = new TextBlock
            {
                Text = $"{hour:D2}:00",
                FontSize = 11,
                FontWeight = FontWeight.Medium,
                Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 190)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(hourText, 0);

            // Productivity bar
            var barContainer = new Border
            {
                Height = 20,
                Background = new SolidColorBrush(Color.FromRgb(45, 45, 50)),
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(8, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            var barGrid = new Grid();

            // Productive portion (green)
            if (stats.ProductiveCount > 0)
            {
                double productivePercentage = stats.Total > 0 ? (double)stats.ProductiveCount / stats.Total * 100 : 0;
                var productiveBar = new Border
                {
                    Width = double.IsNaN(productivePercentage) ? 0 : productivePercentage,
                    Height = 20,
                    Background = new SolidColorBrush(Color.FromRgb(76, 175, 80)),
                    CornerRadius = new CornerRadius(3, 0, 0, 3),
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                barGrid.Children.Add(productiveBar);
            }

            // Unproductive portion (red) - starts after productive
            if (stats.UnproductiveCount > 0)
            {
                double productivePercentage = stats.Total > 0 ? (double)stats.ProductiveCount / stats.Total * 100 : 0;
                double unproductivePercentage = stats.Total > 0 ? (double)stats.UnproductiveCount / stats.Total * 100 : 0;

                var unproductiveBar = new Border
                {
                    Width = double.IsNaN(unproductivePercentage) ? 0 : unproductivePercentage,
                    Height = 20,
                    Background = new SolidColorBrush(Color.FromRgb(244, 67, 54)),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(productivePercentage, 0, 0, 0)
                };

                if (stats.ProductiveCount == 0)
                {
                    unproductiveBar.CornerRadius = new CornerRadius(3, 0, 0, 3);
                }

                barGrid.Children.Add(unproductiveBar);
            }

            barContainer.Child = barGrid;
            Grid.SetColumn(barContainer, 1);

            // Stats text
            var statsText = new TextBlock
            {
                Text = $"{stats.ProductiveCount}✓ {stats.UnproductiveCount}✗",
                FontSize = 11,
                FontWeight = FontWeight.Medium,
                Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 160)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(statsText, 2);

            grid.Children.Add(hourText);
            grid.Children.Add(barContainer);
            grid.Children.Add(statsText);
            container.Child = grid;

            return container;
        }

        private StackPanel CreateEmptyState()
        {
            var panel = new StackPanel
            {
                Spacing = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 60, 0, 0)
            };

            var iconText = new TextBlock
            {
                Text = "📭",
                FontSize = 48,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var messageText = new TextBlock
            {
                Text = "No data available for this period",
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 160)),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var hintText = new TextBlock
            {
                Text = "Keep using Nudge to build your analytics",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 130)),
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
                Spacing = 12,
                Margin = new Thickness(20),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var errorText = new TextBlock
            {
                Text = "❌ " + message,
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(244, 67, 54)),
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center
            };

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

            DateTime filterStartDate = GetFilterStartDate(filter);

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
                                            data.ProductiveMinutes += 1; // Approximate
                                        }
                                        else
                                        {
                                            data.HourlyProductivity[hour].UnproductiveCount++;
                                            data.UnproductiveMinutes += 1; // Approximate
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

        private static DateTime GetFilterStartDate(AnalyticsWindow.TimeFilter filter)
        {
            DateTime now = DateTime.Now;

            switch (filter)
            {
                case AnalyticsWindow.TimeFilter.Today:
                    return now.Date; // Start of today

                case AnalyticsWindow.TimeFilter.ThisWeek:
                    // Start of week (Monday)
                    int daysFromMonday = ((int)now.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
                    return now.Date.AddDays(-daysFromMonday);

                default:
                    return now.Date;
            }
        }
    }
}
