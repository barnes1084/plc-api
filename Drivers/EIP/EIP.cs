using System.Net.Sockets;
using System.Text;

namespace plc_api.Drivers.EIP
{
    public class EIP : IDisposable
    {
        private Socket sock;
        private uint cipConnectionID;
        private int connectionSerialNumber = 0;
        private int oSerialNumber = 0;
        private uint eipSessionHandle;
        private const int MaxReconnectAttempts = 5;
        private const int ReconnectDelay = 5000;
        private string socket_ipAddress;
        private int socket_port;
        private int path_port;
        private int path_slot;
        private string path_linkChannel;
        private string path_linkIP;
        private int path_linkport;
        private int path_linkslot;
        private ushort _seq; // connected sequence; increment each request
        public bool isConnected { get; set; }
        public EIP(string ipAddress, string path = "1,0")
        {
            socket_ipAddress = ipAddress;
            socket_port = 44818;  // Default Ethernet/IP port for Rockwell PLCs

            // Default values
            path_port = 1;       // Default port (usually backplane)
            path_slot = 0;       // Default slot
            path_linkChannel = "";  // Clear since not always used
            path_linkIP = "";       // Clear since not always used
            path_linkport = 1;
            path_linkslot = 0;


            try
            {
                if (!string.IsNullOrEmpty(path))
                {
                    string[] pathParts = path.Split(',');

                    // Always parse port and slot if available
                    if (pathParts.Length >= 2)
                    {
                        path_port = int.Parse(pathParts[0]);
                        path_slot = int.Parse(pathParts[1]);
                    }

                    // Check if there is a link channel and IP address
                    if (pathParts.Length >= 4)
                    {
                        path_linkChannel = pathParts[2];  // Expecting 'A' or another letter
                        path_linkIP = pathParts[3];       // IP Address
                    }

                    // Check if there is a link port and link slot
                    if (pathParts.Length >= 6)
                    {
                        path_linkport = int.Parse(pathParts[4]);  // Expecting '1' for backplane
                        path_linkslot = int.Parse(pathParts[5]);       // slot of controller
                    }
                }
            }
            catch (FormatException fe)
            {
                Console.WriteLine($"Error parsing port/slot: {fe.Message}");
            }
            catch (IndexOutOfRangeException ie)
            {
                Console.WriteLine($"Not enough parameters in path: {ie.Message}");
            }

            isConnected = ConnectToPLC();
        }

        private bool ConnectToPLC()
        {
            sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            sock.ReceiveTimeout = 10000; // 10 seconds
            sock.SendTimeout = 10000; // 10 seconds

            int attempts = 0;
            while (attempts < MaxReconnectAttempts)
            {
                try
                {
                    sock.Connect(socket_ipAddress, socket_port);
                    RegisterSession();
                    if (ConnectToMessageRouter())
                    {
                        return true;
                    }
                    else
                    {
                        Console.WriteLine("Could not connect to message router.");
                        return false;
                    }
                }
                catch (SocketException ex)
                {
                    attempts++;
                    Console.WriteLine($"Attempt {attempts}: Error connecting to PLC: {ex.Message}");
                    if (attempts < MaxReconnectAttempts)
                    {
                        Console.WriteLine("Attempting to reconnect...");
                        Thread.Sleep(ReconnectDelay);
                    }
                }
            }
            Console.WriteLine("Failed to connect after multiple attempts.");
            return false;
        }

        private void RegisterSession()
        {
            try
            {
                byte[] request = new byte[]
                {
            0x65,
            0x00,
            0x04,
            0x00,
            0x00,
            0x00,
            0x00,
            0x00,
            0x00,
            0x00,
            0x00,
            0x00,
            0x00,
            0x00,
            0x00,
            0x00,
            0x00,
            0x00,
            0x00,
            0x00,
            0x00,
            0x00,
            0x00,
            0x00,
            0x01,
            0x00,
            0x00,
            0x00 };
                // Send the request to the PLC
                sock.Send(request);

                // Receive the response from the PLC
                byte[] response = new byte[28];
                int bytesReceived = sock.Receive(response);

                eipSessionHandle = BitConverter.ToUInt32(response, 4);
            }
            catch (Exception e)
            {
                Console.WriteLine($"error RegisterSession:  {e.Message}");
            }
        }

