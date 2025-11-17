// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// TrayIcon Test - Cross-Platform Native Menu Example
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
//
// This demonstrates Avalonia's TrayIcon with NativeMenu working on both Windows and Linux.
// Based on the Nudge productivity tracker menu design.
//
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace TrayIconTest
{
    class Program
    {
        static TrayIcon? _trayIcon;
        static bool _waitingForResponse = false;
        static DateTime _nextSnapshotTime = DateTime.Now.AddMinutes(5);

        [STAThread]
        static void Main(string[] args)
        {
            Console.WriteLine("╔═══════════════════════════════════════════════════════╗");
            Console.WriteLine("║     TrayIcon Test - Native Menu Demo                 ║");
            Console.WriteLine("╚═══════════════════════════════════════════════════════╝");
            Console.WriteLine();
            Console.WriteLine("Testing cross-platform native menu with Avalonia...");
            Console.WriteLine();

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace()
                .AfterSetup(_ =>
                {
                    Console.WriteLine("✓ Avalonia initialized");
                    CreateTrayIcon();
                    StartSimulatedSnapshots();
                });
        }

        static void CreateTrayIcon()
        {
            try
            {
                _trayIcon = new TrayIcon
                {
                    Icon = CreateIcon(),
                    IsVisible = true,
                    ToolTipText = "Nudge Productivity Tracker",
                    Menu = CreateMenu()
                };

                // Register the tray icon with the Application
                if (Application.Current != null)
                {
                    var icons = new TrayIcons { _trayIcon };
                    TrayIcon.SetIcons(Application.Current, icons);
                    Console.WriteLine("✓ Tray icon created successfully");
                    Console.WriteLine("  Right-click the tray icon to see the menu");
                }
                else
                {
                    Console.WriteLine("✗ Application.Current is null");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Failed to create tray icon: {ex.Message}");
                Console.WriteLine($"  Stack trace: {ex.StackTrace}");
            }
        }

        static NativeMenu CreateMenu()
        {
            var menu = new NativeMenu();

            if (_waitingForResponse)
            {
                // When waiting for response, show YES/NO options
                var statusItem = new NativeMenuItem
                {
                    Header = "⏳ Were you productive?",
                    IsEnabled = false
                };
                menu.Add(statusItem);

                menu.Add(new NativeMenuItemSeparator());

                var yesItem = new NativeMenuItem { Header = "✓ Yes - Productive" };
                yesItem.Click += (s, e) =>
                {
                    Console.WriteLine("[USER] Clicked: YES - Productive");
                    HandleResponse(true);
                };
                menu.Add(yesItem);

                var noItem = new NativeMenuItem { Header = "✗ No - Not Productive" };
                noItem.Click += (s, e) =>
                {
                    Console.WriteLine("[USER] Clicked: NO - Not Productive");
                    HandleResponse(false);
                };
                menu.Add(noItem);
            }
            else
            {
                // Normal state - show status and next snapshot time
                var statusText = $"Next snapshot: {_nextSnapshotTime:HH:mm:ss}";
                var statusItem = new NativeMenuItem
                {
                    Header = statusText,
                    IsEnabled = false
                };
                menu.Add(statusItem);
            }

            menu.Add(new NativeMenuItemSeparator());

            // Quit option (always visible)
            var quitItem = new NativeMenuItem { Header = "Quit" };
            quitItem.Click += (s, e) =>
            {
                Console.WriteLine("[USER] Clicked: Quit");
                Quit();
            };
            menu.Add(quitItem);

            return menu;
        }

        static void HandleResponse(bool productive)
        {
            _waitingForResponse = false;
            Console.WriteLine($"✓ Response recorded: {(productive ? "PRODUCTIVE" : "NOT PRODUCTIVE")}");

            // Update next snapshot time
            _nextSnapshotTime = DateTime.Now.AddMinutes(5);

            // Refresh menu to show normal state
            UpdateMenu();
        }

        static void UpdateMenu()
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_trayIcon != null)
                {
                    _trayIcon.Menu = CreateMenu();
                    Console.WriteLine("  Menu updated");
                }
            });
        }

        static void StartSimulatedSnapshots()
        {
            // Simulate snapshot requests every 10 seconds for testing
            var timer = new System.Threading.Timer(_ =>
            {
                if (!_waitingForResponse)
                {
                    Console.WriteLine();
                    Console.WriteLine("📸 SNAPSHOT REQUEST (simulated)");
                    Console.WriteLine("  Right-click the tray icon to respond");
                    _waitingForResponse = true;
                    UpdateMenu();
                }
            }, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
        }

        static WindowIcon CreateIcon()
        {
            // Create a simple blue circle icon programmatically
            var renderBitmap = new RenderTargetBitmap(new PixelSize(32, 32), new Vector(96, 96));

            using (var ctx = renderBitmap.CreateDrawingContext())
            {
                // Clear with transparent background
                ctx.FillRectangle(Brushes.Transparent, new Rect(0, 0, 32, 32));

                // Draw blue circle (#5588FF - same as Nudge)
                var brush = new SolidColorBrush(Color.FromRgb(85, 136, 255));
                ctx.DrawGeometry(brush, null, new EllipseGeometry(new Rect(2, 2, 28, 28)));
            }

            // Save to memory stream
            var stream = new MemoryStream();
            renderBitmap.Save(stream);
            stream.Position = 0;
            return new WindowIcon(stream);
        }

        static void Quit()
        {
            Console.WriteLine("Shutting down...");
            if (_trayIcon != null)
            {
                _trayIcon.IsVisible = false;
                _trayIcon.Dispose();
            }
            Environment.Exit(0);
        }
    }

    public class App : Application
    {
        public override void Initialize()
        {
            // No XAML needed for headless tray app
        }
    }
}
