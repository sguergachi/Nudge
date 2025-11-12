// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// Custom Notification Window - Cross-Platform Animated Notification
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
//
// Features:
// - Smooth center zoom + fade animations
// - Halo selection ring when active
// - Modern, left-aligned UI
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
        private Border? _haloRing;
        private bool _isActive = false;

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
            Width = 340;
            Height = 140;
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
            // Halo ring - outer glow effect when active
            _haloRing = new Border
            {
                Background = Brushes.Transparent,
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(4),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0, 88, 166, 255)), // Initially transparent
                BorderThickness = new Thickness(2),
                BoxShadow = new BoxShadows(
                    new BoxShadow
                    {
                        Blur = 0,
                        Spread = 0,
                        OffsetX = 0,
                        OffsetY = 0,
                        Color = Color.FromArgb(0, 88, 166, 255) // Initially transparent
                    }
                )
            };

            // Main container - Fluent Design System specifications
            _mainBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(245, 18, 18, 20)), // Almost opaque black with acrylic effect
                CornerRadius = new CornerRadius(8), // Fluent: 8px for top-level containers
                Padding = new Thickness(16), // Fluent: 16px standard spacing
                BoxShadow = new BoxShadows(
                    new BoxShadow
                    {
                        Blur = 32,
                        Spread = 0,
                        OffsetX = 0,
                        OffsetY = 8,
                        Color = Color.FromArgb(60, 0, 0, 0) // Elevated shadow
                    }
                ),
                BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative) // Center for scaling
            };

            // Add drag functionality to the border
            _mainBorder.PointerPressed += OnBorderPointerPressed;
            _mainBorder.PointerMoved += OnBorderPointerMoved;
            _mainBorder.PointerReleased += OnBorderPointerReleased;
            _mainBorder.Cursor = new Cursor(StandardCursorType.Hand);

            var stackPanel = new StackPanel
            {
                Spacing = 8, // Fluent: 8px spacing
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            // Title - clean and minimal, left-aligned
            var titleText = new TextBlock
            {
                Text = "Productivity Check",
                FontSize = 14,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(240, 240, 245)),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 4) // Fluent: 4px base unit
            };

            // Message - subtle, left-aligned
            var messageText = new TextBlock
            {
                Text = "Were you productive?",
                FontSize = 12,
                FontWeight = FontWeight.Normal,
                Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 160)),
                TextAlignment = TextAlignment.Left,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 8) // Fluent: 8px spacing
            };

            // Buttons Container - left-aligned
            var buttonsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8, // Fluent: 8px spacing between buttons
                HorizontalAlignment = HorizontalAlignment.Left
            };

            // YES Button - vibrant accent
            var yesButton = CreateStyledButton(
                "Yes",
                "⌘Y",
                Color.FromRgb(88, 166, 255),
                Color.FromRgb(108, 186, 255),
                () => HandleResponse(true)
            );

            // NO Button - subtle gray
            var noButton = CreateStyledButton(
                "No",
                "⌘N",
                Color.FromRgb(45, 45, 50),
                Color.FromRgb(55, 55, 60),
                () => HandleResponse(false),
                false
            );

            buttonsPanel.Children.Add(yesButton);
            buttonsPanel.Children.Add(noButton);

            // Add all elements to stack
            stackPanel.Children.Add(titleText);
            stackPanel.Children.Add(messageText);
            stackPanel.Children.Add(buttonsPanel);

            _mainBorder.Child = stackPanel;
            _haloRing!.Child = _mainBorder;
            Content = _haloRing;
        }

        private StackPanel CreateStyledButton(string mainText, string shortcutText, Color baseColor, Color hoverColor, Action onClick, bool isPrimary = true)
        {
            // Create border for rounded corners - Fluent Design System
            var border = new Border
            {
                MinWidth = 120,
                Height = 32,
                CornerRadius = new CornerRadius(4), // Fluent: 4px for in-page elements (buttons)
                Background = new SolidColorBrush(baseColor),
                BorderBrush = isPrimary
                    ? Brushes.Transparent
                    : new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Cursor = new Cursor(StandardCursorType.Hand),
                Padding = new Thickness(12, 0, 12, 0) // Fluent: 12px horizontal padding
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
                Spacing = 8, // Fluent: 8px spacing between text elements
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var mainTextBlock = new TextBlock
            {
                Text = mainText,
                FontSize = 12,
                FontWeight = FontWeight.Medium,
                Foreground = isPrimary
                    ? Brushes.White
                    : new SolidColorBrush(Color.FromRgb(200, 200, 210)),
                VerticalAlignment = VerticalAlignment.Center
            };

            var shortcutTextBlock = new TextBlock
            {
                Text = shortcutText,
                FontSize = 10,
                FontWeight = FontWeight.Normal,
                Foreground = isPrimary
                    ? new SolidColorBrush(Color.FromArgb(150, 255, 255, 255))
                    : new SolidColorBrush(Color.FromRgb(130, 130, 140)),
                VerticalAlignment = VerticalAlignment.Center
            };

            buttonContent.Children.Add(mainTextBlock);
            buttonContent.Children.Add(shortcutTextBlock);
            button.Content = buttonContent;
            border.Child = button;

            // Hover effects - subtle
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

            if (_haloRing != null)
            {
                if (active)
                {
                    // Show halo ring with glow effect
                    _haloRing.BorderBrush = new SolidColorBrush(Color.FromArgb(180, 88, 166, 255));
                    _haloRing.BoxShadow = new BoxShadows(
                        new BoxShadow
                        {
                            Blur = 20,
                            Spread = 2,
                            OffsetX = 0,
                            OffsetY = 0,
                            Color = Color.FromArgb(120, 88, 166, 255)
                        }
                    );
                }
                else
                {
                    // Hide halo ring
                    _haloRing.BorderBrush = new SolidColorBrush(Color.FromArgb(0, 88, 166, 255));
                    _haloRing.BoxShadow = new BoxShadows(
                        new BoxShadow
                        {
                            Blur = 0,
                            Spread = 0,
                            OffsetX = 0,
                            OffsetY = 0,
                            Color = Color.FromArgb(0, 88, 166, 255)
                        }
                    );
                }
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
        }
    }
}
