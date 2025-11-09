#!/usr/bin/env dotnet run
// Nudge Notifier - Send YES/NO responses
// Single file, no abstractions
//
// Build: csc -out:nudge-notify nudge-notify.cs
// Run:   ./nudge-notify YES
//        ./nudge-notify NO

using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

class NudgeNotify
{
    const string HOST = "127.0.0.1";
    const int PORT = 45001;

    static void Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: nudge-notify [YES|NO]");
            Console.WriteLine("\nSend productivity response to Nudge harvester");
            Console.WriteLine("  YES - I was productive");
            Console.WriteLine("  NO  - I was not productive");
            return;
        }

        string response = args[0].ToUpper();
        if (response != "YES" && response != "NO")
        {
            Console.WriteLine($"Error: Response must be YES or NO, got: {args[0]}");
            return;
        }

        // Send UDP packet
        try
        {
            var client = new UdpClient();
            byte[] data = Encoding.UTF8.GetBytes(response);
            client.Send(data, data.Length, HOST, PORT);
            client.Close();

            Console.WriteLine($"✓ Sent: {response}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }
}
