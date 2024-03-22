using System.Net;
using System.Net.Sockets;
using System.Text;
using NetworkSendingTest;

namespace NetworkSendingTest
{
    public abstract class SenderImplementation
    {
        private readonly Random _random = new Random();
        protected readonly Dictionary<int, DateTime> TimeOfSentPackets = new();

        private int _latestAcknowledgementNumber;
        private int _amountOfAcknowledgementsReceived;
        private float _minimumLatency = 999;
        private float _maximumLatency;
        private float _latencySum;

        public async Task Start()
        {
            var receiver = StartListeningAsync();
            var sender = StartSendingAsync();

            await Task.WhenAll(receiver, sender);

            Console.WriteLine("Acknowledgements Received: " + _amountOfAcknowledgementsReceived + "/1000");
            Console.WriteLine("Average Latency: " + _latencySum / _amountOfAcknowledgementsReceived);
            Console.WriteLine("Latency Min: " + _minimumLatency);
            Console.WriteLine("Latency Max: " + _maximumLatency);
        }

        private byte[] CreateMessageBytes(int packetNumber)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            string data = new string(Enumerable.Repeat(chars, 255)
                .Select(s => s[_random.Next(s.Length)]).ToArray());

            string message = packetNumber + ":" + data;

            return Encoding.ASCII.GetBytes(message);
        }

        protected abstract Task SendPacket(int packetNumber, byte[] messageBytes);

        private async Task CreateAndSendPacket(int packetNumber)
        {
            var messageBytes = CreateMessageBytes(packetNumber);
            await SendPacket(packetNumber, messageBytes);
        }

        private async Task StartSendingAsync()
        {
            int packetNumber = 0;
            while (packetNumber < 1000)
            {
                try
                {
                    await CreateAndSendPacket(packetNumber);
                    packetNumber++;
                    await Task.Delay(1000 / 20);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error sending message: {ex.Message}");
                }
            }
        }

        protected abstract Task<string> ReceiveAcknowledgement();

        protected virtual void HandleAcknowledgement(int packetNumber)
        {
            float latency = (float)(DateTime.UtcNow - TimeOfSentPackets[packetNumber]).TotalMilliseconds / 2.0f;

            _amountOfAcknowledgementsReceived++;
            _latencySum += latency;
            if (latency > _maximumLatency) _maximumLatency = latency;
            if (latency < _minimumLatency) _minimumLatency = latency;

            Console.WriteLine($"Received ack for packet {packetNumber}, Latency: {latency}ms");
        }

        private async Task StartListeningAsync()
        {
            while (_latestAcknowledgementNumber < 999)
            {
                try
                {
                    string receivedAcknowledgement = await ReceiveAcknowledgement();
                    int receivedAcknowledgementNumber = int.Parse(receivedAcknowledgement.Replace("ACK: ", ""));
                    HandleAcknowledgement(receivedAcknowledgementNumber);
                    _latestAcknowledgementNumber = receivedAcknowledgementNumber;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error receiving message: {ex.Message}");
                }
            }
        }
    }

    public class TcpImplementation : SenderImplementation
    {
        private readonly TcpClient _tcpClient;
        private readonly NetworkStream _tcpStream;

        public TcpImplementation(bool useNaglesAlgorithm = false)
        {
            _tcpClient = new TcpClient();
            _tcpClient.Connect(IPAddress.Parse("127.0.0.1"), 11000);
            _tcpClient.NoDelay = !useNaglesAlgorithm;
            _tcpStream = _tcpClient.GetStream();
        }

        protected override async Task SendPacket(int packetNumber, byte[] messageBytes)
        {
            Console.WriteLine($"Sending: {packetNumber}");
            TimeOfSentPackets.Add(packetNumber, DateTime.UtcNow);

            byte[] length = BitConverter.GetBytes(messageBytes.Length);
            byte[] messageIncludingLength = length.Concat(messageBytes).ToArray();

            await _tcpStream.WriteAsync(messageIncludingLength, 0, messageIncludingLength.Length);
        }

        protected override async Task<string> ReceiveAcknowledgement()
        {
            byte[] lengthBytes = new byte[4]; // Assuming 4 bytes for the length prefix
            int bytesRead = await _tcpStream.ReadAsync(lengthBytes, 0, lengthBytes.Length);

            int messageLength = BitConverter.ToInt32(lengthBytes, 0);

            byte[] buffer = new byte[messageLength];
            bytesRead = await _tcpStream.ReadAsync(buffer, 0, buffer.Length);
            return Encoding.ASCII.GetString(buffer, 0, bytesRead);
        }
    }

    public class UdpImplementation : SenderImplementation
    {
        protected readonly UdpClient UdpClient;
        protected readonly IPEndPoint RemoteEndPoint;

        public UdpImplementation()
        {
            UdpClient = new UdpClient(0);
            RemoteEndPoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 11000);

            UdpClient.Client.IOControl(
                -1744830452,
                new byte[] { 0, 0, 0, 0 },
                null
            );
        }

        protected override async Task SendPacket(int packetNumber, byte[] messageBytes)
        {
            Console.WriteLine($"Sending: {packetNumber}");
            TimeOfSentPackets.Add(packetNumber, DateTime.UtcNow);

            await UdpClient.SendAsync(messageBytes, messageBytes.Length, RemoteEndPoint);
        }

        protected override async Task<string> ReceiveAcknowledgement()
        {
            var result = await UdpClient.ReceiveAsync();
            return Encoding.ASCII.GetString(result.Buffer);
        }
    }

    public class ReliableUdpImplementation : UdpImplementation
    {
        private readonly Dictionary<int, byte[]> _notYetAcknowledgedData = new();

        protected override async Task SendPacket(int packetNumber, byte[] messageBytes)
        {
            Console.WriteLine($"Sending: {packetNumber}");
            TimeOfSentPackets.Add(packetNumber, DateTime.UtcNow);

            byte[] lengthUdp = BitConverter.GetBytes(messageBytes.Length);
            byte[] messageIncludingLengthUdp = lengthUdp.Concat(messageBytes).ToArray();

            _notYetAcknowledgedData.Add(packetNumber, messageIncludingLengthUdp);

            byte[] completeMessage = [];
            foreach (KeyValuePair<int,byte[]> keyValuePair in _notYetAcknowledgedData.OrderBy(key => key.Key))
            {
                completeMessage = completeMessage.Concat(keyValuePair.Value).ToArray();
            }

            await UdpClient.SendAsync(completeMessage, completeMessage.Length, RemoteEndPoint);
        }

        protected override void HandleAcknowledgement(int packetNumber)
        {
            List<int> ackPackets = (from keyValuePair in _notYetAcknowledgedData where keyValuePair.Key <= packetNumber select keyValuePair.Key).ToList();

            foreach (int ackPacket in ackPackets)
            {
                base.HandleAcknowledgement(ackPacket);

                _notYetAcknowledgedData.Remove(ackPacket);
            }
        }
    }
}

class Program
{
    static async Task Main(string[] args)
    {
        SenderImplementation implementation = new TcpImplementation();
        await implementation.Start();
    }
}
