using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using System;

namespace NudgeTray
{
    public sealed class SettingsWindow : Window
    {
        private readonly CheckBox _v2Toggle;
        private readonly TextBlock _engineSummary;

        private static readonly Color BackgroundColor = Color.FromRgb(18, 18, 20);
        private static readonly Color SurfaceColor = Color.FromRgb(28, 28, 32);
        private static readonly Color CardColor = Color.FromRgb(25, 25, 28);
        private static readonly Color PrimaryBlue = Color.FromRgb(88, 166, 255);
        private static readonly Color TextPrimary = Color.FromRgb(240, 240, 245);
        private static readonly Color TextSecondary = Color.FromRgb(150, 150, 160);
        private static readonly Color BorderColor = Color.FromArgb(40, 255, 255, 255);

        public SettingsWindow()
        {
            Title = "Nudge Settings";
            Width = 420;
            Height = 420;
            CanResize = false;
            ShowInTaskbar = false;
            Background = new SolidColorBrush(BackgroundColor);

            _v2Toggle = new CheckBox
            {
                IsChecked = Program.CurrentHarvestEngine == HarvestEngineMode.V2,
                VerticalAlignment = VerticalAlignment.Center
            };

            _engineSummary = new TextBlock
            {
                Foreground = new SolidColorBrush(TextSecondary),
                FontSize = 12
            };

            _v2Toggle.IsCheckedChanged += (_, _) => UpdateEngineSummary();
            UpdateEngineSummary();

            Content = BuildContent();
        }

        private Border BuildContent()
        {
            var root = new Border
            {
                Background = new SolidColorBrush(CardColor),
                BorderBrush = new SolidColorBrush(BorderColor),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Margin = new Thickness(16),
                Padding = new Thickness(20)
            };

            var stack = new StackPanel { Spacing = 18 };

            stack.Children.Add(new TextBlock
            {
                Text = "Nudge Settings",
                FontSize = 20,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(TextPrimary)
            });

            stack.Children.Add(new Border
            {
                Background = new SolidColorBrush(SurfaceColor),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16),
                Child = new StackPanel
                {
                    Spacing = 6,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = $"Current Version: {Program.CurrentVersion}",
                            FontSize = 14,
                            FontWeight = FontWeight.Medium,
                            Foreground = new SolidColorBrush(TextPrimary)
                        },
                        new TextBlock
                        {
                            Text = "Nudge is split into four parts: the main app shell, Nudge Toast, Nudge Harvest, and Nudge AI.",
                            FontSize = 12,
                            TextWrapping = TextWrapping.Wrap,
                            Foreground = new SolidColorBrush(TextSecondary)
                        }
                    }
                }
            });

            var engineRow = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                VerticalAlignment = VerticalAlignment.Center
            };

            var engineCopy = new StackPanel { Spacing = 4 };
            engineCopy.Children.Add(new TextBlock
            {
                Text = "Nudge Harvest Engine",
                FontSize = 14,
                FontWeight = FontWeight.Medium,
                Foreground = new SolidColorBrush(TextPrimary)
            });
            engineCopy.Children.Add(new TextBlock
            {
                Text = "Enable the V2 wayland-first fused activity collector. Turn this off to fall back to the legacy V1 harvest engine.",
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(TextSecondary)
            });

            Grid.SetColumn(engineCopy, 0);
            Grid.SetColumn(_v2Toggle, 1);
            engineRow.Children.Add(engineCopy);
            engineRow.Children.Add(_v2Toggle);

            stack.Children.Add(new Border
            {
                Background = new SolidColorBrush(SurfaceColor),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16),
                Child = new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        engineRow,
                        _engineSummary
                    }
                }
            });

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 10
            };

            var closeButton = new Button
            {
                Content = "Close",
                Padding = new Thickness(16, 8),
                Background = Brushes.Transparent,
                BorderBrush = new SolidColorBrush(BorderColor),
                Foreground = new SolidColorBrush(TextPrimary),
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            closeButton.Click += (_, _) => Close();

            var applyButton = new Button
            {
                Content = "Apply",
                Padding = new Thickness(16, 8),
                Background = new SolidColorBrush(PrimaryBlue),
                Foreground = Brushes.White,
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            applyButton.Click += (_, _) =>
            {
                Program.SetHarvestEngine(_v2Toggle.IsChecked == true ? HarvestEngineMode.V2 : HarvestEngineMode.V1);
                Close();
            };

            buttons.Children.Add(closeButton);
            buttons.Children.Add(applyButton);
            stack.Children.Add(buttons);

            root.Child = stack;
            return root;
        }

        private void UpdateEngineSummary()
        {
            bool useV2 = _v2Toggle.IsChecked == true;
            _engineSummary.Text = useV2
                ? "Active mode after Apply: V2. Nudge Harvest will log the richer fused context and extensible feature payload."
                : "Active mode after Apply: V1. Nudge Harvest will keep the legacy app-plus-idle collector and legacy ML payload.";
        }
    }
}
