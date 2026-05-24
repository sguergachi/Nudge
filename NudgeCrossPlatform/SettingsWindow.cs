using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using System;
using System.Globalization;
using System.IO;

using NudgeCore;

namespace NudgeTray
{
    public sealed class SettingsWindow : Window
    {
        private static readonly Color BackgroundColor = Color.FromRgb(18, 18, 20);
        private static readonly Color SurfaceColor = Color.FromRgb(28, 28, 32);
        private static readonly Color CardColor = Color.FromRgb(25, 25, 28);
        private static readonly Color PrimaryBlue = Color.FromRgb(88, 166, 255);
        private static readonly Color DangerRed = Color.FromRgb(220, 50, 50);
        private static readonly Color TextPrimary = Color.FromRgb(240, 240, 245);
        private static readonly Color TextSecondary = Color.FromRgb(150, 150, 160);
        private static readonly Color TextMuted = Color.FromRgb(110, 110, 120);
        private static readonly Color BorderColor = Color.FromArgb(40, 255, 255, 255);
        private static readonly Color SuccessGreen = Color.FromRgb(60, 180, 75);

        private Border? _harvestConfirmPanel;
        private Border? _modelConfirmPanel;
        private TextBlock? _harvestStatus;
        private TextBlock? _modelStatus;
        private Button? _harvestDeleteBtn;
        private Button? _modelDeleteBtn;

        public SettingsWindow()
        {
            Title = "Nudge Settings";
            Width = 420;
            Height = 530;
            CanResize = false;
            ShowInTaskbar = false;
            Background = new SolidColorBrush(BackgroundColor);

            Content = BuildContent();
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes / (1024.0 * 1024.0):F1} MB";
        }

        private Border BuildContent()
        {
            var root = new Border
            {
                Background = new SolidColorBrush(CardColor),
                BorderBrush = new SolidColorBrush(BorderColor),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Margin = new Thickness(16),
                Padding = new Thickness(20)
            };

            var scroll = new ScrollViewer
            {
                Content = BuildStack(),
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };

            root.Child = scroll;
            return root;
        }

