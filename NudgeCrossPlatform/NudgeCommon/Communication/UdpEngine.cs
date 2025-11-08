using System.Net;
using System.Net.Sockets;
using System.Text;

namespace NudgeCommon.Communication;

/// <summary>
/// Handles UDP communication between Harvester and Notifier
/// </summary>
public class UdpEngine
{
    private readonly int _talkPort;
    private readonly int _listenPort;
    private readonly Action<string> _receiveCallback;
    private UdpClient? _talkUdpClient;
    private UdpClient? _listenerUdpClient;
    private readonly IPAddress _talkAddress = IPAddress.Parse("127.0.0.1");
    private IPEndPoint? _talkIpEndPoint;
    private CancellationTokenSource? _cancellationTokenSource;

    public UdpEngine(int talkPort, int listenPort, Action<string> receiveCallback)
    {
        _talkPort = talkPort;
        _listenPort = listenPort;
        _receiveCallback = receiveCallback;
    }

    /// <summary>
    /// Start the UDP server to listen for messages
    /// </summary>
    public async Task StartUdpServerAsync()
    {
        _listenerUdpClient = new UdpClient(_listenPort);
        _talkUdpClient = new UdpClient();
        _talkUdpClient.DontFragment = true;
        _talkIpEndPoint = new IPEndPoint(_talkAddress, _talkPort);
        _talkUdpClient.Connect(_talkIpEndPoint);

        Console.WriteLine($"Started listening on port: {_listenPort}");

        _cancellationTokenSource = new CancellationTokenSource();
        await StartListeningAsync(_cancellationTokenSource.Token);
    }

    /// <summary>
    /// Start listening for incoming UDP messages
    /// </summary>
    private async Task StartListeningAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var receiveResult = await _listenerUdpClient!.ReceiveAsync(cancellationToken);
                string receivedString = Encoding.ASCII.GetString(receiveResult.Buffer);
                _receiveCallback(receivedString);
                Console.WriteLine($">> Received: {receivedString}");
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Listening stopped.");
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error in listening: {e.Message}");
        }
    }

    /// <summary>
    /// Send a message to connected clients
    /// </summary>
    public async Task SendToClientsAsync(string message)
    {
        byte[] sendBuffer = Encoding.ASCII.GetBytes(message);
        try
        {
            await _talkUdpClient!.SendAsync(sendBuffer, sendBuffer.Length);
            Console.WriteLine($"<< Sent: {message} to {_talkIpEndPoint!.Address}:{_talkIpEndPoint.Port}");
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error sending message: {e.Message}");
        }
    }

    /// <summary>
    /// Stop the UDP server
    /// </summary>
    public void Stop()
    {
        _cancellationTokenSource?.Cancel();
        _listenerUdpClient?.Close();
        _talkUdpClient?.Close();
    }
}
