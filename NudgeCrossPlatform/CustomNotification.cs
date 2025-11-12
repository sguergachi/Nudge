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
            Width = 380;
            Height = 180;
            CanResize = false;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            SystemDecorations = SystemDecorations.None;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Blur };
            Background = Brushes.Transparent;
            Topmost = true;

            // Enable keyboard focus
            Focusable = true;
        }

        private void InitializeContent()
        {
            // Main container - Linear-inspired clean design with blur effect
            _mainBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(250, 255, 255, 255)), // Almost opaque white
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(20),
                BoxShadow = new BoxShadows(
                    new BoxShadow
                    {
                        Blur = 40,
                        Spread = 0,
                        OffsetX = 0,
                        OffsetY = 8,
                        Color = Color.FromArgb(30, 0, 0, 0) // Subtle shadow
                    }
                ),
                BorderBrush = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)),
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

            // Title with subtle accent
            var titleText = new TextBlock
            {
                Text = "Productivity Check",
                FontSize = 16,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(30, 30, 40)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4)
            };

            // Message with lighter weight
            var messageText = new TextBlock
            {
                Text = "Were you productive during the last interval?",
                FontSize = 13,
                FontWeight = FontWeight.Normal,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 120)),
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8)
            };

            // Buttons Container
            var buttonsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            // YES Button - Linear's purple accent
            var yesButton = CreateStyledButton(
                "Yes",
                "⌘Y",
                Color.FromRgb(95, 90, 255),
                Color.FromRgb(115, 110, 255),
                () => HandleResponse(true)
            );

            // NO Button - subtle gray
            var noButton = CreateStyledButton(
                "No",
                "⌘N",
                Color.FromRgb(240, 240, 245),
                Color.FromRgb(230, 230, 240),
                () => HandleResponse(false),
                false // Not primary
            );

            buttonsPanel.Children.Add(yesButton);
            buttonsPanel.Children.Add(noButton);

            // Drag hint - very subtle
            var dragHint = new TextBlock
            {
                Text = "Drag to move",
                FontSize = 10,
                FontWeight = FontWeight.Normal,
                Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 175)),
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 8, 0, 0)
            };

            // Add all elements to stack
            stackPanel.Children.Add(titleText);
            stackPanel.Children.Add(messageText);
            stackPanel.Children.Add(buttonsPanel);
            stackPanel.Children.Add(dragHint);

            _mainBorder.Child = stackPanel;
            Content = _mainBorder;
        }

        private StackPanel CreateStyledButton(string mainText, string shortcutText, Color baseColor, Color hoverColor, Action onClick, bool isPrimary = true)
        {
            // Create border for rounded corners - Linear style
            var border = new Border
            {
                MinWidth = 140,
                Height = 44,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(baseColor),
                BorderBrush = isPrimary
                    ? Brushes.Transparent
                    : new SolidColorBrush(Color.FromArgb(30, 0, 0, 0)),
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
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var mainTextBlock = new TextBlock
            {
                Text = mainText,
                FontSize = 13,
                FontWeight = FontWeight.Medium,
                Foreground = isPrimary
                    ? Brushes.White
                    : new SolidColorBrush(Color.FromRgb(60, 60, 75)),
                VerticalAlignment = VerticalAlignment.Center
            };

            var shortcutTextBlock = new TextBlock
            {
                Text = shortcutText,
                FontSize = 11,
                FontWeight = FontWeight.Normal,
                Foreground = isPrimary
                    ? new SolidColorBrush(Color.FromArgb(150, 255, 255, 255))
                    : new SolidColorBrush(Color.FromRgb(140, 140, 160)),
                VerticalAlignment = VerticalAlignment.Center
            };

            buttonContent.Children.Add(mainTextBlock);
            buttonContent.Children.Add(shortcutTextBlock);
            button.Content = buttonContent;
            border.Child = button;

            // Hover effects - subtle like Linear
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

            button.Click += (s, e) => onClick();

            return new StackPanel { Children = { border } };
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
            base.OnClosed(e);
        }
    }
}