        private StackPanel BuildStack()
        {
            var stack = new StackPanel { Spacing = 16 };

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
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14),
                Child = new TextBlock
                {
                    Text = $"Version {Program.CurrentVersion}",
                    FontSize = 13,
                    Foreground = new SolidColorBrush(TextSecondary)
                }
            });

            stack.Children.Add(BuildHarvestCard());
            stack.Children.Add(BuildModelCard());

            var closeButton = new Button
            {
                Content = "Close",
                Padding = new Thickness(16, 8),
                Background = new SolidColorBrush(PrimaryBlue),
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Right,
                Cursor = new Cursor(StandardCursorType.Hand),
                Margin = new Thickness(0, 4, 0, 0)
            };
            closeButton.Click += (_, _) => Close();
            stack.Children.Add(closeButton);

            return stack;
        }

        private static TextBlock MakeStat(string label, string value)
        {
            return new TextBlock
            {
                Text = $"{label}  {value}",
                FontSize = 13,
                Foreground = new SolidColorBrush(TextSecondary)
            };
        }

        private Border BuildHarvestCard()
        {
            string csvPath = PlatformConfig.CsvPath;
            long fileSize = File.Exists(csvPath) ? new FileInfo(csvPath).Length : 0;
            int sampleCount = TrainerState.SampleCount;

            var card = new Border
            {
                Background = new SolidColorBrush(SurfaceColor),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14)
            };

            var inner = new StackPanel { Spacing = 10 };

            inner.Children.Add(new TextBlock
            {
                Text = "Harvest Data",
                FontSize = 15,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(TextPrimary)
            });

            var stats = new StackPanel { Spacing = 3 };
            stats.Children.Add(MakeStat("Samples collected", sampleCount.ToString("N0", CultureInfo.InvariantCulture)));
            stats.Children.Add(MakeStat("File size", FormatFileSize(fileSize)));
            stats.Children.Add(MakeStat("Location", csvPath));
            inner.Children.Add(stats);

            var deleteSection = new StackPanel { Spacing = 6 };

            _harvestDeleteBtn = new Button
            {
                Content = "Delete Harvest Data",
                Padding = new Thickness(12, 7),
                Background = new SolidColorBrush(DangerRed),
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Cursor = new Cursor(StandardCursorType.Hand),
                IsEnabled = (sampleCount > 0 || fileSize > 0)
            };
            _harvestDeleteBtn.Click += OnHarvestDeleteClicked;
            deleteSection.Children.Add(_harvestDeleteBtn);

            _harvestConfirmPanel = MakeConfirmPanel(
                "This will permanently delete all harvest data and reset counters. Continue?",
                DeleteHarvestData,
                () => { if (_harvestConfirmPanel != null) _harvestConfirmPanel.IsVisible = false; });
            _harvestConfirmPanel.IsVisible = false;
            deleteSection.Children.Add(_harvestConfirmPanel);

            _harvestStatus = new TextBlock
            {
                FontSize = 12,
                Foreground = new SolidColorBrush(TextMuted),
                IsVisible = false
            };
            deleteSection.Children.Add(_harvestStatus);

            inner.Children.Add(deleteSection);
            card.Child = inner;
            return card;
        }

        private Border BuildModelCard()
        {
            string modelDir = Path.Combine(PlatformConfig.DataDirectory, "model");
            string modelPath = Path.Combine(modelDir, "productivity_model.joblib");
            bool modelExists = File.Exists(modelPath);

            long dirSize = 0;
            if (Directory.Exists(modelDir))
            {
                foreach (var f in Directory.GetFiles(modelDir))
                    dirSize += new FileInfo(f).Length;
            }

            var card = new Border
            {
                Background = new SolidColorBrush(SurfaceColor),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14)
            };

            var inner = new StackPanel { Spacing = 10 };

            inner.Children.Add(new TextBlock
            {
                Text = "ML Model",
                FontSize = 15,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(TextPrimary)
            });

            var stats = new StackPanel { Spacing = 3 };
            string statusText = TrainerState.LastTrained != DateTime.MinValue ? "Trained" : "Not trained";
            stats.Children.Add(MakeStat("Status", statusText));
            if (TrainerState.LastTrained != DateTime.MinValue)
            {
                stats.Children.Add(MakeStat("Trained",
                    TrainerState.LastTrained.ToString("MMM dd, yyyy HH:mm", CultureInfo.InvariantCulture)));
                stats.Children.Add(MakeStat("Training samples", TrainerState.LastTrainedCount.ToString("N0", CultureInfo.InvariantCulture)));
                if (TrainerState.LastAccuracy >= 0)
                    stats.Children.Add(MakeStat("Accuracy", $"{TrainerState.LastAccuracy:P1}"));
                if (TrainerState.ModelVersion > 0)
                    stats.Children.Add(MakeStat("Version", TrainerState.ModelVersion.ToString(CultureInfo.InvariantCulture)));
                if (TrainerState.Architecture.Length > 0)
                    stats.Children.Add(MakeStat("Architecture", TrainerState.Architecture));
            }
            stats.Children.Add(MakeStat("Size", FormatFileSize(dirSize)));
            inner.Children.Add(stats);

            var deleteSection = new StackPanel { Spacing = 6 };

            _modelDeleteBtn = new Button
            {
                Content = "Delete Model",
                Padding = new Thickness(12, 7),
                Background = new SolidColorBrush(DangerRed),
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Cursor = new Cursor(StandardCursorType.Hand),
                IsEnabled = modelExists || dirSize > 0
            };
            _modelDeleteBtn.Click += OnModelDeleteClicked;
            deleteSection.Children.Add(_modelDeleteBtn);

            _modelConfirmPanel = MakeConfirmPanel(
                "This will permanently delete the trained model. You'll need to retrain. Continue?",
                DeleteModel,
                () => { if (_modelConfirmPanel != null) _modelConfirmPanel.IsVisible = false; });
            _modelConfirmPanel.IsVisible = false;
            deleteSection.Children.Add(_modelConfirmPanel);

            _modelStatus = new TextBlock
            {
                FontSize = 12,
                Foreground = new SolidColorBrush(TextMuted),
                IsVisible = false
            };
            deleteSection.Children.Add(_modelStatus);

            inner.Children.Add(deleteSection);
            card.Child = inner;
            return card;
        }

        private static Border MakeConfirmPanel(string message, Action confirm, Action cancel)
        {
            var panel = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(18, 220, 50, 50)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10)
            };

            var inner = new StackPanel { Spacing = 8 };

            inner.Children.Add(new TextBlock
            {
                Text = message,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(TextSecondary)
            });

            var btnRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var cancelBtn = new Button
            {
                Content = "Cancel",
                Padding = new Thickness(12, 5),
                Background = new SolidColorBrush(Color.FromRgb(45, 45, 50)),
                Foreground = new SolidColorBrush(TextPrimary),
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            cancelBtn.Click += (_, _) => cancel();

            var confirmBtn = new Button
            {
                Content = "Yes, Delete",
                Padding = new Thickness(12, 5),
                Background = new SolidColorBrush(DangerRed),
                Foreground = Brushes.White,
                Cursor = new Cursor(StandardCursorType.Hand),
                IsEnabled = true
            };
            confirmBtn.Click += (_, _) => confirm();

            btnRow.Children.Add(cancelBtn);
            btnRow.Children.Add(confirmBtn);
            inner.Children.Add(btnRow);
            panel.Child = inner;
            return panel;
        }

        private void OnHarvestDeleteClicked(object? sender, EventArgs e)
        {
            if (_harvestConfirmPanel!.IsVisible)
            {
                DeleteHarvestData();
            }
            else
            {
                _harvestConfirmPanel.IsVisible = true;
                _harvestStatus!.IsVisible = false;
            }
        }

        private void OnModelDeleteClicked(object? sender, EventArgs e)
        {
            if (_modelConfirmPanel!.IsVisible)
            {
                DeleteModel();
            }
            else
            {
                _modelConfirmPanel.IsVisible = true;
                _modelStatus!.IsVisible = false;
            }
        }

        private void DeleteHarvestData()
        {
            try
            {
                string csvPath = PlatformConfig.CsvPath;
                string actPath = PlatformConfig.ActivityLogPath;

                long deletedSize = 0;
                if (File.Exists(csvPath))
                {
                    deletedSize += new FileInfo(csvPath).Length;
                    File.Delete(csvPath);
                }
                if (File.Exists(actPath))
                {
                    deletedSize += new FileInfo(actPath).Length;
                    File.Delete(actPath);
                }

                TrainerState.SampleCount = 0;
                TrainerState.LastChecked = DateTime.Now;

                _harvestConfirmPanel!.IsVisible = false;
                _harvestDeleteBtn!.IsEnabled = false;
                _harvestStatus!.Text = $"Deleted ({FormatFileSize(deletedSize)})";
                _harvestStatus.Foreground = new SolidColorBrush(SuccessGreen);
                _harvestStatus.IsVisible = true;
            }
            catch (Exception ex)
            {
                _harvestConfirmPanel!.IsVisible = false;
                _harvestStatus!.Text = $"Failed: {ex.Message}";
                _harvestStatus.Foreground = new SolidColorBrush(DangerRed);
                _harvestStatus.IsVisible = true;
            }
        }

        private void DeleteModel()
        {
            try
            {
                string modelDir = Path.Combine(PlatformConfig.DataDirectory, "model");

                long deletedSize = 0;
                if (Directory.Exists(modelDir))
                {
                    foreach (var f in Directory.GetFiles(modelDir))
                    {
                        deletedSize += new FileInfo(f).Length;
                        File.Delete(f);
                    }
                }

                TrainerState.LastTrained = DateTime.MinValue;
                TrainerState.LastTrainedCount = 0;
                TrainerState.LastAccuracy = -1f;
                TrainerState.ModelVersion = 0;
                TrainerState.Architecture = "";

                _modelConfirmPanel!.IsVisible = false;
                _modelDeleteBtn!.IsEnabled = false;
                _modelStatus!.Text = $"Deleted ({FormatFileSize(deletedSize)})";
                _modelStatus.Foreground = new SolidColorBrush(SuccessGreen);
                _modelStatus.IsVisible = true;
            }
            catch (Exception ex)
            {
                _modelConfirmPanel!.IsVisible = false;
                _modelStatus!.Text = $"Failed: {ex.Message}";
                _modelStatus.Foreground = new SolidColorBrush(DangerRed);
                _modelStatus.IsVisible = true;
            }
        }
    }
}
