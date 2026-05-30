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

using NudgeCore;

namespace NudgeTray
{
    sealed partial class AnalyticsWindow
    {
        // ─── Pause / Active toggle ───────────────────────────────────────────────

        private Border CreatePauseToggle()
        {
            _pauseToggleBadge = new Border
            {
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(7, 3, 7, 3),
                VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(Color.FromArgb(25, 76, 175, 80)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(55, 76, 175, 80)),
                BorderThickness = new Thickness(1),
                Cursor = new Cursor(StandardCursorType.Hand)
            };

            _pauseToggleText = new TextBlock
            {
                Text = StrActive,
                FontSize = 10,
                FontWeight = FontWeight.Medium,
                Foreground = new SolidColorBrush(AIStatusActive),
                VerticalAlignment = VerticalAlignment.Center
            };

            _pauseToggleBadge.Child = _pauseToggleText;

            ToolTip.SetTip(_pauseToggleBadge, StrNotificationsActive);

            _pauseToggleBadge.PointerEntered += (s, e) => ApplyHover(true);
            _pauseToggleBadge.PointerExited  += (s, e) => ApplyHover(false);

            _pauseToggleBadge.PointerPressed += (s, e) =>
            {
                Program.TogglePauseNotifications();
                UpdatePauseToggle();
            };

            return _pauseToggleBadge;
        }

