using System.Net;
using System.Net.Sockets;
using System.Text;
using NetworkSendingTest;

namespace NetworkSendingTest
{
    public abstract class ReceiverImplementation
    {
        private int _latestPacketId = -1;

        public void StartListening()
        {
            while (true)
            {
                var receivedMessages = ReceiveMessages();
                var receivedPacketNumber = HandleIncomingMessages(receivedMessages);
                SendAcknowledgement(receivedPacketNumber);
            }
        }

        protected abstract List<string> ReceiveMessages();

        protected virtual byte[] SendAcknowledgement(int packetNumber)
        {
            string ackMessage = $"ACK: {packetNumber}";
            return Encoding.ASCII.GetBytes(ackMessage);
        }

        private int HandleIncomingMessages(List<string> incomingMessages)
        {
            int packetNumber = -1;
            foreach (string message in incomingMessages)
            {
                int messageNumber = int.Parse(message.Split(":")[0]);
                if (messageNumber <= _latestPacketId) continue; // Skip duplicate packets

                Console.WriteLine(message);

                if (messageNumber > packetNumber) packetNumber = messageNumber;
            }

            if (packetNumber > _latestPacketId) _latestPacketId = packetNumber;
            return packetNumber;
        }
    }

    public class TcpReceiverImplementation : ReceiverImplementation
    {
        private readonly NetworkStream _tcpStream;

        public TcpReceiverImplementation(bool useNaglesAlgorithm = false)
        {
            TcpListener tcpListener = new TcpListener(IPAddress.Any, 11000);
            tcpListener.Start();

            TcpClient client = tcpListener.AcceptTcpClient();
            client.NoDelay = !useNaglesAlgorithm;
            _tcpStream = client.GetStream();
        }

        protected override List<string> ReceiveMessages()
        {
            byte[] lengthBytes = new byte[4]; // Assuming 4 bytes for the length prefix
            int bytesRead = _tcpStream.Read(lengthBytes, 0, lengthBytes.Length);

            // Convert the length prefix to an integer
            int messageLength = BitConverter.ToInt32(lengthBytes, 0);

            // Read the actual message
            var receivedBytes = new byte[messageLength];
            bytesRead = _tcpStream.Read(receivedBytes, 0, receivedBytes.Length);

            return [Encoding.ASCII.GetString(receivedBytes, 0, bytesRead)];
        }

        protected override byte[] SendAcknowledgement(int packetNumber)
        {
            byte[] ackBytes = base.SendAcknowledgement(packetNumber);

            byte[] length = BitConverter.GetBytes(ackBytes.Length);
            byte[] ackMessageIncludingLength = length.Concat(ackBytes).ToArray();

            _tcpStream.Write(ackMessageIncludingLength, 0, ackMessageIncludingLength.Length);

            return ackBytes;
        }
    }

    public class UdpReceiverImplementation : ReceiverImplementation
    {
        protected readonly UdpClient UdpClient;
        protected IPEndPoint RemoteEndPoint;

        public UdpReceiverImplementation()
        {
            UdpClient = new UdpClient(11000);
            RemoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
        }

        protected override List<string> ReceiveMessages()
        {
            var receivedBytes = UdpClient.Receive(ref RemoteEndPoint);
            return [Encoding.ASCII.GetString(receivedBytes)];
        }

        protected override byte[] SendAcknowledgement(int packetNumber)
        {
            byte[] ackBytes = base.SendAcknowledgement(packetNumber);
            UdpClient.Send(ackBytes, ackBytes.Length, RemoteEndPoint);
            return ackBytes;
        }
    }

    public class ReliableUdpReceiverImplementation : UdpReceiverImplementation
    {
        protected override List<string> ReceiveMessages()
        {
            var receivedBytes = UdpClient.Receive(ref RemoteEndPoint);
            return SplitByteArray(receivedBytes);
        }

        private List<string> SplitByteArray(byte[] input)
        {
            var result = new List<string>();
            int index = 0;

            while (index < input.Length)
            {
                // Assuming the first 4 bytes represent the length of the data
                int length = BitConverter.ToInt32(input, index);
                index += 4; // Move past the length bytes

                // Ensure there's enough data left in the array
                if (index + length > input.Length)
                {
                    throw new ArgumentException("Invalid data format");
                }

                // Extract the data
                byte[] data = new byte[length];
                Array.Copy(input, index, data, 0, length);
                result.Add(Encoding.ASCII.GetString(data));

                index += length; // Move past the data
            }

            return result;
        }
    }
}

class Program
{
    static void Main(string[] args)
    {
        ReceiverImplementation implementation = new TcpReceiverImplementation();
        implementation.StartListening();
    }
}
