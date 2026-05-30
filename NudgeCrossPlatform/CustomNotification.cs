// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// Custom Notification Window - Cross-Platform Animated Notification
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
//
// Features:
// - Smooth center zoom + fade animations
// - Halo selection ring when active
// - Modern, left-aligned UI
// - Keyboard shortcuts: Y (YES), N (NO), Enter (YES), Escape (dismiss)
// - Draggable window with position persistence
// - Cross-platform support (Windows, Linux, macOS)
//
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using System;
using System.IO;
using System.Text.Json;

namespace NudgeTray
{
    public sealed class CustomNotificationWindow : Window
    {
        private const string CONFIG_FILE = "nudge-notification-config.json";
        private const int AUTO_DISMISS_SECONDS = 30;

        // ── Color constants ───────────────────────────────────────────────────
        private static readonly Color BgWindow = Color.FromArgb(250, 18, 18, 20);
        private static readonly Color TitleText = Color.FromRgb(160, 160, 170);
        private static readonly Color MessageText = Color.FromRgb(232, 232, 238);
        private static readonly Color TimerStroke = Color.FromArgb(160, 150, 150, 165);
        private static readonly Color TimerText = Color.FromArgb(200, 180, 180, 190);
        private static readonly Color PauseIconFill = Color.FromArgb(160, 150, 150, 165);
        private static readonly Color UrgentRed = Color.FromArgb(210, 255, 80, 80);
        private static readonly Color WarmAmber = Color.FromArgb(170, 255, 150, 80);
        private static readonly Color CalmNeutral = Color.FromArgb(160, 150, 150, 165);
        private static readonly Color ActiveGlow = Color.FromArgb(160, 88, 166, 255);
        private static readonly Color InactiveBorder = Color.FromArgb(50, 255, 255, 255);
        private static readonly Color BtnYesBase = Color.FromRgb(88, 166, 255);
        private static readonly Color BtnYesHover = Color.FromRgb(108, 186, 255);
        private static readonly Color BtnYesPressed = Color.FromRgb(60, 130, 220);
        private static readonly Color BtnNoBase = Color.FromRgb(52, 52, 58);
        private static readonly Color BtnNoHover = Color.FromRgb(64, 64, 70);
        private static readonly Color BtnNoPressed = Color.FromRgb(35, 35, 40);
        private static readonly Color NoBtnText = Color.FromRgb(210, 210, 220);
        private static readonly Color NoBtnBorder = Color.FromArgb(40, 255, 255, 255);
        private static readonly Color ShadowColor = Color.FromArgb(50, 0, 0, 0);
        private static readonly Color GlowColor = Color.FromArgb(80, 88, 166, 255);
        private Point? _dragStartPosition;
        private bool _isDragging;
        private Action<bool?>? _onResponse; // Nullable bool: true=YES, false=NO, null=auto-dismissed
        private Border? _mainBorder;
        private bool _isActive;
        private TextBlock? _countdownText;
        private DispatcherTimer? _countdownTimer;
        private int _remainingSeconds = AUTO_DISMISS_SECONDS;
        private bool _responseSent;
        private Arc? _progressArc;
        private Arc? _backgroundArc;
        private StackPanel? _pauseIconView;
        private readonly string _appName;
        private readonly string _detail;

        public CustomNotificationWindow(string? appName = null, string? detail = null)
        {
            _appName = appName ?? "";
            _detail = detail ?? "";
            InitializeWindow();
            InitializeContent();
            LoadPosition();
            SetupKeyboardShortcuts();
        }

        private void InitializeWindow()
        {
            Width = 340;
            Height = 148;
            CanResize = false;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            WindowDecorations = WindowDecorations.None;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            Background = Brushes.Transparent;
            Topmost = true;

            // Enable keyboard focus
            Focusable = true;

            // Track focus to show/hide the card glow (signals keyboard shortcuts are active)
            GotFocus += (s, e) => SetActiveState(true);
            LostFocus += (s, e) => SetActiveState(false);
            Activated += (s, e) => SetActiveState(true);
            Deactivated += (s, e) => SetActiveState(false);
        }