        private void ApplyHover(bool hovering)
        {
            if (_pauseToggleBadge == null || _pauseToggleText == null) return;

            bool paused = Program._notificationsPaused;
            if (hovering)
            {
                if (paused)
                {
                    _pauseToggleText.Text = "\u25B6 Resume";
                    _pauseToggleText.Foreground = new SolidColorBrush(AIStatusActive);
                    _pauseToggleBadge.Background = new SolidColorBrush(Color.FromArgb(45, 76, 175, 80));
                    _pauseToggleBadge.BorderBrush = new SolidColorBrush(Color.FromArgb(80, 76, 175, 80));
                }
                else
                {
                    _pauseToggleText.Text = "\u23F8 Pause";
                    _pauseToggleText.Foreground = new SolidColorBrush(AIStatusLearning);
                    _pauseToggleBadge.Background = new SolidColorBrush(Color.FromArgb(45, 255, 193, 7));
                    _pauseToggleBadge.BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 193, 7));
                }
            }
            else
            {
                UpdatePauseToggle();
            }
        }

        private void UpdatePauseToggle()
        {
            if (_pauseToggleBadge == null || _pauseToggleText == null) return;

            bool paused = Program._notificationsPaused;
            if (paused)
            {
                _pauseToggleText.Text = "\u23F8 Paused";
                _pauseToggleText.Foreground = new SolidColorBrush(AIStatusInactive);
                _pauseToggleBadge.Background = new SolidColorBrush(Color.FromArgb(20, 150, 150, 160));
                _pauseToggleBadge.BorderBrush = new SolidColorBrush(Color.FromArgb(40, 150, 150, 160));
                ToolTip.SetTip(_pauseToggleBadge, "Notifications are paused. Snapshots are marked as skipped.");
            }
            else
            {
                _pauseToggleText.Text = StrActive;
                _pauseToggleText.Foreground = new SolidColorBrush(AIStatusActive);
                _pauseToggleBadge.Background = new SolidColorBrush(Color.FromArgb(25, 76, 175, 80));
                _pauseToggleBadge.BorderBrush = new SolidColorBrush(Color.FromArgb(55, 76, 175, 80));
                ToolTip.SetTip(_pauseToggleBadge, StrNotificationsActive);
            }
        }


        private Border CreatePinButton()
        {
            var border = new Border
            {
                Background = Brushes.Transparent,
                CornerRadius = new CornerRadius(6),
                Width = 32,
                Height = 32,
                Cursor = new Cursor(StandardCursorType.Hand)
            };

            _pinIcon = new TextBlock
            {
                Text = StrPinIcon,
                FontSize = 16,
                Foreground = new SolidColorBrush(TextSecondary),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };

            var button = new Button
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Width = 32,
                Height = 32,
                Content = _pinIcon
            };

            button.Click += (s, e) =>
            {
                _isPinned = !_isPinned;
                Topmost = _isPinned;
                if (_pinIcon != null)
                {
                    _pinIcon.Text = _isPinned ? "◉" : StrPinIcon;
                    _pinIcon.Foreground = _isPinned
                        ? new SolidColorBrush(PrimaryBlue)
                        : new SolidColorBrush(TextSecondary);
                }
            };

            border.Child = button;
            ToolTip.SetTip(border, "Pin window on top");

            border.PointerEntered += (s, e) =>
                border.Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));
            border.PointerExited += (s, e) =>
                border.Background = Brushes.Transparent;

            return border;
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

            var closeIcon = new TextBlock
            {
                Text = "✕",
                FontSize = 16,
                FontWeight = FontWeight.Normal,
                Foreground = new SolidColorBrush(TextSecondary),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };

            var button = new Button
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Width = 32,
                Height = 32,
                Content = closeIcon
            };

            button.Click += (s, e) => Hide();
            border.Child = button;
            ToolTip.SetTip(border, "Close");

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
            if (_contentPanel == null) return;

            // ── AI Brain live tab ─────────────────────────────────────────────────
            if (_aiTabActive)
            {
                // Stop all live timers from the previous content build before replacing
                StopLiveTimers();
                _contentPanel.Children.Clear();
                _contentPanel.Children.Add(CreateAILiveView());
                Dispatcher.UIThread.Post(ClampContentScrollOffset, DispatcherPriority.Background);
                return;
            }

            if (_data == null) return;
            // Stop AI live timers if switching away from the AI tab
            StopLiveTimers();
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
            if (_data.AppUsage.Count > 0)
            {
                Console.WriteLine($"[Analytics] Adding 'Most Used Apps' section with {_data.AppUsage.Count} apps");
                _contentPanel.Children.Add(CreateSection("Most Used Apps", CreateAppUsageView()));
            }
            else
            {
                Console.WriteLine("[Analytics] Skipping 'Most Used Apps' section - no data");
            }

            // Hourly Productivity Section — the bar chart already encodes all the same
            // information as the old "Activity Timeline" canvas chart, with precise labels and
            // counts, so we only render one section here.
            if (_data.HourlyProductivity.Count > 0)
            {
                Console.WriteLine($"[Analytics] Adding 'Productivity by Hour' section with {_data.HourlyProductivity.Count} hours");
                _contentPanel.Children.Add(CreateSection("Productivity by Hour", CreateHourlyProductivityView()));
            }
            else
            {
                Console.WriteLine("[Analytics] Skipping 'Productivity by Hour' section - no HARVEST.CSV data (respond to notifications to populate this)");
            }

            // Empty State
            if (_data.AppUsage.Count == 0 && _data.HourlyProductivity.Count == 0)
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

        private Grid CreateSummarySection()
        {
            // No outer card wrapper — the three stat tiles are standalone,
            // their own borders provide the structure. A wrapper-within-wrapper
            // creates clashing nested borders with only 14px between them.
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,8,*,8,*"),
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
            double productiveRate = _data?.ProductivePercentage ?? 0;
            bool hasProductiveData = (_data?.TotalActivityMinutes ?? 0) > 0;
            Color productiveValueColor = !hasProductiveData
                ? TextSecondary
                : productiveRate >= 60 ? ProductiveGreen
                : productiveRate >= 30 ? AIStatusLearning
                : UnproductiveRed;

            // Show "<1%" when there is activity but the rate rounds to zero
            bool hasProductiveMinutes = (_data?.ProductiveMinutes ?? 0) > 0;
            string productiveLabel = hasProductiveMinutes && productiveRate < 0.5
                ? "<1%"
                : productiveRate.ToString("F0", CultureInfo.InvariantCulture) + "%";

            var productivePanel = CreateStatCard(
                "trend",
                productiveLabel,
                "Productive",
                DetailViewType.Productivity,
                productiveValueColor,
                null  // icon always stays blue
            );
            Grid.SetColumn(productivePanel, 2);

            // Apps Used
            var appsPanel = CreateStatCard(
                "apps",
                (_data?.AppUsage.Count ?? 0).ToString(CultureInfo.InvariantCulture),
                "Apps",
                DetailViewType.Apps
            );
            Grid.SetColumn(appsPanel, 4);

            grid.Children.Add(activityPanel);
            grid.Children.Add(productivePanel);
            grid.Children.Add(appsPanel);

            return grid;
        }

        private Border CreateStatCard(string iconType, string value, string label, DetailViewType detailView, Color? valueColor = null, Color? iconColor = null)
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
                Fill = new SolidColorBrush(iconColor ?? PrimaryBlue),
                Data = Geometry.Parse(GetIconPath(iconType))
            };

            iconCanvas.Children.Add(iconPath);
            iconViewBox.Child = iconCanvas;

            var valueText = new TextBlock
            {
                Text = value,
                FontSize = 19,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(valueColor ?? TextPrimary),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            // Label + "›" inline — explicit drill-in affordance
            var labelRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 3,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var labelText = new TextBlock
            {
                Text = label,
                FontSize = 10,
                FontWeight = FontWeight.Normal,
                Foreground = new SolidColorBrush(TextSecondary),
                VerticalAlignment = VerticalAlignment.Center
            };

            var chevronText = new TextBlock
            {
                Text = "›",
                FontSize = 11,
                FontWeight = FontWeight.Light,
                Foreground = new SolidColorBrush(TextTertiary),
                VerticalAlignment = VerticalAlignment.Center
            };

            labelRow.Children.Add(labelText);
            labelRow.Children.Add(chevronText);

            panel.Children.Add(iconViewBox);
            panel.Children.Add(valueText);
            panel.Children.Add(labelRow);

            // Resting state has a visible background + border so the card reads as a button
            var border = new Border
            {
                Child = panel,
                Padding = new Thickness(8, 6, 8, 6),
                CornerRadius = new CornerRadius(8),
                Cursor = new Cursor(StandardCursorType.Hand),
                Background = new SolidColorBrush(Color.FromArgb(14, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(22, 255, 255, 255)),
                BorderThickness = new Thickness(1)
            };

            border.PointerEntered += (s, e) =>
            {
                border.Background = new SolidColorBrush(Color.FromArgb(35, 255, 255, 255));
                border.BorderBrush = new SolidColorBrush(Color.FromArgb(55, 255, 255, 255));
            };
            border.PointerExited += (s, e) =>
            {
                border.Background = new SolidColorBrush(Color.FromArgb(14, 255, 255, 255));
                border.BorderBrush = new SolidColorBrush(Color.FromArgb(22, 255, 255, 255));
            };
            border.PointerPressed += (s, e) =>
            {
                _activeDetailView = detailView;
                _contentScrollOffset = 0;
                RefreshContent();
            };

            return border;
        }

        private static string GetIconPath(string iconType)
        {
            // Material Design Icons SVG paths
            switch (iconType)
            {
                case "clock": // Clock/Time icon
                    return "M12,20A8,8 0 0,0 20,12A8,8 0 0,0 12,4A8,8 0 0,0 4,12A8,8 0 0,0 12,20M12,2A10,10 0 0,1 22,12A10,10 0 0,1 12,22C6.47,22 2,17.5 2,12A10,10 0 0,1 12,2M12.5,7V12.25L17,14.92L16.25,16.15L11,13V7H12.5Z";
                case "star": // Star/Achievement icon
                    return "M12,17.27L18.18,21L16.54,13.97L22,9.24L14.81,8.62L12,2L9.19,8.62L2,9.24L7.45,13.97L5.82,21L12,17.27Z";
                case "trend": // Trending up arrow — used for productive %
                    return "M16,6L18.29,8.29L13.41,13.17L9.41,9.17L2,16.59L3.41,18L9.41,12L13.41,16L19.71,9.71L22,12V6H16Z";
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

        private static Border CreateSection(string title, Control content)
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

            var titleText = new TextBlock
            {
                Text = title,
                FontSize = 11,
                FontWeight = FontWeight.Medium,
                Foreground = new SolidColorBrush(TextSecondary),
                Margin = new Thickness(0, 0, 0, 2)
            };

            stack.Children.Add(titleText);
            stack.Children.Add(content);
            border.Child = stack;

            return border;
        }

        private Border CreateDetailSection()
        {
            Control content;
            string title;

            switch (_activeDetailView)
            {
                case DetailViewType.Activity:
                    title = "Activity Details";
                    content = CreateActivityDetailView();
                    break;
                case DetailViewType.Apps:
                    title = "App Usage";
                    content = CreateAppsDetailView();
                    break;
                case DetailViewType.Productivity:
                    title = "Productivity Details";
                    content = CreateProductivityDetailView();
                    break;
                default:
                    title = StrDetails;
                    content = new TextBlock { Text = "No detail selected." };
                    break;
            }

            // Single card: nav header IS the title — no separate back button floating above
            var card = new Border
            {
                Background = new SolidColorBrush(SurfaceColor),
                CornerRadius = new CornerRadius(8),
                BorderBrush = new SolidColorBrush(BorderColor),
                BorderThickness = new Thickness(1),
                ClipToBounds = true
            };

            var outerStack = new StackPanel { Spacing = 0 };

            // Full-width clickable nav header: [‹] Title
            var navHeader = new Border
            {
                Padding = new Thickness(14, 11, 14, 11),
                Cursor = new Cursor(StandardCursorType.Hand),
                BorderBrush = new SolidColorBrush(BorderColor),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };

            var navRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                VerticalAlignment = VerticalAlignment.Center
            };

            // Crisp SVG chevron — left-pointing
            var chevronViewBox = new Viewbox
            {
                Width = 14,
                Height = 14,
                VerticalAlignment = VerticalAlignment.Center
            };
            var chevronCanvas = new Canvas { Width = 24, Height = 24 };
            chevronCanvas.Children.Add(new Avalonia.Controls.Shapes.Path
            {
                Stroke = new SolidColorBrush(TextSecondary),
                StrokeThickness = 2.5,
                StrokeLineCap = PenLineCap.Round,
                Data = Geometry.Parse("M15,5 L8,12 L15,19")
            });
            chevronViewBox.Child = chevronCanvas;

            var titleText = new TextBlock
            {
                Text = title,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(TextPrimary),
                VerticalAlignment = VerticalAlignment.Center
            };

            navRow.Children.Add(chevronViewBox);
            navRow.Children.Add(titleText);
            navHeader.Child = navRow;

            // Click anywhere on the header to go back
            navHeader.PointerPressed += (s, e) =>
            {
                _activeDetailView = DetailViewType.None;
                _contentScrollOffset = 0;
                RefreshContent();
            };
            navHeader.PointerEntered += (s, e) =>
                navHeader.Background = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255));
            navHeader.PointerExited += (s, e) =>
                navHeader.Background = Brushes.Transparent;

            // Content with consistent padding
            var contentBorder = new Border { Padding = new Thickness(14) };
            contentBorder.Child = content;

            outerStack.Children.Add(navHeader);
            outerStack.Children.Add(contentBorder);
            card.Child = outerStack;

            return card;
        }

        private Control CreateActivityDetailView()
        {
            if (_data == null)
                return new TextBlock { Text = "No activity data available.", Foreground = new SolidColorBrush(TextSecondary) };

            double rate = _data.ProductivePercentage;
            bool hasData = _data.TotalActivityMinutes > 0;

            Color rateColor = !hasData ? TextSecondary
                : rate >= 60 ? ProductiveGreen
                : rate >= 30 ? AIStatusLearning
                : UnproductiveRed;

            var stack = new StackPanel { Spacing = 6 };

            stack.Children.Add(CreateSemanticRow("Total Activity",    FormatDuration(_data.TotalActivityMinutes)));
            stack.Children.Add(CreateSemanticRow("Productive Time",   FormatDuration(_data.ProductiveMinutes),   ProductiveGreen));
            stack.Children.Add(CreateSemanticRow("Unproductive Time", FormatDuration(_data.UnproductiveMinutes), UnproductiveRed));
            stack.Children.Add(CreateSemanticRow("Productivity Rate", $"{rate:F0}%",                              rateColor));
            stack.Children.Add(CreateSemanticRow("Tracked Apps",      _data.AppUsage.Count.ToString(CultureInfo.InvariantCulture)));
            stack.Children.Add(CreateSemanticRow("Tracked Hours",     _data.HourlyProductivity.Count(h => h.Value.Total > 0).ToString(CultureInfo.InvariantCulture)));

            return stack;
        }

        private static Border CreateSemanticRow(string label, string value, Color? valueColor = null)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(15, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(BorderColor),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 9)
            };

            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto")
            };

            var labelText = new TextBlock
            {
                Text = label,
                FontSize = 11,
                FontWeight = FontWeight.Normal,
                Foreground = new SolidColorBrush(TextSecondary),
                VerticalAlignment = VerticalAlignment.Center
            };

            var valueText = new TextBlock
            {
                Text = value,
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(valueColor ?? TextPrimary),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            Grid.SetColumn(labelText, 0);
            Grid.SetColumn(valueText, 1);
            grid.Children.Add(labelText);
            grid.Children.Add(valueText);
            border.Child = grid;
            return border;
        }

        private Control CreateAppsDetailView()
        {
            if (_data == null || _data.AppUsage.Count == 0)
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
                    entry.Value.ProductiveCount.ToString(CultureInfo.InvariantCulture),
                    entry.Value.UnproductiveCount.ToString(CultureInfo.InvariantCulture),
                    $"{entry.Value.ProductivePercentage:F0}%"
                ));
            }

            return table;
        }

        private static StackPanel CreateTwoColumnTable(string firstHeader, string secondHeader, IEnumerable<(string First, string Second)> rows)
        {
            var table = new StackPanel { Spacing = 8 };
            table.Children.Add(CreateTableHeader(firstHeader, secondHeader));

            foreach (var row in rows)
            {
                table.Children.Add(CreateTableRow(row.First, row.Second));
            }

            return table;
        }

        private static Grid CreateTableHeader(params string[] titles)
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

        private static Border CreateTableRow(params string[] values)
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
                    Foreground = new SolidColorBrush(i == 0 ? TextSecondary : TextPrimary),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    HorizontalAlignment = i == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right
                };
                Grid.SetColumn(text, i);
                grid.Children.Add(text);
            }

            border.Child = grid;
            return border;
        }

        private static Grid CreateTableGrid(int columnCount, bool isHeader)
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

        private static Grid CreateAppUsageBar(string appName, int minutes, int totalMinutes)
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

            // Split "appId [subtitle]" into primary + optional subtitle
            var (primaryName, subtitleName) = ParseAppDisplayName(appName);

            var nameText = new TextBlock
            {
                Text = primaryName,
                FontSize = 11,
                FontWeight = FontWeight.Medium,
                Foreground = new SolidColorBrush(TextPrimary),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            // Progress bar — proportional fill using Grid star columns
            double percentage = totalMinutes > 0 ? (double)minutes / totalMinutes * 100 : 0;
            var progressBorder = new Border
            {
                Height = 5,
                Background = new SolidColorBrush(ProgressBarBg),
                CornerRadius = new CornerRadius(2.5),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 3, 0, 0),
                ClipToBounds = true
            };

            // Use a two-column grid (fill* + remainder*) so the fill is always proportional
            // to the container width, regardless of window or column size.
            var progressGrid = new Grid();
            if (percentage >= 99.5)
            {
                progressGrid.ColumnDefinitions = new ColumnDefinitions("*");
                progressGrid.Children.Add(new Border
                {
                    Background = new SolidColorBrush(PrimaryBlue),
                    CornerRadius = new CornerRadius(2.5),
                    HorizontalAlignment = HorizontalAlignment.Stretch
                });
            }
            else if (percentage > 0.5)
            {
                double remainder = 100.0 - percentage;
                progressGrid.ColumnDefinitions = new ColumnDefinitions($"{percentage}*,{remainder}*");
                var fill = new Border
                {
                    Background = new SolidColorBrush(PrimaryBlue),
                    CornerRadius = new CornerRadius(2.5, 0, 0, 2.5),
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                Grid.SetColumn(fill, 0);
                progressGrid.Children.Add(fill);
            }

            progressBorder.Child = progressGrid;

            leftStack.Children.Add(nameText);
            if (!string.IsNullOrEmpty(subtitleName))
            {
                leftStack.Children.Add(new TextBlock
                {
                    Text = subtitleName,
                    FontSize = 9,
                    Foreground = new SolidColorBrush(TextSecondary),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, -2, 0, 0)
                });
            }
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
                Text = percentage.ToString("F0", CultureInfo.InvariantCulture) + "%",
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

        private static Grid CreateHourlyBar(int hour, ProductivityStats stats)
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

            // Calculate percentages
            double productivePercentage = stats.Total > 0 ? (double)stats.ProductiveCount / stats.Total * 100 : 0;
            double unproductivePercentage = 100.0 - productivePercentage;

            // Proportional bar using Grid star columns — no pixel widths
            var barGrid = new Grid();

            bool hasProductive   = stats.ProductiveCount > 0;
            bool hasUnproductive = stats.UnproductiveCount > 0;

            if (hasProductive && hasUnproductive)
            {
                barGrid.ColumnDefinitions = new ColumnDefinitions(
                    $"{productivePercentage}*,{unproductivePercentage}*");

                var productiveBar = new Border
                {
                    Background = new SolidColorBrush(ProductiveGreen),
                    CornerRadius = new CornerRadius(3, 0, 0, 3),
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                Grid.SetColumn(productiveBar, 0);
                barGrid.Children.Add(productiveBar);

                var unproductiveBar = new Border
                {
                    Background = new SolidColorBrush(UnproductiveRed),
                    CornerRadius = new CornerRadius(0, 3, 3, 0),
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                Grid.SetColumn(unproductiveBar, 1);
                barGrid.Children.Add(unproductiveBar);
            }
            else if (hasProductive)
            {
                barGrid.ColumnDefinitions = new ColumnDefinitions("*");
                barGrid.Children.Add(new Border
                {
                    Background = new SolidColorBrush(ProductiveGreen),
                    CornerRadius = new CornerRadius(3),
                    HorizontalAlignment = HorizontalAlignment.Stretch
                });
            }
            else if (hasUnproductive)
            {
                barGrid.ColumnDefinitions = new ColumnDefinitions("*");
                barGrid.Children.Add(new Border
                {
                    Background = new SolidColorBrush(UnproductiveRed),
                    CornerRadius = new CornerRadius(3),
                    HorizontalAlignment = HorizontalAlignment.Stretch
                });
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
                HorizontalAlignment = HorizontalAlignment.Right,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            var percentageText = new TextBlock
            {
                Text = $"{productivePercentage:F0}%",
                FontSize = 9,
                FontWeight = FontWeight.Normal,
                Foreground = new SolidColorBrush(TextSecondary),
                HorizontalAlignment = HorizontalAlignment.Right,
                TextTrimming = TextTrimming.CharacterEllipsis
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
                    : _currentFilter == TimeFilter.ThisWeek
                        ? "No activity recorded this week yet"
                        : _currentFilter == TimeFilter.ThisMonth
                            ? "No activity recorded this month yet"
                            : "No activity recorded yet",
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

        private static string FormatDuration(int minutes)
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

                case TimeFilter.ThisMonth:
                    return new DateTime(now.Year, now.Month, 1);

                case TimeFilter.AllTime:
                    return DateTime.MinValue;

                default:
                    return now.Date;
            }
        }

        // ━━ UI Audit helpers — called from nudge-tray --ui-audit mode ━━━━━━━━━━━

        public void AuditSelectTab(TimeFilter filter, AnalyticsData? injectedData = null)
        {
            _aiTabActive = false;
            _aiLiveRefreshTimer?.Stop();
            _currentFilter = filter;
            _contentScrollOffset = 0;
            UpdateTabStyles();
            _data = injectedData ?? AnalyticsData.LoadFromCSV(_currentFilter);
            RefreshContent();
        }

        public void AuditSelectAIBrainTab()
        {
            _aiTabActive = true;
            _activeDetailView = DetailViewType.None;
            _contentScrollOffset = 0;
            UpdateTabStyles();
            RefreshContent();
        }

        public void AuditSetSections(bool sensorSignalsOpen, bool trainingDetailsOpen)
        {
            _sensorSignalsOpen = sensorSignalsOpen;
            _trainingDetailsOpen = trainingDetailsOpen;
            if (_aiTabActive) RefreshContent();
        }

        public void RequestTrainingViewRefresh()
        {
            if (_aiTabActive)
                Dispatcher.UIThread.Post(RefreshContent, DispatcherPriority.Normal);
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // Analytics Data Models and CSV Parsing
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    public sealed class ProductivityStats
    {
        public int ProductiveCount { get; set; }
        public int UnproductiveCount { get; set; }
        public int Total => ProductiveCount + UnproductiveCount;
        public double ProductivePercentage => Total > 0 ? (double)ProductiveCount / Total * 100 : 0;
    }

    public sealed class AnalyticsData
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
            string activityLogPath = PlatformConfig.ActivityLogPath;
            string harvestPath = PlatformConfig.CsvPath;
            DateTime filterStartDate = AnalyticsWindow.GetFilterStartDate(filter);

            Console.WriteLine($"[Analytics] Loading data for {filter} (from {filterStartDate:yyyy-MM-dd HH:mm})");
            Console.WriteLine($"[Analytics] Activity log: {activityLogPath}");
            Console.WriteLine($"[Analytics] Harvest data: {harvestPath}");

            if (File.Exists(activityLogPath))
            {
                try
                {
                    Console.WriteLine("[Analytics] Streaming ACTIVITY_LOG.CSV...");
                    int processedLines = 0;
                    bool skippedHeader = false;

                    foreach (var line in File.ReadLines(activityLogPath))
                    {
                        if (!skippedHeader)
                        {
                            skippedHeader = true;
                            continue;
                        }

                        if (!NudgeCoreLogic.TryParseActivityLogLine(line, out var entry) ||
                            entry.Timestamp < filterStartDate ||
                            NudgeCoreLogic.ShouldIgnoreAnalyticsApp(entry.AppName))
                        {
                            continue;
                        }

                        ref int minutes = ref CollectionsMarshal.GetValueRefOrAddDefault(data.AppUsage, entry.AppName, out _);
                        minutes += 1;
                        data.TotalActivityMinutes += 1;
                        processedLines++;
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

            if (File.Exists(harvestPath))
            {
                try
                {
                    Console.WriteLine("[Analytics] Streaming HARVEST.CSV...");
                    int processedHarvest = 0;
                    bool skippedHeader = false;

                    foreach (var line in File.ReadLines(harvestPath))
                    {
                        if (!skippedHeader)
                        {
                            skippedHeader = true;
                            continue;
                        }

                        if (!NudgeCoreLogic.TryParseHarvestLine(line, out var entry) ||
                            entry.Timestamp < filterStartDate ||
                            NudgeCoreLogic.ShouldIgnoreAnalyticsApp(entry.AppName))
                        {
                            continue;
                        }

                        if (!data.HourlyProductivity.TryGetValue(entry.HourOfDay, out var stats))
                        {
                            stats = new ProductivityStats();
                            data.HourlyProductivity[entry.HourOfDay] = stats;
                        }

                        if (entry.Productive)
                        {
                            stats.ProductiveCount++;
                            data.ProductiveMinutes += 1;
                        }
                        else
                        {
                            stats.UnproductiveCount++;
                            data.UnproductiveMinutes += 1;
                        }

                        processedHarvest++;
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
