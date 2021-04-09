using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using Shared;
using Lidgren.Network;
using ProtoBuf;

namespace LiteServer
{
    public class Client
    {
        public NetConnection NetConnection { get; private set; }
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public float Latency { get; set; }
        public ScriptVersion RemoteScriptVersion { get; set; }
        public int GameVersion { get; set; }

        public LVector3 LastKnownPosition { get; internal set; }
        public int Health { get; internal set; }
        public float VehicleHealth { get; internal set; }
        public bool IsInVehicle { get; internal set; }
        public bool Afk { get; set; }

        public DateTime LastUpdate { get; internal set; }

        public Client(NetConnection nc)
        {
            NetConnection = nc;
        }
    }

    public enum NotificationIconType
    {
        Chatbox = 1,
        Email = 2,
        AddFriendRequest = 3,
        Nothing = 4,
        RightJumpingArrow = 7,
        RP_Icon = 8,
        DollarIcon = 9,
    }

    public enum NotificationPicType
    {
        CHAR_DEFAULT, // : Default profile pic
        CHAR_FACEBOOK, // Facebook
        CHAR_SOCIAL_CLUB, // Social Club Star
        CHAR_CARSITE2, // Super Auto San Andreas Car Site
        CHAR_BOATSITE, // Boat Site Anchor
        CHAR_BANK_MAZE, // Maze Bank Logo
        CHAR_BANK_FLEECA, // Fleeca Bank
        CHAR_BANK_BOL, // Bank Bell Icon
        CHAR_MINOTAUR, // Minotaur Icon
        CHAR_EPSILON, // Epsilon E
        CHAR_MILSITE, // Warstock W
        CHAR_CARSITE, // Legendary Motorsports Icon
        CHAR_DR_FRIEDLANDER, // Dr Freidlander Face
        CHAR_BIKESITE, // P&M Logo
        CHAR_LIFEINVADER, // Liveinvader
        CHAR_PLANESITE, // Plane Site E
        CHAR_MICHAEL, // Michael's Face
        CHAR_FRANKLIN, // Franklin's Face
        CHAR_TREVOR, // Trevor's Face
        CHAR_SIMEON, // Simeon's Face
        CHAR_RON, // Ron's Face
        CHAR_JIMMY, // Jimmy's Face
        CHAR_LESTER, // Lester's Shadowed Face
        CHAR_DAVE, // Dave Norton's Face
        CHAR_LAMAR, // Chop's Face
        CHAR_DEVIN, // Devin Weston's Face
        CHAR_AMANDA, // Amanda's Face
        CHAR_TRACEY, // Tracey's Face
        CHAR_STRETCH, // Stretch's Face
        CHAR_WADE, // Wade's Face
        CHAR_MARTIN, // Martin Madrazo's Face
    }

    public class GameServer
    {
        public GameServer(int port, string name, string gamemodeName)
        {
            Clients = new List<Client>();
            MaxPlayers = 32;
            Port = port;
            GamemodeName = gamemodeName;

            Name = name;
            SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
            NetPeerConfiguration config = new NetPeerConfiguration("LITEMPNET");
            config.Port = port;
            config.EnableMessageType(NetIncomingMessageType.ConnectionApproval);
            config.EnableMessageType(NetIncomingMessageType.DiscoveryRequest);
            config.EnableMessageType(NetIncomingMessageType.UnconnectedData);
            config.EnableMessageType(NetIncomingMessageType.ConnectionLatencyUpdated);
            Server = new NetServer(config);
        }

        public NetServer Server;

        public int MaxPlayers { get; set; }
        public int Port { get; set; }
        public List<Client> Clients { get; set; }
        public string Name { get; set; }
        public string Password { get; set; }
        public bool PasswordProtected { get; set; }
        public string GamemodeName { get; set; }
        public string MasterServer { get; set; }
        public bool AnnounceSelf { get; set; }

        public bool AllowDisplayNames { get; set; }

        public readonly ScriptVersion ServerVersion = ScriptVersion.VERSION_0_1_2_1;

        private ServerScript _gamemode { get; set; }
        private List<ServerScript> _filterscripts;
        private DateTime _lastAnnounceDateTime;