        private void InitializeContent()
        {
            // Main container — card owns its own margin for drop shadow bleed
            _mainBorder = new Border
            {
                Background = new SolidColorBrush(BgWindow),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16),
                Margin = new Thickness(10, 8, 10, 12), // Space for shadow bleed
                ClipToBounds = false,
                BoxShadow = new BoxShadows(
                    new BoxShadow
                    {
                        Blur = 12,
                        Spread = -2,
                        OffsetX = 0,
                        OffsetY = 4,
                        Color = ShadowColor
                    }
                ),
                BorderBrush = new SolidColorBrush(InactiveBorder),
                BorderThickness = new Thickness(1),
                RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative)
            };

            // Add drag functionality to the border
            _mainBorder.PointerPressed += OnBorderPointerPressed;
            _mainBorder.PointerMoved += OnBorderPointerMoved;
            _mainBorder.PointerReleased += OnBorderPointerReleased;
            _mainBorder.PointerEntered += (s, e) =>
            {
                if (!_isDragging && _countdownTimer != null && _countdownTimer.IsEnabled)
                {
                    _countdownTimer.Stop();
                }
                if (!_responseSent && _remainingSeconds > 0)
                {
                    SetTimerPauseDisplay(true);
                }
            };
            _mainBorder.PointerExited += (s, e) =>
            {
                SetTimerPauseDisplay(false);
                if (!_isDragging && !_responseSent && _remainingSeconds > 0)
                {
                    _countdownTimer?.Start();
                }
            };
            _mainBorder.Cursor = new Cursor(StandardCursorType.Hand);

