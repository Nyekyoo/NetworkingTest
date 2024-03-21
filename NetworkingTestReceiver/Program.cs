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

    private static NetworkStream tcpStream;

    private static UdpClient udpClient;
    private static IPEndPoint remoteEndPoint;

    private static int latestPacketId = -1;

    static void Main(string[] args)
    {
        switch (method)
        {
            case Method.TCP:
                TcpListener tcpListener = new TcpListener(IPAddress.Any, 11000);
                tcpListener.Start();

                TcpClient client = tcpListener.AcceptTcpClient();
                client.NoDelay = !naglesAlgorithm;
                tcpStream = client.GetStream();
                break;

            case Method.UDP:
            case Method.R_UDP:
                udpClient = new UdpClient(11000);
                remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                break;
        }

        // Loop to handle multiple requests from the same client
        while (true)
        {
            byte[] receivedBytes;
            string receivedMessage = "";
            List<string> receivedMessages = new List<string>();

            switch (method)
            {
                case Method.TCP:
                    byte[] lengthBytes = new byte[4]; // Assuming 4 bytes for the length prefix
                    int bytesRead = tcpStream.Read(lengthBytes, 0, lengthBytes.Length);
                    if (bytesRead == 0) // No more data to read, client has closed the connection
                        goto End;

                    // Convert the length prefix to an integer
                    int messageLength = BitConverter.ToInt32(lengthBytes, 0);

                    // Read the actual message
                    receivedBytes = new byte[messageLength];
                    bytesRead = tcpStream.Read(receivedBytes, 0, receivedBytes.Length);
                    if (bytesRead == 0) // No more data to read, client has closed the connection
                        goto End;

                    receivedMessage = Encoding.ASCII.GetString(receivedBytes, 0, bytesRead);
                    break;

                case Method.UDP:
                    receivedBytes = udpClient.Receive(ref remoteEndPoint);
                    receivedMessage = Encoding.ASCII.GetString(receivedBytes);
                    break;

                case Method.R_UDP:
                    receivedBytes = udpClient.Receive(ref remoteEndPoint);
                    receivedMessages = SplitByteArray(receivedBytes);

                    break;
            }

            int packetNumber = -1;
            if (method == Method.R_UDP)
            {
                foreach (string message in receivedMessages)
                {
                    int messageNumber = int.Parse(message.Split(":")[0]);
                    if (messageNumber <= latestPacketId) continue; // Skip duplicate packets

                    Console.WriteLine(message);

                    if (messageNumber > packetNumber) packetNumber = messageNumber;
                }

                if (packetNumber > latestPacketId) latestPacketId = packetNumber;
            }
            else
            {
                Console.WriteLine(receivedMessage);
                packetNumber = int.Parse(receivedMessage.Split(":")[0]);
            }

            string ackMessage = $"ACK: {packetNumber}";
            byte[] ackBytes = Encoding.ASCII.GetBytes(ackMessage);

            switch (method)
            {
                case Method.TCP:
                    byte[] length = BitConverter.GetBytes(ackBytes.Length);
                    byte[] ackMessageIncludingLength = length.Concat(ackBytes).ToArray();

                    tcpStream.Write(ackMessageIncludingLength, 0, ackMessageIncludingLength.Length);
                    break;

                case Method.R_UDP:
                case Method.UDP:
                    udpClient.Send(ackBytes, ackBytes.Length, remoteEndPoint);
                    break;
            }
        }

        End:
        Console.WriteLine("Connection Closed");
    }

    public static List<string> SplitByteArray(byte[] input)
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