        public void Start(string[] filterscripts)
        {
            Server.Start();

            if (AnnounceSelf)
            {
                _lastAnnounceDateTime = DateTime.Now;
                Log.LogToConsole(0, "MasterServer", "Announcing to master server...");
                AnnounceSelfToMaster();
            }

            if (GamemodeName.ToLower() != "freeroam")
            {
                try
                {
                    Log.LogToConsole(0, "Server", "Loading gamemode...");

                    try
                    {
                        Program.DeleteFile(Program.Location + "gamemodes" + Path.DirectorySeparatorChar + GamemodeName + ".dll:Zone.Identifier");
                    }
                    catch
                    {
                    }

                    var asm = Assembly.LoadFrom(Program.Location + "gamemodes" + Path.DirectorySeparatorChar + GamemodeName + ".dll");
                    var types = asm.GetExportedTypes();
                    var validTypes = types.Where(t =>
                        !t.IsInterface &&
                        !t.IsAbstract)
                        .Where(t => typeof(ServerScript).IsAssignableFrom(t));
                    if (!validTypes.Any())
                    {
                        Log.LogToConsole(3, "Server", "No classes that inherit from ServerScript have been found in the assembly. Starting freeroam.");
                        return;
                    }

                    _gamemode = Activator.CreateInstance(validTypes.ToArray()[0]) as ServerScript;
                    if (_gamemode == null)
                        Log.LogToConsole(4, "Server", "Could not create gamemode: it is null.");
                    else
                        _gamemode.Start();
                }
                catch (Exception e)
                {
                    Console.WriteLine("ERROR: Error while loading script: " + e.Message + " at " + e.Source +
                                      ".\nStack Trace:" + e.StackTrace);
                    Console.WriteLine("Inner Exception: ");
                    Exception r = e;
                    while (r != null && r.InnerException != null)
                    {
                        Console.WriteLine("at " + r.InnerException);
                        r = r.InnerException;
                    }
                }
            }

            Log.LogToConsole(0, "Server", "Loading filterscripts..");
            var list = new List<ServerScript>();
            foreach (var path in filterscripts)
            {
                if (string.IsNullOrWhiteSpace(path)) continue;

                try
                {
                    try
                    {
                        Program.DeleteFile(Program.Location + "filterscripts" + Path.DirectorySeparatorChar + GamemodeName + ".dll:Zone.Identifier");
                    }
                    catch
                    {
                    }

                    var fsAsm = Assembly.LoadFrom(Program.Location + "filterscripts" + Path.DirectorySeparatorChar + path + ".dll");
                    var fsObj = InstantiateScripts(fsAsm);
                    list.AddRange(fsObj);
                }
                catch (Exception ex)
                {
                    Log.LogToConsole(4, "Server", "Failed to load filterscript \"" + path + "\", error: " + ex.Message);
                }
            }

            list.ForEach(fs =>
            {
                fs.Start();
                Log.LogToConsole(2, "Server", "Starting filterscript " + fs.Name + "...");
            });
            _filterscripts = list;
        }

        public void AnnounceSelfToMaster()
        {
            using (var wb = new WebClient())
            {
                try
                {
                    wb.UploadData(MasterServer, Encoding.UTF8.GetBytes(Port.ToString()));
                }
                catch (WebException)
                {
                    Log.LogToConsole(4, "Server", "Failed to announce self: master server is not available at this time.");
                }
            }
        }

        private bool isIPLocal(string ipaddress)
        {
            string[] straryIPAddress = ipaddress.ToString().Split(new String[] { "." }, StringSplitOptions.RemoveEmptyEntries);
            int[] iaryIPAddress = new int[] { int.Parse(straryIPAddress[0]), int.Parse(straryIPAddress[1]), int.Parse(straryIPAddress[2]), int.Parse(straryIPAddress[3]) };

            return (iaryIPAddress[0] == 10 || (iaryIPAddress[0] == 192 && iaryIPAddress[1] == 168) || (iaryIPAddress[0] == 172 && iaryIPAddress[1] >= 16 && iaryIPAddress[1] <= 31));
        }

        private IEnumerable<ServerScript> InstantiateScripts(Assembly targetAssembly)
        {
            var types = targetAssembly.GetExportedTypes();
            var validTypes = types.Where(t =>
                !t.IsInterface &&
                !t.IsAbstract)
                .Where(t => typeof(ServerScript).IsAssignableFrom(t));
            if (!validTypes.Any())
            {
                yield break;
            }
            foreach (var type in validTypes)
            {
                ServerScript obj = Activator.CreateInstance(type) as ServerScript;
                if (obj != null)
                    yield return obj;
            }
        }