            var stackPanel = new StackPanel
            {
                Spacing = 0, // Gaps controlled explicitly per-element for hierarchy
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            // Header with title and countdown
            var headerPanel = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            // Title - show app and detail/site when available
            var titleText = new TextBlock
            {
                Text = string.IsNullOrEmpty(_appName) ? "Nudge"
                     : string.IsNullOrEmpty(_detail) ? $"Nudge  ›  {_appName}"
                     : $"Nudge  ›  {_appName} ({_detail})",
                FontSize = 11,
                FontWeight = FontWeight.Normal,
                Foreground = new SolidColorBrush(TitleText),
                TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(titleText, 0);

            // Countdown timer with progress wheel - right-aligned
            var timerContainer = new Grid
            {
                Width = 20,
                Height = 20,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };

            // Background circle (track)
            _backgroundArc = new Arc
            {
                Width = 18,
                Height = 18,
                Stroke = new SolidColorBrush(Color.FromArgb(65, 255, 255, 255)),
                StrokeThickness = 1.5,
                StartAngle = 0,
                SweepAngle = 360,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            // Progress arc (animates from 360° to 0°) — starts calm/neutral
            _progressArc = new Arc
            {
                Width = 18,
                Height = 18,
                Stroke = new SolidColorBrush(TimerStroke),
                StrokeThickness = 1.5,
                StartAngle = -90, // Start at top (12 o'clock)
                SweepAngle = 360, // Full circle initially
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                StrokeLineCap = PenLineCap.Round
            };

            // Countdown text (overlaid in center)
            _countdownText = new TextBlock
            {
                Text = $"{AUTO_DISMISS_SECONDS}",
                FontSize = 8,
                FontWeight = FontWeight.Normal,
                Foreground = new SolidColorBrush(TimerText),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            // Pause icon — two vertical bars, sits in the same grid cell as the arc/text,
            // shown when the user hovers and the timer is paused.
            _pauseIconView = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 3,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsVisible = false
            };
            _pauseIconView.Children.Add(new Border
            {
                Width = 2.5, Height = 8,
                CornerRadius = new CornerRadius(1.25),
                Background = new SolidColorBrush(PauseIconFill)
            });
            _pauseIconView.Children.Add(new Border
            {
                Width = 2.5, Height = 8,
                CornerRadius = new CornerRadius(1.25),
                Background = new SolidColorBrush(PauseIconFill)
            });

            timerContainer.Children.Add(_backgroundArc);
            timerContainer.Children.Add(_progressArc);
            timerContainer.Children.Add(_countdownText);
            timerContainer.Children.Add(_pauseIconView);
            Grid.SetColumn(timerContainer, 1);

            headerPanel.Children.Add(titleText);
            headerPanel.Children.Add(timerContainer);

            // Message - the visual anchor; user reads this first
            var messageText = new TextBlock
            {
                Text = "Were you productive?",
                FontSize = 14,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(MessageText),
                TextAlignment = TextAlignment.Left,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 8, 0, 12) // 8px below title, 12px above buttons
            };

            // Buttons Container - equal columns, wider gap for visual separation
            var buttonsPanel = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,12,*"),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            // YES Button - vibrant accent
            var yesButton = CreateStyledButton(
                "Yes",
                "Y",
                BtnYesBase,
                BtnYesHover,
                BtnYesPressed,
                () => HandleResponse(true),
                tabIndex: 0
            );
            Grid.SetColumn(yesButton, 0);

            // NO Button - clearly distinct from card background
            var noButton = CreateStyledButton(
                "No",
                "N",
                BtnNoBase,
                BtnNoHover,
                BtnNoPressed,
                () => HandleResponse(false),
                false,
                tabIndex: 1
            );
            Grid.SetColumn(noButton, 2);

            buttonsPanel.Children.Add(yesButton);
            buttonsPanel.Children.Add(noButton);

            // Add all elements to stack
            stackPanel.Children.Add(headerPanel);
            stackPanel.Children.Add(messageText);
            stackPanel.Children.Add(buttonsPanel);

            _mainBorder.Child = stackPanel;
            Content = _mainBorder;
        }

        private static StackPanel CreateStyledButton(string mainText, string shortcutText, Color baseColor, Color hoverColor, Color pressedColor, Action onClick, bool isPrimary = true, int tabIndex = 0)
        {
            var border = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Height = 34,
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(baseColor),
                BorderBrush = isPrimary
                    ? Brushes.Transparent
                    : new SolidColorBrush(NoBtnBorder),
                BorderThickness = new Thickness(1),
                Cursor = new Cursor(StandardCursorType.Hand),
                Padding = new Thickness(12, 0, 12, 0),
                RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative)
            };

