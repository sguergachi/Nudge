#!/usr/bin/env dotnet run
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// Nudge Notify - Productivity Response Tool
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
//
// Send productivity responses to Nudge tracker via UDP.
// Built with obsessive attention to detail.
//
// Usage:
//   nudge-notify <response>
//
// Responses:
//   YES  - I was productive during that time
//   NO   - I was not productive during that time
//
// Examples:
//   nudge-notify YES
//   nudge-notify NO
//
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

class NudgeNotify
{
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // VERSION & CONSTANTS
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    const string VERSION = "1.0.0";
    const string HOST = "127.0.0.1";
    const int PORT = 45001;
    const int TIMEOUT_MS = 5000;

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // ANSI COLORS - Professional terminal output
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    static class Color
    {
        public const string RESET = "\u001b[0m";
        public const string BOLD = "\u001b[1m";
        public const string DIM = "\u001b[2m";

        public const string RED = "\u001b[31m";
        public const string GREEN = "\u001b[32m";
        public const string YELLOW = "\u001b[33m";
        public const string CYAN = "\u001b[36m";

        public const string BRED = "\u001b[1;31m";      // Bold red
        public const string BGREEN = "\u001b[1;32m";    // Bold green
        public const string BYELLOW = "\u001b[1;33m";   // Bold yellow
        public const string BCYAN = "\u001b[1;36m";     // Bold cyan
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // MAIN - Send response with professional validation and feedback
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    static int Main(string[] args)
    {
        // Handle special flags
        if (args.Length > 0)
        {
            if (args[0] == "--help" || args[0] == "-h")
            {
                ShowHelp();
                return 0;
            }
            if (args[0] == "--version" || args[0] == "-v")
            {
                Console.WriteLine($"Nudge Notify v{VERSION}");
                return 0;
            }
        }

        // Validate arguments
        if (args.Length == 0)
        {
            Error("Missing response argument");
            Console.WriteLine();
            Console.WriteLine($"{Color.DIM}Run with {Color.BCYAN}--help{Color.DIM} for usage information{Color.RESET}");
            return 1;
        }

        // Parse and validate response
        string response = args[0].ToUpper();
        if (response != "YES" && response != "NO")
        {
            Error($"Invalid response: '{args[0]}'");
            Error("Response must be YES or NO");
            Console.WriteLine();
            Console.WriteLine($"{Color.BOLD}Valid responses:{Color.RESET}");
            Console.WriteLine($"  {Color.BGREEN}YES{Color.RESET} - I was productive");
            Console.WriteLine($"  {Color.YELLOW}NO{Color.RESET}  - I was not productive");
            return 1;
        }

        // Send response
        return SendResponse(response);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // NETWORK - Send UDP packet with detailed error handling
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    static int SendResponse(string response)
    {
        try
        {
            Info($"Sending response: {FormatResponse(response)}");
            Info($"Target: {Color.DIM}{HOST}:{PORT}{Color.RESET}");

            using var client = new UdpClient();
            client.Client.SendTimeout = TIMEOUT_MS;
            client.Client.ReceiveTimeout = TIMEOUT_MS;

            byte[] data = Encoding.UTF8.GetBytes(response);
            int sent = client.Send(data, data.Length, HOST, PORT);

            if (sent == data.Length)
            {
                Success("✓ Response sent successfully");
                Console.WriteLine();
                Console.WriteLine($"{Color.DIM}Nudge should acknowledge within a few seconds{Color.RESET}");
                return 0;
            }
            else
            {
                Warning($"Partial send: {sent}/{data.Length} bytes");
                return 1;
            }
        }
        catch (SocketException ex)
        {
            Error("Connection failed");

            if (ex.ErrorCode == 10061 || ex.Message.Contains("refused"))
            {
                Error("Nudge is not running or not listening on port " + PORT);
                Console.WriteLine();
                Console.WriteLine($"{Color.BOLD}To fix:{Color.RESET}");
                Console.WriteLine($"  1. Start Nudge in another terminal: {Color.BCYAN}nudge{Color.RESET}");
                Console.WriteLine($"  2. Wait for a snapshot request");
                Console.WriteLine($"  3. Run this command again");
            }
            else
            {
                Error($"Network error: {ex.Message}");
            }

            return 1;
        }
        catch (Exception ex)
        {
            Error($"Failed to send response: {ex.Message}");
            return 1;
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // CONSOLE OUTPUT - Professional logging with colors
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    static void Success(string msg) =>
        Console.WriteLine($"{Color.BGREEN}{msg}{Color.RESET}");

    static void Info(string msg) =>
        Console.WriteLine($"{Color.CYAN}{msg}{Color.RESET}");

    static void Warning(string msg) =>
        Console.WriteLine($"{Color.BYELLOW}{msg}{Color.RESET}");

    static void Error(string msg) =>
        Console.WriteLine($"{Color.BRED}{msg}{Color.RESET}");

    static string FormatResponse(string response) =>
        response == "YES" ?
            $"{Color.BGREEN}{response}{Color.RESET} (productive)" :
            $"{Color.YELLOW}{response}{Color.RESET} (not productive)";

    static void ShowHelp()
    {
        Console.WriteLine($"{Color.BOLD}Nudge Notify{Color.RESET} - Productivity Response Tool");
        Console.WriteLine($"Version {VERSION}");
        Console.WriteLine();
        Console.WriteLine($"{Color.BOLD}USAGE:{Color.RESET}");
        Console.WriteLine($"  nudge-notify <response>");
        Console.WriteLine();
        Console.WriteLine($"{Color.BOLD}RESPONSES:{Color.RESET}");
        Console.WriteLine($"  {Color.BGREEN}YES{Color.RESET}  - I was productive during that time");
        Console.WriteLine($"  {Color.YELLOW}NO{Color.RESET}   - I was not productive during that time");
        Console.WriteLine();
        Console.WriteLine($"{Color.BOLD}EXAMPLES:{Color.RESET}");
        Console.WriteLine($"  nudge-notify YES    # Mark as productive");
        Console.WriteLine($"  nudge-notify NO     # Mark as not productive");
        Console.WriteLine();
        Console.WriteLine($"{Color.BOLD}WORKFLOW:{Color.RESET}");
        Console.WriteLine($"  1. Nudge takes a snapshot and waits for response");
        Console.WriteLine($"  2. You run this command with YES or NO");
        Console.WriteLine($"  3. Nudge saves your response to the training data");
        Console.WriteLine();
        Console.WriteLine($"{Color.BOLD}OPTIONS:{Color.RESET}");
        Console.WriteLine($"  {Color.CYAN}--help, -h{Color.RESET}     Show this help");
        Console.WriteLine($"  {Color.CYAN}--version, -v{Color.RESET}  Show version information");
        Console.WriteLine();
    }
}