        public void Tick()
        {
            if (AnnounceSelf && DateTime.Now.Subtract(_lastAnnounceDateTime).TotalMinutes >= 5)
            {
                _lastAnnounceDateTime = DateTime.Now;
                AnnounceSelfToMaster();
            }

            List<NetIncomingMessage> messages = new List<NetIncomingMessage>();
            int msgsRead = Server.ReadMessages(messages);
            if (msgsRead > 0)
                foreach (var msg in messages)
                {
                    //Console.WriteLine("messages: " + messages + " | msgsRead: " + msgsRead + " | msg: " + msg);
                    Client client = null;
                    lock (Clients)
                    {
                        foreach (Client c in Clients)
                        {
                            if (c != null && c.NetConnection != null &&
                                c.NetConnection.RemoteUniqueIdentifier != 0 &&
                                msg.SenderConnection != null &&
                                c.NetConnection.RemoteUniqueIdentifier == msg.SenderConnection.RemoteUniqueIdentifier)
                            {
                                client = c;
                                break;
                            }
                        }
                    }

                    if (client == null) client = new Client(msg.SenderConnection);

                    switch (msg.MessageType)
                    {
                        case NetIncomingMessageType.UnconnectedData:
                            string isPing = msg.ReadString();
                            if (isPing == "ping")
                            {
                                Log.LogToConsole(0, "Server", "INFO: ping received from " + msg.SenderEndPoint.Address.ToString());
                                NetOutgoingMessage pong = Server.CreateMessage();
                                pong.Write("pong");
                                Server.SendMessage(pong, client.NetConnection, NetDeliveryMethod.ReliableOrdered);
                            }
                            if (isPing == "query")
                            {
                                int playersonline = 0;
                                lock (Clients) playersonline = Clients.Count;
                                Log.LogToConsole(0, "Server", "INFO: query received from " + msg.SenderEndPoint.Address.ToString());
                                NetOutgoingMessage pong = Server.CreateMessage();
                                pong.Write(Name + "%" + PasswordProtected + "%" + playersonline + "%" + MaxPlayers + "%" + GamemodeName);
                                Server.SendMessage(pong, client.NetConnection, NetDeliveryMethod.ReliableOrdered);
                            }
                            break;
                        case NetIncomingMessageType.VerboseDebugMessage:
                            Log.LogToConsole(1, "NetworkVerboseDebug", msg.ReadString());
                            break;
                        case NetIncomingMessageType.DebugMessage:
                            Log.LogToConsole(1, "DebugMessage", msg.ReadString());
                            break;
                        case NetIncomingMessageType.WarningMessage:
                            Log.LogToConsole(3, "NetworkWarning", msg.ReadString());
                            break;
                        case NetIncomingMessageType.ErrorMessage:
                            Log.LogToConsole(4, "NetworkError", msg.ReadString());
                            break;
                        case NetIncomingMessageType.ConnectionLatencyUpdated:
                            client.Latency = msg.ReadFloat();
                            break;
                        case NetIncomingMessageType.ConnectionApproval:
                            var type = msg.ReadInt32();
                            var leng = msg.ReadInt32();

                            if (!(DeserializeBinary<ConnectionRequest>(msg.ReadBytes(leng)) is ConnectionRequest connReq))
                            {
                                client.NetConnection.Deny("Connection Object is null");
                                Server.Recycle(msg);
                                continue;
                            }

                            if ((ScriptVersion)connReq.ScriptVersion == ScriptVersion.Unknown)
                            {
                                client.NetConnection.Deny("Unknown version. Please update your client.");
                                Server.Recycle(msg);
                                continue;
                            } else if ((ScriptVersion)connReq.ScriptVersion != ServerVersion)
                            {
                                client.NetConnection.Deny("Wrong version. Server version: " + ServerVersion);
                                Server.Recycle(msg);
                                continue;
                            }

                            int clients = 0;
                            lock (Clients) clients = Clients.Count;
                            if (clients < MaxPlayers)
                            {
                                if (PasswordProtected && !string.IsNullOrWhiteSpace(Password))
                                {
                                    if (Password != connReq.Password)
                                    {
                                        client.NetConnection.Deny("Wrong password.");

                                        if (_gamemode != null) _gamemode.OnConnectionRefused(client, "Wrong password");
                                        if (_filterscripts != null) _filterscripts.ForEach(fs => fs.OnConnectionRefused(client, "Wrong password"));

                                        Server.Recycle(msg);

                                        continue;
                                    }
                                }

                                lock (Clients)
                                {
                                    int duplicate = 0;
                                    string displayname = connReq.DisplayName;
                                    while (AllowDisplayNames && Clients.Any(c => c.DisplayName == connReq.DisplayName))
                                    {
                                        duplicate++;

                                        connReq.DisplayName = displayname + " (" + duplicate + ")";
                                    }

                                    Clients.Add(client);
                                }

                                client.Name = connReq.Name;
                                client.DisplayName = AllowDisplayNames ? connReq.DisplayName : connReq.Name;

                                if (client.RemoteScriptVersion != (ScriptVersion)connReq.ScriptVersion) client.RemoteScriptVersion = (ScriptVersion)connReq.ScriptVersion;
                                if (client.GameVersion != connReq.GameVersion) client.GameVersion = connReq.GameVersion;

                                var channelHail = Server.CreateMessage();
                                channelHail.Write(GetChannelIdForConnection(client));
                                client.NetConnection.Approve(channelHail);

                                if (_gamemode != null)
                                    _gamemode.OnIncomingConnection(client);

                                if (_filterscripts != null)
                                    _filterscripts.ForEach(fs => fs.OnIncomingConnection(client));

                                Log.LogToConsole(0, "New incoming connection", client.Name + " (" + client.DisplayName + ")");
                            }
                            else
                            {
                                client.NetConnection.Deny("No available player slots.");
                                if (_gamemode != null)
                                    _gamemode.OnConnectionRefused(client, "Server is full");

                                if (_filterscripts != null)
                                    _filterscripts.ForEach(fs => fs.OnConnectionRefused(client, "Server is full"));
                            }
                            break;
                        case NetIncomingMessageType.StatusChanged:
                            NetConnectionStatus newStatus = (NetConnectionStatus)msg.ReadByte();

                            if (newStatus == NetConnectionStatus.Connected)
                            {
                                bool sendMsg = true;

                                if (_gamemode != null)
                                    sendMsg = sendMsg && _gamemode.OnPlayerConnect(client);

                                if (_filterscripts != null)
                                    _filterscripts.ForEach(fs => sendMsg = sendMsg && fs.OnPlayerConnect(client));

                                if (sendMsg)
                                    SendNotificationToAll("Player ~h~" + client.DisplayName + "~h~ has connected.");

                                Log.LogToConsole(0, "New player connected", client.Name + " (" + client.DisplayName + ")");
                            }
                            else if (newStatus == NetConnectionStatus.Disconnected)
                            {
                                lock (Clients)
                                {
                                    if (Clients.Contains(client))
                                    {
                                        bool sendMsg = true;

                                        if (_gamemode != null)
                                            sendMsg = sendMsg && _gamemode.OnPlayerDisconnect(client);

                                        if (_filterscripts != null)
                                            _filterscripts.ForEach(fs => sendMsg = sendMsg && fs.OnPlayerDisconnect(client));

                                        if (sendMsg)
                                            SendNotificationToAll("Player ~h~" + client.DisplayName + "~h~ has disconnected.");

                                        PlayerDisconnect dcObj = new PlayerDisconnect()
                                        {
                                            Id = client.NetConnection.RemoteUniqueIdentifier,
                                        };

                                        SendToAll(dcObj, PacketType.PlayerDisconnect, true);

                                        Log.LogToConsole(0, "Player disconnected: ", client.Name + " (" + client.DisplayName + ")");

                                        Clients.Remove(client);
                                    }
                                }
                            }
                            break;
                        case NetIncomingMessageType.DiscoveryRequest:
                            NetOutgoingMessage response = Server.CreateMessage();
                            DiscoveryResponse obj = new DiscoveryResponse
                            {
                                ServerName = Name,
                                MaxPlayers = MaxPlayers,
                                PasswordProtected = PasswordProtected,
                                Gamemode = GamemodeName
                            };
                            lock (Clients) obj.PlayerCount = (short)Clients.Count(c => DateTime.Now.Subtract(c.LastUpdate).TotalMilliseconds < 60000);
                            obj.Port = Port;

                            obj.LAN = isIPLocal(msg.SenderEndPoint.Address.ToString());
                            byte[] bin = SerializeBinary(obj);

                            response.Write((int)PacketType.DiscoveryResponse);
                            response.Write(bin.Length);
                            response.Write(bin);

                            Server.SendDiscoveryResponse(response, msg.SenderEndPoint);
                            break;
                        case NetIncomingMessageType.Data:
                            var packetType = (PacketType)msg.ReadInt32();

                            switch (packetType)
                            {
                                case PacketType.ChatData:
                                    {
                                        try
                                        {
                                            int len = msg.ReadInt32();
                                            if (DeserializeBinary<ChatData>(msg.ReadBytes(len)) is ChatData data)
                                            {
                                                bool pass = true;
                                                if (_gamemode != null) pass = _gamemode.OnChatMessage(client, data.Message);

                                                if (_filterscripts != null) _filterscripts.ForEach(fs => pass = pass && fs.OnChatMessage(client, data.Message));

                                                if (pass)
                                                {
                                                    data.Id = client.NetConnection.RemoteUniqueIdentifier;
                                                    data.Sender = client.DisplayName;
                                                    SendToAll(data, PacketType.ChatData, true);
                                                    Console.WriteLine(data.Sender + ": " + data.Message);
                                                }
                                            }
                                        }
                                        catch (IndexOutOfRangeException)
                                        { }
                                    }
                                    break;
                                case PacketType.VehiclePositionData:
                                    {
                                        try
                                        {
                                            int len = msg.ReadInt32();
                                            if (DeserializeBinary<VehicleData>(msg.ReadBytes(len)) is VehicleData data)
                                            {
                                                data.Id = client.NetConnection.RemoteUniqueIdentifier;
                                                data.Name = client.DisplayName;
                                                data.Latency = client.Latency;

                                                client.Health = data.PlayerHealth;
                                                client.LastKnownPosition = data.Position;
                                                client.VehicleHealth = data.VehicleHealth;
                                                client.IsInVehicle = true;
                                                client.LastUpdate = DateTime.Now;

                                                SendToAll(data, PacketType.VehiclePositionData, false, client);
                                            }
                                        }
                                        catch (IndexOutOfRangeException)
                                        { }
                                    }
                                    break;
                                case PacketType.PedPositionData:
                                    {
                                        try
                                        {
                                            int len = msg.ReadInt32();
                                            if (DeserializeBinary<PedData>(msg.ReadBytes(len)) is PedData data)
                                            {
                                                data.Id = client.NetConnection.RemoteUniqueIdentifier;
                                                data.Name = client.DisplayName;
                                                data.Latency = client.Latency;

                                                client.Health = data.PlayerHealth;
                                                client.LastKnownPosition = data.Position;
                                                client.IsInVehicle = false;
                                                client.LastUpdate = DateTime.Now;

                                                SendToAll(data, PacketType.PedPositionData, false, client);
                                            }
                                        }
                                        catch (IndexOutOfRangeException)
                                        { }
                                    }
                                    break;
                                case PacketType.NpcVehPositionData:
                                    {
                                        try
                                        {
                                            int len = msg.ReadInt32();
                                            if (DeserializeBinary<VehicleData>(msg.ReadBytes(len)) is VehicleData data)
                                            {
                                                data.Id = client.NetConnection.RemoteUniqueIdentifier;
                                                SendToAll(data, PacketType.NpcVehPositionData, false, client);
                                            }
                                        }
                                        catch (IndexOutOfRangeException)
                                        { }
                                    }
                                    break;
                                case PacketType.NpcPedPositionData:
                                    {
                                        try
                                        {
                                            int len = msg.ReadInt32();
                                            if (DeserializeBinary<PedData>(msg.ReadBytes(len)) is PedData data)
                                            {
                                                data.Id = msg.SenderConnection.RemoteUniqueIdentifier;
                                                SendToAll(data, PacketType.NpcPedPositionData, false, client);
                                            }
                                        }
                                        catch (IndexOutOfRangeException)
                                        { }
                                    }
                                    break;
                                case PacketType.WorldSharingStop:
                                    {
                                        PlayerDisconnect dcObj = new PlayerDisconnect()
                                        {
                                            Id = client.NetConnection.RemoteUniqueIdentifier,
                                        };
                                        SendToAll(dcObj, PacketType.WorldSharingStop, true);
                                    }
                                    break;
                                case PacketType.NativeResponse:
                                    {
                                        int len = msg.ReadInt32();
                                        if (!(DeserializeBinary<NativeResponse>(msg.ReadBytes(len)) is NativeResponse data) || !_callbacks.ContainsKey(data.Id)) continue;
                                        object resp = null;
                                        if (data.Response is IntArgument argument)
                                        {
                                            resp = argument.Data;
                                        }
                                        else if (data.Response is UIntArgument argument1)
                                        {
                                            resp = argument1.Data;
                                        }
                                        else if (data.Response is StringArgument argument2)
                                        {
                                            resp = argument2.Data;
                                        }
                                        else if (data.Response is FloatArgument argument3)
                                        {
                                            resp = argument3.Data;
                                        }
                                        else if (data.Response is BooleanArgument argument4)
                                        {
                                            resp = argument4.Data;
                                        }
                                        else if (data.Response is Vector3Argument tmp)
                                        {
                                            resp = new LVector3()
                                            {
                                                X = tmp.X,
                                                Y = tmp.Y,
                                                Z = tmp.Z,
                                            };
                                        }
                                        if (_callbacks.ContainsKey(data.Id))
                                            _callbacks[data.Id].Invoke(resp);
                                        _callbacks.Remove(data.Id);
                                    }
                                    break;
                                case PacketType.PlayerKilled:
                                    {
                                        if (_gamemode != null)
                                            _gamemode.OnPlayerKilled(client);

                                        if (_filterscripts != null)
                                            _filterscripts.ForEach(fs => fs.OnPlayerKilled(client));
                                    }
                                    break;
                            }
                            break;
                        default:
                            Log.LogToConsole(3, "Unhandled type", "" + msg.MessageType);
                            break;
                    }
                    Server.Recycle(msg);
                }
            if (_gamemode != null)
                _gamemode.OnTick();

            if (_filterscripts != null)
                _filterscripts.ForEach(fs => fs.OnTick());
        }