            var buttonContent = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };

            var mainTextBlock = new TextBlock
            {
                Text = mainText,
                FontSize = 12,
                FontWeight = FontWeight.Medium,
                Foreground = isPrimary
                    ? Brushes.White
                    : new SolidColorBrush(NoBtnText),
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };

            var keyBadge = new Border
            {
                Background = new SolidColorBrush(
                    isPrimary ? Color.FromArgb(55, 0, 0, 0) : Color.FromArgb(25, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(
                    isPrimary ? Color.FromArgb(70, 0, 0, 0) : Color.FromArgb(45, 200, 200, 220)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(5, 2, 5, 2),
                VerticalAlignment = VerticalAlignment.Center
            };

            keyBadge.Child = new TextBlock
            {
                Text = shortcutText,
                FontSize = 9,
                FontWeight = FontWeight.Medium,
                Foreground = new SolidColorBrush(
                    isPrimary ? Color.FromArgb(230, 255, 255, 255) : Color.FromArgb(160, 180, 180, 195)),
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };

            buttonContent.Children.Add(mainTextBlock);
            buttonContent.Children.Add(keyBadge);
            border.Child = buttonContent;

            // Hover effects
            border.PointerEntered += (s, e) =>
            {
                border.Background = new SolidColorBrush(hoverColor);
                border.RenderTransform = new ScaleTransform(1.02, 1.02);
            };

            border.PointerExited += (s, e) =>
            {
                border.Background = new SolidColorBrush(baseColor);
                border.RenderTransform = new ScaleTransform(1.0, 1.0);
            };

            // Pressed — visual feedback + trigger action
            border.PointerPressed += (s, e) =>
            {
                border.Background = new SolidColorBrush(pressedColor);
                border.RenderTransform = new ScaleTransform(0.98, 0.98);
                onClick();
            };

            // Released — restore hover
            border.PointerReleased += (s, e) =>
            {
                border.Background = new SolidColorBrush(hoverColor);
                border.RenderTransform = new ScaleTransform(1.02, 1.02);
            };

            return new StackPanel { Children = { border } };
        }


        private void SetupKeyboardShortcuts()
        {
            KeyDown += (s, e) =>
            {
                // Y = Yes (productive), N = No (not productive) — no modifiers needed when window is focused
                if (e.KeyModifiers == KeyModifiers.None && e.Key == Key.Y)
                {
                    Console.WriteLine("[CustomNotification] Keyboard shortcut: Y pressed");
                    HandleResponse(true);
                    e.Handled = true;
                }
                else if (e.KeyModifiers == KeyModifiers.None && e.Key == Key.N)
                {
                    Console.WriteLine("[CustomNotification] Keyboard shortcut: N pressed");
                    HandleResponse(false);
                    e.Handled = true;
                }
                // Also support Enter = Yes (fast confirm for power users)
                else if (e.KeyModifiers == KeyModifiers.None && e.Key == Key.Return)
                {
                    Console.WriteLine("[CustomNotification] Keyboard shortcut: Enter pressed (Yes)");
                    HandleResponse(true);
                    e.Handled = true;
                }
                // Escape = dismiss without recording
                else if (e.KeyModifiers == KeyModifiers.None && e.Key == Key.Escape)
                {
                    Console.WriteLine("[CustomNotification] Keyboard shortcut: Escape pressed (dismiss)");
                    AutoDismiss();
                    e.Handled = true;
                }
            };

            // Register global hotkeys when window is opened
            Opened += async (s, e) =>
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Focus();
                    Activate();
                });
            };
        }

        private void OnBorderPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                _isDragging = true;
                _dragStartPosition = e.GetPosition(this);
                e.Pointer.Capture(_mainBorder);

                if (_mainBorder != null)
                {
                    _mainBorder.Cursor = new Cursor(StandardCursorType.SizeAll);
                }

                // Pause countdown timer while dragging
                _countdownTimer?.Stop();
            }
        }

        private void OnBorderPointerMoved(object? sender, PointerEventArgs e)
        {
            if (_isDragging && _dragStartPosition.HasValue)
            {
                var currentPosition = e.GetPosition(this);
                var offset = currentPosition - _dragStartPosition.Value;

                Position = new PixelPoint(
                    Position.X + (int)offset.X,
                    Position.Y + (int)offset.Y
                );
            }
        }

        private void OnBorderPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                _dragStartPosition = null;
                e.Pointer.Capture(null);

                if (_mainBorder != null)
                {
                    _mainBorder.Cursor = new Cursor(StandardCursorType.Hand);
                }

                // Resume countdown timer after dragging
                if (!_responseSent && _remainingSeconds > 0)
                {
                    _countdownTimer?.Start();
                }

                // Save new position
                SavePosition();
            }
        }

        private void LoadPosition()
        {
            try
            {
                string configPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    CONFIG_FILE
                );

                if (File.Exists(configPath))
                {
                    string json = File.ReadAllText(configPath);
                    var config = JsonSerializer.Deserialize(json, NudgeJsonContext.Default.NotificationPositionConfig);

                    if (config != null && config.HasSavedPosition)
                    {
                        Position = new PixelPoint((int)config.X, (int)config.Y);
                        Console.WriteLine($"[CustomNotification] Loaded saved position: ({config.X}, {config.Y})");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CustomNotification] Failed to load position: {ex.Message}");
            }

            // Default position: bottom-right corner with margin (accounting for screen DPI scaling)
            var screen = Screens.Primary;
            if (screen != null)
            {
                double scaling = screen.Scaling;
                int x = screen.WorkingArea.Right - (int)(Width * scaling + 40 * scaling);
                int y = screen.WorkingArea.Bottom - (int)(Height * scaling + 40 * scaling);
                Position = new PixelPoint(x, y);
                Console.WriteLine($"[CustomNotification] Using default position: ({x}, {y}) scaling={scaling}");
            }
        }

        private void SavePosition()
        {
            try
            {
                var config = new NotificationPositionConfig
                {
                    X = Position.X,
                    Y = Position.Y,
                    HasSavedPosition = true
                };

                string configPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    CONFIG_FILE
                );

                string json = JsonSerializer.Serialize(config, NudgeJsonContext.Default.NotificationPositionConfig);
                File.WriteAllText(configPath, json);

                Console.WriteLine($"[CustomNotification] Saved position: ({config.X}, {config.Y})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CustomNotification] Failed to save position: {ex.Message}");
            }
        }

        public void ShowWithAnimation(Action<bool?> onResponse)
        {
            _onResponse = onResponse;
            _responseSent = false;
            _remainingSeconds = AUTO_DISMISS_SECONDS;

            // Set initial state for zoom + fade animation
            Opacity = 0;
            if (_mainBorder != null)
            {
                _mainBorder.RenderTransform = new ScaleTransform(0.8, 0.8);
            }

            Show();
            Focus();
            Activate();

            // Set active state and show halo ring
            SetActiveState(true);

            // Start countdown timer
            StartCountdownTimer();

            // Animate center zoom + fade in
            AnimateZoomIn();
        }

        private async void AnimateZoomIn()
        {
            int steps = 20;
            int delayMs = 15;

            double startScale = 0.8;
            double endScale = 1.0;
            double startOpacity = 0;
            double endOpacity = 1.0;

            for (int i = 0; i <= steps; i++)
            {
                double progress = (double)i / steps;

                // Ease-out cubic for smooth deceleration
                double easedProgress = 1 - Math.Pow(1 - progress, 3);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (_mainBorder != null)
                    {
                        double scale = startScale + (endScale - startScale) * easedProgress;
                        _mainBorder.RenderTransform = new ScaleTransform(scale, scale);
                    }
                    Opacity = startOpacity + (endOpacity - startOpacity) * easedProgress;
                });

                await System.Threading.Tasks.Task.Delay(delayMs);
            }
        }

        private void StartCountdownTimer()
        {
            _countdownTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };

            _countdownTimer.Tick += (s, e) =>
            {
                _remainingSeconds--;

                // Calculate progress (percentage of time remaining)
                double progress = (double)_remainingSeconds / AUTO_DISMISS_SECONDS;
                double sweepAngle = 360 * progress;

                // Update progress arc — calm neutral until last 5 seconds
                if (_progressArc != null)
                {
                    _progressArc.SweepAngle = sweepAngle;

                    if (_remainingSeconds <= 3)
                    {
                        _progressArc.Stroke = new SolidColorBrush(UrgentRed);
                    }
                    else if (_remainingSeconds <= 5)
                    {
                        _progressArc.Stroke = new SolidColorBrush(WarmAmber);
                    }
                    else
                    {
                        _progressArc.Stroke = new SolidColorBrush(CalmNeutral);
                    }
                }

                // Update countdown text — no "s" suffix
                if (_countdownText != null)
                {
                    _countdownText.Text = $"{_remainingSeconds}";

                    if (_remainingSeconds <= 3)
                    {
                        _countdownText.Foreground = new SolidColorBrush(UrgentRed);
                    }
                    else if (_remainingSeconds <= 5)
                    {
                        _countdownText.Foreground = new SolidColorBrush(WarmAmber);
                    }
                    else
                    {
                        _countdownText.Foreground = new SolidColorBrush(TimerText);
                    }
                }

                if (_remainingSeconds <= 0)
                {
                    _countdownTimer?.Stop();
                    AutoDismiss();
                }
            };

            _countdownTimer.Start();
        }

        private async void AutoDismiss()
        {
            if (_responseSent)
                return;

            _responseSent = true;
            Console.WriteLine("[CustomNotification] Auto-dismissed after timeout - no snapshot taken");

            // Stop timer
            _countdownTimer?.Stop();

            // Animate fade out
            await AnimateFadeOut();

            // Invoke callback with null to signal auto-dismiss (no snapshot taken)
            _onResponse?.Invoke(null);

            // Close window
            Close();
        }

        private async void HandleResponse(bool productive)
        {
            if (_responseSent)
                return;

            _responseSent = true;
            Console.WriteLine($"[CustomNotification] User responded: {(productive ? "YES" : "NO")}");

            // Stop countdown timer
            _countdownTimer?.Stop();

            // Animate fade out
            await AnimateFadeOut();

            // Invoke callback (snapshot will be taken)
            _onResponse?.Invoke(productive);

            // Close window
            Close();
        }

        private async System.Threading.Tasks.Task AnimateFadeOut()
        {
            int steps = 15;
            int delayMs = 12;

            double startScale = 1.0;
            double endScale = 0.8;
            double startOpacity = 1.0;
            double endOpacity = 0;

            for (int i = 0; i <= steps; i++)
            {
                double progress = (double)i / steps;

                // Ease-in cubic for smooth acceleration
                double easedProgress = progress * progress * progress;

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (_mainBorder != null)
                    {
                        double scale = startScale + (endScale - startScale) * easedProgress;
                        _mainBorder.RenderTransform = new ScaleTransform(scale, scale);
                    }
                    Opacity = startOpacity + (endOpacity - startOpacity) * easedProgress;
                });
                await System.Threading.Tasks.Task.Delay(delayMs);
            }
        }

        private void SetActiveState(bool active)
        {
            _isActive = active;

            if (_mainBorder == null) return;

            if (active)
            {
                // Focused: drop shadow + subtle blue glow on the card border — signals keyboard is active
                _mainBorder.BorderBrush = new SolidColorBrush(ActiveGlow);
                _mainBorder.BoxShadow = new BoxShadows(
                    new BoxShadow { Blur = 12, Spread = -2, OffsetX = 0, OffsetY = 4, Color = ShadowColor },
                    new[] { new BoxShadow { Blur = 10, Spread = 0, OffsetX = 0, OffsetY = 0, Color = GlowColor } }
                );
            }
            else
            {
                _mainBorder.BorderBrush = new SolidColorBrush(InactiveBorder);
                _mainBorder.BoxShadow = new BoxShadows(
                    new BoxShadow { Blur = 12, Spread = -2, OffsetX = 0, OffsetY = 4, Color = ShadowColor }
                );
            }
        }

        private void SetTimerPauseDisplay(bool paused)
        {
            if (_backgroundArc != null)  _backgroundArc.IsVisible  = !paused;
            if (_progressArc != null)    _progressArc.IsVisible     = !paused;
            if (_countdownText != null)  _countdownText.IsVisible   = !paused;
            if (_pauseIconView != null)  _pauseIconView.IsVisible   = paused;
        }

        protected override void OnClosed(EventArgs e)
        {
            // Clean up timer
            _countdownTimer?.Stop();
            _countdownTimer = null;

            base.OnClosed(e);
        }

        internal void AuditSetActive(bool active) => SetActiveState(active);
    }
}
