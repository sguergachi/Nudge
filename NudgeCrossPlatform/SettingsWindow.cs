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
            Height = 320;
            CanResize = false;
            ShowInTaskbar = false;
            Background = new SolidColorBrush(BackgroundColor);

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

            var closeButton = new Button
            {
                Content = "Close",
                Padding = new Thickness(16, 8),
                Background = new SolidColorBrush(PrimaryBlue),
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Right,
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            closeButton.Click += (_, _) => Close();

            stack.Children.Add(closeButton);

            root.Child = stack;
            return root;
        }

    }
}