        public void SendToAll(object newData, PacketType packetType, bool important)
        {
            byte[] data = SerializeBinary(newData);
            NetOutgoingMessage msg = Server.CreateMessage();
            msg.Write((int)packetType);
            msg.Write(data.Length);
            msg.Write(data);
            Server.SendToAll(msg, important ? NetDeliveryMethod.ReliableOrdered : NetDeliveryMethod.ReliableSequenced);
        }

        public void SendToAll(object newData, PacketType packetType, bool important, Client exclude)
        {
            byte[] data = SerializeBinary(newData);
            NetOutgoingMessage msg = Server.CreateMessage();
            msg.Write((int)packetType);
            msg.Write(data.Length);
            msg.Write(data);
            Server.SendToAll(msg, exclude.NetConnection, important ? NetDeliveryMethod.ReliableOrdered : NetDeliveryMethod.ReliableSequenced, GetChannelIdForConnection(exclude));
        }

        public object DeserializeBinary<T>(byte[] data)
        {
            using (MemoryStream stream = new MemoryStream(data))
            {
                try
                {
                    return Serializer.Deserialize<T>(stream);
                }
                catch (ProtoException e)
                {
                    Log.LogToConsole(0, "Deserialization failed", e.Message);
                    return null;
                }
            }
        }

