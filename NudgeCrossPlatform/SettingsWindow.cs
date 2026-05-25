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
        // Design tokens — identical to AnalyticsWindow
        private static readonly Color SurfaceColor  = Color.FromRgb(28, 28, 32);
        private static readonly Color CardColor      = Color.FromRgb(22, 22, 26);
        private static readonly Color ElevatedColor  = Color.FromRgb(34, 34, 40);
        private static readonly Color PrimaryBlue    = Color.FromRgb(88, 166, 255);
        private static readonly Color DangerRed      = Color.FromRgb(220, 50, 50);
        private static readonly Color TextPrimary    = Color.FromRgb(240, 240, 245);
        private static readonly Color TextSecondary  = Color.FromRgb(150, 150, 160);
        private static readonly Color TextMuted      = Color.FromRgb(100, 100, 110);
        private static readonly Color BorderSubtle   = Color.FromArgb(35, 255, 255, 255);
        private static readonly Color BorderNormal   = Color.FromArgb(55, 255, 255, 255);
        private static readonly Color SuccessGreen   = Color.FromRgb(60, 180, 75);
        private static readonly Color AccentHarvest  = Color.FromRgb(255, 165, 50);
        private static readonly Color AccentModel    = Color.FromRgb(88, 166, 255);

        private Border?    _harvestConfirmPanel;
        private Border?    _modelConfirmPanel;
        private TextBlock? _harvestStatus;
        private TextBlock? _modelStatus;
        private Button?    _harvestDeleteBtn;
        private Button?    _modelDeleteBtn;

        public SettingsWindow()
        {
            Title                  = "Nudge Settings";
            Width                  = 440;
            Height                 = 590;
            CanResize              = false;
            ShowInTaskbar          = false;
            WindowDecorations      = WindowDecorations.None;
            Background             = Brushes.Transparent;
            TransparencyLevelHint  = new[] { WindowTransparencyLevel.Transparent };
            Focusable              = true;

            Content = BuildRoot();
        }

        // ── Root shell — provides the visible window frame ────────────────────
        private Border BuildRoot()
        {
            var shell = new Border
            {
                Background      = new SolidColorBrush(CardColor),
                BorderBrush     = new SolidColorBrush(BorderNormal),
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(14),
                ClipToBounds    = true,
                Margin          = new Thickness(10),
                BoxShadow       = new BoxShadows(
                    new BoxShadow { Blur = 24, Spread = -4, OffsetX = 0, OffsetY = 8,
                                    Color = Color.FromArgb(120, 0, 0, 0) },
                    new[] { new BoxShadow { Blur = 6, Spread = 0, OffsetX = 0, OffsetY = 2,
                                            Color = Color.FromArgb(60, 0, 0, 0) } }
                )
            };

            var outer = new StackPanel { Spacing = 0 };
            outer.Children.Add(BuildTitleBar());
            outer.Children.Add(new Border
            {
                Height     = 1,
                Background = new SolidColorBrush(BorderSubtle)
            });
            outer.Children.Add(BuildScrollBody());

            shell.Child = outer;
            return shell;
        }

        // ── Custom title bar ──────────────────────────────────────────────────
        private Border BuildTitleBar()
        {
            var bar = new Border
            {
                Background = new SolidColorBrush(SurfaceColor),
                Padding    = new Thickness(16, 10, 8, 10),
                Cursor     = new Cursor(StandardCursorType.SizeAll)
            };

            // Drag to move
            bar.PointerPressed += (s, e) =>
            {
                if (e.GetCurrentPoint(bar).Properties.IsLeftButtonPressed)
                    BeginMoveDrag(e);
            };

            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,4,Auto")
            };

            // Gear icon
            var gear = new TextBlock
            {
                Text = "⚙",
                FontSize = 15,
                Foreground = new SolidColorBrush(TextSecondary),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            Grid.SetColumn(gear, 0);

            // Title
            var title = new TextBlock
            {
                Text = "Settings",
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(TextPrimary),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(title, 1);

            // Minimize button
            var minimizeBtn = MakeChromeButton("–", 15, () => WindowState = WindowState.Minimized, "Minimize");
            Grid.SetColumn(minimizeBtn, 2);

            // Close button
            var closeBtn = MakeChromeButton("✕", 13, Close, "Close", danger: true);
            Grid.SetColumn(closeBtn, 4);

            grid.Children.Add(gear);
            grid.Children.Add(title);
            grid.Children.Add(minimizeBtn);
            grid.Children.Add(closeBtn);
            bar.Child = grid;
            return bar;
        }

        private static Border MakeChromeButton(string symbol, double fontSize, Action action, string tooltip, bool danger = false)
        {
            var icon = new TextBlock
            {
                Text = symbol,
                FontSize = fontSize,
                Foreground = new SolidColorBrush(TextSecondary),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var btn = new Button
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Width = 30,
                Height = 30,
                Cursor = new Cursor(StandardCursorType.Hand),
                Content = icon
            };
            btn.Click += (_, _) => action();
            ToolTip.SetTip(btn, tooltip);

            var border = new Border
            {
                CornerRadius = new CornerRadius(6),
                Width = 30,
                Height = 30,
                Background = Brushes.Transparent,
                Child = btn
            };

            var hoverBg = danger
                ? new SolidColorBrush(Color.FromArgb(50, 220, 50, 50))
                : new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));
            var hoverFg = danger
                ? new SolidColorBrush(Color.FromRgb(255, 80, 80))
                : new SolidColorBrush(TextPrimary);

            border.PointerEntered += (_, _) =>
            {
                border.Background = hoverBg;
                icon.Foreground = hoverFg;
            };
            border.PointerExited += (_, _) =>
            {
                border.Background = Brushes.Transparent;
                icon.Foreground = new SolidColorBrush(TextSecondary);
            };

            return border;
        }

        // ── Scrollable body ───────────────────────────────────────────────────
        private ScrollViewer BuildScrollBody()
        {
            var stack = new StackPanel { Spacing = 16, Margin = new Thickness(16) };

            stack.Children.Add(BuildVersionChip());
            stack.Children.Add(BuildHarvestCard());
            stack.Children.Add(BuildModelCard());

            return new ScrollViewer
            {
                Content = stack,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
        }

        // ── Version chip ──────────────────────────────────────────────────────
        private static Border BuildVersionChip()
        {
            var pill = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(30, 88, 166, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(60, 88, 166, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(20),
                Padding = new Thickness(12, 5),
                HorizontalAlignment = HorizontalAlignment.Left
            };

            pill.Child = new TextBlock
            {
                Text = $"Nudge  v{Program.CurrentVersion}",
                FontSize = 11,
                FontWeight = FontWeight.Medium,
                Foreground = new SolidColorBrush(PrimaryBlue),
                LetterSpacing = 0.3
            };

            return pill;
        }

        // ── Section card helper ───────────────────────────────────────────────
        private static Border MakeCard(Color accentColor)
        {
            return new Border
            {
                Background = new SolidColorBrush(ElevatedColor),
                BorderBrush = new SolidColorBrush(BorderSubtle),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                ClipToBounds = true
            };
        }

        // ── Stat row (label | value) ──────────────────────────────────────────
        private static Grid MakeStatRow(string label, string value, bool dimValue = false)
        {
            var g = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                Margin = new Thickness(0, 2, 0, 2)
            };

            var lbl = new TextBlock
            {
                Text = label,
                FontSize = 12,
                Foreground = new SolidColorBrush(TextMuted),
                MinWidth = 110
            };
            Grid.SetColumn(lbl, 0);

            var val = new TextBlock
            {
                Text = value,
                FontSize = 12,
                FontWeight = FontWeight.Medium,
                Foreground = new SolidColorBrush(dimValue ? TextMuted : TextSecondary),
                TextAlignment = TextAlignment.Right,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetColumn(val, 1);

            g.Children.Add(lbl);
            g.Children.Add(val);
            return g;
        }

        // ── Harvest Data card ─────────────────────────────────────────────────
        private Border BuildHarvestCard()
        {
            string csvPath   = PlatformConfig.CsvPath;
            long fileSize    = File.Exists(csvPath) ? new FileInfo(csvPath).Length : 0;
            int sampleCount  = TrainerState.SampleCount;

            var card  = MakeCard(AccentHarvest);
            var inner = new StackPanel { Spacing = 0 };

            // Accent header strip
            inner.Children.Add(BuildSectionHeader("Harvest Data", "◈", AccentHarvest));

            // Stats body
            var body = new StackPanel { Spacing = 2, Margin = new Thickness(14, 10, 14, 14) };
            body.Children.Add(MakeStatRow("Samples collected", sampleCount.ToString("N0", CultureInfo.InvariantCulture)));
            body.Children.Add(MakeStatRow("File size", FormatFileSize(fileSize)));
            body.Children.Add(MakeStatRow("Location", csvPath, dimValue: true));

            body.Children.Add(new Border { Height = 10 });

            // Delete row
            _harvestDeleteBtn = MakeDangerButton("Delete Harvest Data", sampleCount > 0 || fileSize > 0);
            _harvestDeleteBtn.Click += OnHarvestDeleteClicked;
            body.Children.Add(_harvestDeleteBtn);

            _harvestConfirmPanel = MakeConfirmPanel(
                "Permanently delete all harvest data and reset counters?",
                DeleteHarvestData,
                () => { if (_harvestConfirmPanel != null) _harvestConfirmPanel.IsVisible = false; });
            _harvestConfirmPanel.IsVisible = false;
            body.Children.Add(_harvestConfirmPanel);

            _harvestStatus = new TextBlock { FontSize = 12, Foreground = new SolidColorBrush(TextMuted), IsVisible = false };
            body.Children.Add(_harvestStatus);

            inner.Children.Add(body);
            card.Child = inner;
            return card;
        }

        // ── ML Model card ─────────────────────────────────────────────────────
        private Border BuildModelCard()
        {
            string modelDir  = System.IO.Path.Combine(PlatformConfig.DataDirectory, "model");
            string modelPath = System.IO.Path.Combine(modelDir, "productivity_model.joblib");
            bool modelExists = File.Exists(modelPath);

            long dirSize = 0;
            if (Directory.Exists(modelDir))
                foreach (var f in Directory.GetFiles(modelDir))
                    dirSize += new FileInfo(f).Length;

            var card  = MakeCard(AccentModel);
            var inner = new StackPanel { Spacing = 0 };

            inner.Children.Add(BuildSectionHeader("ML Model", "◉", AccentModel));

            var body = new StackPanel { Spacing = 2, Margin = new Thickness(14, 10, 14, 14) };

            string statusText = TrainerState.LastTrained != DateTime.MinValue ? "Trained" : "Not trained";
            body.Children.Add(MakeStatRow("Status", statusText));

            if (TrainerState.LastTrained != DateTime.MinValue)
            {
                body.Children.Add(MakeStatRow("Trained on",
                    TrainerState.LastTrained.ToString("MMM dd yyyy, HH:mm", CultureInfo.InvariantCulture)));
                body.Children.Add(MakeStatRow("Training samples",
                    TrainerState.LastTrainedCount.ToString("N0", CultureInfo.InvariantCulture)));
                if (TrainerState.LastAccuracy >= 0)
                    body.Children.Add(MakeStatRow("Accuracy", $"{TrainerState.LastAccuracy:P1}"));
                if (TrainerState.ModelVersion > 0)
                    body.Children.Add(MakeStatRow("Version",
                        TrainerState.ModelVersion.ToString(CultureInfo.InvariantCulture)));
                if (TrainerState.Architecture.Length > 0)
                    body.Children.Add(MakeStatRow("Architecture", TrainerState.Architecture));
            }
            body.Children.Add(MakeStatRow("Size", FormatFileSize(dirSize)));

            body.Children.Add(new Border { Height = 10 });

            _modelDeleteBtn = MakeDangerButton("Delete Model", modelExists || dirSize > 0);
            _modelDeleteBtn.Click += OnModelDeleteClicked;
            body.Children.Add(_modelDeleteBtn);

            _modelConfirmPanel = MakeConfirmPanel(
                "Permanently delete the trained model? You'll need to retrain.",
                DeleteModel,
                () => { if (_modelConfirmPanel != null) _modelConfirmPanel.IsVisible = false; });
            _modelConfirmPanel.IsVisible = false;
            body.Children.Add(_modelConfirmPanel);

            _modelStatus = new TextBlock { FontSize = 12, Foreground = new SolidColorBrush(TextMuted), IsVisible = false };
            body.Children.Add(_modelStatus);

            inner.Children.Add(body);
            card.Child = inner;
            return card;
        }

        // ── Section header strip ──────────────────────────────────────────────
        private static Border BuildSectionHeader(string title, string icon, Color accent)
        {
            var strip = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(18, accent.R, accent.G, accent.B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(40, accent.R, accent.G, accent.B)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(14, 10, 14, 10)
            };

            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

            row.Children.Add(new TextBlock
            {
                Text = icon,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromArgb(200, accent.R, accent.G, accent.B)),
                VerticalAlignment = VerticalAlignment.Center
            });

            row.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(TextPrimary),
                VerticalAlignment = VerticalAlignment.Center
            });

            strip.Child = row;
            return strip;
        }

        // ── Danger button ─────────────────────────────────────────────────────
        private static Button MakeDangerButton(string label, bool enabled)
        {
            var btn = new Button
            {
                Content = label,
                FontSize = 12,
                FontWeight = FontWeight.Medium,
                Padding = new Thickness(14, 8),
                Background = new SolidColorBrush(Color.FromArgb(40, 220, 50, 50)),
                Foreground = new SolidColorBrush(Color.FromRgb(240, 100, 100)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(80, 220, 50, 50)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Cursor = new Cursor(StandardCursorType.Hand),
                IsEnabled = enabled
            };
            return btn;
        }

        // ── Confirm panel ─────────────────────────────────────────────────────
        private static Border MakeConfirmPanel(string message, Action confirm, Action cancel)
        {
            var panel = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(25, 220, 50, 50)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(60, 220, 50, 50)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 8, 0, 0)
            };

            var inner = new StackPanel { Spacing = 10 };

            inner.Children.Add(new TextBlock
            {
                Text = message,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(220, 140, 140))
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
                FontSize = 12,
                Padding = new Thickness(14, 6),
                Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                Foreground = new SolidColorBrush(TextSecondary),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(6),
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            cancelBtn.Click += (_, _) => cancel();

            var confirmBtn = new Button
            {
                Content = "Yes, Delete",
                FontSize = 12,
                FontWeight = FontWeight.Medium,
                Padding = new Thickness(14, 6),
                Background = new SolidColorBrush(Color.FromArgb(180, 220, 50, 50)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(6),
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            confirmBtn.Click += (_, _) => confirm();

            btnRow.Children.Add(cancelBtn);
            btnRow.Children.Add(confirmBtn);
            inner.Children.Add(btnRow);
            panel.Child = inner;
            return panel;
        }

        // ── File size helper ──────────────────────────────────────────────────
        private static string FormatFileSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes / (1024.0 * 1024.0):F1} MB";
        }

        // ── Event handlers ────────────────────────────────────────────────────
        private void OnHarvestDeleteClicked(object? sender, EventArgs e)
        {
            if (_harvestConfirmPanel!.IsVisible)
                DeleteHarvestData();
            else
            {
                _harvestConfirmPanel.IsVisible = true;
                _harvestStatus!.IsVisible = false;
            }
        }

        private void OnModelDeleteClicked(object? sender, EventArgs e)
        {
            if (_modelConfirmPanel!.IsVisible)
                DeleteModel();
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
                if (File.Exists(csvPath))  { deletedSize += new FileInfo(csvPath).Length;  File.Delete(csvPath); }
                if (File.Exists(actPath))  { deletedSize += new FileInfo(actPath).Length;  File.Delete(actPath); }

                TrainerState.SampleCount = 0;
                TrainerState.LastChecked = DateTime.Now;

                _harvestConfirmPanel!.IsVisible = false;
                _harvestDeleteBtn!.IsEnabled = false;
                ShowStatus(_harvestStatus!, $"Deleted ({FormatFileSize(deletedSize)})", SuccessGreen);
            }
            catch (Exception ex)
            {
                _harvestConfirmPanel!.IsVisible = false;
                ShowStatus(_harvestStatus!, $"Failed: {ex.Message}", DangerRed);
            }
        }

        private void DeleteModel()
        {
            try
            {
                string modelDir = System.IO.Path.Combine(PlatformConfig.DataDirectory, "model");
                long deletedSize = 0;
                if (Directory.Exists(modelDir))
                    foreach (var f in Directory.GetFiles(modelDir))
                    {
                        deletedSize += new FileInfo(f).Length;
                        File.Delete(f);
                    }

                TrainerState.LastTrained      = DateTime.MinValue;
                TrainerState.LastTrainedCount = 0;
                TrainerState.LastAccuracy     = -1f;
                TrainerState.ModelVersion     = 0;
                TrainerState.Architecture     = "";

                _modelConfirmPanel!.IsVisible = false;
                _modelDeleteBtn!.IsEnabled = false;
                ShowStatus(_modelStatus!, $"Deleted ({FormatFileSize(deletedSize)})", SuccessGreen);
            }
            catch (Exception ex)
            {
                _modelConfirmPanel!.IsVisible = false;
                ShowStatus(_modelStatus!, $"Failed: {ex.Message}", DangerRed);
            }
        }

        private static void ShowStatus(TextBlock tb, string text, Color color)
        {
            tb.Text       = text;
            tb.Foreground = new SolidColorBrush(color);
            tb.Margin     = new Thickness(0, 6, 0, 0);
            tb.IsVisible  = true;
        }
    }
}
