using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

enum Method
{
    UDP,
    TCP,
    R_UDP,
}

class Program
{
    private static Method method = Method.TCP;

    private static Random random = new Random();
    static int packetNumber = 0;
    static int latestAck = 0;
    static Dictionary<int, DateTime> packetTimes = new();

    //UDP
    static UdpClient udpClient;
    static IPEndPoint remoteEndPoint;

    // TCP
    static TcpClient tcpClient;
    static NetworkStream tcpStream;

    // Latency Calculations
    private static float latencySum = 0;
    private static float packetAmount = 0;
    private static float latencyMin = 999;
    private static float latencyMax = 0;

    static async Task Main(string[] args)
    {
        switch (method)
        {
            case Method.TCP:
                tcpClient = new TcpClient();
                await tcpClient.ConnectAsync(IPAddress.Parse("127.0.0.1"), 11000);
                tcpStream = tcpClient.GetStream();
                break;
            case Method.UDP:
                udpClient = new UdpClient(0);
                remoteEndPoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 11000);

                udpClient.Client.IOControl(
                    -1744830452,
                    new byte[] { 0, 0, 0, 0 },
                    null
                );

                break;
        }

        var receiver = ReceiveMessagesAsync();
        var sender = SendMessagesAsync();

        await Task.WhenAll(receiver, sender);

        Console.WriteLine("Packets Received: " + packetAmount + "/1000");
        Console.WriteLine("Average Latency: " + latencySum / packetAmount);
        Console.WriteLine("Latency Min: " + latencyMin);
        Console.WriteLine("Latency Max: " + latencyMax);
    }

    static async Task ReceiveMessagesAsync()
    {
        while (latestAck < 999)
        {
            try
            {
                string receivedMessage = "";
                switch (method)
                {
                    case Method.TCP:
                        byte[] buffer = new byte[1024];
                        int bytesRead = await tcpStream.ReadAsync(buffer, 0, buffer.Length);
                        receivedMessage = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                        break;
                    case Method.UDP:
                        var result = await udpClient.ReceiveAsync();
                        receivedMessage = Encoding.ASCII.GetString(result.Buffer);
                        break;
                }

                int ackNumber = int.Parse(receivedMessage.Replace("ACK: ", ""));

                float latency = (float)(DateTime.UtcNow - packetTimes[ackNumber]).TotalMilliseconds / 2.0f;

                packetAmount++;
                latencySum += latency;
                if (latency > latencyMax) latencyMax = latency;
                if (latency < latencyMin) latencyMin = latency;

                Console.WriteLine($"Received: {receivedMessage}, Latency: {latency}ms");

                latestAck = ackNumber;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error receiving message: {ex.Message}");
            }
        }
    }

    static async Task SendMessagesAsync()
    {
        while (packetNumber < 1000)
        {
            try
            {
                string packet = packetNumber.ToString();

                const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
                string data = new string(Enumerable.Repeat(chars, 255)
                    .Select(s => s[random.Next(s.Length)]).ToArray());

                string message = packet + ":" + data;

                byte[] messageBytes = Encoding.ASCII.GetBytes(message);

                packetTimes.Add(packetNumber, DateTime.UtcNow);
                packetNumber++;

                switch (method)
                {
                    case Method.TCP:
                        await tcpStream.WriteAsync(messageBytes, 0, messageBytes.Length);
                        break;
                    case Method.UDP:
                        await udpClient.SendAsync(messageBytes, messageBytes.Length, remoteEndPoint);
                        break;
                }

                Console.WriteLine($"Sent: {message}");

                await Task.Delay(1000 / 60);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending message: {ex.Message}");
            }
        }
    }
}
