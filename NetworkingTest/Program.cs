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
    private static Method method = Method.R_UDP;
    private static bool naglesAlgorithm = true;

    private static Random random = new Random();
    static int packetNumber = 0;
    static int latestAck = 0;
    static Dictionary<int, DateTime> packetTimes = new();

    private static Dictionary<int, byte[]> notYetAcknowledgedData = new();

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
                tcpClient.NoDelay = !naglesAlgorithm;
                tcpStream = tcpClient.GetStream();
                break;
            case Method.R_UDP:
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
                        byte[] lengthBytes = new byte[4]; // Assuming 4 bytes for the length prefix
                        int bytesRead = await tcpStream.ReadAsync(lengthBytes, 0, lengthBytes.Length);

                        int messageLength = BitConverter.ToInt32(lengthBytes, 0);

                        byte[] buffer = new byte[messageLength];
                        bytesRead = await tcpStream.ReadAsync(buffer, 0, buffer.Length);
                        receivedMessage = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                        break;
                    case Method.R_UDP:
                    case Method.UDP:
                        var result = await udpClient.ReceiveAsync();
                        receivedMessage = Encoding.ASCII.GetString(result.Buffer);
                        break;
                }

                int ackNumber = int.Parse(receivedMessage.Replace("ACK: ", ""));

                if (method == Method.R_UDP)
                {
                    List<int> ackPackets = (from keyValuePair in notYetAcknowledgedData where keyValuePair.Key <= ackNumber select keyValuePair.Key).ToList();

                    foreach (int ackPacket in ackPackets)
                    {
                        float dataLatency = (float)(DateTime.UtcNow - packetTimes[ackPacket]).TotalMilliseconds / 2.0f;
                        packetAmount++;
                        latencySum += dataLatency;
                        if (dataLatency > latencyMax) latencyMax = dataLatency;
                        if (dataLatency < latencyMin) latencyMin = dataLatency;

                        notYetAcknowledgedData.Remove(ackPacket);

                        Console.WriteLine($"Received ack for packet {ackPacket}, Latency: {dataLatency}ms");
                    }
                }
                else
                {
                    float latency = (float)(DateTime.UtcNow - packetTimes[ackNumber]).TotalMilliseconds / 2.0f;

                    packetAmount++;
                    latencySum += latency;
                    if (latency > latencyMax) latencyMax = latency;
                    if (latency < latencyMin) latencyMin = latency;

                    Console.WriteLine($"Received: {receivedMessage}, Latency: {latency}ms");
                }

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

                switch (method)
                {
                    case Method.TCP:
                        byte[] length = BitConverter.GetBytes(messageBytes.Length);
                        byte[] messageIncludingLength = length.Concat(messageBytes).ToArray();

                        await tcpStream.WriteAsync(messageIncludingLength, 0, messageIncludingLength.Length);
                        break;
                    case Method.UDP:
                        await udpClient.SendAsync(messageBytes, messageBytes.Length, remoteEndPoint);
                        break;
                    case Method.R_UDP:
                        byte[] lengthUdp = BitConverter.GetBytes(messageBytes.Length);
                        byte[] messageIncludingLengthUdp = lengthUdp.Concat(messageBytes).ToArray();

                        notYetAcknowledgedData.Add(packetNumber, messageIncludingLengthUdp);

                        byte[] completeMessage = [];
                        foreach (KeyValuePair<int,byte[]> keyValuePair in notYetAcknowledgedData.OrderBy(key => key.Key))
                        {
                            completeMessage = completeMessage.Concat(keyValuePair.Value).ToArray();
                        }

                        await udpClient.SendAsync(completeMessage, completeMessage.Length, remoteEndPoint);
                        break;
                }

                Console.WriteLine($"Sent: {message}");

                packetNumber++;

                await Task.Delay(1000 / 60);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending message: {ex.Message}");
            }
        }
    }
}
