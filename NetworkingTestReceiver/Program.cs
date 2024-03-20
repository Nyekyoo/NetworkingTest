using System.Net;
using System.Net.Sockets;
using System.Text;

using UdpClient udpClient = new UdpClient(11000);

IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

while (true)
{
    byte[] receivedBytes = udpClient.Receive(ref remoteEndPoint);
    string receivedMessage = Encoding.ASCII.GetString(receivedBytes);

    Console.WriteLine(receivedMessage);

    // Send ACK with timestamp
    string ackMessage = $"ACK: {receivedMessage}";
    byte[] ackBytes = Encoding.ASCII.GetBytes(ackMessage);
    udpClient.Send(ackBytes, ackBytes.Length, remoteEndPoint);
}