        private bool ConnectToMessageRouter()
        {
            bool connected = false;
            try
            {
                connectionSerialNumber = new Random().Next(0, 65536);
                byte[] connectionSerialNumberBytes = BitConverter.GetBytes((ushort)connectionSerialNumber);

                oSerialNumber = (int)((uint)new Random().Next(0, 65536) << 16 | (uint)new Random().Next(0, 65536));
                byte[] OSerialNumberBytes = BitConverter.GetBytes(oSerialNumber);

                // Basic path components
                List<byte> path = new List<byte>();
                path.Add((byte)path_port); // Port number
                path.Add((byte)path_slot); // Slot number

                // Check if IP routing is needed
                if (!string.IsNullOrEmpty(path_linkIP))
                {
                    path.Add(0x12); // Segment type for IP address
                    byte[] ipBytes = Encoding.ASCII.GetBytes(path_linkIP);
                    // if ipBytes length is an odd number then add a 0x00 byte to it.
                    if (ipBytes.Length % 2 == 1)
                    {
                        ipBytes = ipBytes.Concat(new byte[] { 0x00 }).ToArray();
                    }
                    path.Add((byte)ipBytes.Length);
                    path.AddRange(ipBytes);

                    path.Add((byte)path_linkport); // Link Port number
                    path.Add((byte)path_linkslot); // Link Slot number
                }


                // Message Router Path (assuming this is standard and required)
                path.AddRange(new byte[] { 0x20, 0x02 }); // Class ID for the Message Router
                path.Add(0x24); // Instance Segment
                path.Add(0x01); // Instance ID

                // Convert path list to byte array
                byte[] pathBytes = path.ToArray();
                byte pathSizeInWords = (byte)((pathBytes.Length + 1) / 2); // Calculate size in words, rounding up


                uint TtoONetworkConnectionID = (uint)new Random().Next(0, 65536) << 16 | (uint)new Random().Next(0, 65536);
                byte[] TtoONetworkConnectionIDBytes = BitConverter.GetBytes(TtoONetworkConnectionID);

                byte[] eipSessionHandleBytes = BitConverter.GetBytes(eipSessionHandle);

                int subMessageLength = pathBytes.Length + 42;
                byte[] subMessageLengthBytes = BitConverter.GetBytes((ushort)subMessageLength);

                int messageLength = subMessageLength + 16;
                byte[] messageLengthBytes = BitConverter.GetBytes((ushort)messageLength);

                byte[] request = new byte[] {
            0x6F, 0x00}
                 .Concat(messageLengthBytes)
                 .Concat(eipSessionHandleBytes)
                 .Concat(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0xB2, 0x00 })
                 .Concat(subMessageLengthBytes)
                 .Concat(new byte[] { 0x54, 0x02, 0x20, 0x06, 0x24, 0x01, 0x0A, 0x05, 0x00, 0x00, 0x00, 0x00 })
                 .Concat(TtoONetworkConnectionIDBytes)
                 .Concat(connectionSerialNumberBytes)
                 .Concat(new byte[] { 0xDD, 0xBA })
                 .Concat(OSerialNumberBytes)
                 .Concat(new byte[] { 0x01, 0x00, 0x00, 0x00, 0x80, 0x84, 0x1E, 0x00, 0xF8, 0x43, 0x80, 0x84, 0x1E, 0x00, 0xF8, 0x43, 0xA3 })
                 .Concat(new byte[] { pathSizeInWords }) // Add path size in words
                 .Concat(pathBytes) // Add path
                 .ToArray();

                int bytesSent = sock.Send(request);
                byte[] response = new byte[99];
                int bytesReceived = sock.Receive(response);

                // Extract status and handle connection ID from response
                int generalStatusPosition = 42;
                byte generalStatus = response[generalStatusPosition];
                if (generalStatus == 0)
                {
                    cipConnectionID = BitConverter.ToUInt32(response, 44);
                    connected = true;
                }
                else
                {
                    connected = false;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"error ConnectToMessageRouter: {e.Message}");
            }

            return connected;
        }


