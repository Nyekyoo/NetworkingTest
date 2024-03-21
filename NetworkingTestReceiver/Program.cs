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
    private static Method method = Method.UDP;
    private static bool naglesAlgorithm = true;

    private static NetworkStream tcpStream;

    private static UdpClient udpClient;
    private static IPEndPoint remoteEndPoint;

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
                udpClient = new UdpClient(11000);
                remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                break;
        }

        // Loop to handle multiple requests from the same client
        while (true)
        {
            byte[] receivedBytes;
            string receivedMessage = "";

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
            }

            Console.WriteLine(receivedMessage);

            // Send ACK with timestamp
            string packetNumber = receivedMessage.Split(":")[0];
            string ackMessage = $"ACK: {packetNumber}";
            byte[] ackBytes = Encoding.ASCII.GetBytes(ackMessage);

            switch (method)
            {
                case Method.TCP:
                    byte[] length = BitConverter.GetBytes(ackBytes.Length);
                    byte[] ackMessageIncludingLength = length.Concat(ackBytes).ToArray();

                    tcpStream.Write(ackMessageIncludingLength, 0, ackMessageIncludingLength.Length);
                    break;

                case Method.UDP:
                    udpClient.Send(ackBytes, ackBytes.Length, remoteEndPoint);
                    break;
            }
        }

        End:
        Console.WriteLine("Connection Closed");
    }
}
