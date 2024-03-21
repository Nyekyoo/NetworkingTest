using System.Net;
using System.Net.Sockets;
using System.Text;

class Program
{
    static int packetNumber = 0;
    static int latestAck = 0;
    static Dictionary<int, DateTime> packetTimes = new();
    static UdpClient udpClient;
    static IPEndPoint remoteEndPoint;

    private static float latencySum = 0;
    private static float packetAmount = 0;
    private static float latencyMin = 999;
    private static float latencyMax = 0;

    static async Task Main(string[] args)
    {
        udpClient = new UdpClient(0);
        remoteEndPoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 11000);

        udpClient.Client.IOControl(
            -1744830452,
            new byte[] { 0, 0, 0, 0 },
            null
        );

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
                var result = await udpClient.ReceiveAsync();
                string receivedMessage = Encoding.ASCII.GetString(result.Buffer);

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
                string message = packetNumber.ToString();
                byte[] messageBytes = Encoding.ASCII.GetBytes(message);

                packetTimes.Add(packetNumber, DateTime.UtcNow);
                packetNumber++;

                await udpClient.SendAsync(messageBytes, messageBytes.Length, remoteEndPoint);
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
