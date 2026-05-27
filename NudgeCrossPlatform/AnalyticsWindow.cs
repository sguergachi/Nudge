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
using System.Threading.Tasks;

using NudgeCore;

namespace NudgeTray
{
    public sealed partial class AnalyticsWindow : Window
    {
        private enum DetailViewType
        {
            None,
            Activity,
            Apps,
            Productivity
        }

        private TimeFilter _currentFilter;
        private AnalyticsData? _data;
        private readonly AnalyticsData? _initialData;
        private Border? _contentViewport;
        private StackPanel? _contentPanel;
        private TranslateTransform? _contentTransform;
        private double _contentScrollOffset;
        private DetailViewType _activeDetailView;
        private Border? _todayTab;
        private Border? _weekTab;
        private Border? _monthTab;
        private Border? _allTimeTab;

        // Pause/Active toggle
        private Border? _pauseToggleBadge;
        private TextBlock? _pauseToggleText;

        // AI Brain live tab
        private bool _aiTabActive;
        private Border? _aiLiveTab;
        private DispatcherTimer? _aiLiveRefreshTimer;
        private int _lastAiEventCount;

        // Pin (always-on-top) state
        private bool _isPinned;
        private TextBlock? _pinIcon;

        // AI Brain tab collapse state — survives refreshes
        private static bool _sensorSignalsOpen;
        private static bool _trainingDetailsOpen;

        // Countdown / progress bar (kept for timer lifetime across rebuilds)
        private DispatcherTimer? _countdownTimer;
        private Border? _progressTrack;
        private TextBlock? _cdLabel;

        // Fluent Design System Colors - matching CustomNotification
        private static readonly Color BackgroundColor = Color.FromRgb(18, 18, 20);
        private static readonly Color SurfaceColor = Color.FromRgb(28, 28, 32);
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

        // AI Status Colors
        private static readonly Color AIStatusActive = Color.FromRgb(76, 175, 80);
        private static readonly Color AIStatusLearning = Color.FromRgb(255, 193, 7);
        private static readonly Color AIStatusInactive = Color.FromRgb(150, 150, 160);

        // ── String constants (DRY) ───────────────────────────────────────────
        private const string StrWaitingFirstCheck = "Waiting for first check…";
        private const string StrWaitingFirstAICheck = "Waiting for first AI check…";
        private const string StrCheckingNow = "Checking now…";
        private const string StrNoModelAvailable = "No model available yet";
        private const string StrNoModelSubtext = "AI predictions will begin once training completes";
        private const string StrEnableAI = "Enable AI";
        private const string StrActive = "Active";
        private const string StrDetails = "Details";
        private const string StrNotificationsActive = "Notifications are active. Click to pause.";
        private const string StrSensorSignalsOpen = "▾ Sensor Signals";
        private const string StrSensorSignalsClosed = "▸ Sensor Signals";
        private const string StrChevronOpen = "▾";
        private const string StrChevronClosed = "▸";
        private const string StrPinIcon = "⊙";
        private const string StrProductive = "productive";
        private const string StrNotProductive = "not productive";
        private const string StrNudgedAction = "nudged";
        private const string StrSkippedAction = "skipped";
        private const string StrTabToday = "Today";
        private const string StrTabThisWeek = "This Week";
        private const string StrTabThisMonth = "This Month";
        private const string StrTabAllTime = "All Time";

        public enum TimeFilter
        {
            Today,
            ThisWeek,
            ThisMonth,
            AllTime
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
            Height = 640;
            CanResize = false;
            ShowInTaskbar = false;
            WindowDecorations = WindowDecorations.None;
            Title = "Nudge";
            Background = Brushes.Transparent;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            Focusable = true;

            // Live AI Brain tab refresh — only runs when AI tab is active
            _aiLiveRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _aiLiveRefreshTimer.Tick += (s, e) =>
            {
                if (_aiTabActive)
                {
                    int eventCount = LiveAIState.GetRecent().Count;
                    if (eventCount != _lastAiEventCount)
                    {
                        _lastAiEventCount = eventCount;
                        RefreshContent();
                    }
                }
            };

        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            PositionNearBottomRight();
            StartCountdown();
        }

        private void StartCountdown()
        {
            UpdateCountdown();
            _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _countdownTimer.Tick += (_, _) => UpdateCountdown();
            _countdownTimer.Start();
        }

        private void UpdateCountdown()
        {
            if (_cdLabel == null || _progressTrack == null) return;

            if (Program._notificationsPaused)
            {
                SetCountdownText("● Paused", TextSecondary, false);
                SetProgressBar(0);
                return;
            }

            long totalSec;
            long secLeft;
            bool isAi;

            if (Program._mlEnabled)
            {
                long nextAt = LiveAIState.NextCheckAt;
                if (nextAt > 0)
                {
                    long nowSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    totalSec = Math.Max(10, Program.MlCheckIntervalSeconds);
                    secLeft = Math.Max(0, nextAt - nowSec);
                    isAi = true;
                    goto show;
                }
            }

            {
                var nextSnap = Program.GetNextSnapshotTime();
                if (nextSnap.HasValue)
                {
                    var remaining = nextSnap.Value - DateTime.Now;
                    totalSec = Math.Max(1, (long)TimeSpan.FromMinutes(Program.IntervalMinutes).TotalSeconds);
                    secLeft = remaining <= TimeSpan.Zero ? 0 : (long)Math.Min(totalSec, remaining.TotalSeconds);
                    isAi = false;
                    goto show;
                }
            }

            SetCountdownText("waiting for data...", TextSecondary, false);
            SetProgressBar(0);
            return;

        show:
            if (secLeft <= 0)
            {
                SetCountdownText("● checking now", PrimaryBlue, false);
                SetProgressBar(1.0);
                return;
            }

            double prog = (double)(totalSec - secLeft) / totalSec;
            SetProgressBar(Math.Clamp(prog, 0, 1));
            string prefix = isAi ? "AI check in " : "Snapshot in ";
            SetCountdownText(prefix + FormatSec(secLeft), PrimaryBlue, true);
        }

        private void SetCountdownText(string text, Color color, bool medium)
        {
            _cdLabel!.Text = text;
            _cdLabel.Foreground = new SolidColorBrush(color);
            _cdLabel.FontWeight = medium ? FontWeight.Medium : FontWeight.Normal;
        }