        public int ReadDint(string tagName)
        {
            try
            {
                byte[] path = BuildLogixTagPath(tagName);
                byte pathWords = (byte)((path.Length + 1) / 2);

                // CIP: Read Tag (0x4C) + path + elements(1)
                var cip = new List<byte>();
                cip.Add(0x4C);
                cip.Add(pathWords);
                cip.AddRange(path);
                cip.AddRange(new byte[] { 0x01, 0x00 }); // elements = 1

                byte[] cipReply = SendConnectedCip(cip.ToArray(), recvBuffer: 256);

                if (!TryParseCipReply(cipReply, out byte status, out int dataStart))
                    throw new InvalidOperationException("Malformed CIP reply.");

                if (status != 0)
                    throw new InvalidOperationException($"CIP general status error: {status}");

                // Reply data: [type(2)][value...] or [type(2)][count(2)][value...]
                if (dataStart + 2 > cipReply.Length)
                    throw new InvalidOperationException("CIP reply missing data type.");

                int valueOffset;
                if (dataStart + 2 + 2 + 4 <= cipReply.Length)
                {
                    ushort maybeCount = BitConverter.ToUInt16(cipReply, dataStart + 2);
                    valueOffset = maybeCount >= 1 && maybeCount <= 0x4000 ? dataStart + 4 : dataStart + 2;
                }
                else
                {
                    valueOffset = dataStart + 2;
                }

                if (valueOffset + 4 > cipReply.Length)
                    throw new InvalidOperationException("CIP reply does not contain a DINT value.");

                return BitConverter.ToInt32(cipReply, valueOffset);
            }
            catch (Exception e)
            {
                Console.WriteLine($"error ReadDint: {e.Message}");
                return -1;
            }
        }


        public bool WriteDint(string tagName, int value)
        {
            try
            {
                byte[] path = BuildLogixTagPath(tagName);
                byte pathWords = (byte)((path.Length + 1) / 2);

                var cip = new List<byte>();
                cip.Add(0x4D);               // Write Tag
                cip.Add(pathWords);
                cip.AddRange(path);
                cip.AddRange(new byte[] { 0xC4, 0x00 });          // DINT type
                cip.AddRange(new byte[] { 0x01, 0x00 });          // elements = 1
                cip.AddRange(BitConverter.GetBytes(value));       // data

                byte[] cipReply = SendConnectedCip(cip.ToArray());

                if (!TryParseCipReply(cipReply, out byte status, out _))
                    throw new InvalidOperationException("Malformed CIP reply.");

                return status == 0;
            }
            catch (Exception e)
            {
                Console.WriteLine($"error WriteDint: {e.Message}");
                return false;
            }
        }


        public bool ReadBool(string tagName)
        {
            try
            {
                // ".<bit>" means bit-of-DINT (read/shift)
                if (TryParseBitSuffix(tagName, out string baseTag, out int bit))
                {
                    int v = ReadDint(baseTag);
                    if (v < 0) throw new InvalidOperationException("Failed to read base DINT.");
                    return (v & 1 << bit) != 0;
                }

                byte[] path = BuildLogixTagPath(tagName);
                byte pathWords = (byte)((path.Length + 1) / 2);

                var cip = new List<byte>();
                cip.Add(0x4C);
                cip.Add(pathWords);
                cip.AddRange(path);
                cip.AddRange(new byte[] { 0x01, 0x00 });

                byte[] cipReply = SendConnectedCip(cip.ToArray(), recvBuffer: 256);

                if (!TryParseCipReply(cipReply, out byte status, out int dataStart))
                    throw new InvalidOperationException("Malformed CIP reply.");
                if (status != 0)
                    throw new InvalidOperationException($"CIP general status error: {status}");

                // [type(2)][value...] or [type(2)][count(2)][value...]
                int valueOffset;
                if (dataStart + 2 + 2 + 1 <= cipReply.Length)
                {
                    ushort maybeCount = BitConverter.ToUInt16(cipReply, dataStart + 2);
                    valueOffset = maybeCount >= 1 && maybeCount <= 0x4000 ? dataStart + 4 : dataStart + 2;
                }
                else valueOffset = dataStart + 2;

                if (valueOffset + 1 > cipReply.Length)
                    throw new InvalidOperationException("CIP reply does not contain a BOOL value.");

                return cipReply[valueOffset] != 0;
            }
            catch (Exception e)
            {
                Console.WriteLine($"error ReadBool: {e.Message}");
                return false;
            }
        }