        public byte[] SerializeBinary(object data)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                Serializer.Serialize(stream, data);
                return stream.ToArray();
            }
        }

        public int GetChannelIdForConnection(Client conn)
        {
            lock (Clients) return (Clients.IndexOf(conn) % 31) + 1;
        }

        private List<NativeArgument> ParseNativeArguments(params object[] args)
        {
            var list = new List<NativeArgument>();
            foreach (var o in args)
            {
                if (o is int @int)
                {
                    list.Add(new IntArgument() { Data = @int });
                }
                else if (o is uint int1)
                {
                    list.Add(new UIntArgument() { Data = int1 });
                }
                else if (o is string @string)
                {
                    list.Add(new StringArgument() { Data = @string });
                }
                else if (o is float single)
                {
                    list.Add(new FloatArgument() { Data = single });
                }
                else if (o is bool boolean)
                {
                    list.Add(new BooleanArgument() { Data = boolean });
                }
                else if (o is LVector3 tmp)
                {
                    list.Add(new Vector3Argument()
                    {
                        X = tmp.X,
                        Y = tmp.Y,
                        Z = tmp.Z,
                    });
                }
                else if (o is LocalPlayerArgument argument)
                {
                    list.Add(argument);
                }
                else if (o is OpponentPedHandleArgument argument1)
                {
                    list.Add(argument1);
                }
                else if (o is LocalGamePlayerArgument argument2)
                {
                    list.Add(argument2);
                }
            }

            return list;
        }

        public void SendNativeCallToPlayer(Client player, ulong hash, params object[] arguments)
        {
            NativeData obj = new NativeData
            {
                Hash = hash,
                Arguments = ParseNativeArguments(arguments)
            };

            byte[] bin = SerializeBinary(obj);

            NetOutgoingMessage msg = Server.CreateMessage();

            msg.Write((int)PacketType.NativeCall);
            msg.Write(bin.Length);
            msg.Write(bin);

            player.NetConnection.SendMessage(msg, NetDeliveryMethod.ReliableOrdered, GetChannelIdForConnection(player));
        }

        public void SendNativeCallToAllPlayers(ulong hash, params object[] arguments)
        {
            NativeData obj = new NativeData
            {
                Hash = hash,
                Arguments = ParseNativeArguments(arguments),
                ReturnType = null,
                Id = null
            };

            byte[] bin = SerializeBinary(obj);

            NetOutgoingMessage msg = Server.CreateMessage();

            msg.Write((int)PacketType.NativeCall);
            msg.Write(bin.Length);
            msg.Write(bin);

            Server.SendToAll(msg, NetDeliveryMethod.ReliableOrdered);
        }

        public void SetNativeCallOnTickForPlayer(Client player, string identifier, ulong hash, params object[] arguments)
        {
            NativeData obj = new NativeData
            {
                Hash = hash,

                Arguments = ParseNativeArguments(arguments)
            };

            NativeTickCall wrapper = new NativeTickCall
            {
                Identifier = identifier,
                Native = obj
            };

            byte[] bin = SerializeBinary(wrapper);

            NetOutgoingMessage msg = Server.CreateMessage();

            msg.Write((int)PacketType.NativeTick);
            msg.Write(bin.Length);
            msg.Write(bin);

            player.NetConnection.SendMessage(msg, NetDeliveryMethod.ReliableOrdered, GetChannelIdForConnection(player));
        }

        public void SetNativeCallOnTickForAllPlayers(string identifier, ulong hash, params object[] arguments)
        {
            NativeData obj = new NativeData
            {
                Hash = hash,

                Arguments = ParseNativeArguments(arguments)
            };

            NativeTickCall wrapper = new NativeTickCall
            {
                Identifier = identifier,
                Native = obj
            };

            byte[] bin = SerializeBinary(wrapper);

            NetOutgoingMessage msg = Server.CreateMessage();

            msg.Write((int)PacketType.NativeTick);
            msg.Write(bin.Length);
            msg.Write(bin);

            Server.SendToAll(msg, NetDeliveryMethod.ReliableOrdered);
        }

        public void RecallNativeCallOnTickForPlayer(Client player, string identifier)
        {
            NativeTickCall wrapper = new NativeTickCall
            {
                Identifier = identifier
            };

            byte[] bin = SerializeBinary(wrapper);

            NetOutgoingMessage msg = Server.CreateMessage();
            msg.Write((int)PacketType.NativeTickRecall);
            msg.Write(bin.Length);
            msg.Write(bin);

            player.NetConnection.SendMessage(msg, NetDeliveryMethod.ReliableOrdered, GetChannelIdForConnection(player));
        }

        public void RecallNativeCallOnTickForAllPlayers(string identifier)
        {
            NativeTickCall wrapper = new NativeTickCall();
            wrapper.Identifier = identifier;

            byte[] bin = SerializeBinary(wrapper);

            NetOutgoingMessage msg = Server.CreateMessage();
            msg.Write((int)PacketType.NativeTickRecall);
            msg.Write(bin.Length);
            msg.Write(bin);

            Server.SendToAll(msg, NetDeliveryMethod.ReliableOrdered);
        }

        public void SetNativeCallOnDisconnectForPlayer(Client player, string identifier, ulong hash, params object[] arguments)
        {
            var obj = new NativeData
            {
                Hash = hash,
                Id = identifier,
                Arguments = ParseNativeArguments(arguments)
            };


            byte[] bin = SerializeBinary(obj);

            NetOutgoingMessage msg = Server.CreateMessage();

            msg.Write((int)PacketType.NativeOnDisconnect);
            msg.Write(bin.Length);
            msg.Write(bin);

            player.NetConnection.SendMessage(msg, NetDeliveryMethod.ReliableOrdered, GetChannelIdForConnection(player));
        }

        public void SetNativeCallOnDisconnectForAllPlayers(string identifier, ulong hash, params object[] arguments)
        {
            var obj = new NativeData
            {
                Hash = hash,
                Id = identifier,
                Arguments = ParseNativeArguments(arguments)
            };

            byte[] bin = SerializeBinary(obj);

            NetOutgoingMessage msg = Server.CreateMessage();

            msg.Write((int)PacketType.NativeOnDisconnect);
            msg.Write(bin.Length);
            msg.Write(bin);

            Server.SendToAll(msg, NetDeliveryMethod.ReliableOrdered);
        }

        public void RecallNativeCallOnDisconnectForPlayer(Client player, string identifier)
        {
            var obj = new NativeData
            {
                Id = identifier
            };

            byte[] bin = SerializeBinary(obj);

            NetOutgoingMessage msg = Server.CreateMessage();
            msg.Write((int)PacketType.NativeOnDisconnectRecall);
            msg.Write(bin.Length);
            msg.Write(bin);

            player.NetConnection.SendMessage(msg, NetDeliveryMethod.ReliableOrdered, GetChannelIdForConnection(player));
        }

        public void RecallNativeCallOnDisconnectForAllPlayers(string identifier)
        {
            var obj = new NativeData
            {
                Id = identifier
            };

            byte[] bin = SerializeBinary(obj);

            NetOutgoingMessage msg = Server.CreateMessage();
            msg.Write((int)PacketType.NativeOnDisconnectRecall);
            msg.Write(bin.Length);
            msg.Write(bin);

            Server.SendToAll(msg, NetDeliveryMethod.ReliableOrdered);
        }

        private Dictionary<string, Action<object>> _callbacks = new Dictionary<string, Action<object>>();
        public void GetNativeCallFromPlayer(Client player, string salt, ulong hash, NativeArgument returnType, Action<object> callback,
            params object[] arguments)
        {
            var obj = new NativeData
            {
                Hash = hash,
                ReturnType = returnType
            };

            salt = Environment.TickCount.ToString() + salt + player.NetConnection.RemoteUniqueIdentifier.ToString() + DateTime.Now.Subtract(new DateTime(1970, 1, 1, 0, 0, 0)).TotalMilliseconds.ToString();
            obj.Id = salt;
            obj.Arguments = ParseNativeArguments(arguments);

            byte[] bin = SerializeBinary(obj);

            NetOutgoingMessage msg = Server.CreateMessage();

            msg.Write((int)PacketType.NativeCall);
            msg.Write(bin.Length);
            msg.Write(bin);

            _callbacks.Add(salt, callback);
            player.NetConnection.SendMessage(msg, NetDeliveryMethod.ReliableOrdered, GetChannelIdForConnection(player));
        }

        // SCRIPTING

        public void SendChatMessageToAll(string message)
        {
            SendChatMessageToAll("", message);
        }

        public void SendChatMessageToAll(string sender, string message)
        {
            var chatObj = new ChatData()
            {
                Sender = sender,
                Message = message,
            };

            SendToAll(chatObj, PacketType.ChatData, true);
        }

        public void SendChatMessageToPlayer(Client player, string message)
        {
            SendChatMessageToPlayer(player, "", message);
        }

        public void SendChatMessageToPlayer(Client player, string sender, string message)
        {
            var chatObj = new ChatData()
            {
                Sender = sender,
                Message = message,
            };

            var data = SerializeBinary(chatObj);

            NetOutgoingMessage msg = Server.CreateMessage();
            msg.Write((int)PacketType.ChatData);
            msg.Write(data.Length);
            msg.Write(data);
            player.NetConnection.SendMessage(msg, NetDeliveryMethod.ReliableOrdered, 0);
        }

        public void GivePlayerWeapon(Client player, uint weaponHash, int ammo, bool equipNow, bool ammoLoaded)
        {
            SendNativeCallToPlayer(player, 0xBF0FD6E56C964FCB, new LocalPlayerArgument(), weaponHash, ammo, equipNow, ammo);
        }

        public void KickPlayer(Client player, string reason)
        {
            player.NetConnection.Disconnect("Kicked: " + reason);
        }

        public void SetPlayerPosition(Client player, LVector3 newPosition)
        {
            SendNativeCallToPlayer(player, 0x06843DA7060A026B, new LocalPlayerArgument(), newPosition.X, newPosition.Y, newPosition.Z, 0, 0, 0, 1);
        }

        public void GetPlayerPosition(Client player, Action<object> callback, string salt = "salt")
        {
            GetNativeCallFromPlayer(player, salt, 0x3FEF770D40960D5A, new Vector3Argument(), callback, new LocalPlayerArgument(), 0);
        }

        public void HasPlayerControlBeenPressed(Client player, int controlId, Action<object> callback, string salt = "salt")
        {
            GetNativeCallFromPlayer(player, salt, 0x580417101DDB492F, new BooleanArgument(), callback, 0, controlId);
        }

        public void SetPlayerHealth(Client player, int health)
        {
            SendNativeCallToPlayer(player, 0x6B76DC1F3AE6E6A3, new LocalPlayerArgument(), health + 100);
        }

        public void SendNotificationToPlayer(Client player, string message, bool flashing = false)
        {
            for (int i = 0; i < message.Length; i += 99)
            {
                SendNativeCallToPlayer(player, 0x202709F4C58A0424, "STRING");
                SendNativeCallToPlayer(player, 0x6C188BE134E074AA, message.Substring(i, Math.Min(99, message.Length - i)));
                SendNativeCallToPlayer(player, 0xF020C96915705B3A, flashing, true);
            }
        }

        public void SendNotificationToAll(string message, bool flashing = false)
        {
            for (int i = 0; i < message.Length; i += 99)
            {
                SendNativeCallToAllPlayers(0x202709F4C58A0424, "STRING");
                SendNativeCallToAllPlayers(0x6C188BE134E074AA, message.Substring(i, Math.Min(99, message.Length - i)));
                SendNativeCallToAllPlayers(0xF020C96915705B3A, flashing, true);
            }
        }

        public void SendPictureNotificationToPlayer(Client player, string body, NotificationPicType pic, int flash, NotificationIconType iconType, string sender, string subject)
        {
            //Crash with new LocalPlayerArgument()!
            SendNativeCallToPlayer(player, 0x202709F4C58A0424, "STRING");
            SendNativeCallToPlayer(player, 0x6C188BE134E074AA, body);
            SendNativeCallToPlayer(player, 0x1CCD9A37359072CF, pic.ToString(), pic.ToString(), flash, (int)iconType, sender, subject);
            SendNativeCallToPlayer(player, 0xF020C96915705B3A, false, true);
        }


        public void SendPictureNotificationToPlayer(Client player, string body, string pic, int flash, int iconType, string sender, string subject)
        {
            //Crash with new LocalPlayerArgument()!
            SendNativeCallToPlayer(player, 0x202709F4C58A0424, "STRING");
            SendNativeCallToPlayer(player, 0x6C188BE134E074AA, body);
            SendNativeCallToPlayer(player, 0x1CCD9A37359072CF, pic, pic, flash, iconType, sender, subject);
            SendNativeCallToPlayer(player, 0xF020C96915705B3A, false, true);
        }

        public void SendPictureNotificationToAll(Client player, string body, NotificationPicType pic, int flash, NotificationIconType iconType, string sender, string subject)
        {
            //Crash with new LocalPlayerArgument()!
            SendNativeCallToAllPlayers(0x202709F4C58A0424, "STRING");
            SendNativeCallToAllPlayers(0x6C188BE134E074AA, body);
            SendNativeCallToAllPlayers(0x1CCD9A37359072CF, pic.ToString(), pic.ToString(), flash, (int)iconType, sender, subject);
            SendNativeCallToAllPlayers(0xF020C96915705B3A, false, true);
        }

        public void SendPictureNotificationToAll(Client player, string body, string pic, int flash, int iconType, string sender, string subject)
        {
            //Crash with new LocalPlayerArgument()!
            SendNativeCallToAllPlayers(0x202709F4C58A0424, "STRING");
            SendNativeCallToAllPlayers(0x6C188BE134E074AA, body);
            SendNativeCallToAllPlayers(0x1CCD9A37359072CF, pic, pic, flash, iconType, sender, subject);
            SendNativeCallToAllPlayers(0xF020C96915705B3A, false, true);
        }

        public void GetPlayerHealth(Client player, Action<object> callback, string salt = "salt")
        {
            GetNativeCallFromPlayer(player, salt,
                0xEEF059FAD016D209, new IntArgument(), callback, new LocalPlayerArgument());
        }

        public void ToggleNightVisionForPlayer(Client player, bool status)
        {
            SendNativeCallToPlayer(player, 0x18F621F7A5B1F85D, status);
        }

        public void ToggleNightVisionForAll(Client player, bool status)
        {
            SendNativeCallToAllPlayers(0x18F621F7A5B1F85D, status);
        }

        public void IsNightVisionActive(Client player, Action<object> callback, string salt = "salt")
        {
            GetNativeCallFromPlayer(player, salt, 0x2202A3F42C8E5F79, new BooleanArgument(), callback, new LocalPlayerArgument());
        }
    }
}
