using System;
using System.IO;
using System.Threading;
using Shared;
using Lidgren.Network;
using ProtoBuf;

namespace LiteClient
{
    public static class Program
    {
        public static string Location { get { return AppDomain.CurrentDomain.BaseDirectory; } }

        public static void Main()
        {
            Console.WriteLine("Starting...");

            SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());

            var _config = new NetPeerConfiguration("LITEMPNET")
            {
                Port = new Random().Next(1000, 9999)
            };

            var _client = new NetClient(_config);
            _client.Start();

            var msg = _client.CreateMessage();
            msg.Write("Player");
            _client.Connect("127.0.0.1", 4499, msg);
            _client.RegisterReceivedCallback(ProcessMessages, SynchronizationContext.Current);

            while (true)
            {
            }
        }

        public static void ProcessMessages(object sender)
        {
            Console.WriteLine("Received message.");

            NetPeer peer = (NetPeer)sender;
            NetIncomingMessage msg = peer.ReadMessage();

            PacketType type = (PacketType)msg.ReadInt32();


            Console.WriteLine("Data is " + type);

            switch (type)
            {
                case PacketType.ChatData:
                    {
                        int len = msg.ReadInt32();
                        if (DeserializeBinary<ChatData>(msg.ReadBytes(len)) is ChatData data)
                            Console.WriteLine("Chat: " + data.Message);
                    }
                    break;
                case PacketType.VehiclePositionData:
                    {
                        var len = msg.ReadInt32();
                        var data = DeserializeBinary<VehicleData>(msg.ReadBytes(len)) as VehicleData; // Unnused
                        Console.WriteLine("Updated Vehicle Data");
                    }
                    break;
            }
        }

        public static object DeserializeBinary<T>(byte[] data)
        {
            object output;
            using (var stream = new MemoryStream(data))
            {
                try
                {
                    output = Serializer.Deserialize<T>(stream);
                }
                catch (ProtoException e)
                {
                    Console.WriteLine("ERROR: " + e.Message);
                    return null;
                }
            }
            return output;
        }

        public static byte[] SerializeBinary(object data)
        {
            using (var stream = new MemoryStream())
            {
                stream.SetLength(0);
                Serializer.Serialize(stream, data);
                return stream.ToArray();
            }
        }
    }
}