        public bool WriteBool(string tagName, bool value)
        {
            try
            {
                if (TryParseBitSuffix(tagName, out string baseTag, out int bit))
                {
                    int cur = ReadDint(baseTag);
                    if (cur < 0) throw new InvalidOperationException("Failed to read base DINT.");
                    int mask = 1 << bit;
                    int next = value ? cur | mask : cur & ~mask;
                    return WriteDint(baseTag, next);
                }

                byte[] path = BuildLogixTagPath(tagName);
                byte pathWords = (byte)((path.Length + 1) / 2);

                var cip = new List<byte>();
                cip.Add(0x4D);
                cip.Add(pathWords);
                cip.AddRange(path);
                cip.AddRange(new byte[] { 0xC1, 0x00 });          // BOOL type
                cip.AddRange(new byte[] { 0x01, 0x00 });          // elements = 1
                cip.Add(value ? (byte)1 : (byte)0);               // data
                cip.Add(0x00);                                    // pad to even (optional but safe)

                byte[] cipReply = SendConnectedCip(cip.ToArray());

                if (!TryParseCipReply(cipReply, out byte status, out _))
                    throw new InvalidOperationException("Malformed CIP reply.");

                return status == 0;
            }
            catch (Exception e)
            {
                Console.WriteLine($"error WriteBool: {e.Message}");
                return false;
            }
        }


        // - Helper Methods -----------------------------------------------------------------------------------------

