// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// Custom Notification Window - Cross-Platform Animated Notification
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
//
// Features:
// - Smooth slide-in animation
// - Modern, well-designed UI
// - Keyboard shortcuts: Ctrl+Shift+Y (YES), Ctrl+Shift+N (NO)
// - Draggable window with position persistence
// - Cross-platform support (Windows, Linux, macOS)
//
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
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
    public class CustomNotificationWindow : Window
    {
        private const string CONFIG_FILE = "nudge-notification-config.json";
        private Point? _dragStartPosition;
        private bool _isDragging = false;
        private Action<bool>? _onResponse;
        private DispatcherTimer? _pulseTimer;
        private bool _pulseDirection = true;
        private Border? _mainBorder;

        // Notification configuration
        private class NotificationConfig
        {
            public double X { get; set; }
            public double Y { get; set; }
            public bool HasSavedPosition { get; set; }
        }

        public CustomNotificationWindow()
        {
            InitializeWindow();
            InitializeContent();
            LoadPosition();
            SetupKeyboardShortcuts();
        }

        private void InitializeWindow()
        {
            Width = 420;
            Height = 220;
            CanResize = false;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            SystemDecorations = SystemDecorations.None;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            Background = Brushes.Transparent;
            Topmost = true;

            // Enable keyboard focus
            Focusable = true;
        }

        private void InitializeContent()
        {
            // Main container with rounded corners and shadow effect
            _mainBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(28, 28, 35)),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(24),
                BoxShadow = new BoxShadows(
                    new BoxShadow
                    {
                        Blur = 30,
                        Spread = 0,
                        OffsetX = 0,
                        OffsetY = 10,
                        Color = Color.FromArgb(100, 0, 0, 0)
                    }
                ),
                BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 75)),
                BorderThickness = new Thickness(1)
            };

            // Add drag functionality to the border
            _mainBorder.PointerPressed += OnBorderPointerPressed;
            _mainBorder.PointerMoved += OnBorderPointerMoved;
            _mainBorder.PointerReleased += OnBorderPointerReleased;
            _mainBorder.Cursor = new Cursor(StandardCursorType.Hand);

            var stackPanel = new StackPanel
            {
                Spacing = 16,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            // Icon and Title Row
            var headerPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            // Icon (pulsing circle)
            var iconBorder = new Border
            {
                Width = 40,
                Height = 40,
                CornerRadius = new CornerRadius(20),
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Color.FromRgb(85, 136, 255), 0),
                        new GradientStop(Color.FromRgb(120, 160, 255), 1)
                    }
                },
                Child = new TextBlock
                {
                    Text = "📊",
                    FontSize = 24,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

            var titleText = new TextBlock
            {
                Text = "Nudge - Productivity Check",
                FontSize = 20,
                FontWeight = FontWeight.Bold,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            };

            headerPanel.Children.Add(iconBorder);
            headerPanel.Children.Add(titleText);

            // Message
            var messageText = new TextBlock
            {
                Text = "Were you productive during the last interval?",
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 210)),
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            // Buttons Container
            var buttonsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 8, 0, 0)
            };

            // YES Button
            var yesButton = CreateStyledButton(
                "YES - Productive",
                "Ctrl+Shift+Y",
                Color.FromRgb(40, 180, 100),
                Color.FromRgb(50, 200, 120),
                () => HandleResponse(true)
            );

            // NO Button
            var noButton = CreateStyledButton(
                "NO - Not Productive",
                "Ctrl+Shift+N",
                Color.FromRgb(220, 60, 80),
                Color.FromRgb(240, 80, 100),
                () => HandleResponse(false)
            );

            buttonsPanel.Children.Add(yesButton);
            buttonsPanel.Children.Add(noButton);

            // Drag hint
            var dragHint = new TextBlock
            {
                Text = "💡 Drag to reposition • Stays on top",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(130, 130, 150)),
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0),
                Opacity = 0.7
            };

            // Add all elements to stack
            stackPanel.Children.Add(headerPanel);
            stackPanel.Children.Add(messageText);
            stackPanel.Children.Add(buttonsPanel);
            stackPanel.Children.Add(dragHint);

            _mainBorder.Child = stackPanel;
            Content = _mainBorder;

            // Start pulsing animation
            StartPulseAnimation(iconBorder);
        }

        private StackPanel CreateStyledButton(string mainText, string shortcutText, Color baseColor, Color hoverColor, Action onClick)
        {
            // Create border for rounded corners
            var border = new Border
            {
                Width = 180,
                Height = 60,
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(baseColor),
                BorderBrush = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Cursor = new Cursor(StandardCursorType.Hand)
            };

            var button = new Button
            {
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = new Cursor(StandardCursorType.Hand),
                Padding = new Thickness(0)
            };

            var buttonContent = new StackPanel
            {
                Spacing = 4,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var mainTextBlock = new TextBlock
            {
                Text = mainText,
                FontSize = 14,
                FontWeight = FontWeight.SemiBold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var shortcutTextBlock = new TextBlock
            {
                Text = shortcutText,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            buttonContent.Children.Add(mainTextBlock);
            buttonContent.Children.Add(shortcutTextBlock);
            button.Content = buttonContent;
            border.Child = button;

            // Hover effects
            border.PointerEntered += (s, e) =>
            {
                border.Background = new SolidColorBrush(hoverColor);
                border.RenderTransform = new ScaleTransform(1.05, 1.05);
            };

            border.PointerExited += (s, e) =>
            {
                border.Background = new SolidColorBrush(baseColor);
                border.RenderTransform = new ScaleTransform(1.0, 1.0);
            };

            button.Click += (s, e) => onClick();

            return new StackPanel { Children = { border } };
        }

        private void StartPulseAnimation(Border iconBorder)
        {
            _pulseTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };

            double minOpacity = 0.7;
            double maxOpacity = 1.0;
            double step = 0.03;

            _pulseTimer.Tick += (s, e) =>
            {
                if (_pulseDirection)
                {
                    iconBorder.Opacity += step;
                    if (iconBorder.Opacity >= maxOpacity)
                    {
                        iconBorder.Opacity = maxOpacity;
                        _pulseDirection = false;
                    }
                }
                else
                {
                    iconBorder.Opacity -= step;
                    if (iconBorder.Opacity <= minOpacity)
                    {
                        iconBorder.Opacity = minOpacity;
                        _pulseDirection = true;
                    }
                }
            };

            _pulseTimer.Start();
        }

        private void SetupKeyboardShortcuts()
        {
            KeyDown += (s, e) =>
            {
                // Check for Ctrl+Shift+Y (YES)
                if (e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift) && e.Key == Key.Y)
                {
                    Console.WriteLine("[CustomNotification] Keyboard shortcut: Ctrl+Shift+Y pressed");
                    HandleResponse(true);
                    e.Handled = true;
                }
                // Check for Ctrl+Shift+N (NO)
                else if (e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift) && e.Key == Key.N)
                {
                    Console.WriteLine("[CustomNotification] Keyboard shortcut: Ctrl+Shift+N pressed");
                    HandleResponse(false);
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

                // Save new position
                SavePosition();
            }
        }

        private void LoadPosition()
        {
            try
            {
                string configPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    CONFIG_FILE
                );

                if (File.Exists(configPath))
                {
                    string json = File.ReadAllText(configPath);
                    var config = JsonSerializer.Deserialize<NotificationConfig>(json);

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

            // Default position: bottom-right corner with margin
            var screen = Screens.Primary;
            if (screen != null)
            {
                int x = (int)(screen.WorkingArea.Width - Width - 40);
                int y = (int)(screen.WorkingArea.Height - Height - 40);
                Position = new PixelPoint(x, y);
                Console.WriteLine($"[CustomNotification] Using default position: ({x}, {y})");
            }
        }

        private void SavePosition()
        {
            try
            {
                var config = new NotificationConfig
                {
                    X = Position.X,
                    Y = Position.Y,
                    HasSavedPosition = true
                };

                string configPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    CONFIG_FILE
                );

                string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(configPath, json);

                Console.WriteLine($"[CustomNotification] Saved position: ({config.X}, {config.Y})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CustomNotification] Failed to save position: {ex.Message}");
            }
        }

        public void ShowWithAnimation(Action<bool> onResponse)
        {
            _onResponse = onResponse;

            // Set initial position for slide-in animation (start off-screen to the right)
            var targetPosition = Position;
            Position = new PixelPoint(targetPosition.X + 400, targetPosition.Y);
            Opacity = 0;

            Show();
            Focus();
            Activate();

            // Animate slide-in
            AnimateSlideIn(targetPosition);
        }

        private async void AnimateSlideIn(PixelPoint targetPosition)
        {
            int steps = 30;
            int delayMs = 10;

            double startX = Position.X;
            double startOpacity = 0;

            for (int i = 0; i <= steps; i++)
            {
                double progress = (double)i / steps;

                // Ease out cubic for smooth deceleration
                double easedProgress = 1 - Math.Pow(1 - progress, 3);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Position = new PixelPoint(
                        (int)(startX + (targetPosition.X - startX) * easedProgress),
                        targetPosition.Y
                    );
                    Opacity = startOpacity + (1.0 - startOpacity) * easedProgress;
                });

                await System.Threading.Tasks.Task.Delay(delayMs);
            }
        }

        private async void HandleResponse(bool productive)
        {
            Console.WriteLine($"[CustomNotification] User responded: {(productive ? "YES" : "NO")}");

            // Stop pulse animation
            _pulseTimer?.Stop();

            // Animate fade out
            await AnimateFadeOut();

            // Invoke callback
            _onResponse?.Invoke(productive);

            // Close window
            Close();
        }

        private async System.Threading.Tasks.Task AnimateFadeOut()
        {
            int steps = 15;
            int delayMs = 10;

            for (int i = 0; i <= steps; i++)
            {
                double progress = (double)i / steps;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Opacity = 1.0 - progress;
                });
                await System.Threading.Tasks.Task.Delay(delayMs);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _pulseTimer?.Stop();
            base.OnClosed(e);
        }
    }
}