        private void SetProgressBar(double prog)
        {
            _progressTrack!.Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255));
            var barGrid = new Grid();
            if (prog >= 0.995)
            {
                barGrid.ColumnDefinitions = new ColumnDefinitions("*");
                barGrid.Children.Add(new Border
                {
                    Background = new SolidColorBrush(PrimaryBlue),
                    CornerRadius = new CornerRadius(1),
                    HorizontalAlignment = HorizontalAlignment.Stretch
                });
            }
            else if (prog > 0.005)
            {
                double rem = 1.0 - prog;
                barGrid.ColumnDefinitions = new ColumnDefinitions($"{prog * 100:F1}*,{rem * 100:F1}*");
                barGrid.Children.Add(new Border
                {
                    Background = new SolidColorBrush(PrimaryBlue),
                    CornerRadius = new CornerRadius(1, 0, 0, 1),
                    HorizontalAlignment = HorizontalAlignment.Stretch
                });
            }
            _progressTrack.Child = barGrid;
        }

        private static string FormatSec(long s) =>
            s < 60 ? $"{s}s" : $"{s / 60}m {s % 60:D2}s";

        private void PositionNearBottomRight()
        {
            var screen = Screens.Primary;
            if (screen == null) return;
            double scale = RenderScaling;
            Position = new PixelPoint(
                screen.WorkingArea.Right - (int)(Width * scale + 20 * scale),
                screen.WorkingArea.Bottom - (int)(Height * scale + 20 * scale)
            );
        }

        protected override void OnClosed(EventArgs e)
        {
            _aiLiveRefreshTimer?.Stop();
            _countdownTimer?.Stop();
            base.OnClosed(e);
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
                Height = 608
            };

            // Main layout grid (outer) - will contain header and content area
            var outerGrid = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*"),
                Width = 388,
                Height = 608,
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

            // ── Title bar (title row + countdown subtitle + progress separator) ──
            var topBar = new Border
            {
                Background = new SolidColorBrush(SurfaceColor),
                Padding = new Thickness(16, 14, 12, 0),
                CornerRadius = new CornerRadius(12, 12, 0, 0)
            };

            var topContent = new StackPanel { Spacing = 0 };

            // Row 1 — title + controls
            var topGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto,8,Auto,4,Auto")
            };

            var titleText = new TextBlock
            {
                Text = "Nudge",
                FontSize = 14,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(TextPrimary),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(titleText, 0);

            var pauseToggle = CreatePauseToggle();
            Grid.SetColumn(pauseToggle, 1);

            var pinButton = CreatePinButton();
            Grid.SetColumn(pinButton, 3);

            var closeButton = CreateCloseButton();
            Grid.SetColumn(closeButton, 5);

            topGrid.Children.Add(titleText);
            topGrid.Children.Add(pauseToggle);
            topGrid.Children.Add(pinButton);
            topGrid.Children.Add(closeButton);
            topContent.Children.Add(topGrid);

            // Row 2 — countdown subtitle: clearly secondary
            var cdRow = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"),
                Margin = new Thickness(0, 8, 0, 5)
            };

            var cdIconCanvas = new Canvas { Width = 24, Height = 24 };
            cdIconCanvas.Children.Add(new Avalonia.Controls.Shapes.Path
            {
                Fill = new SolidColorBrush(TextSecondary),
                Data = Geometry.Parse(GetIconPath("clock"))
            });
            var cdIcon = new Viewbox
            {
                Width = 12,
                Height = 12,
                Stretch = Stretch.Uniform,
                Child = cdIconCanvas,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 5, 0)
            };
            Grid.SetColumn(cdIcon, 0);

            var cdPrefix = new TextBlock
            {
                Text = "Next check: ",
                FontSize = 10,
                Foreground = new SolidColorBrush(TextSecondary),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(cdPrefix, 1);

            _cdLabel = new TextBlock
            {
                Text = "--",
                FontSize = 10,
                FontWeight = FontWeight.Medium,
                Foreground = new SolidColorBrush(PrimaryBlue),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_cdLabel, 2);

            cdRow.Children.Add(cdIcon);
            cdRow.Children.Add(cdPrefix);
            cdRow.Children.Add(_cdLabel);
            topContent.Children.Add(cdRow);

            // Row 3 — 1px progress line, reads as a border/separator
            _progressTrack = new Border
            {
                Height = 1,
                Background = new SolidColorBrush(Color.FromArgb(25, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            topContent.Children.Add(_progressTrack);

            topBar.Child = topContent;
            headerStack.Children.Add(topBar);

            // ── Tabs bar ────────────────────────────────────────────────────────
            var tabsBar = new Border
            {
                Background = new SolidColorBrush(SurfaceColor),
                BorderBrush = new SolidColorBrush(BorderColor),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(16, 4, 16, 0)
            };

            var tabsGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            _todayTab   = CreateTab(StrTabToday,     true);
            _weekTab    = CreateTab(StrTabThisWeek,  false);
            _monthTab   = CreateTab(StrTabThisMonth, false);
            _allTimeTab = CreateTab(StrTabAllTime,   false);

            var leftTabs = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 0
            };
            leftTabs.Children.Add(_todayTab);
            leftTabs.Children.Add(_weekTab);
            leftTabs.Children.Add(_monthTab);
            leftTabs.Children.Add(_allTimeTab);
            Grid.SetColumn(leftTabs, 0);

            _aiLiveTab = CreateAIBrainTab();
            var rightGroup = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 0,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            rightGroup.Children.Add(new Border
            {
                Width = 1,
                Background = new SolidColorBrush(BorderColor),
                Margin = new Thickness(0, 8, 0, 8),
                VerticalAlignment = VerticalAlignment.Stretch
            });
            rightGroup.Children.Add(_aiLiveTab);
            Grid.SetColumn(rightGroup, 1);

            tabsGrid.Children.Add(leftTabs);
            tabsGrid.Children.Add(rightGroup);
            tabsBar.Child = tabsGrid;
            headerStack.Children.Add(tabsBar);

            return new Border { Child = headerStack };
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
                // Snapshot whether AI tab was active before switching away
                bool wasAiTab = _aiTabActive;
                _aiTabActive = false;
                _aiLiveRefreshTimer?.Stop();

                TimeFilter newFilter = label switch
                {
                    StrTabToday => TimeFilter.Today,
                    StrTabThisWeek => TimeFilter.ThisWeek,
                    StrTabThisMonth => TimeFilter.ThisMonth,
                    StrTabAllTime => TimeFilter.AllTime,
                    _ => TimeFilter.Today
                };

                // Reload if the filter changed OR if we're switching away from AI tab
                if (newFilter != _currentFilter || wasAiTab)
                {
                    _currentFilter = newFilter;
                    UpdateTabStyles();
                    // Reload data and refresh content only (don't rebuild entire UI)
                    _data = AnalyticsData.LoadFromCSV(_currentFilter);
                    RefreshContent();
                }
            };

            border.Child = button;

            ToolTip.SetTip(border, label switch
            {
                StrTabToday => "Show today's activity",
                StrTabThisWeek => "Show this week's activity",
                StrTabThisMonth => "Show this month's activity",
                StrTabAllTime => "Show all activity since Nudge started",
                _ => label
            });

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
            if (_todayTab != null && _weekTab != null && _monthTab != null && _allTimeTab != null)
            {
                var tabs = new[] {
                    (_todayTab, TimeFilter.Today),
                    (_weekTab, TimeFilter.ThisWeek),
                    (_monthTab, TimeFilter.ThisMonth),
                    (_allTimeTab, TimeFilter.AllTime)
                };

                foreach (var (tab, filter) in tabs)
                {
                    // Time-filter tabs are active only when AI tab is NOT active and filter matches
                    bool isActive = !_aiTabActive && _currentFilter == filter;
                    tab.BorderBrush = isActive ? new SolidColorBrush(PrimaryBlue) : Brushes.Transparent;
                    if (tab.Child is Button btn && btn.Content is TextBlock tb)
                    {
                        tb.FontWeight = isActive ? FontWeight.SemiBold : FontWeight.Medium;
                        tb.Foreground = new SolidColorBrush(isActive ? PrimaryBlue : TextSecondary);
                    }
                }
            }

            // AI Brain tab
            if (_aiLiveTab != null)
            {
                bool isActive = _aiTabActive;
                _aiLiveTab.BorderBrush = isActive
                    ? new SolidColorBrush(AIStatusActive)
                    : Brushes.Transparent;
                if (_aiLiveTab.Child is Button btn && btn.Content is TextBlock tb)
                {
                    tb.FontWeight = isActive ? FontWeight.SemiBold : FontWeight.Medium;
                    tb.Foreground = new SolidColorBrush(isActive ? AIStatusActive : TextSecondary);
                }
            }
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // AI BRAIN TAB — live sparkline + score meter + event log
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        private Border CreateAIBrainTab()
        {
            var border = new Border
            {
                Padding = new Thickness(12, 10, 12, 10),
                Cursor = new Cursor(StandardCursorType.Hand),
                BorderThickness = new Thickness(0, 0, 0, 2),
                BorderBrush = Brushes.Transparent
            };

            var button = new Button
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = new Cursor(StandardCursorType.Hand),
                Padding = new Thickness(0),
                Content = new TextBlock
                {
                    Text = "AI Brain",
                    FontSize = 11,
                    FontWeight = FontWeight.Medium,
                    Foreground = new SolidColorBrush(TextSecondary)
                }
            };

            ToolTip.SetTip(border, "Live AI predictions, confidence scores, and ML training status");

            button.Click += (s, e) =>
            {
                _aiTabActive = true;
                _activeDetailView = DetailViewType.None;
                _contentScrollOffset = 0;
                UpdateTabStyles();
                RefreshContent();
                _aiLiveRefreshTimer?.Start();
            };

            border.PointerEntered += (s, e) =>
            {
                if (!_aiTabActive && border.Child is Button btn && btn.Content is TextBlock tb)
                    tb.Foreground = new SolidColorBrush(TextPrimary);
            };
            border.PointerExited += (s, e) =>
            {
                if (!_aiTabActive && border.Child is Button btn && btn.Content is TextBlock tb)
                    tb.Foreground = new SolidColorBrush(TextSecondary);
            };

            border.Child = button;
            return border;
        }

        /// <summary>Builds the entire AI Brain tab content panel.</summary>
        private static StackPanel CreateAILiveView(AnalyticsWindow window)
        {
            var panel = new StackPanel { Spacing = 10 };

            if (!Program._mlEnabled)
            {
                panel.Children.Add(CreateAINotEnabledCard(window));
                return panel;
            }

            var events = LiveAIState.GetRecent();
            var latest = events.Count > 0 ? events[events.Count - 1] : null;

            // ── Live app focus ────────────────────────────────────────────────────
            var focusCard = CreateLiveFocusCard(latest);
            focusCard.Tag = "ai_focus_card";
            panel.Children.Add(focusCard);

            // ── Prediction History (countdown + score + chart merged) ─────────────
            var predSection = CreatePredictionHistorySection(events, latest);
            predSection.Tag = "ai_prediction_section";
            panel.Children.Add(predSection);

            // ── Recent events log ─────────────────────────────────────────────────
            if (events.Count > 0)
            {
                var eventsSection = CreateSection("Recent Checks", CreateEventsLog(events));
                eventsSection.Tag = "ai_events_section";
                panel.Children.Add(eventsSection);
            }

            // ── Training status ───────────────────────────────────────────────────
            var trainingSection = CreateSection("Model Training", CreateTrainingView());
            trainingSection.Tag = "ai_training_section";
            panel.Children.Add(trainingSection);

            return panel;
        }

        private static StackPanel CreateTrainingView()
        {
            var (sampleCount, minSamples, lastTrainedCount, isTraining,
                 lastAccuracy, prevAccuracy, architecture, lastError,
                 lastChecked, lastTrained, modelVersion, log,
                 trainingProgress) = TrainerState.Snapshot();

            var panel = new StackPanel { Spacing = 8 };

            // ── Status row ──────────────────────────────────────────────────────
            Color statusColor;
            string statusText;
            if (isTraining)
            {
                statusColor = AIStatusLearning;
                statusText  = architecture is "" or "…" ? "Training…" : $"Training ({architecture})…";
            }
            else if (!string.IsNullOrEmpty(lastError))
            {
                statusColor = UnproductiveRed;
                statusText  = "Error";
            }
            else if (lastTrained != DateTime.MinValue && sampleCount - lastTrainedCount <= 5)
            {
                statusColor = ProductiveGreen;
                statusText  = "Up to date";
            }
            else if (sampleCount >= minSamples && lastTrained != DateTime.MinValue)
            {
                statusColor = AIStatusLearning;
                statusText  = "Waiting to retrain…";
            }
            else if (sampleCount >= minSamples)
            {
                statusColor = AIStatusLearning;
                statusText  = "Waiting to train…";
            }
            else
            {
                statusColor = TextSecondary;
                statusText  = "Collecting data";
            }

            // ── Header row: status left, button right ──────────────────────────
            {
                var headerGrid = new Grid();
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

                var statusStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Tag = "training_status_row" };
                statusStack.Children.Add(new Border
                {
                    Width = 8, Height = 8,
                    CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush(statusColor),
                    VerticalAlignment = VerticalAlignment.Center
                });
                statusStack.Children.Add(new TextBlock
                {
                    Text = statusText,
                    FontSize = 12,
                    FontWeight = FontWeight.Medium,
                    Foreground = new SolidColorBrush(statusColor),
                    VerticalAlignment = VerticalAlignment.Center
                });
                Grid.SetColumn(statusStack, 0);
                headerGrid.Children.Add(statusStack);

                var trainNowBtn = new Button
                {
                    Content = "Train Now",
                    Background = new SolidColorBrush(PrimaryBlue),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(14, 6),
                    FontSize = 11,
                    FontWeight = FontWeight.Medium,
                    Cursor = new Cursor(StandardCursorType.Hand),
                    CornerRadius = new CornerRadius(5),
                    IsEnabled = !isTraining,
                    Opacity = isTraining ? 0.5 : 1.0
                };
                ToolTip.SetTip(trainNowBtn, "Force an immediate training run using current data");
                trainNowBtn.Click += (s, e) => Program.TriggerTrainingNow();
                Grid.SetColumn(trainNowBtn, 1);
                headerGrid.Children.Add(trainNowBtn);

                panel.Children.Add(headerGrid);
            }

            // ── Current model info (right under status) ──────────────────────────
            if (lastAccuracy >= 0)
            {
                var modelRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 0, 0, 0) };
                modelRow.Children.Add(new TextBlock
                {
                    Text = "Current Model:",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(TextTertiary)
                });
                var versionLabel = modelVersion > 0 ? $"v{modelVersion} · " : "";
                modelRow.Children.Add(new TextBlock
                {
                    Text = $"{versionLabel}{lastAccuracy * 100:F0}% accuracy",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(TextSecondary)
                });
                if (prevAccuracy >= 0)
                {
                    double delta = (lastAccuracy - prevAccuracy) * 100;
                    string deltaStr = delta >= 0 ? $"+{delta:F1}%" : $"{delta:F1}%";
                    Color deltaColor = delta >= 0 ? ProductiveGreen : UnproductiveRed;
                    modelRow.Children.Add(new TextBlock
                    {
                        Text = $"({deltaStr})",
                        FontSize = 11,
                        Foreground = new SolidColorBrush(deltaColor)
                    });
                }
                if (!string.IsNullOrEmpty(architecture) && !isTraining)
                {
                    modelRow.Children.Add(new TextBlock
                    {
                        Text = $"· {architecture}",
                        FontSize = 11,
                        Foreground = new SolidColorBrush(TextTertiary)
                    });
                }
                panel.Children.Add(modelRow);
            }

            // ── Training / Sample progress bar ────────────────────────────────────
            if (isTraining)
            {
                string trainLabel = architecture is "" or "…"
                    ? "Training model…"
                    : $"Training {architecture} model…";

                panel.Children.Add(new TextBlock
                {
                    Text = trainLabel,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(AIStatusLearning)
                });

                if (trainingProgress >= 0f)
                {
                    var barBg = new Border
                    {
                        Height = 4,
                        CornerRadius = new CornerRadius(2),
                        Background = new SolidColorBrush(ProgressBarBg),
                        Margin = new Thickness(0, 2, 0, 0),
                        ClipToBounds = true
                    };
                    var fillGrid = new Grid();
                    if (trainingProgress >= 0.995)
                    {
                        fillGrid.ColumnDefinitions = new ColumnDefinitions("*");
                    }
                    else
                    {
                        double rem = 1.0 - trainingProgress;
                        fillGrid.ColumnDefinitions = new ColumnDefinitions($"{trainingProgress * 100:F1}*,{rem * 100:F1}*");
                    }
                    fillGrid.Children.Add(new Border
                    {
                        Background = new SolidColorBrush(PrimaryBlue),
                        CornerRadius = new CornerRadius(2, 0, 0, 2),
                        HorizontalAlignment = HorizontalAlignment.Stretch
                    });
                    barBg.Child = fillGrid;
                    panel.Children.Add(barBg);
                }
                else
                {
                    panel.Children.Add(CreateIndeterminateBar());
                }
            }
            else
            {
                bool   hasModel = lastTrained != DateTime.MinValue;
                int    retrainThreshold = hasModel
                    ? lastTrainedCount + Math.Max(10, (int)(lastTrainedCount * 0.10))
                    : minSamples;
                int    newSamples = hasModel ? Math.Max(0, sampleCount - lastTrainedCount) : sampleCount;
                int    neededTotal = hasModel ? retrainThreshold : minSamples;

                string progressLabel = hasModel
                    ? $"{newSamples} new samples since last training"
                    : $"{sampleCount} / {minSamples} samples needed for first model";

                panel.Children.Add(new TextBlock
                {
                    Text = progressLabel,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(TextSecondary)
                });

                // ── Animated countdown bar to next trainer check ───────────────────
                if (lastChecked != DateTime.MinValue)
                {
                    var anchor = lastChecked;
                    const double checkIntervalSec = 300;

                    var barBg = new Border
                    {
                        Height = 4,
                        CornerRadius = new CornerRadius(2),
                        Background = new SolidColorBrush(ProgressBarBg),
                        Margin = new Thickness(0, 2, 0, 0),
                        ClipToBounds = true
                    };

                    var fill = new Border
                    {
                        Background = new SolidColorBrush(PrimaryBlue),
                        CornerRadius = new CornerRadius(2),
                        HorizontalAlignment = HorizontalAlignment.Stretch
                    };
                    var fillScale = new ScaleTransform { ScaleX = 0 };
                    fill.RenderTransform = fillScale;
                    fill.RenderTransformOrigin = new RelativePoint(0, 0.5, RelativeUnit.Relative);
                    barBg.Child = fill;

                    var countdownLabel = new TextBlock
                    {
                        FontSize = 10,
                        Foreground = new SolidColorBrush(TextTertiary),
                        Margin = new Thickness(0, 2, 0, 0)
                    };

                    panel.Children.Add(barBg);
                    panel.Children.Add(countdownLabel);

                    // Update once on creation, then every second
                    Action update = () =>
                    {
                        double elapsed = (DateTime.Now - anchor).TotalSeconds;
                        double p = Math.Min(1.0, Math.Max(0.0, elapsed / checkIntervalSec));
                        fillScale.ScaleX = p;

                        double rem = Math.Max(0, checkIntervalSec - elapsed);
                        countdownLabel.Text = rem > 1
                            ? $"Next check: in {(int)rem / 60}m {(int)rem % 60}s"
                            : "Next check: any moment";
                    };
                    update();

                    var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                    timer.Tick += (_, _) => update();
                    timer.Start();
                    barBg.Unloaded += (_, _) => timer.Stop();
                }
            }

            // ── Collapsible details (error, log, last-checked) ───────────────────
            bool hasDetails = !string.IsNullOrEmpty(lastError)
                           || log.Count > 0
                           || lastChecked != DateTime.MinValue;

            if (hasDetails)
            {
                var detailPanel = new StackPanel
                {
                    Spacing = 4,
                    Margin = new Thickness(0, 4, 0, 0),
                    IsVisible = _trainingDetailsOpen
                };

                if (!string.IsNullOrEmpty(lastError))
                {
                    detailPanel.Children.Add(new TextBlock
                    {
                        Text = lastError,
                        FontSize = 10,
                        Foreground = new SolidColorBrush(UnproductiveRed),
                        TextWrapping = TextWrapping.Wrap
                    });
                }

                if (log.Count > 0)
                {
                    var logPanel = new StackPanel { Spacing = 1 };
                    foreach (var line in log)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        logPanel.Children.Add(new TextBlock
                        {
                            Text = line,
                            FontSize = 9,
                            Foreground = new SolidColorBrush(TextTertiary),
                            TextWrapping = TextWrapping.Wrap,
                            FontFamily = new FontFamily("Monospace")
                        });
                    }
                    detailPanel.Children.Add(logPanel);
                }

                if (lastChecked != DateTime.MinValue)
                {
                    detailPanel.Children.Add(new TextBlock
                    {
                        Text = $"Last checked: {lastChecked:HH:mm:ss}",
                        FontSize = 9,
                        Foreground = new SolidColorBrush(TextTertiary)
                    });
                }

                // Toggle row: "▸ Details" / "▾ Details"
                var chevron = new TextBlock
                {
                    Text = _trainingDetailsOpen ? StrChevronOpen : StrChevronClosed,
                    FontSize = 9,
                    Foreground = new SolidColorBrush(TextTertiary),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 4, 0)
                };
                var toggleRow = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 0,
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Margin = new Thickness(0, 2, 0, 0)
                };
                toggleRow.Children.Add(chevron);
                toggleRow.Children.Add(new TextBlock
                {
                    Text = StrDetails,
                    FontSize = 9,
                    Foreground = new SolidColorBrush(TextTertiary),
                    VerticalAlignment = VerticalAlignment.Center
                });

                toggleRow.PointerPressed += (s, e) =>
                {
                    _trainingDetailsOpen = !detailPanel.IsVisible;
                    detailPanel.IsVisible = _trainingDetailsOpen;
                    chevron.Text = _trainingDetailsOpen ? StrChevronOpen : StrChevronClosed;
                };
                toggleRow.PointerEntered += (s, e) =>
                {
                    foreach (var child in toggleRow.Children)
                        if (child is TextBlock tb) tb.Foreground = new SolidColorBrush(TextSecondary);
                };
                toggleRow.PointerExited += (s, e) =>
                {
                    foreach (var child in toggleRow.Children)
                        if (child is TextBlock tb) tb.Foreground = new SolidColorBrush(TextTertiary);
                };

                panel.Children.Add(toggleRow);
                panel.Children.Add(detailPanel);
            }

            return panel;
        }

        /// <summary>Current prediction card: verdict dot, score, bar, app+time.</summary>
        private static StackPanel CreateCurrentPredictionView(MLLiveEvent? latest)
        {
            if (latest == null)
            {
                var waitPanel = new StackPanel
                {
                    Spacing = 4,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 8, 0, 4)
                };
                waitPanel.Children.Add(new TextBlock
                {
                    Text = StrWaitingFirstCheck,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(TextSecondary),
                    HorizontalAlignment = HorizontalAlignment.Center
                });
                waitPanel.Children.Add(new TextBlock
                {
                    Text = "AI checks every 60 s",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(TextTertiary),
                    HorizontalAlignment = HorizontalAlignment.Center
                });
                return waitPanel;
            }

            bool productive = latest.Productive;
            double score = latest.Score;
            double confidence = latest.Confidence;
            // Amber for low-confidence predictions, green/red for high-confidence
            Color stateColor = confidence < 0.5
                ? AIStatusLearning
                : (productive ? ProductiveGreen : UnproductiveRed);

            var stack = new StackPanel { Spacing = 8 };

            // ── Top row: verdict + score ──
            var topGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto")
            };

            var verdictRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 7,
                VerticalAlignment = VerticalAlignment.Center
            };
            verdictRow.Children.Add(new Border
            {
                Width = 8, Height = 8,
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(stateColor),
                VerticalAlignment = VerticalAlignment.Center
            });
            verdictRow.Children.Add(new TextBlock
            {
                Text = productive ? "PRODUCTIVE" : "NOT PRODUCTIVE",
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(stateColor),
                VerticalAlignment = VerticalAlignment.Center
            });
            Grid.SetColumn(verdictRow, 0);

            var scoreText = new TextBlock
            {
                Text = $"{confidence * 100:F0}%",
                FontSize = 14,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(stateColor),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetColumn(scoreText, 2);

            topGrid.Children.Add(verdictRow);
            topGrid.Children.Add(scoreText);
            stack.Children.Add(topGrid);

            // ── App + time subtitle ──
            var localTime = DateTimeOffset.FromUnixTimeSeconds(latest.T).LocalDateTime;
            stack.Children.Add(new TextBlock
            {
                Text = $"{TruncateAppName(latest.App, 32)}  ·  {localTime:HH:mm}",
                FontSize = 10,
                Foreground = new SolidColorBrush(TextTertiary)
            });

            // ── Score bar ──
            double remainder = 1.0 - score;
            var barContainer = new Border
            {
                Height = 6,
                Background = new SolidColorBrush(ProgressBarBg),
                CornerRadius = new CornerRadius(3),
                ClipToBounds = true,
                Margin = new Thickness(0, 2, 0, 0)
            };
            var barGrid = new Grid();
            if (score >= 0.995)
            {
                barGrid.ColumnDefinitions = new ColumnDefinitions("*");
                barGrid.Children.Add(new Border
                {
                    Background = new SolidColorBrush(stateColor),
                    CornerRadius = new CornerRadius(3),
                    HorizontalAlignment = HorizontalAlignment.Stretch
                });
            }
            else if (score > 0.005)
            {
                barGrid.ColumnDefinitions = new ColumnDefinitions(
                    $"{score * 100:F1}*,{remainder * 100:F1}*");
                var fill = new Border
                {
                    Background = new SolidColorBrush(stateColor),
                    CornerRadius = new CornerRadius(3, 0, 0, 3),
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                Grid.SetColumn(fill, 0);
                barGrid.Children.Add(fill);
            }
            barContainer.Child = barGrid;
            stack.Children.Add(barContainer);

            return stack;
        }

        /// <summary>Sparkline canvas: dots + connecting line for last N predictions.</summary>
        private static Canvas BuildSparkline(IReadOnlyList<MLLiveEvent> events)
        {
            const double W = 320;
            const double H = 52;
            const double dotR = 3.0;
            const double yTop    = dotR + 3;          // score 1.0 → top
            const double yBottom = H - dotR - 3;      // score 0.0 → bottom
            const double yRange  = yBottom - yTop;

            var canvas = new Canvas
            {
                Width = W,
                Height = H,
                HorizontalAlignment = HorizontalAlignment.Left
            };

            if (events.Count == 0)
            {
                canvas.Children.Add(new TextBlock
                {
                    Text = StrWaitingFirstAICheck,
                    FontSize = 10,
                    Foreground = new SolidColorBrush(TextTertiary)
                });
                Canvas.SetLeft(canvas.Children[0], W / 2 - 80);
                Canvas.SetTop(canvas.Children[0], H / 2 - 7);
                return canvas;
            }

            // Faint midline at score=0.5 (boundary between productive/not)
            canvas.Children.Add(new Border
            {
                Width = W,
                Height = 1,
                Background = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255))
            });
            Canvas.SetLeft(canvas.Children[0], 0);
            Canvas.SetTop(canvas.Children[0], yTop + yRange * 0.5);

            // Compute (x, y) for each point
            int n = events.Count;
            var pts = new List<(double x, double y, MLLiveEvent e)>(n);
            for (int i = 0; i < n; i++)
            {
                double xFrac = n == 1 ? 0.5 : (double)i / (n - 1);
                double x = dotR + xFrac * (W - dotR * 2);
                double y = yTop + (1.0 - events[i].Score) * yRange;
                pts.Add((x, y, events[i]));
            }

            // Connecting line (drawn first so dots sit on top)
            if (n >= 2)
            {
                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    ctx.BeginFigure(new Point(pts[0].x, pts[0].y), false);
                    for (int i = 1; i < pts.Count; i++)
                        ctx.LineTo(new Point(pts[i].x, pts[i].y));
                }
                canvas.Children.Add(new Avalonia.Controls.Shapes.Path
                {
                    Data = geo,
                    Stroke = new SolidColorBrush(Color.FromArgb(38, 255, 255, 255)),
                    StrokeThickness = 1.5,
                    StrokeLineCap = PenLineCap.Round
                });
            }

            // Dots — latest is slightly larger with a glow ring
            for (int i = 0; i < pts.Count; i++)
            {
                var (x, y, evt) = pts[i];
                bool isLatest = i == pts.Count - 1;
                double r = isLatest ? dotR + 1.5 : dotR;

                Color dotColor = evt.Confidence < 0.5
                    ? AIStatusLearning          // amber = uncertain
                    : (evt.Productive ? ProductiveGreen : UnproductiveRed);

                // Glow ring for the latest dot
                if (isLatest)
                {
                    var ring = new Avalonia.Controls.Shapes.Ellipse
                    {
                        Width  = (r + 3) * 2,
                        Height = (r + 3) * 2,
                        Stroke = new SolidColorBrush(Color.FromArgb(50, dotColor.R, dotColor.G, dotColor.B)),
                        StrokeThickness = 1.5
                    };
                    Canvas.SetLeft(ring, x - r - 3);
                    Canvas.SetTop(ring, y - r - 3);
                    canvas.Children.Add(ring);
                }

                var dot = new Avalonia.Controls.Shapes.Ellipse
                {
                    Width  = r * 2,
                    Height = r * 2,
                    Fill   = new SolidColorBrush(dotColor)
                };
                Canvas.SetLeft(dot, x - r);
                Canvas.SetTop(dot, y - r);
                canvas.Children.Add(dot);
            }

            return canvas;
        }

        /// <summary>Live card: current app in focus + latest prediction verdict.</summary>
        private static Border CreateLiveFocusCard(MLLiveEvent? latest)
        {
            var currentApp  = LiveAIState.CurrentApp;
            var currentDetail = LiveAIState.CurrentDetail;
            var harvest     = LiveAIState.LastHarvest;
            bool hasApp     = !string.IsNullOrEmpty(currentApp);
            string displayApp = hasApp
                ? BrowserDetector.GetBrowserDisplayName(currentApp) ?? currentApp
                : "";

            // Effective quality blends Harvest Engine signal quality with category confidence.
            // A trusted signal is downgraded to Usable when the app category is too uncertain
            // (conf < 0.45 means Fallback or Unknown — we genuinely don't know what the app is).
            string effectiveQuality = harvest?.Quality ?? "";
            if (effectiveQuality == "trusted" && harvest != null && harvest.CategoryConf < 0.45f && !string.IsNullOrEmpty(harvest.Category))
                effectiveQuality = "usable";

            Color fusionColor = harvest == null         ? TextTertiary
                : effectiveQuality == "trusted"         ? ProductiveGreen
                : effectiveQuality == "usable"          ? AIStatusLearning
                                                        : UnproductiveRed;

            string qualityLabel = harvest == null            ? "Initializing"
                : effectiveQuality == "trusted"              ? "Trusted"
                : effectiveQuality == "usable"               ? "Usable"
                                                             : "Poor Signal";

            // ── Main row ──────────────────────────────────────────────────────
            var mainGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };

            var dot = new Border
            {
                Width = 10, Height = 10,
                CornerRadius = new CornerRadius(5),
                Background = new SolidColorBrush(fusionColor),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            Grid.SetColumn(dot, 0);

            bool showDetail = !string.IsNullOrWhiteSpace(currentDetail)
                && !currentDetail.Equals(currentApp, StringComparison.OrdinalIgnoreCase)
                && !currentDetail.Contains(currentApp, StringComparison.OrdinalIgnoreCase);

            var textStack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            textStack.Children.Add(new TextBlock
            {
                Text       = hasApp ? TruncateAppName(displayApp, 24) : "Engine starting up…",
                FontSize   = 13,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(hasApp ? TextPrimary : TextTertiary)
            });
            if (showDetail)
            {
                textStack.Children.Add(new TextBlock
                {
                    Text          = TruncateAppName(currentDetail, 55),
                    FontSize      = 10,
                    Foreground    = new SolidColorBrush(TextSecondary),
                    TextTrimming  = TextTrimming.CharacterEllipsis
                });
            }
            // Fusion quality + "In Focus Now" on the same line
            string qualityReason = harvest != null ? ComputeQualityReason(harvest, effectiveQuality) : "";
            var qualityRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            qualityRow.Children.Add(new Border
            {
                Width = 6, Height = 6,
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(fusionColor),
                VerticalAlignment = VerticalAlignment.Center
            });
            qualityRow.Children.Add(new TextBlock
            {
                Text       = qualityLabel,
                FontSize   = 9,
                FontWeight = FontWeight.Medium,
                Foreground = new SolidColorBrush(fusionColor)
            });
            qualityRow.Children.Add(new TextBlock
            {
                Text       = "· In Focus Now",
                FontSize   = 9,
                Foreground = new SolidColorBrush(TextTertiary)
            });
            textStack.Children.Add(qualityRow);
            Grid.SetColumn(textStack, 1);

            mainGrid.Children.Add(dot);
            mainGrid.Children.Add(textStack);

            // ── Separator ────────────────────────────────────────────────────
            var sep = new Border
            {
                Height = 1,
                Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
                Margin = new Thickness(0, 10, 0, 8)
            };

            // ── Sensor Signals collapse ──────────────────────────────────────
            var signalPanel = new StackPanel { Spacing = 5, IsVisible = _sensorSignalsOpen, Margin = new Thickness(0, 6, 0, 2) };
            if (harvest != null)
            {
                AddFusionRow(signalPanel, "Signal Quality",
                    string.IsNullOrEmpty(qualityReason) ? qualityLabel : $"{qualityLabel} ({qualityReason})",
                    fusionColor);
                AddFusionRow(signalPanel, "Win Tracking",  FormatKWinStatus(harvest.FocusSrc),
                    harvest.FocusSrc == "KWinScript" ? ProductiveGreen : AIStatusLearning);
                AddFusionRow(signalPanel, "Idle",           FormatMs(harvest.IdleMs),      TextSecondary);
                AddFusionRow(signalPanel, "In Focus",       FormatMs(harvest.FocusedMs),   TextSecondary);
                {
                    string cat = !string.IsNullOrEmpty(harvest.Category) ? harvest.Category : GetHarvestCategoryFallback(harvest);
                    if (!string.IsNullOrEmpty(cat))
                        AddCategoryBadgeRow(signalPanel, cat, harvest.CategoryConf);
                    if (!string.IsNullOrEmpty(harvest.Domain))
                        AddFusionRow(signalPanel, "Domain", harvest.Domain, PrimaryBlue);
                    if (showDetail && !string.IsNullOrEmpty(currentDetail))
                        AddFusionRow(signalPanel, "Tab", currentDetail, TextSecondary);
                    AddFusionRow(signalPanel, "Activity (5m)",
                        $"{harvest.Sw300} switches · {harvest.Share * 100:F0}% dominant", TextSecondary);
                    if (harvest.Apps300 > 1)
                        AddFusionRow(signalPanel, "Distinct Apps", $"{harvest.Apps300} apps seen", TextSecondary);
                    if (harvest.Fullscreen == 1)
                        AddFusionRow(signalPanel, "Fullscreen", "Yes", AIStatusLearning);
                }
            }
            else
            {
                signalPanel.Children.Add(new TextBlock
                {
                    Text       = "Win Tracking: detecting…",
                    FontSize   = 10,
                    Foreground = new SolidColorBrush(Color.FromArgb(120, 255, 193, 7))
                });
                signalPanel.Children.Add(new TextBlock
                {
                    Text       = "Signal Quality: initializing",
                    FontSize   = 10,
                    Foreground = new SolidColorBrush(TextTertiary),
                    Margin     = new Thickness(0, 2, 0, 0)
                });
            }

            // Toggle button for the collapse
            var toggleText = new TextBlock
            {
                Text       = _sensorSignalsOpen ? StrSensorSignalsOpen : StrSensorSignalsClosed,
                FontSize   = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(140, 100, 180, 255)),
                Cursor     = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
            };
            var toggleBtn = new Border
            {
                Background = Brushes.Transparent,
                Cursor     = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                Child      = toggleText
            };
            toggleBtn.PointerPressed += (_, _) =>
            {
                bool nowVisible = !signalPanel.IsVisible;
                _sensorSignalsOpen = nowVisible;
                signalPanel.IsVisible = nowVisible;
                toggleText.Text = nowVisible ? StrSensorSignalsOpen : StrSensorSignalsClosed;
            };

            var outerStack = new StackPanel { Spacing = 0 };
            outerStack.Children.Add(mainGrid);
            outerStack.Children.Add(sep);

            // ── Toggle row: Sensor Signals + engine badge flush right ────────
            var toggleRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,auto") };
            toggleRow.Children.Add(toggleBtn);
            var badge = new TextBlock
            {
                Text       = "POWERED BY NUDGE HARVEST ENGINE",
                FontSize   = 7.5,
                LetterSpacing = 1.0,
                Foreground = new SolidColorBrush(Color.FromArgb(80, 100, 180, 255)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(badge, 1);
            toggleRow.Children.Add(badge);
            outerStack.Children.Add(toggleRow);

            outerStack.Children.Add(signalPanel);

            return new Border
            {
                Background      = new SolidColorBrush(SurfaceColor),
                CornerRadius    = new CornerRadius(8),
                Padding         = new Thickness(14, 12),
                BorderBrush     = new SolidColorBrush(BorderColor),
                BorderThickness = new Thickness(1),
                Child           = outerStack
            };
        }

        private static void AddFusionRow(StackPanel parent, string label, string value, Color valueColor)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("90,*") };
            row.Children.Add(new TextBlock
            {
                Text       = label,
                FontSize   = 10,
                Foreground = new SolidColorBrush(TextTertiary)
            });
            var valBlock = new TextBlock
            {
                Text          = value,
                FontSize      = 10,
                FontWeight    = FontWeight.Medium,
                Foreground    = new SolidColorBrush(valueColor),
                TextTrimming  = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(valBlock, 1);
            row.Children.Add(valBlock);
            parent.Children.Add(row);
        }

        private static string ComputeQualityReason(HarvestSignal harvest, string effectiveQuality)
        {
            if (effectiveQuality == "trusted") return "";

            // Downgraded from trusted because category is unclassified
            if (harvest.Quality == "trusted" && effectiveQuality == "usable")
                return "category unclassified";

            if (effectiveQuality == "poor")
            {
                return harvest.FocusSrc switch
                {
                    "HeuristicProcessScan" => "process scan only",
                    "Unknown" or ""        => "no focus source",
                    _                      => "unreliable source"
                };
            }

            // Native usable
            if (harvest.Browser == 1 && string.IsNullOrEmpty(harvest.Domain))
                return "browser tab unknown";

            return "degraded signal";
        }

        private static string FormatKWinStatus(string src) => src switch
        {
            "KWinScript"              => "KWin Script ✓",
            "X11Ewmh"                 => "X11 EWMH",
            "WaylandActivatedProtocol"=> "Wayland Protocol",
            "SwayIpc"                 => "Sway IPC",
            "GnomeShell"              => "GNOME Shell",
            "WindowsApi"              => "Windows API",
            "HeuristicProcessScan"    => "Process Scan (fallback)",
            _                         => "detecting…"
        };

        private static string FormatFocusSource(string src) => src switch
        {
            "KWinScript"              => "KWin Script",
            "X11Ewmh"                 => "X11 EWMH",
            "WaylandActivatedProtocol"=> "Wayland Protocol",
            "SwayIpc"                 => "Sway IPC",
            "GnomeShell"              => "GNOME Shell",
            "WindowsApi"              => "Windows API",
            "HeuristicProcessScan"    => "Process Scan (heuristic)",
            _                         => src
        };

        private static string FormatMs(int ms)
        {
            if (ms < 1000) return $"{ms}ms";
            int s = ms / 1000;
            if (s < 60)  return $"{s}s";
            int m = s / 60; s %= 60;
            return s > 0 ? $"{m}m {s}s" : $"{m}m";
        }

        // Category colors: each string name maps to a background tint and text color
        private static (Color Bg, Color Fg, Color Border) GetCategoryBadgeColors(string category) => category switch
        {
            "Development"     => (Color.FromArgb(40,  180, 140, 255), Color.FromRgb(180, 140, 255), Color.FromArgb(80, 180, 140, 255)),
            "Creative & Design" => (Color.FromArgb(40, 0, 188, 212),  Color.FromRgb(0, 188, 212),   Color.FromArgb(80, 0, 188, 212)),
            "Office & Writing"  => (Color.FromArgb(40, 76, 175, 80),  Color.FromRgb(76, 175, 80),   Color.FromArgb(80, 76, 175, 80)),
            "Communication"   => (Color.FromArgb(40,  255, 193, 7),   AIStatusLearning,   Color.FromArgb(80, 255, 193, 7)),
            "Entertainment"   => (Color.FromArgb(40,  255, 82, 82),   Color.FromRgb(255, 82, 82),   Color.FromArgb(80, 255, 82, 82)),
            "Work"            => (Color.FromArgb(40,  76, 175, 80),   Color.FromRgb(76, 175, 80),   Color.FromArgb(80, 76, 175, 80)),
            "AFK"             => (Color.FromArgb(40,  150, 150, 160), Color.FromRgb(150, 150, 160), Color.FromArgb(80, 150, 150, 160)),
            _                 => (Color.FromArgb(40,  144, 164, 174), Color.FromRgb(144, 164, 174), Color.FromArgb(80, 144, 164, 174)),
        };

        private static string GetConfidenceLabelFromScore(float score)
        {
            if (score >= 0.90f) return "Verified";
            if (score >= 0.70f) return "Estimated";
            if (score >= 0.45f) return "Inferred";
            return "";
        }

        private static void AddCategoryBadgeRow(StackPanel parent, string category, float confidence = 1f)
        {
            var (bg, fg, borderColor) = GetCategoryBadgeColors(category);
            string confLabel = GetConfidenceLabelFromScore(confidence);

            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("90,*") };
            row.Children.Add(new TextBlock
            {
                Text       = "Category",
                FontSize   = 10,
                Foreground = new SolidColorBrush(TextTertiary),
                VerticalAlignment = VerticalAlignment.Center
            });

            // Badge content: category name + optional confidence label
            var badgeContent = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                VerticalAlignment = VerticalAlignment.Center
            };
            badgeContent.Children.Add(new TextBlock
            {
                Text       = category,
                FontSize   = 9.5,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(fg),
                VerticalAlignment = VerticalAlignment.Center
            });
            if (!string.IsNullOrEmpty(confLabel))
            {
                badgeContent.Children.Add(new TextBlock
                {
                    Text       = $"· {confLabel}",
                    FontSize   = 8.5,
                    Foreground = new SolidColorBrush(Color.FromArgb(160, fg.R, fg.G, fg.B)),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            var badge = new Border
            {
                Background      = new SolidColorBrush(bg),
                BorderBrush     = new SolidColorBrush(borderColor),
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(4),
                Padding         = new Thickness(6, 2),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = badgeContent
            };
            Grid.SetColumn(badge, 1);
            row.Children.Add(badge);
            parent.Children.Add(row);
        }

        /// <summary>Creates a self-animating indeterminate progress bar (bouncing bubble).</summary>
        private static Border CreateIndeterminateBar()
        {
            const double bubbleWidth = 48;
            const double speed = 2.5;

            var track = new Border
            {
                Height = 4,
                CornerRadius = new CornerRadius(2),
                Background = new SolidColorBrush(ProgressBarBg),
                ClipToBounds = true,
                Margin = new Thickness(0, 2, 0, 0)
            };

            var bubble = new Border
            {
                Width = bubbleWidth,
                Height = 4,
                Background = new SolidColorBrush(PrimaryBlue),
                CornerRadius = new CornerRadius(2),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            track.Child = bubble;

            double pos = -(bubbleWidth / 2);
            double dir = 1;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            timer.Tick += (_, _) =>
            {
                double w = track.Bounds.Width;
                if (w <= 0) return;

                pos += speed * dir;
                if (pos > w - bubbleWidth + 4) { pos = w - bubbleWidth + 4; dir = -1; }
                else if (pos < -4) { pos = -4; dir = 1; }

                bubble.Margin = new Thickness(pos, 0, 0, 0);
            };
            timer.Start();

            track.Unloaded += (_, _) => timer.Stop();
            return track;
        }

        private static string GetHarvestCategoryFallback(HarvestSignal h)
        {
            if (h.Afk == 1)     return "AFK";
            if (h.Work == 1)    return "Work";
            if (h.Comm == 1)    return "Communication";
            if (h.Ent == 1)     return "Entertainment";
            if (h.Browser == 1) return "Browser";
            return "";
        }

        /// <summary>
        /// Merged card: Prediction History title, countdown bar, latest score, and gradient chart.
        /// </summary>
        private static Border CreatePredictionHistorySection(IReadOnlyList<MLLiveEvent> events, MLLiveEvent? latest)
        {
            bool hasModel = TrainerState.ModelVersion > 0 || TrainerState.LastAccuracy >= 0f;

            Color mlColor = latest == null ? TextTertiary
                : latest.Confidence < 0.5 ? AIStatusLearning
                : latest.Productive       ? ProductiveGreen
                                          : UnproductiveRed;

            var panel = new StackPanel { Spacing = 8 };

            // ── Header row: title + countdown text ───────────────────────────
            var headerGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            headerGrid.Children.Add(new TextBlock
            {
                Text = "Prediction History",
                FontSize = 11,
                FontWeight = FontWeight.Medium,
                Foreground = new SolidColorBrush(TextSecondary),
                VerticalAlignment = VerticalAlignment.Center
            });
            var cdLabel = new TextBlock
            {
                Text = hasModel ? StrWaitingFirstCheck : StrNoModelAvailable,
                FontSize = 10,
                Foreground = new SolidColorBrush(hasModel ? TextTertiary : AIStatusLearning),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(cdLabel, 1);
            headerGrid.Children.Add(cdLabel);
            panel.Children.Add(headerGrid);

            if (!hasModel)
            {
                // ── Disabled state: no model trained yet ─────────────────────
                panel.Children.Add(new TextBlock
                {
                    Text = StrNoModelSubtext,
                    FontSize = 9,
                    Foreground = new SolidColorBrush(TextTertiary),
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.7
                });

                var emptyBar = new Border
                {
                    Height = 4,
                    CornerRadius = new CornerRadius(2),
                    Background = new SolidColorBrush(ProgressBarBg),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Opacity = 0.4
                };
                panel.Children.Add(emptyBar);
            }
            else
            {
                // ── Countdown progress bar ────────────────────────────────────
                const long totalInterval = 60;

                var barContainer = new Border
                {
                    Height = 4,
                    CornerRadius = new CornerRadius(2),
                    Background = new SolidColorBrush(ProgressBarBg),
                    ClipToBounds = true,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                Action rebuildProgressBar = () =>
                {
                    long nextAt = LiveAIState.NextCheckAt;
                    long nowSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    long secLeft = nextAt > 0 ? Math.Max(0, nextAt - nowSec) : 0;
                    long secDone = totalInterval - secLeft;
                    double prog = nextAt > 0
                        ? Math.Min(1.0, Math.Max(0.0, (double)secDone / totalInterval))
                        : 0;

                    if (nextAt > 0 && secLeft == 0)
                    {
                        barContainer.Child = CreateIndeterminateBar();
                    }
                    else
                    {
                        var barGrid = new Grid();
                        if (prog >= 0.995)
                        {
                            barGrid.ColumnDefinitions = new ColumnDefinitions("*");
                            barGrid.Children.Add(new Border
                            {
                                Background = new SolidColorBrush(PrimaryBlue),
                                CornerRadius = new CornerRadius(2),
                                HorizontalAlignment = HorizontalAlignment.Stretch
                            });
                        }
                        else if (prog > 0.005)
                        {
                            double rem = 1.0 - prog;
                            barGrid.ColumnDefinitions = new ColumnDefinitions($"{prog * 100:F1}*,{rem * 100:F1}*");
                            var fill = new Border
                            {
                                Background = new SolidColorBrush(PrimaryBlue),
                                CornerRadius = new CornerRadius(2, 0, 0, 2),
                                HorizontalAlignment = HorizontalAlignment.Stretch
                            };
                            Grid.SetColumn(fill, 0);
                            barGrid.Children.Add(fill);
                        }
                        barContainer.Child = barGrid;
                    }
                };
                rebuildProgressBar();
                panel.Children.Add(barContainer);

                // ── Live countdown update every second (text only) ───────────
                Action updateCountdown = () =>
                {
                    long nextAt = LiveAIState.NextCheckAt;
                    long nowSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    long secLeft = nextAt > 0 ? Math.Max(0, nextAt - nowSec) : 0;

                    cdLabel.Text = nextAt == 0 ? StrWaitingFirstCheck
                        : secLeft == 0 ? StrCheckingNow
                        : $"{secLeft / 60}:{secLeft % 60:D2} until next check";
                    cdLabel.HorizontalAlignment = nextAt == 0
                        ? HorizontalAlignment.Left
                        : HorizontalAlignment.Right;
                };
                updateCountdown();

                var countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                countdownTimer.Tick += (_, _) => updateCountdown();
                countdownTimer.Start();
                barContainer.Unloaded += (_, _) => countdownTimer.Stop();
            }

            // ── Latest score row ──────────────────────────────────────────────
            if (latest != null)
            {
                var scoreGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                scoreGrid.Children.Add(new TextBlock
                {
                    Text = "Last prediction",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(TextTertiary),
                    VerticalAlignment = VerticalAlignment.Center
                });
                var scoreStack = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 5,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                };
                scoreStack.Children.Add(new TextBlock
                {
                    Text = $"{(latest.Confidence > 0 ? latest.Confidence : latest.Score) * 100:F0}%",
                    FontSize = 15,
                    FontWeight = FontWeight.Bold,
                    Foreground = new SolidColorBrush(mlColor),
                    VerticalAlignment = VerticalAlignment.Center
                });
                scoreStack.Children.Add(new TextBlock
                {
                    Text = latest.Productive ? StrProductive : StrNotProductive,
                    FontSize = 9,
                    FontWeight = FontWeight.Medium,
                    Foreground = new SolidColorBrush(mlColor),
                    VerticalAlignment = VerticalAlignment.Center
                });
                Grid.SetColumn(scoreStack, 1);
                scoreGrid.Children.Add(scoreStack);
                panel.Children.Add(scoreGrid);
            }

            // ── Gradient chart ────────────────────────────────────────────────
            panel.Children.Add(BuildGradientChart(events));

            return new Border
            {
                Background = new SolidColorBrush(SurfaceColor),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 12),
                BorderBrush = new SolidColorBrush(BorderColor),
                BorderThickness = new Thickness(1),
                Child = panel
            };
        }

        /// <summary>Progress bar counting down to the next AI check.</summary>
        /// <summary>Full-width gradient area chart of prediction history.</summary>
        private static Canvas BuildGradientChart(IReadOnlyList<MLLiveEvent> events)
        {
            const double W = 328;
            const double H = 92;
            const double dotR = 3.5;
            const double yPad = dotR + 4;
            const double yTop = yPad;
            const double yBottom = H - yPad - 12;
            const double yRange = yBottom - yTop;

            var canvas = new Canvas { Width = W, Height = H };

            // Zone gradient background: green (productive) → red (unproductive)
            var zoneBg = new Border
            {
                Width = W, Height = H,
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                    GradientStops = new GradientStops
                    {
                        new GradientStop(Color.FromArgb(20, 76, 175, 80), 0),
                        new GradientStop(Color.FromArgb(0, 128, 128, 128), 0.5),
                        new GradientStop(Color.FromArgb(20, 244, 67, 54), 1)
                    }
                }
            };
            Canvas.SetLeft(zoneBg, 0);
            Canvas.SetTop(zoneBg, 0);
            canvas.Children.Add(zoneBg);

            // Faint zone labels
            var prodLabel = new TextBlock
            {
                Text = StrProductive,
                FontSize = 8,
                Foreground = new SolidColorBrush(Color.FromArgb(40, 76, 175, 80))
            };
            Canvas.SetLeft(prodLabel, 4);
            Canvas.SetTop(prodLabel, 2);
            canvas.Children.Add(prodLabel);

            var notProdLabel = new TextBlock
            {
                Text = StrNotProductive,
                FontSize = 8,
                Foreground = new SolidColorBrush(Color.FromArgb(40, 244, 67, 54))
            };
            Canvas.SetLeft(notProdLabel, 4);
            Canvas.SetTop(notProdLabel, yBottom - 11);
            canvas.Children.Add(notProdLabel);

            // Midline at score = 0.5
            var midline = new Border
            {
                Width = W, Height = 1,
                Background = new SolidColorBrush(Color.FromArgb(25, 255, 255, 255))
            };
            Canvas.SetLeft(midline, 0);
            Canvas.SetTop(midline, yTop + yRange * 0.5);
            canvas.Children.Add(midline);

            if (events.Count == 0)
            {
                var tb = new TextBlock
                {
                    Text = StrWaitingFirstAICheck,
                    FontSize = 10,
                    Foreground = new SolidColorBrush(TextTertiary),
                    Width = W,
                    TextAlignment = TextAlignment.Center
                };
                Canvas.SetLeft(tb, 0);
                Canvas.SetTop(tb, yBottom / 2);
                canvas.Children.Add(tb);
                return canvas;
            }

            int n = events.Count;
            var pts = new List<(double x, double y, MLLiveEvent ev)>(n);
            for (int i = 0; i < n; i++)
            {
                double xFrac = n == 1 ? 0.5 : (double)i / (n - 1);
                double x = dotR + xFrac * (W - dotR * 2);
                double y = yTop + (1.0 - events[i].Score) * yRange;
                pts.Add((x, y, events[i]));
            }

            // Filled area under the line (gradient fill)
            if (n >= 2)
            {
                var latestEv = pts[pts.Count - 1].ev;
                Color fillColor = latestEv.Confidence < 0.5
                    ? AIStatusLearning
                    : (latestEv.Productive ? ProductiveGreen : UnproductiveRed);

                var areaGeo = new StreamGeometry();
                using (var ctx = areaGeo.Open())
                {
                    ctx.BeginFigure(new Point(pts[0].x, pts[0].y), true);
                    for (int i = 1; i < pts.Count; i++)
                        ctx.LineTo(new Point(pts[i].x, pts[i].y));
                    ctx.LineTo(new Point(pts[pts.Count - 1].x, yBottom));
                    ctx.LineTo(new Point(pts[0].x, yBottom));
                }
                canvas.Children.Add(new Avalonia.Controls.Shapes.Path
                {
                    Data = areaGeo,
                    Fill = new LinearGradientBrush
                    {
                        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                        GradientStops = new GradientStops
                        {
                            new GradientStop(Color.FromArgb(60, fillColor.R, fillColor.G, fillColor.B), 0),
                            new GradientStop(Color.FromArgb(5, fillColor.R, fillColor.G, fillColor.B), 1)
                        }
                    }
                });
            }

            // Connecting line
            if (n >= 2)
            {
                var lineGeo = new StreamGeometry();
                using (var ctx = lineGeo.Open())
                {
                    ctx.BeginFigure(new Point(pts[0].x, pts[0].y), false);
                    for (int i = 1; i < pts.Count; i++)
                        ctx.LineTo(new Point(pts[i].x, pts[i].y));
                }
                canvas.Children.Add(new Avalonia.Controls.Shapes.Path
                {
                    Data = lineGeo,
                    Stroke = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
                    StrokeThickness = 1.5,
                    StrokeLineCap = PenLineCap.Round
                });
            }

            // Dots — latest is larger with a glow ring
            var dotElements = new List<(Avalonia.Controls.Shapes.Ellipse Dot, double R, double X, double Y, MLLiveEvent Ev)>();
            for (int i = 0; i < pts.Count; i++)
            {
                var (x, y, ev) = pts[i];
                bool isLatest = i == pts.Count - 1;
                double r = isLatest ? dotR + 1.5 : dotR;

                Color dotColor = ev.Confidence < 0.5
                    ? AIStatusLearning
                    : (ev.Productive ? ProductiveGreen : UnproductiveRed);

                if (isLatest)
                {
                    var ring = new Avalonia.Controls.Shapes.Ellipse
                    {
                        Width = (r + 4) * 2,
                        Height = (r + 4) * 2,
                        Stroke = new SolidColorBrush(Color.FromArgb(50, dotColor.R, dotColor.G, dotColor.B)),
                        StrokeThickness = 2,
                        Fill = new SolidColorBrush(Color.FromArgb(15, dotColor.R, dotColor.G, dotColor.B))
                    };
                    Canvas.SetLeft(ring, x - r - 4);
                    Canvas.SetTop(ring, y - r - 4);
                    canvas.Children.Add(ring);
                }

                var dot = new Avalonia.Controls.Shapes.Ellipse
                {
                    Width = r * 2,
                    Height = r * 2,
                    Fill = new SolidColorBrush(dotColor),
                };
                Canvas.SetLeft(dot, x - r);
                Canvas.SetTop(dot, y - r);
                canvas.Children.Add(dot);
                dotElements.Add((dot, r, x, y, ev));

                if (isLatest)
                {
                    var scoreLabel = new TextBlock
                    {
                        Text = $"{(ev.Confidence > 0 ? ev.Confidence : ev.Score) * 100:F0}%",
                        FontSize = 9,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = new SolidColorBrush(dotColor)
                    };
                    // Place label above the dot when near bottom of chart, below when near top
                    bool nearBottom = y > H * 0.6;
                    Canvas.SetLeft(scoreLabel, x - 10);
                    Canvas.SetTop(scoreLabel, nearBottom ? y - r - 14 : y + r + 3);
                    canvas.Children.Add(scoreLabel);
                }
            }

            // ── Sweep line + hover tooltip ──────────────────────────────────
            if (dotElements.Count > 0)
            {
                // Shared tooltip content (updated on hover)
                var tipGrid = new Grid
                {
                    RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
                    ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                    Width = 180
                };
                var timeTb = new TextBlock
                {
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255))
                };
                Grid.SetColumn(timeTb, 1);
                tipGrid.Children.Add(timeTb);
                var appTb = new TextBlock
                {
                    FontSize = 13,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromArgb(230, 255, 255, 255))
                };
                Grid.SetRow(appTb, 1);
                Grid.SetColumnSpan(appTb, 2);
                tipGrid.Children.Add(appTb);
                var scoreTb = new TextBlock
                {
                    FontSize = 11,
                };
                Grid.SetRow(scoreTb, 2);
                Grid.SetColumnSpan(scoreTb, 2);
                tipGrid.Children.Add(scoreTb);
                var tipBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(200, 30, 30, 30)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(10, 8),
                    Child = tipGrid
                };

                // Sweep vertical line
                var sweepLine = new Border
                {
                    Width = 1,
                    Height = yBottom - yPad,
                    Background = new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)),
                    IsVisible = false,
                };
                Canvas.SetLeft(sweepLine, 0);
                Canvas.SetTop(sweepLine, yPad);
                canvas.Children.Add(sweepLine);

                // Transparent overlay to capture pointer across whole chart
                var overlay = new Border
                {
                    Width = W,
                    Height = H,
                    Background = Brushes.Transparent,
                };
                Canvas.SetLeft(overlay, 0);
                Canvas.SetTop(overlay, 0);

                // Tooltip (added after overlay so it renders on top)
                tipBorder.IsVisible = false;
                canvas.Children.Add(tipBorder);

                overlay.PointerMoved += (_, e) =>
                {
                    var pos = e.GetPosition(canvas);

                    int nearest = 0;
                    double minDist = double.MaxValue;
                    for (int i = 0; i < dotElements.Count; i++)
                    {
                        double dist = Math.Abs(dotElements[i].X - pos.X);
                        if (dist < minDist)
                        {
                            minDist = dist;
                            nearest = i;
                        }
                    }

                    foreach (var (d, r, _, _, _) in dotElements)
                    {
                        d.Width = r * 2;
                        d.Height = r * 2;
                    }

                    var (nearestDot, nr, nx, ny, nev) = dotElements[nearest];
                    nearestDot.Width = (nr + 2) * 2;
                    nearestDot.Height = (nr + 2) * 2;

                    Canvas.SetLeft(sweepLine, nx);
                    sweepLine.IsVisible = true;

                    timeTb.Text = DateTimeOffset.FromUnixTimeSeconds(nev.T).LocalDateTime.ToString("t", CultureInfo.CurrentCulture);
                    appTb.Text = nev.App;
                    Color evColor = nev.Confidence < 0.5
                        ? AIStatusLearning
                        : (nev.Productive ? ProductiveGreen : UnproductiveRed);
                    scoreTb.Text = $"{(nev.Confidence > 0 ? nev.Confidence : nev.Score) * 100:F0}% · {(nev.Productive ? StrProductive : StrNotProductive)}";
                    scoreTb.Foreground = new SolidColorBrush(evColor);

                    // Position tooltip above the dot, centered horizontally
                    double tipW = tipBorder.Width > 0 ? tipBorder.Width : 180;
                    Canvas.SetLeft(tipBorder, nx - tipW / 2);
                    Canvas.SetTop(tipBorder, ny - 60);
                    tipBorder.IsVisible = true;
                };

                overlay.PointerExited += (_, _) =>
                {
                    foreach (var (d, r, _, _, _) in dotElements)
                    {
                        d.Width = r * 2;
                        d.Height = r * 2;
                    }
                    sweepLine.IsVisible = false;
                    tipBorder.IsVisible = false;
                };

                canvas.Children.Add(overlay);
            }

            // ── Time axis ───────────────────────────────────────────────────
            if (events.Count >= 2)
            {
                var firstDt = DateTimeOffset.FromUnixTimeSeconds(events[0].T).LocalDateTime;
                var lastDt  = DateTimeOffset.FromUnixTimeSeconds(events[^1].T).LocalDateTime;
                double spanHours = (lastDt - firstDt).TotalHours;
                int stepHours = spanHours <= 3 ? 1 : spanHours <= 8 ? 2 : spanHours <= 16 ? 3 : 6;

                var start = new DateTime(firstDt.Year, firstDt.Month, firstDt.Day,
                    firstDt.Hour - (firstDt.Hour % stepHours), 0, 0);
                if (start < firstDt) start = start.AddHours(stepHours);

                double timeW = W - dotR * 2;
                double totalSec = (lastDt - firstDt).TotalSeconds;

                for (var t = start; t <= lastDt; t = t.AddHours(stepHours))
                {
                    double frac = (t - firstDt).TotalSeconds / totalSec;
                    double x = dotR + frac * timeW;

                    if (x < dotR || x > W - dotR) continue;

                    var label = new TextBlock
                    {
                        Text = t.ToString("h tt", CultureInfo.InvariantCulture),
                        FontSize = 8,
                        Foreground = new SolidColorBrush(Color.FromArgb(80, 180, 180, 190))
                    };
                    Canvas.SetLeft(label, x - 10);
                    Canvas.SetTop(label, yBottom + 3);
                    canvas.Children.Add(label);

                    var tick = new Border
                    {
                        Width = 1,
                        Height = 3,
                        Background = new SolidColorBrush(Color.FromArgb(60, 180, 180, 190))
                    };
                    Canvas.SetLeft(tick, x);
                    Canvas.SetTop(tick, yBottom);
                    canvas.Children.Add(tick);
                }
            }

            return canvas;
        }

        /// <summary>Build tooltip content Grid for a given event.</summary>
        private static Grid CreateTooltipContent(MLLiveEvent evt)
        {
            Color dotColor = evt.Confidence < 0.5
                ? AIStatusLearning
                : (evt.Productive ? ProductiveGreen : UnproductiveRed);

            string actionLabel;
            Color actionColor;
            if (evt.Triggered)
            {
                actionLabel = StrNudgedAction;
                actionColor = UnproductiveRed;
            }
            else
            {
                actionLabel = StrSkippedAction;
                actionColor = ProductiveGreen;
            }

            string statusLabel = "";
            if (evt.AiCorrect == true)
                statusLabel = " · ✓ confirmed";
            else if (evt.AiCorrect == false)
                statusLabel = " · ✗ rejected";
            else if (evt.Triggered)
                statusLabel = " · ⏸ skipped";

            var tipGrid = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto"),
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                Width = 180
            };

            var timeTb = new TextBlock
            {
                Text = DateTimeOffset.FromUnixTimeSeconds(evt.T).LocalDateTime.ToString("t", CultureInfo.CurrentCulture),
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255))
            };
            Grid.SetColumn(timeTb, 1);
            tipGrid.Children.Add(timeTb);

            var triggerTb = new TextBlock
            {
                Text = evt.TriggerSource == "int" ? "Interval-based check" : "AI-predicted check",
                FontSize = 10,
                Foreground = new SolidColorBrush(evt.TriggerSource == "int" ? AIStatusLearning : ProductiveGreen)
            };
            Grid.SetColumn(triggerTb, 1);
            Grid.SetRow(triggerTb, 1);
            tipGrid.Children.Add(triggerTb);

            var appTb = new TextBlock
            {
                Text = evt.App,
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(230, 255, 255, 255))
            };
            Grid.SetRow(appTb, 2);
            Grid.SetColumnSpan(appTb, 2);
            tipGrid.Children.Add(appTb);

            var scoreTb = new TextBlock
            {
                Text = $"{(evt.Confidence > 0 ? evt.Confidence : evt.Score) * 100:F0}% · {(evt.Productive ? StrProductive : StrNotProductive)}",
                FontSize = 11,
                Foreground = new SolidColorBrush(dotColor)
            };
            Grid.SetRow(scoreTb, 3);
            Grid.SetColumnSpan(scoreTb, 2);
            tipGrid.Children.Add(scoreTb);

            var responseTb = new TextBlock
            {
                Text = $"{actionLabel}{statusLabel}",
                FontSize = 11,
                Foreground = new SolidColorBrush(actionColor)
            };
            Grid.SetRow(responseTb, 4);
            Grid.SetColumnSpan(responseTb, 2);
            tipGrid.Children.Add(responseTb);

            return tipGrid;
        }

        /// <summary>Build a hover tooltip Border (same style as the gradient chart tooltip).</summary>
        private static Border CreateEventTooltip(MLLiveEvent evt)
        {
            return new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(200, 30, 30, 30)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 8),
                Child = CreateTooltipContent(evt)
            };
        }

        /// <summary>Compact log of most-recent ML checks, newest first.</summary>
        private static Grid CreateEventsLog(IReadOnlyList<MLLiveEvent> events)
        {
            var grid = new Grid();
            var panel = new StackPanel { Spacing = 5 };
            grid.Children.Add(panel);

            var tipBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(200, 30, 30, 30)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 8),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                IsHitTestVisible = false,
                IsVisible = false,
            };
            grid.Children.Add(tipBorder);

            // Newest first, cap at 8 rows
            int start = Math.Max(0, events.Count - 8);
            for (int i = events.Count - 1; i >= start; i--)
            {
                var evt = events[i];
                var row = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("34,32,*,46,40,28")
                };

                // Time
                var localTime = DateTimeOffset.FromUnixTimeSeconds(evt.T).LocalDateTime;
                var timeText = new TextBlock
                {
                    Text = localTime.ToString("HH:mm", CultureInfo.InvariantCulture),
                    FontSize = 10,
                    Foreground = new SolidColorBrush(TextTertiary),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(timeText, 0);

                // Trigger source
                var sourceText = new TextBlock
                {
                    Text = evt.TriggerSource == "int" ? "INT" : "AI",
                    FontSize = 10,
                    FontWeight = FontWeight.Medium,
                    Foreground = new SolidColorBrush(
                        evt.TriggerSource == "int" ? AIStatusLearning : ProductiveGreen),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                Grid.SetColumn(sourceText, 1);

                // App name
                var appText = new TextBlock
                {
                    Text = TruncateAppName(evt.App, 22),
                    FontSize = 10,
                    Foreground = new SolidColorBrush(TextSecondary),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                Grid.SetColumn(appText, 2);

                // Score with colored dot
                Color dotColor = evt.Confidence < 0.5
                    ? AIStatusLearning
                    : (evt.Productive ? ProductiveGreen : UnproductiveRed);
                var scoreRow = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 4,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                scoreRow.Children.Add(new Border
                {
                    Width = 5, Height = 5,
                    CornerRadius = new CornerRadius(2.5),
                    Background = new SolidColorBrush(dotColor),
                    VerticalAlignment = VerticalAlignment.Center
                });
                scoreRow.Children.Add(new TextBlock
                {
                    Text = $"{(evt.Confidence > 0 ? evt.Confidence : evt.Score) * 100:F0}%",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(TextSecondary),
                    VerticalAlignment = VerticalAlignment.Center
                });
                Grid.SetColumn(scoreRow, 3);

                // Action label
                string actionLabel;
                Color actionColor;
                if (evt.Triggered)
                {
                    actionLabel = StrNudgedAction;
                    actionColor = UnproductiveRed;
                }
                else
                {
                    actionLabel = StrSkippedAction;
                    actionColor = ProductiveGreen;
                }
                var actionText = new TextBlock
                {
                    Text = actionLabel,
                    FontSize = 10,
                    FontWeight = FontWeight.Medium,
                    Foreground = new SolidColorBrush(actionColor),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(actionText, 4);

                // Response icon (✓ = AI correct, ✗ = AI wrong, ⏸ = skipped)
                var respText = new TextBlock
                {
                    Text = evt.AiCorrect == true ? "✓"
                         : evt.AiCorrect == false ? "✗"
                         : evt.Triggered ? "⏸"
                         : "",
                    FontSize = 11,
                    FontWeight = FontWeight.Medium,
                    Foreground = new SolidColorBrush(
                        evt.AiCorrect == true ? ProductiveGreen
                        : evt.AiCorrect == false ? UnproductiveRed
                        : evt.Triggered ? AIStatusInactive
                        : Colors.Transparent),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(respText, 5);

                row.Children.Add(timeText);
                row.Children.Add(sourceText);
                row.Children.Add(appText);
                row.Children.Add(scoreRow);
                row.Children.Add(actionText);
                row.Children.Add(respText);

                row.PointerEntered += (_, _) =>
                {
                    tipBorder.Child = CreateTooltipContent(evt);
                    var pos = row.TranslatePoint(new Point(0, 0), grid);
                    if (pos.HasValue)
                    {
                        double rowCenter = pos.Value.X + row.Bounds.Width / 2;
                        tipBorder.Margin = new Thickness(rowCenter - 90, pos.Value.Y - 70, 0, 0);
                        tipBorder.IsVisible = true;
                    }
                };

                panel.Children.Add(row);
            }

            panel.PointerExited += (_, _) =>
            {
                tipBorder.IsVisible = false;
            };

            return grid;
        }

        /// <summary>"Enable AI" placeholder shown when ML is not running.</summary>
        private static Border CreateAINotEnabledCard(AnalyticsWindow window)
        {
            var panel = new StackPanel
            {
                Spacing = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 44, 0, 8)
            };

            panel.Children.Add(new TextBlock
            {
                Text = "◎",
                FontSize = 44,
                FontWeight = FontWeight.Light,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new SolidColorBrush(TextTertiary),
                Opacity = 0.55
            });

            var titleText = new TextBlock
            {
                Text = "Enable AI Predictions",
                FontSize = 14,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(TextPrimary),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            panel.Children.Add(titleText);

            var descText = new TextBlock
            {
                Text = "AI learns from your Yes/No responses to predict\nwhen you're productive vs distracted.",
                FontSize = 11,
                Foreground = new SolidColorBrush(TextSecondary),
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                MaxWidth = 240,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            panel.Children.Add(descText);

            var enableBtn = new Button
            {
                Content = StrEnableAI,
                Background = new SolidColorBrush(PrimaryBlue),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(22, 9),
                FontSize = 11,
                FontWeight = FontWeight.Medium,
                Cursor = new Cursor(StandardCursorType.Hand),
                HorizontalAlignment = HorizontalAlignment.Center,
                CornerRadius = new CornerRadius(6)
            };
            enableBtn.Click += async (s, e) =>
            {
                enableBtn.IsEnabled = false;
                enableBtn.Content = "Starting AI…";
                descText.Text = "Setting up AI — this can take 1–3 minutes on first run…";

                var progressTimer = new Avalonia.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(400)
                };
                progressTimer.Tick += (_, _) =>
                {
                    var step = Program.MlLoadingStep;
                    if (!string.IsNullOrEmpty(step))
                        descText.Text = step;
                };
                progressTimer.Start();

                bool success = await Task.Run(() => Program.RestartWithML());
                progressTimer.Stop();

                if (success)
                {
                    window.RefreshContent();
                }
                else
                {
                    enableBtn.IsEnabled = true;
                    enableBtn.Content = StrEnableAI;
                    var reason = Program.MlSetupError;
                    descText.Text = string.IsNullOrEmpty(reason)
                        ? "Failed to start AI. Check the logs for details."
                        : $"Could not enable AI:\n{reason}";
                }
            };
            panel.Children.Add(enableBtn);

            return new Border { Child = panel };
        }

        private static string TruncateAppName(string app, int maxLen) =>
            app.Length <= maxLen ? app : string.Concat(app.AsSpan(0, maxLen - 1), "…");

        private static readonly System.Collections.Generic.Dictionary<string, string> s_appDisplayNames =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["zen"]          = "Zen Browser",
                ["zen-bin"]      = "Zen Browser",
                ["firefox-bin"]  = "Firefox",
                ["chromium-bin"] = "Chromium",
            };

        private static (string Primary, string Subtitle) ParseAppDisplayName(string appName)
        {
            int bracketStart = appName.LastIndexOf(" [", StringComparison.Ordinal);
            if (bracketStart > 0 && appName.EndsWith(']'))
            {
                string primary = appName[..bracketStart];
                if (s_appDisplayNames.TryGetValue(primary, out var mapped)) primary = mapped;
                string subtitle = appName[(bracketStart + 2)..^1];
                return (primary, subtitle);
            }
            if (s_appDisplayNames.TryGetValue(appName, out var displayName))
                return (displayName, string.Empty);
            return (appName, string.Empty);
        }
    }
}