        private byte[] SendConnectedCip(byte[] cip, int recvBuffer = 512)
        {
            // Connected Data Item = [seq(2)] + CIP(...)
            ushort seq = NextSequence();
            byte[] seqBytes = BitConverter.GetBytes(seq);

            ushort connectedItemLen = (ushort)(2 + cip.Length);
            byte[] connectedItemLenBytes = BitConverter.GetBytes(connectedItemLen);

            // Encapsulation payload length after 24-byte header:
            // interface(4) + timeout(2) + itemCount(2)
            // + A1(type+len+connId) + B1(type+len+data)
            int encapPayloadLen =
                4 + 2 + 2 +
                2 + 2 + 4 +
                2 + 2 + connectedItemLen;

            byte[] messageLengthBytes = BitConverter.GetBytes((ushort)encapPayloadLen);

            byte[] eipSessionHandleBytes = BitConverter.GetBytes(eipSessionHandle);
            byte[] cipConnectionIDBytes = BitConverter.GetBytes(cipConnectionID);

            var req = new List<byte>(24 + encapPayloadLen);

            // Encapsulation header (24)
            req.AddRange(new byte[] { 0x70, 0x00 });  // SendUnitData
            req.AddRange(messageLengthBytes);
            req.AddRange(eipSessionHandleBytes);
            req.AddRange(new byte[4]);  // status
            req.AddRange(new byte[8]);  // sender context
            req.AddRange(new byte[4]);  // options

            // Encapsulation payload
            req.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x00 }); // interface handle
            req.AddRange(new byte[] { 0x00, 0x00 });             // timeout
            req.AddRange(new byte[] { 0x02, 0x00 });             // item count

            // Item 1: A1 connected address
            req.AddRange(new byte[] { 0xA1, 0x00, 0x04, 0x00 });
            req.AddRange(cipConnectionIDBytes);

            // Item 2: B1 connected data
            req.AddRange(new byte[] { 0xB1, 0x00 });
            req.AddRange(connectedItemLenBytes);
            req.AddRange(seqBytes);
            req.AddRange(cip);

            sock.Send(req.ToArray());

            byte[] resp = new byte[recvBuffer];
            int bytesReceived = sock.Receive(resp);
            if (bytesReceived <= 0) throw new InvalidOperationException("No response.");

            // Parse encapsulation body to find the B1 item and return its CIP payload (skip seq)
            // Layout after 24 bytes: interface(4), timeout(2), itemCount(2), items...
            int p = 24 + 6;
            ushort itemCount = BitConverter.ToUInt16(resp, p);
            p += 2;

            for (int i = 0; i < itemCount; i++)
            {
                ushort typeId = BitConverter.ToUInt16(resp, p);
                ushort len = BitConverter.ToUInt16(resp, p + 2);
                p += 4;

                if (p + len > bytesReceived) throw new InvalidOperationException("Malformed response.");

                if (typeId == 0x00B1)
                {
                    if (len < 2) throw new InvalidOperationException("B1 item too short.");
                    int cipStart = p + 2;           // skip connected sequence
                    int cipLen = len - 2;

                    var cipReply = new byte[cipLen];
                    Buffer.BlockCopy(resp, cipStart, cipReply, 0, cipLen);
                    return cipReply;
                }

                p += len;
            }

            throw new InvalidOperationException("No connected data item (0x00B1) in response.");
        }


        private static bool TryParseCipReply(byte[] cip, out byte generalStatus, out int replyDataStart)
        {
            generalStatus = 0xFF;
            replyDataStart = 0;

            // CIP reply: [replySvc][reserved][genStatus][addStatusWords]...
            if (cip.Length < 4) return false;

            generalStatus = cip[2];
            byte addStatusWords = cip[3];

            int headerBytes = 4 + addStatusWords * 2;
            if (cip.Length < headerBytes) return false;

            replyDataStart = headerBytes;
            return true;
        }


        private static bool TryParseBitSuffix(string tag, out string baseTag, out int bit)
        {
            baseTag = tag;
            bit = 0;

            int dot = tag.LastIndexOf('.');
            if (dot < 0) return false;

            string suffix = tag[(dot + 1)..];
            if (!int.TryParse(suffix, out int b)) return false;
            if (b < 0 || b > 31) throw new ArgumentOutOfRangeException(nameof(tag), "BOOL bit must be 0–31");

            bit = b;
            baseTag = tag[..dot];
            return true;
        }


        private ushort NextSequence()
        {
            unchecked { _seq++; }
            if (_seq == 0) _seq = 1;
            return _seq;
        }


        private static byte[] BuildLogixTagPath(string tag)
        {
            // Supports: Tag, Tag[1], Tag.Member, Tag[1].Member, etc.
            var path = new List<byte>();
            var parts = tag.Split('.');

            foreach (var part0 in parts)
            {
                string part = part0;

                while (true)
                {
                    int lb = part.IndexOf('[');
                    if (lb < 0)
                    {
                        AddSymbol(path, part);
                        break;
                    }

                    // Symbol before [
                    string sym = part.Substring(0, lb);
                    if (!string.IsNullOrEmpty(sym))
                        AddSymbol(path, sym);

                    int rb = part.IndexOf(']', lb + 1);
                    if (rb < 0)
                        throw new ArgumentException($"Invalid tag (missing ]): {tag}");

                    string idxStr = part.Substring(lb + 1, rb - lb - 1);
                    if (!int.TryParse(idxStr, out int idx) || idx < 0)
                        throw new ArgumentException($"Invalid array index '{idxStr}' in tag: {tag}");

                    AddArrayIndex(path, idx);

                    part = part.Substring(rb + 1);
                    if (string.IsNullOrEmpty(part))
                        break;
                }
            }

            return path.ToArray();
        }

        private static void AddSymbol(List<byte> path, string symbol)
        {
            byte[] sym = Encoding.ASCII.GetBytes(symbol);
            path.Add(0x91);
            path.Add((byte)sym.Length);
            path.AddRange(sym);
            if ((sym.Length & 1) == 1) path.Add(0x00); // pad to even
        }

        private static void AddArrayIndex(List<byte> path, int index)
        {
            if (index <= byte.MaxValue)
            {
                path.Add(0x28);            // 8-bit index
                path.Add((byte)index);
            }
            else if (index <= ushort.MaxValue)
            {
                path.Add(0x29);            // 16-bit index
                path.AddRange(BitConverter.GetBytes((ushort)index));
            }
            else
            {
                path.Add(0x2A);            // 32-bit index
                path.AddRange(BitConverter.GetBytes(index));
            }
        }


        public void Dispose()
        {
            if (sock != null)
            {
                sock.Close();
                sock.Dispose();
            }
        }
    }
}
