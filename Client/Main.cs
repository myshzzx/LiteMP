using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using GTA;
using GTA.Math;
using GTA.Native;
using Shared;
using Lidgren.Network;
using NativeUI;
using Newtonsoft.Json;
using ProtoBuf;
using NativeUI.PauseMenu;
using Control = GTA.Control;
using LiteClient.GUI;

namespace LiteClient
{
    public class Main : Script
    {
        public static PlayerSettings PlayerSettings;

        public static readonly ScriptVersion LocalScriptVersion = ScriptVersion.VERSION_0_1_2_1;

        public static bool BlockControls;
        public static bool _networkMonitoring;

        public static TabView MainMenu;

        private string _clientIp;
        private readonly Chat _chat;

        private static NetClient _client;
        private static NetPeerConfiguration _config;

        public static bool SendNpcs;
        private static int _channel;

        private readonly Queue<Action> _threadJumping;
        private string _password;
        private bool _lastDead;
        private bool _wasTyping;
        private bool _isTrafficEnabled;

        private DebugWindow _debug;

        // STATS
        public static int _bytesSent = 0;
        public static int _bytesReceived = 0;

        public static int _messagesSent = 0;
        public static int _messagesReceived = 0;

        public static List<int> _averagePacketSize = new List<int>();
        //

        public Main()
        {
            PlayerSettings = Util.ReadSettings(Program.Location + Path.DirectorySeparatorChar + "Settings.xml");
            _threadJumping = new Queue<Action>();

            Opponents = new Dictionary<long, SyncPed>();
            Npcs = new Dictionary<string, SyncPed>();
            _tickNatives = new Dictionary<string, NativeData>();
            _dcNatives = new Dictionary<string, NativeData>();

            _entityCleanup = new List<int>();
            _blipCleanup = new List<int>();

            _emptyVehicleMods = new Dictionary<int, int>();
            for (int i = 0; i < 50; i++) _emptyVehicleMods.Add(i, 0);

            _chat = new Chat();
            _chat.OnComplete += (sender, args) =>
            {
                string message = _chat.CurrentInput;
                if (!string.IsNullOrEmpty(message))
                {
                    ChatData obj = new ChatData()
                    {
                        Message = message,
                    };

                    byte[] data = SerializeBinary(obj);

                    NetOutgoingMessage msg = _client.CreateMessage();
                    msg.Write((int)PacketType.ChatData);
                    msg.Write(data.Length);
                    msg.Write(data);
                    _client.SendMessage(msg, NetDeliveryMethod.ReliableOrdered, 1);
                }
                _chat.IsFocused = false;
            };

            Tick += OnTick;
            KeyDown += OnKeyDown;

            KeyUp += (sender, args) =>
            {
                if (args.KeyCode == Keys.Escape && _wasTyping)
                {
                    _wasTyping = false;
                }
            };

            _config = new NetPeerConfiguration("LITEMPNET")
            {
                Port = 8888
            };
            _config.EnableMessageType(NetIncomingMessageType.ConnectionLatencyUpdated);


            #region Menu Set up
            BuildMainMenu();
            #endregion

            _debug = new DebugWindow();
        }

        // Debug stuff
        private bool display;

        private bool _isGoingToCar;
        //

        private int _currentOnlinePlayers;
        private int _currentOnlineServers;

        private TabInteractiveListItem _serverBrowser;
        private TabInteractiveListItem _lanBrowser;
        private TabInteractiveListItem _favBrowser;
        private TabInteractiveListItem _recentBrowser;

        private TabItemSimpleList _serverPlayers;
        private TabSubmenuItem _serverItem;
        private TabSubmenuItem _connectTab;

        private int _currentServerPort;
        private string _currentServerIp;

        public static Dictionary<long, SyncPed> Opponents;
        public static Dictionary<string, SyncPed> Npcs;
        public static float Latency;
        private int Port = 4499;

        private void AddToFavorites(string server)
        {
            if (string.IsNullOrWhiteSpace(server)) return;
            string[] split = server.Split(':');
            if (split.Length < 2 || string.IsNullOrWhiteSpace(split[0]) || string.IsNullOrWhiteSpace(split[1])) return;
            PlayerSettings.FavoriteServers.Add(server);
            Util.SaveSettings(Program.Location + Path.DirectorySeparatorChar + "Settings.xml");
        }

        private void RemoveFromFavorites(string server)
        {
            PlayerSettings.FavoriteServers.Remove(server);
            Util.SaveSettings(Program.Location + Path.DirectorySeparatorChar + "Settings.xml");
        }

        private void SaveSettings()
        {
            Util.SaveSettings(Program.Location + Path.DirectorySeparatorChar + "Settings.xml");
        }

        private void AddServerToRecent(UIMenuItem server)
        {
            if (string.IsNullOrWhiteSpace(server.Description)) return;
            string[] split = server.Description.Split(':');
            if (split.Length < 2 || string.IsNullOrWhiteSpace(split[0]) || string.IsNullOrWhiteSpace(split[1]) || !int.TryParse(split[1], out int tmpPort)) return;
            if (!PlayerSettings.RecentServers.Contains(server.Description))
            {
                PlayerSettings.RecentServers.Add(server.Description);
                if (PlayerSettings.RecentServers.Count > 20)
                    PlayerSettings.RecentServers.RemoveAt(0);
                Util.SaveSettings(Program.Location + Path.DirectorySeparatorChar + "Settings.xml");

                UIMenuItem item = new UIMenuItem(server.Text)
                {
                    Description = server.Description
                };
                item.SetRightLabel(server.RightLabel);
                item.SetLeftBadge(server.LeftBadge);
                item.Activated += (sender, selectedItem) =>
                {
                    if (IsOnServer())
                    {
                        _client.Disconnect("Switching servers.");

                        if (Opponents != null)
                        {
                            Opponents.ToList().ForEach(pair => pair.Value.Clear());
                            Opponents.Clear();
                        }

                        if (Npcs != null)
                        {
                            Npcs.ToList().ForEach(pair => pair.Value.Clear());
                            Npcs.Clear();
                        }

                        while (IsOnServer()) Script.Yield();
                    }

                    if (server.LeftBadge == UIMenuItem.BadgeStyle.Lock)
                    {
                        _password = Game.GetUserInput(256);
                    }

                    string[] splt = server.Description.Split(':');
                    if (splt.Length < 2) return;
                    if (!int.TryParse(splt[1], out int port)) return;
                    ConnectToServer(splt[0], port);
                    MainMenu.TemporarilyHidden = true;
                    _connectTab.RefreshIndex();
                };
                _recentBrowser.Items.Add(item);
            }
        }

        private void AddServerToRecent(string server, string password)
        {
            if (string.IsNullOrWhiteSpace(server)) return;
            string[] split = server.Split(':');
            if (split.Length < 2 || string.IsNullOrWhiteSpace(split[0]) || string.IsNullOrWhiteSpace(split[1]) || !int.TryParse(split[1], out int tmpPort)) return;
            if (!PlayerSettings.RecentServers.Contains(server))
            {
                PlayerSettings.RecentServers.Add(server);
                if (PlayerSettings.RecentServers.Count > 20)
                    PlayerSettings.RecentServers.RemoveAt(0);
                Util.SaveSettings(Program.Location + Path.DirectorySeparatorChar + "Settings.xml");

                UIMenuItem item = new UIMenuItem(server)
                {
                    Description = server
                };
                item.SetRightLabel(server);
                item.Activated += (sender, selectedItem) =>
                {
                    if (IsOnServer())
                    {
                        _client.Disconnect("Switching servers.");

                        if (Opponents != null)
                        {
                            Opponents.ToList().ForEach(pair => pair.Value.Clear());
                            Opponents.Clear();
                        }

                        if (Npcs != null)
                        {
                            Npcs.ToList().ForEach(pair => pair.Value.Clear());
                            Npcs.Clear();
                        }

                        while (IsOnServer()) Script.Yield();
                    }

                    if (!string.IsNullOrWhiteSpace(password))
                    {
                        _password = Game.GetUserInput(256);
                    }

                    string[] splt = server.Split(':');
                    if (splt.Length < 2) return;
                    if (!int.TryParse(splt[1], out int port)) return;
                    ConnectToServer(splt[0], port);
                    MainMenu.TemporarilyHidden = true;
                    _connectTab.RefreshIndex();
                };
                _recentBrowser.Items.Add(item);
            }
        }

        private void RebuildServerBrowser()
        {
            _serverBrowser.Items.Clear();
            _favBrowser.Items.Clear();
            _lanBrowser.Items.Clear();
            _recentBrowser.Items.Clear();

            _serverBrowser.RefreshIndex();
            _favBrowser.RefreshIndex();
            _lanBrowser.RefreshIndex();
            _recentBrowser.RefreshIndex();

            _currentOnlinePlayers = 0;
            _currentOnlineServers = 0;

            Thread fetchThread = new Thread((ThreadStart)delegate
            {
                if (string.IsNullOrEmpty(PlayerSettings.MasterServer))
                    return;
                string response = String.Empty;
                try
                {
                    using (Util.ImpatientWebClient wc = new Util.ImpatientWebClient())
                    {
                        response = wc.DownloadString(PlayerSettings.MasterServer);
                    }
                }
                catch (Exception e)
                {
                    UI.Notify("~r~~h~ERROR~h~~w~~n~Could not contact master server. Try again later.");
                    string logOutput = "===== EXCEPTION CONTACTING MASTER SERVER @ " + DateTime.UtcNow + " ======\n";
                    logOutput += "Message: " + e.Message;
                    logOutput += "\nData: " + e.Data;
                    logOutput += "\nStack: " + e.StackTrace;
                    logOutput += "\nSource: " + e.Source;
                    logOutput += "\nTarget: " + e.TargetSite;
                    if (e.InnerException != null)
                        logOutput += "\nInnerException: " + e.InnerException.Message;
                    logOutput += "\n";
                    File.AppendAllText("scripts\\Log.log", logOutput);
                    return;
                }

                if (string.IsNullOrWhiteSpace(response))
                    return;

                if (!(JsonConvert.DeserializeObject<MasterServerList>(response) is MasterServerList dejson)) return;

                if (_client == null)
                {
                    int port = GetOpenUdpPort();
                    if (port == 0)
                    {
                        UI.Notify("No available UDP port was found.");
                        return;
                    }
                    _config.Port = port;
                    _client = new NetClient(_config);
                    _client.Start();
                }

                List<string> list = new List<string>();

                foreach (string server in dejson.List)
                {
                    string[] split = server.Split(':');
                    if (split.Length != 2 || !int.TryParse(split[1], out int port)) continue;

                    list.Add(server);

                    UIMenuItem item = new UIMenuItem(server)
                    {
                        Description = server
                    };

                    int lastIndx = 0;
                    if (_serverBrowser.Items.Count > 0)
                        lastIndx = _serverBrowser.Index;

                    _serverBrowser.Items.Add(item);
                    _serverBrowser.Index = lastIndx;

                    if (PlayerSettings.RecentServers.Contains(server))
                    {
                        _recentBrowser.Items.Add(item);
                        _recentBrowser.Index = lastIndx;
                    }

                    if (PlayerSettings.FavoriteServers.Contains(server))
                    {
                        _favBrowser.Items.Add(item);
                        _favBrowser.Index = lastIndx;
                    }
                }

                _currentOnlineServers = list.Count;

                _client.DiscoverLocalPeers(Port);

                for (int i = 0; i < list.Count; i++)
                {
                    if (i != 0 && i % 10 == 0)
                    {
                        Thread.Sleep(3000);
                    }
                    string[] spl = list[i].Split(':');
                    _client.DiscoverKnownPeer(spl[0], int.Parse(spl[1]));
                }
            });

            fetchThread.Start();
        }

        private void RebuildPlayersList()
        {
            _serverPlayers.Dictionary.Clear();

            List<SyncPed> list = null;
            lock (Opponents)
            {
                if (Opponents == null) return;

                list = new List<SyncPed>(Opponents.Select(pair => pair.Value));
            }

            _serverPlayers.Dictionary.Add(PlayerSettings.DisplayName, ((int)(Latency * 1000)) + "ms");

            foreach (SyncPed ped in list)
            {
                _serverPlayers.Dictionary.Add(ped.Name ?? "<Unknown>", ((int)(ped.Latency * 1000)) + "ms");
            }
        }

        private void TickSpinner()
        {
            OnTick(null, EventArgs.Empty);
        }

        private void BuildMainMenu()
        {
            MainMenu = new TabView("LiteMP");
            MainMenu.CanLeave = false;
            MainMenu.MoneySubtitle = "Version 0.1.2";

            #region Welcome Screen
            {
                TabTextItem welcomeItem = new TabTextItem("Welcome", "Welcome to LiteMP", "Join a server on the right! Weekly Updates!\n\nwww.lite-mp.com");
                MainMenu.Tabs.Add(welcomeItem);
            }
            #endregion

            #region ServerBrowser
            {
                TabButtonArrayItem dConnect = new TabButtonArrayItem("Quick Connect");

                {
                    TabButton ipButton = new TabButton
                    {
                        Text = "IP Address",
                        Size = new Size(500, 40)
                    };
                    ipButton.Activated += (sender, args) =>
                    {
                        string newIp = InputboxThread.GetUserInput(_clientIp ?? "", 30, TickSpinner);
                        _clientIp = newIp;
                        ipButton.Text = string.IsNullOrWhiteSpace(newIp) ? "IP Address" : newIp;
                    };
                    dConnect.Buttons.Add(ipButton);
                }

                {
                    TabButton ipButton = new TabButton
                    {
                        Text = "Port",
                        Size = new Size(500, 40)
                    };
                    ipButton.Activated += (sender, args) =>
                    {
                        string newIp = InputboxThread.GetUserInput(Port.ToString(), 30, TickSpinner);

                        if (string.IsNullOrWhiteSpace(newIp)) return;

                        if (!int.TryParse(newIp, out int newPort))
                        {
                            UI.Notify("Wrong port format!");
                            return;
                        }
                        Port = newPort;
                        ipButton.Text = newIp;
                    };
                    dConnect.Buttons.Add(ipButton);
                }

                {
                    TabButton ipButton = new TabButton
                    {
                        Text = "Password",
                        Size = new Size(500, 40)
                    };
                    ipButton.Activated += (sender, args) =>
                    {
                        string newIp = InputboxThread.GetUserInput("", 30, TickSpinner);
                        _password = newIp;
                        ipButton.Text = string.IsNullOrWhiteSpace(newIp) ? "Password" : "*******";
                    };
                    dConnect.Buttons.Add(ipButton);
                }

                {
                    TabButton ipButton = new TabButton
                    {
                        Text = "Connect",
                        Size = new Size(500, 40)
                    };
                    ipButton.Activated += (sender, args) =>
                    {
                        AddServerToRecent(_clientIp + ":" + Port, _password);
                        ConnectToServer(_clientIp, Port);
                    };
                    dConnect.Buttons.Add(ipButton);
                }

                _serverBrowser = new TabInteractiveListItem("Internet", new List<UIMenuItem>());
                _lanBrowser = new TabInteractiveListItem("Local Area Network", new List<UIMenuItem>());
                _favBrowser = new TabInteractiveListItem("Favorites", new List<UIMenuItem>());
                _recentBrowser = new TabInteractiveListItem("Recent", new List<UIMenuItem>());


                _connectTab = new TabSubmenuItem("connect", new List<TabItem>() { dConnect, _serverBrowser, _lanBrowser, _favBrowser, _recentBrowser });
                MainMenu.AddTab(_connectTab);
                _connectTab.DrawInstructionalButtons += (sender, args) =>
                {
                    MainMenu.DrawInstructionalButton(4, Control.Jump, "Refresh");

                    if (Game.IsControlJustPressed(0, Control.Jump))
                    {
                        RebuildServerBrowser();
                    }

                    if (_connectTab.Index == 1 && _connectTab.Items[1].Focused)
                    {
                        MainMenu.DrawInstructionalButton(5, Control.Enter, "Favorite");
                        if (Game.IsControlJustPressed(0, Control.Enter))
                        {
                            UIMenuItem selectedServer = _serverBrowser.Items[_serverBrowser.Index];
                            selectedServer.SetRightBadge(UIMenuItem.BadgeStyle.None);
                            if (PlayerSettings.FavoriteServers.Contains(selectedServer.Description))
                            {
                                RemoveFromFavorites(selectedServer.Description);
                                UIMenuItem favItem = _favBrowser.Items.FirstOrDefault(i => i.Description == selectedServer.Description);
                                if (favItem != null)
                                {
                                    _favBrowser.Items.Remove(favItem);
                                    _favBrowser.RefreshIndex();
                                }
                            }
                            else
                            {
                                AddToFavorites(selectedServer.Description);
                                selectedServer.SetRightBadge(UIMenuItem.BadgeStyle.Star);
                                UIMenuItem item = new UIMenuItem(selectedServer.Text)
                                {
                                    Description = selectedServer.Description
                                };
                                item.SetRightLabel(selectedServer.RightLabel);
                                item.SetLeftBadge(selectedServer.LeftBadge);
                                item.Activated += (faf, selectedItem) =>
                                {
                                    if (IsOnServer())
                                    {
                                        _client.Disconnect("Switching servers.");

                                        if (Opponents != null)
                                        {
                                            Opponents.ToList().ForEach(pair => pair.Value.Clear());
                                            Opponents.Clear();
                                        }

                                        if (Npcs != null)
                                        {
                                            Npcs.ToList().ForEach(pair => pair.Value.Clear());
                                            Npcs.Clear();
                                        }

                                        while (IsOnServer()) Script.Yield();
                                    }

                                    if (selectedServer.LeftBadge == UIMenuItem.BadgeStyle.Lock)
                                    {
                                        _password = Game.GetUserInput(256);
                                    }

                                    string[] splt = selectedServer.Description.Split(':');

                                    if (splt.Length < 2 || !int.TryParse(splt[1], out int port)) return;

                                    ConnectToServer(splt[0], port);
                                    MainMenu.TemporarilyHidden = true;
                                    _connectTab.RefreshIndex();
                                    AddServerToRecent(selectedServer);
                                };
                                _favBrowser.Items.Add(item);
                            }
                        }
                    }

                    if (_connectTab.Index == 3 && _connectTab.Items[3].Focused)
                    {
                        MainMenu.DrawInstructionalButton(5, Control.Enter, "Favorite by IP");
                        if (Game.IsControlJustPressed(0, Control.Enter))
                        {
                            string serverIp = InputboxThread.GetUserInput("Server IP:Port", 40, TickSpinner);

                            if (!serverIp.Contains(":"))
                            {
                                UI.Notify("Server IP and port need to be separated by a : character!");
                                return;
                            }

                            if (!PlayerSettings.FavoriteServers.Contains(serverIp))
                            {
                                AddToFavorites(serverIp);
                                UIMenuItem item = new UIMenuItem(serverIp)
                                {
                                    Description = serverIp
                                };
                                item.Activated += (faf, selectedItem) =>
                                {
                                    if (IsOnServer())
                                    {
                                        _client.Disconnect("Switching servers.");

                                        if (Opponents != null)
                                        {
                                            Opponents.ToList().ForEach(pair => pair.Value.Clear());
                                            Opponents.Clear();
                                        }

                                        if (Npcs != null)
                                        {
                                            Npcs.ToList().ForEach(pair => pair.Value.Clear());
                                            Npcs.Clear();
                                        }

                                        while (IsOnServer()) Script.Yield();
                                    }

                                    string[] splt = serverIp.Split(':');

                                    if (splt.Length < 2 || !int.TryParse(splt[1], out int port)) return;

                                    ConnectToServer(splt[0], port);
                                    MainMenu.TemporarilyHidden = true;
                                    _connectTab.RefreshIndex();
                                    AddServerToRecent(serverIp, "");
                                };
                                _favBrowser.Items.Add(item);
                            }
                        }
                    }
                };
            }
            #endregion

            #region Settings

            {

                TabInteractiveListItem internetServers = new TabInteractiveListItem("Multiplayer", new List<UIMenuItem>());
                {
                    UIMenuItem nameItem = new UIMenuItem("Name");
                    nameItem.SetRightLabel(PlayerSettings.DisplayName);
                    nameItem.Activated += (sender, item) =>
                    {
                        string newName = InputboxThread.GetUserInput(PlayerSettings.DisplayName ?? "Enter new name", 40, TickSpinner);
                        if (!string.IsNullOrWhiteSpace(newName))
                        {
                            PlayerSettings.DisplayName = newName;
                            SaveSettings();
                            nameItem.SetRightLabel(newName);
                        }
                    };

                    internetServers.Items.Add(nameItem);
                }

                {
                    UIMenuCheckboxItem npcItem = new UIMenuCheckboxItem("Share World With Players", false);
                    npcItem.CheckboxEvent += (item, check) =>
                    {
                        SendNpcs = check;
                        if (!check && _client != null)
                        {
                            NetOutgoingMessage msg = _client.CreateMessage();
                            PlayerDisconnect obj = new PlayerDisconnect
                            {
                                Id = _client.UniqueIdentifier
                            };

                            byte[] bin = SerializeBinary(obj);

                            msg.Write((int)PacketType.WorldSharingStop);
                            msg.Write(bin.Length);
                            msg.Write(bin);

                            _client.SendMessage(msg, NetDeliveryMethod.ReliableOrdered, 3);
                        }
                    };
                    internetServers.Items.Add(npcItem);
                }

                {
                    UIMenuCheckboxItem trafficItem = new UIMenuCheckboxItem("Enable Traffic When Sharing (May affect performance)", false);
                    trafficItem.CheckboxEvent += (item, check) =>
                    {
                        _isTrafficEnabled = check;
                    };
                    internetServers.Items.Add(trafficItem);
                }

                TabInteractiveListItem otherOptions = new TabInteractiveListItem("Other", new List<UIMenuItem>());
                {
                    UIMenuCheckboxItem networkMonitoring = new UIMenuCheckboxItem("Network monitoring", false);
                    networkMonitoring.CheckboxEvent += (item, check) =>
                    {
                        _networkMonitoring = check;
                    };
                    otherOptions.Items.Add(networkMonitoring);
                }

#if DEBUG
                {
                    UIMenuCheckboxItem spawnItem = new UIMenuCheckboxItem("Debug", false);
                    spawnItem.CheckboxEvent += (item, check) =>
                    {
                        display = check;
                        if (!display)
                        {
                            if (_debugSyncPed != null)
                            {
                                _debugSyncPed.Clear();
                                _debugSyncPed = null;
                            }
                            SyncPed.Debug = false;
                        } else
                        {
                            SyncPed.Debug = true;
                        }
                    };
                    internetServers.Items.Add(spawnItem);
                }
#endif

                TabSubmenuItem welcomeItem = new TabSubmenuItem("settings", new List<TabItem>() { internetServers, otherOptions });
                MainMenu.AddTab(welcomeItem);
            }

            #endregion

            #region Current Server Tab

            #region Players
            _serverPlayers = new TabItemSimpleList("Players", new Dictionary<string, string>());
            #endregion

            TabTextItem favTab = new TabTextItem("Favorite", "Add to Favorites", "Add the current server to favorites.")
            {
                CanBeFocused = false
            };
            favTab.Activated += (sender, args) =>
            {
                string serb = _currentServerIp + ":" + _currentServerPort;
                AddToFavorites(_currentServerIp + ":" + _currentServerPort);
                UIMenuItem item = new UIMenuItem(serb)
                {
                    Description = serb
                };
                UI.Notify("Server added to favorites!");
                item.Activated += (faf, selectedItem) =>
                {
                    if (IsOnServer())
                    {
                        _client.Disconnect("Switching servers.");

                        if (Opponents != null)
                        {
                            Opponents.ToList().ForEach(pair => pair.Value.Clear());
                            Opponents.Clear();
                        }

                        if (Npcs != null)
                        {
                            Npcs.ToList().ForEach(pair => pair.Value.Clear());
                            Npcs.Clear();
                        }

                        while (IsOnServer()) Script.Yield();
                    }

                    string[] splt = serb.Split(':');

                    if (splt.Length < 2 || !int.TryParse(splt[1], out int port)) return;

                    ConnectToServer(splt[0], port);
                    MainMenu.TemporarilyHidden = true;
                    _connectTab.RefreshIndex();
                    AddServerToRecent(serb, "");
                };
                _favBrowser.Items.Add(item);
            };

            TabTextItem dcItem = new TabTextItem("Disconnect", "Disconnect", "Disconnect from the current server.")
            {
                CanBeFocused = false
            };
            dcItem.Activated += (sender, args) =>
            {
                if (_client != null) _client.Disconnect("Connection closed by peer.");
            };

            _serverItem = new TabSubmenuItem("server", new List<TabItem>() { _serverPlayers, favTab, dcItem });
            _serverItem.Parent = MainMenu;
            #endregion

            RebuildServerBrowser();
            MainMenu.RefreshIndex();
        }

        private static Dictionary<int, int> _emptyVehicleMods;
        private Dictionary<string, NativeData> _tickNatives;
        private Dictionary<string, NativeData> _dcNatives;
        private List<int> _entityCleanup;
        private List<int> _blipCleanup;

        private static int _modSwitch = 0;
        private static int _pedSwitch = 0;
        private static Dictionary<int, int> _vehMods = new Dictionary<int, int>();
        private static Dictionary<int, int> _pedClothes = new Dictionary<int, int>();

        public static Dictionary<int, int> CheckPlayerVehicleMods()
        {
            if (!Game.Player.Character.IsInVehicle()) return null;

            if (_modSwitch % 30 == 0)
            {
                int id = _modSwitch / 30,
                    mod = Game.Player.Character.CurrentVehicle.GetMod((VehicleMod)id);

                if (mod != -1)
                {
                    lock (_vehMods)
                    {
                        if (!_vehMods.ContainsKey(id)) _vehMods.Add(id, mod);

                        _vehMods[id] = mod;
                    }
                }
            }

            _modSwitch++;

            if (_modSwitch >= 1500) _modSwitch = 0;

            return _vehMods;
        }

        public static Dictionary<int, int> CheckPlayerProps()
        {
            if (_pedSwitch % 30 == 0)
            {
                int id = _pedSwitch / 30,
                    mod = Function.Call<int>(Hash.GET_PED_DRAWABLE_VARIATION, Game.Player.Character.Handle, id);

                if (mod != -1)
                {
                    lock (_pedClothes)
                    {
                        if (!_pedClothes.ContainsKey(id)) _pedClothes.Add(id, mod);

                        _pedClothes[id] = mod;
                    }
                }
            }

            _pedSwitch++;

            if (_pedSwitch >= 450) _pedSwitch = 0;

            return _pedClothes;
        }

        private static int _lastDataSend;
        private static int _tickRate = 60;
        public static void SendPlayerData()
        {
            if (Environment.TickCount - _lastDataSend < 1000 / _tickRate) return;
            _lastDataSend = Environment.TickCount;

            Ped player = Game.Player.Character;
            if (player.IsInVehicle())
            {
                Vehicle veh = player.CurrentVehicle;

                bool siren = veh.SirenActive,
                    horn = Game.Player.IsPressingHorn,
                    burnout = veh.IsInBurnout(),
                    highBeams = veh.HighBeamsOn,
                    lights = veh.LightsOn,
                    engine = veh.EngineRunning;

                VehicleData obj = new VehicleData();
                obj.Position = veh.Position.ToLVector();
                obj.Quaternion = veh.Rotation.ToLVector();
                obj.Velocity = veh.Velocity.ToLVector();
                obj.PedModelHash = player.Model.Hash;
                obj.VehicleModelHash = veh.Model.Hash;
                obj.PrimaryColor = (int)veh.PrimaryColor;
                obj.SecondaryColor = (int)veh.SecondaryColor;
                obj.PlayerHealth = (int)(100 * ((player.Health < 0 ? 0 : player.Health) / (float)player.MaxHealth));
                obj.VehicleHealth = veh.EngineHealth;
                obj.VehicleSeat = Util.GetPedSeat(player);
                obj.VehicleMods = CheckPlayerVehicleMods();
                obj.Speed = veh.Speed;
                obj.RPM = veh.CurrentRPM;
                obj.Steering = veh.SteeringAngle;

                obj.RadioStation = (int)Game.RadioStation;
                obj.Plate = veh.NumberPlate;

                obj.Flag = 0;

                if (horn)
                    obj.Flag |= (byte)VehicleDataFlags.PressingHorn;

                if (siren)
                    obj.Flag |= (byte)VehicleDataFlags.SirenActive;

                if (burnout)
                    obj.Flag |= (byte)VehicleDataFlags.IsInBurnout;

                if (highBeams)
                    obj.Flag |= (byte)VehicleDataFlags.HighbeamsOn;

                if (lights)
                    obj.Flag |= (byte)VehicleDataFlags.LightsOn;

                if (engine)
                    obj.Flag |= (byte)VehicleDataFlags.IsEngineRunning;

                byte[] bin = SerializeBinary(obj);

                NetOutgoingMessage msg = _client.CreateMessage();
                msg.Write((int)PacketType.VehiclePositionData);
                msg.Write(bin.Length);
                msg.Write(bin);

                _client.SendMessage(msg, NetDeliveryMethod.UnreliableSequenced, _channel);

                _averagePacketSize.Add(bin.Length);
                if (_averagePacketSize.Count > 10)
                    _averagePacketSize.RemoveAt(0);

                _bytesSent += bin.Length;
                _messagesSent++;
            }
            else
            {
                bool aiming = Game.IsControlPressed(0, GTA.Control.Aim),
                    shooting = player.IsShooting,
                    jumping = player.IsJumping;

                Vector3 aimCoord = new Vector3();
                if (aiming || shooting)
                    aimCoord = ScreenRelToWorld(GameplayCamera.Position, GameplayCamera.Rotation, new Vector2(0, 0));

                PedData obj = new PedData();
                obj.AimCoords = aimCoord.ToLVector();
                obj.Position = player.Position.ToLVector();
                obj.Quaternion = player.Rotation.ToLVector();
                obj.PedModelHash = player.Model.Hash;
                obj.WeaponHash = (int)player.Weapons.Current.Hash;
                obj.PlayerHealth = (int)(100 * ((player.Health < 0 ? 0 : player.Health) / (float)player.MaxHealth));

                obj.Flag = 0;

                if (aiming)
                    obj.Flag |= (byte)PedDataFlags.IsAiming;

                if (shooting || (player.IsInMeleeCombat && Game.IsControlJustPressed(0, Control.Attack)))
                    obj.Flag |= (byte)PedDataFlags.IsShooting;

                if (jumping)
                    obj.Flag |= (byte)PedDataFlags.IsJumping;

                if (Function.Call<int>(Hash.GET_PED_PARACHUTE_STATE, Game.Player.Character.Handle) == 2)
                    obj.Flag |= (byte)PedDataFlags.IsParachuteOpen;

                obj.PedProps = CheckPlayerProps();

                byte[] bin = SerializeBinary(obj);

                NetOutgoingMessage msg = _client.CreateMessage();

                msg.Write((int)PacketType.PedPositionData);
                msg.Write(bin.Length);
                msg.Write(bin);

                _client.SendMessage(msg, NetDeliveryMethod.UnreliableSequenced, _channel);

                _averagePacketSize.Add(bin.Length);
                if (_averagePacketSize.Count > 10)
                    _averagePacketSize.RemoveAt(0);

                _bytesSent += bin.Length;
                _messagesSent++;
            }
        }

        public static void SendPedData(Ped ped)
        {
            if (ped.IsInVehicle())
            {
                Vehicle veh = ped.CurrentVehicle;

                bool siren = veh.SirenActive,
                    burnout = veh.IsInBurnout(),
                    highBeams = veh.HighBeamsOn,
                    lights = veh.LightsOn,
                    engine = veh.EngineRunning;

                VehicleData obj = new VehicleData();
                obj.Position = veh.Position.ToLVector();
                obj.Quaternion = veh.Rotation.ToLVector();
                obj.Velocity = veh.Velocity.ToLVector();
                obj.PedModelHash = ped.Model.Hash;
                obj.VehicleModelHash = veh.Model.Hash;
                obj.PrimaryColor = (int)veh.PrimaryColor;
                obj.SecondaryColor = (int)veh.SecondaryColor;
                obj.PlayerHealth = (int)(100 * ((ped.Health < 0 ? 0 : ped.Health) / (float)ped.MaxHealth));
                obj.VehicleHealth = veh.EngineHealth;
                obj.VehicleSeat = Util.GetPedSeat(ped);
                obj.Name = ped.Handle.ToString();
                obj.Speed = veh.Speed;
                obj.RPM = veh.CurrentRPM;
                obj.Steering = veh.SteeringAngle;
                obj.RadioStation = (int)Game.RadioStation;
                obj.Plate = veh.NumberPlate;

                if (siren)
                    obj.Flag |= (byte)VehicleDataFlags.SirenActive;

                if (burnout)
                    obj.Flag |= (byte)VehicleDataFlags.IsInBurnout;

                if (highBeams)
                    obj.Flag |= (byte)VehicleDataFlags.HighbeamsOn;

                if (lights)
                    obj.Flag |= (byte)VehicleDataFlags.LightsOn;

                if (engine)
                    obj.Flag |= (byte)VehicleDataFlags.IsEngineRunning;


                byte[] bin = SerializeBinary(obj);

                NetOutgoingMessage msg = _client.CreateMessage();
                msg.Write((int)PacketType.NpcVehPositionData);
                msg.Write(bin.Length);
                msg.Write(bin);

                _client.SendMessage(msg, NetDeliveryMethod.Unreliable, _channel);

                _averagePacketSize.Add(bin.Length);
                if (_averagePacketSize.Count > 10)
                    _averagePacketSize.RemoveAt(0);

                _bytesSent += bin.Length;
                _messagesSent++;
            }
            else
            {
                bool shooting = ped.IsShooting,
                    aiming = Game.IsControlPressed(0, GTA.Control.Aim),
                    jumping = ped.IsJumping;

                Vector3 aimCoord = new Vector3();
                if (shooting)
                    aimCoord = Util.GetLastWeaponImpact(ped);

                PedData obj = new PedData();
                obj.AimCoords = aimCoord.ToLVector();
                obj.Position = ped.Position.ToLVector();
                obj.Quaternion = ped.Rotation.ToLVector();
                obj.PedModelHash = ped.Model.Hash;
                obj.WeaponHash = (int)ped.Weapons.Current.Hash;
                obj.PlayerHealth = (int)(100 * ((ped.Health < 0 ? 0 : ped.Health) / (float)ped.MaxHealth));
                obj.Name = ped.Handle.ToString();

                obj.Flag = 0;

                if (aiming)
                    obj.Flag |= (byte)PedDataFlags.IsAiming;

                if (shooting || (ped.IsInMeleeCombat && Game.IsControlJustPressed(0, Control.Attack)))
                    obj.Flag |= (byte)PedDataFlags.IsShooting;

                if (jumping)
                    obj.Flag |= (byte)PedDataFlags.IsJumping;

                if (Function.Call<int>(Hash.GET_PED_PARACHUTE_STATE, Game.Player.Character.Handle) == 2)
                    obj.Flag |= (byte)PedDataFlags.IsParachuteOpen;

                byte[] bin = SerializeBinary(obj);

                NetOutgoingMessage msg = _client.CreateMessage();

                msg.Write((int)PacketType.NpcPedPositionData);
                msg.Write(bin.Length);
                msg.Write(bin);

                _client.SendMessage(msg, NetDeliveryMethod.Unreliable, _channel);

                _averagePacketSize.Add(bin.Length);
                if (_averagePacketSize.Count > 10)
                    _averagePacketSize.RemoveAt(0);

                _bytesSent += bin.Length;
                _messagesSent++;
            }
        }

        // netstats
        private int _lastBytesSent,
            _lastBytesReceived,
            _lastCheck,
            _bytesSentPerSecond,
            _bytesReceivedPerSecond;

        public void OnTick(object sender, EventArgs e)
        {
            Ped player = Game.Player.Character;

            if (Environment.TickCount - _lastCheck > 1000)
            {
                _bytesSentPerSecond = _bytesSent - _lastBytesSent;
                _bytesReceivedPerSecond = _bytesReceived - _lastBytesReceived;

                _lastBytesReceived = _bytesReceived;
                _lastBytesSent = _bytesSent;


                _lastCheck = Environment.TickCount;
            }

            if (Function.Call<bool>(Hash.IS_STUNT_JUMP_IN_PROGRESS))
                Function.Call(Hash.CANCEL_STUNT_JUMP);

            Function.Call(Hash.HIDE_HELP_TEXT_THIS_FRAME);

            if (!BlockControls)
                MainMenu.ProcessControls();

            MainMenu.Update();
            MainMenu.CanLeave = IsOnServer();

            if (!MainMenu.Visible || MainMenu.TemporarilyHidden)
                _chat.Tick();

            if (_isGoingToCar && Game.IsControlJustPressed(0, Control.PhoneCancel))
            {
                Game.Player.Character.Task.ClearAll();
                _isGoingToCar = false;
            }

#if DEBUG
            if (display)
            {
                Debug();
            }
#endif
            ProcessMessages();

            if (_client == null || _client.ConnectionStatus == NetConnectionStatus.Disconnected ||
                _client.ConnectionStatus == NetConnectionStatus.None) return;

            if (_wasTyping)
                Game.DisableControlThisFrame(0, Control.FrontendPauseAlternate);

            int time = 1000;
            if ((time = Function.Call<int>(Hash.GET_TIME_SINCE_LAST_DEATH)) < 50 && !_lastDead)
            {
                _lastDead = true;
                NetOutgoingMessage msg = _client.CreateMessage();
                msg.Write((int)PacketType.PlayerKilled);
                _client.SendMessage(msg, NetDeliveryMethod.ReliableOrdered, 0);
            }

            if (time > 50 && _lastDead)
                _lastDead = false;

            if ((!_isTrafficEnabled && SendNpcs) || !SendNpcs)
            {
                Function.Call(Hash.SET_VEHICLE_DENSITY_MULTIPLIER_THIS_FRAME, 0f);
                Function.Call(Hash.SET_RANDOM_VEHICLE_DENSITY_MULTIPLIER_THIS_FRAME, 0f);
                Function.Call(Hash.SET_PARKED_VEHICLE_DENSITY_MULTIPLIER_THIS_FRAME, 0f);

                Function.Call(Hash.SET_PED_DENSITY_MULTIPLIER_THIS_FRAME, 0f);
                Function.Call(Hash.SET_SCENARIO_PED_DENSITY_MULTIPLIER_THIS_FRAME, 0f, 0f);

                Function.Call((Hash)0x2F9A292AD0A3BD89);
                Function.Call((Hash)0x5F3B7749C112D552);
            }

            Function.Call(Hash.SET_CAN_ATTACK_FRIENDLY, Game.Player.Character, true, true);

            Function.Call(Hash.SET_TIME_SCALE, 1f);

            if (_networkMonitoring)
            {
                double aver = 0;
                lock (_averagePacketSize)
                    aver = _averagePacketSize.Count > 0 ? _averagePacketSize.Average() : 0;

                string stats =
                    string.Format(
                        "~h~Bytes Sent~h~: {0}~n~~h~Bytes Received~h~: {1}~n~~h~Bytes Sent / Second~h~: {5}~n~~h~Bytes Received / Second~h~: {6}~n~~h~Average Packet Size~h~: {4}~n~~n~~h~Messages Sent~h~: {2}~n~~h~Messages Received~h~: {3}",
                        _bytesSent, _bytesReceived, _messagesSent, _messagesReceived,
                        _averagePacketSize.Count > 0 ? _averagePacketSize.Average() : 0, _bytesSentPerSecond,
                        _bytesReceivedPerSecond);


                UI.ShowSubtitle(stats);
            }

            lock (_threadJumping)
            {
                if (_threadJumping.Any())
                {
                    Action action = _threadJumping.Dequeue();
                    if (action != null)
                        action.Invoke();
                }
            }

            Dictionary<string, NativeData> tickNatives = null;
            lock (_tickNatives) tickNatives = new Dictionary<string, NativeData>(_tickNatives);

            for (int i = 0; i < tickNatives.Count; i++) DecodeNativeCall(tickNatives.ElementAt(i).Value);
        }

        public static bool IsOnServer()
        {
            return _client != null && _client.ConnectionStatus == NetConnectionStatus.Connected;
        }

        public void OnKeyDown(object sender, KeyEventArgs e)
        {
            _chat.OnKeyDown(e.KeyCode);
            if (e.KeyCode == PlayerSettings.ActivationKey && !_chat.IsFocused)
            {
                MainMenu.Visible = !MainMenu.Visible;

                if (MainMenu.Visible)
                    RebuildPlayersList();
            }

            if (e.KeyCode == Keys.G && !Game.Player.Character.IsInVehicle() && IsOnServer())
            {
                List<Vehicle> vehs = World.GetAllVehicles().OrderBy(v => (v.Position - Game.Player.Character.Position).Length()).Take(1).ToList();
                if (vehs.Any() && Game.Player.Character.IsInRangeOf(vehs[0].Position, 6f))
                {
                    Vector3 relPos = vehs[0].GetOffsetFromWorldCoords(Game.Player.Character.Position);
                    VehicleSeat seat = VehicleSeat.Any;

                    if (relPos.X < 0)
                    {
                        if (relPos.Y > 0)
                            seat = VehicleSeat.RightFront;
                        else
                            seat = VehicleSeat.LeftRear;
                    }
                    else
                    {
                        if (relPos.Y > 0)
                            seat = VehicleSeat.RightFront;
                        else
                            seat = VehicleSeat.RightRear;
                    }

                    if (vehs[0].PassengerSeats == 1) seat = VehicleSeat.Passenger;

                    if (vehs[0].PassengerSeats > 3 && vehs[0].GetPedOnSeat(seat).Handle != 0)
                    {
                        if (seat == VehicleSeat.LeftRear)
                        {
                            for (int i = 3; i < vehs[0].PassengerSeats; i += 2)
                            {
                                if (vehs[0].GetPedOnSeat((VehicleSeat)i).Handle != 0) continue;

                                seat = (VehicleSeat)i;
                                break;
                            }
                        }
                        else if (seat == VehicleSeat.RightRear)
                        {
                            for (int i = 4; i < vehs[0].PassengerSeats; i += 2)
                            {
                                if (vehs[0].GetPedOnSeat((VehicleSeat)i).Handle != 0) continue;

                                seat = (VehicleSeat)i;
                                break;
                            }
                        }
                    }

                    Game.Player.Character.Task.EnterVehicle(vehs[0], seat, -1, 2f);
                    _isGoingToCar = true;
                }
            }

            if (e.KeyCode == Keys.T && IsOnServer())
            {
                _chat.IsFocused = true;
                _wasTyping = true;
            }
        }

        public void ConnectToServer(string ip, int port = 0)
        {
            SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());

            _chat.Init();

            if (_client == null)
            {
                int cport = GetOpenUdpPort();
                if (cport == 0)
                {
                    UI.Notify("No available UDP port was found.");
                    return;
                }
                _config.Port = cport;
                _client = new NetClient(_config);
                _client.Start();
            }

            lock (Opponents) Opponents = new Dictionary<long, SyncPed>();
            lock (Npcs) Npcs = new Dictionary<string, SyncPed>();
            lock (_tickNatives) _tickNatives = new Dictionary<string, NativeData>();

            NetOutgoingMessage msg = _client.CreateMessage();

            ConnectionRequest obj = new ConnectionRequest
            {
                Name = string.IsNullOrWhiteSpace(Game.Player.Name) ? "Player" : Game.Player.Name // To be used as identifiers in server files
            };
            obj.DisplayName = string.IsNullOrWhiteSpace(PlayerSettings.DisplayName) ? obj.Name : PlayerSettings.DisplayName.Trim();
            if (!string.IsNullOrEmpty(_password)) obj.Password = _password;
            obj.ScriptVersion = (byte)LocalScriptVersion;
            obj.GameVersion = (int)Game.Version;

            byte[] bin = SerializeBinary(obj);

            msg.Write((int)PacketType.ConnectionRequest);
            msg.Write(bin.Length);
            msg.Write(bin);

            _client.Connect(ip, port == 0 ? Port : port, msg);

            Vector3 pos = Game.Player.Character.Position;
            Function.Call(Hash.CLEAR_AREA_OF_PEDS, pos.X, pos.Y, pos.Z, 100f, 0);
            Function.Call(Hash.CLEAR_AREA_OF_VEHICLES, pos.X, pos.Y, pos.Z, 100f, 0);

            Function.Call(Hash.SET_GARBAGE_TRUCKS, 0);
            Function.Call(Hash.SET_RANDOM_BOATS, 0);
            Function.Call(Hash.SET_RANDOM_TRAINS, 0);

            _currentServerIp = ip;
            _currentServerPort = port == 0 ? Port : port;
        }

        public void ProcessMessages()
        {
            NetIncomingMessage msg;
            while (_client != null && (msg = _client.ReadMessage()) != null)
            {
                _messagesReceived++;
                _bytesReceived += msg.LengthBytes;

                if (msg.MessageType == NetIncomingMessageType.Data)
                {
                    PacketType type = (PacketType)msg.ReadInt32();
                    int len = msg.ReadInt32();

                    switch (type)
                    {
                        case PacketType.VehiclePositionData:
                            {
                                if (!(DeserializeBinary<VehicleData>(msg.ReadBytes(len)) is VehicleData data)) return;

                                lock (Opponents)
                                {
                                    if (!Opponents.ContainsKey(data.Id))
                                    {
                                        SyncPed repr = new SyncPed(data.PedModelHash, data.Position.ToVector(), data.Quaternion.ToVector());
                                        Opponents.Add(data.Id, repr);
                                    }

                                    Opponents[data.Id].Name = data.Name;
                                    Opponents[data.Id].LastUpdateReceived = Environment.TickCount;
                                    Opponents[data.Id].VehiclePosition = data.Position.ToVector();
                                    Opponents[data.Id].VehicleVelocity = data.Velocity.ToVector();
                                    Opponents[data.Id].ModelHash = data.PedModelHash;
                                    Opponents[data.Id].VehicleHash = data.VehicleModelHash;
                                    Opponents[data.Id].VehicleRotation = data.Quaternion.ToVector();
                                    Opponents[data.Id].PedHealth = data.PlayerHealth;
                                    Opponents[data.Id].VehicleHealth = data.VehicleHealth;
                                    Opponents[data.Id].VehiclePrimaryColor = data.PrimaryColor;
                                    Opponents[data.Id].VehicleSecondaryColor = data.SecondaryColor;
                                    Opponents[data.Id].VehicleSeat = data.VehicleSeat;
                                    Opponents[data.Id].IsInVehicle = true;
                                    Opponents[data.Id].Latency = data.Latency;

                                    Opponents[data.Id].VehicleMods = data.VehicleMods;
                                    Opponents[data.Id].Speed = data.Speed;
                                    Opponents[data.Id].VehicleRPM = data.RPM;
                                    Opponents[data.Id].Steering = data.Steering;

                                    Opponents[data.Id].RadioStation = data.RadioStation;
                                    Opponents[data.Id].Plate = data.Plate;

                                    if (data.Flag != null)
                                    {
                                        Opponents[data.Id].Siren = (data.Flag.Value & (short)VehicleDataFlags.SirenActive) > 0;
                                        Opponents[data.Id].IsInBurnout = (data.Flag.Value & (short)VehicleDataFlags.IsInBurnout) > 0;
                                        Opponents[data.Id].highbeamsOn = (data.Flag.Value & (short)VehicleDataFlags.HighbeamsOn) > 0;
                                        Opponents[data.Id].LightsOn = (data.Flag.Value & (short)VehicleDataFlags.LightsOn) > 0;
                                        Opponents[data.Id].IsEngineRunning = (data.Flag.Value & (short)VehicleDataFlags.IsEngineRunning) > 0;
                                        Opponents[data.Id].IsHornPressed = (data.Flag.Value & (short)VehicleDataFlags.PressingHorn) > 0;
                                    }
                                }
                            }
                            break;
                        case PacketType.PedPositionData:
                            {
                                if (!(DeserializeBinary<PedData>(msg.ReadBytes(len)) is PedData data)) return;

                                lock (Opponents)
                                {
                                    if (!Opponents.ContainsKey(data.Id))
                                    {
                                        SyncPed repr = new SyncPed(data.PedModelHash, data.Position.ToVector(), data.Quaternion.ToVector());
                                        Opponents.Add(data.Id, repr);
                                    }

                                    Opponents[data.Id].Name = data.Name;
                                    Opponents[data.Id].LastUpdateReceived = Environment.TickCount;
                                    Opponents[data.Id].Position = data.Position.ToVector();
                                    Opponents[data.Id].ModelHash = data.PedModelHash;
                                    Opponents[data.Id].Rotation = data.Quaternion.ToVector();
                                    Opponents[data.Id].PedHealth = data.PlayerHealth;
                                    Opponents[data.Id].IsInVehicle = false;
                                    Opponents[data.Id].AimCoords = data.AimCoords.ToVector();
                                    Opponents[data.Id].CurrentWeapon = data.WeaponHash;
                                    Opponents[data.Id].Latency = data.Latency;
                                    Opponents[data.Id].PedProps = data.PedProps;

                                    if (data.Flag != null)
                                    {
                                        Opponents[data.Id].IsAiming = (data.Flag.Value & (short)PedDataFlags.IsAiming) > 0;
                                        Opponents[data.Id].IsJumping = (data.Flag.Value & (short)PedDataFlags.IsJumping) > 0;
                                        Opponents[data.Id].IsShooting = (data.Flag.Value & (short)PedDataFlags.IsShooting) > 0;
                                        Opponents[data.Id].IsParachuteOpen = (data.Flag.Value & (short)PedDataFlags.IsParachuteOpen) > 0;
                                    }
                                }
                            }
                            break;
                        case PacketType.NpcVehPositionData:
                            {
                                if (!(DeserializeBinary<VehicleData>(msg.ReadBytes(len)) is VehicleData data)) return;

                                lock (Npcs)
                                {
                                    if (!Npcs.ContainsKey(data.Name))
                                    {
                                        SyncPed repr = new SyncPed(data.PedModelHash, data.Position.ToVector(), data.Quaternion.ToVector(), false);
                                        Npcs.Add(data.Name, repr);
                                        Npcs[data.Name].Name = "";
                                        Npcs[data.Name].Host = data.Id;
                                    }

                                    Npcs[data.Name].LastUpdateReceived = Environment.TickCount;
                                    Npcs[data.Name].VehiclePosition = data.Position.ToVector();
                                    Npcs[data.Name].VehicleVelocity = data.Velocity.ToVector();
                                    Npcs[data.Name].ModelHash = data.PedModelHash;
                                    Npcs[data.Name].VehicleHash = data.VehicleModelHash;
                                    Npcs[data.Name].VehicleRotation = data.Quaternion.ToVector();
                                    Npcs[data.Name].PedHealth = data.PlayerHealth;
                                    Npcs[data.Name].VehicleHealth = data.VehicleHealth;
                                    Npcs[data.Name].VehiclePrimaryColor = data.PrimaryColor;
                                    Npcs[data.Name].VehicleSecondaryColor = data.SecondaryColor;
                                    Npcs[data.Name].VehicleSeat = data.VehicleSeat;
                                    Npcs[data.Name].IsInVehicle = true;

                                    Npcs[data.Name].Speed = data.Speed;
                                    Npcs[data.Name].VehicleRPM = data.RPM;
                                    Npcs[data.Name].Steering = data.Steering;

                                    Npcs[data.Name].RadioStation = data.RadioStation;
                                    Npcs[data.Name].Plate = data.Plate;

                                    if (data.Flag != null)
                                    {
                                        Npcs[data.Name].Siren = (data.Flag.Value & (short)VehicleDataFlags.SirenActive) > 0;
                                        Npcs[data.Name].IsInBurnout = (data.Flag.Value & (short)VehicleDataFlags.IsInBurnout) > 0;
                                        Npcs[data.Name].highbeamsOn = (data.Flag.Value & (short)VehicleDataFlags.HighbeamsOn) > 0;
                                        Npcs[data.Name].LightsOn = (data.Flag.Value & (short)VehicleDataFlags.LightsOn) > 0;
                                        Npcs[data.Name].IsEngineRunning = (data.Flag.Value & (short)VehicleDataFlags.IsEngineRunning) > 0;
                                        Npcs[data.Name].IsHornPressed = (data.Flag.Value & (short)VehicleDataFlags.PressingHorn) > 0;
                                    }
                                }
                            }
                            break;
                        case PacketType.NpcPedPositionData:
                            {
                                if (!(DeserializeBinary<PedData>(msg.ReadBytes(len)) is PedData data)) return;

                                lock (Npcs)
                                {
                                    if (!Npcs.ContainsKey(data.Name))
                                    {
                                        SyncPed repr = new SyncPed(data.PedModelHash, data.Position.ToVector(), data.Quaternion.ToVector(), false);
                                        Npcs.Add(data.Name, repr);
                                        Npcs[data.Name].Name = "";
                                        Npcs[data.Name].Host = data.Id;
                                    }

                                    Npcs[data.Name].LastUpdateReceived = Environment.TickCount;
                                    Npcs[data.Name].Position = data.Position.ToVector();
                                    Npcs[data.Name].ModelHash = data.PedModelHash;
                                    Npcs[data.Name].Rotation = data.Quaternion.ToVector();
                                    Npcs[data.Name].PedHealth = data.PlayerHealth;
                                    Npcs[data.Name].IsInVehicle = false;
                                    Npcs[data.Name].AimCoords = data.AimCoords.ToVector();
                                    Npcs[data.Name].CurrentWeapon = data.WeaponHash;

                                    if (data.Flag != null)
                                    {
                                        Npcs[data.Name].IsAiming = (data.Flag.Value & (short)PedDataFlags.IsAiming) > 0;
                                        Npcs[data.Name].IsJumping = (data.Flag.Value & (short)PedDataFlags.IsJumping) > 0;
                                        Npcs[data.Name].IsShooting = (data.Flag.Value & (short)PedDataFlags.IsShooting) > 0;
                                        Npcs[data.Name].IsParachuteOpen = (data.Flag.Value & (short)PedDataFlags.IsParachuteOpen) > 0;
                                    }
                                }
                            }
                            break;
                        case PacketType.ChatData:
                            {
                                if (DeserializeBinary<ChatData>(msg.ReadBytes(len)) is ChatData data && !string.IsNullOrEmpty(data.Message))
                                {
                                    string sender = string.IsNullOrEmpty(data.Sender) ? "SERVER" : data.Sender;
                                    _chat.AddMessage(sender, data.Message);
                                }
                            }
                            break;
                        case PacketType.PlayerDisconnect:
                            {
                                lock (Opponents)
                                {
                                    if (DeserializeBinary<PlayerDisconnect>(msg.ReadBytes(len)) is PlayerDisconnect data && Opponents.ContainsKey(data.Id))
                                    {
                                        Opponents[data.Id].Clear();
                                        Opponents.Remove(data.Id);

                                        lock (Npcs)
                                        {
                                            foreach (KeyValuePair<string, SyncPed> pair in new Dictionary<string, SyncPed>(Npcs).Where(p => p.Value.Host == data.Id))
                                            {
                                                pair.Value.Clear();
                                                Npcs.Remove(pair.Key);
                                            }
                                        }
                                    }
                                }
                            }
                            break;
                        case PacketType.WorldSharingStop:
                            {
                                if (!(DeserializeBinary<PlayerDisconnect>(msg.ReadBytes(len)) is PlayerDisconnect data)) return;
                                lock (Npcs)
                                {
                                    foreach (KeyValuePair<string, SyncPed> pair in new Dictionary<string, SyncPed>(Npcs).Where(p => p.Value.Host == data.Id).ToList())
                                    {
                                        pair.Value.Clear();
                                        Npcs.Remove(pair.Key);
                                    }
                                }
                            }
                            break;
                        case PacketType.NativeCall:
                            {
                                NativeData data = (NativeData)DeserializeBinary<NativeData>(msg.ReadBytes(len));
                                if (data == null) return;
                                DecodeNativeCall(data);
                            }
                            break;
                        case PacketType.NativeTick:
                            {
                                NativeTickCall data = (NativeTickCall)DeserializeBinary<NativeTickCall>(msg.ReadBytes(len));
                                if (data == null) return;
                                lock (_tickNatives)
                                {
                                    if (!_tickNatives.ContainsKey(data.Identifier)) _tickNatives.Add(data.Identifier, data.Native);

                                    _tickNatives[data.Identifier] = data.Native;
                                }
                            }
                            break;
                        case PacketType.NativeTickRecall:
                            {
                                NativeTickCall data = (NativeTickCall)DeserializeBinary<NativeTickCall>(msg.ReadBytes(len));
                                if (data == null) return;
                                lock (_tickNatives) if (_tickNatives.ContainsKey(data.Identifier)) _tickNatives.Remove(data.Identifier);
                            }
                            break;
                        case PacketType.NativeOnDisconnect:
                            {
                                NativeData data = (NativeData)DeserializeBinary<NativeData>(msg.ReadBytes(len));
                                if (data == null) return;
                                lock (_dcNatives)
                                {
                                    if (!_dcNatives.ContainsKey(data.Id)) _dcNatives.Add(data.Id, data);
                                    _dcNatives[data.Id] = data;
                                }
                            }
                            break;
                        case PacketType.NativeOnDisconnectRecall:
                            {
                                NativeData data = (NativeData)DeserializeBinary<NativeData>(msg.ReadBytes(len));
                                if (data == null) return;
                                lock (_dcNatives) if (_dcNatives.ContainsKey(data.Id)) _dcNatives.Remove(data.Id);
                            }
                            break;
                    }
                }
                else if (msg.MessageType == NetIncomingMessageType.ConnectionLatencyUpdated)
                {
                    Latency = msg.ReadFloat();
                }
                else if (msg.MessageType == NetIncomingMessageType.StatusChanged)
                {
                    NetConnectionStatus newStatus = (NetConnectionStatus)msg.ReadByte();
                    switch (newStatus)
                    {
                        case NetConnectionStatus.InitiatedConnect:
                            UI.Notify("Connecting...");
                            if (MainMenu.Visible)
                            {
                                World.RenderingCamera = null;
                                MainMenu.TemporarilyHidden = false;
                                MainMenu.Visible = false;
                            }
                            break;
                        case NetConnectionStatus.Connected:
                            UI.Notify("Connection successful!");
                            _channel = msg.SenderConnection.RemoteHailMessage.ReadInt32();
                            MainMenu.Tabs.Insert(1, _serverItem);
                            MainMenu.RefreshIndex();
                            break;
                        case NetConnectionStatus.Disconnected:
                            string reason = msg.ReadString();
                            UI.Notify("You have been disconnected" + (string.IsNullOrEmpty(reason) ? " from the server." : ": " + reason));

                            lock (Opponents)
                                if (Opponents != null)
                                {
                                    Opponents.ToList().ForEach(pair => pair.Value.Clear());
                                    Opponents.Clear();
                                }

                            lock (Npcs)
                                if (Npcs != null)
                                {
                                    Npcs.ToList().ForEach(pair => pair.Value.Clear());
                                    Npcs.Clear();
                                }

                            lock (_dcNatives)
                                if (_dcNatives != null && _dcNatives.Any())
                                {
                                    _dcNatives.ToList().ForEach(pair => DecodeNativeCall(pair.Value));
                                    _dcNatives.Clear();
                                }

                            lock (_tickNatives)
                                if (_tickNatives != null)
                                    _tickNatives.Clear();

                            lock (_entityCleanup)
                            {
                                _entityCleanup.ForEach(ent => new Prop(ent).Delete());
                                _entityCleanup.Clear();
                            }

                            lock (_blipCleanup)
                            {
                                _blipCleanup.ForEach(blip => new Blip(blip).Remove());
                                _blipCleanup.Clear();
                            }

                            _chat.Reset();
                            MainMenu.Tabs.Remove(_serverItem);
                            MainMenu.RefreshIndex();
                            break;
                    }
                }
                else if (msg.MessageType == NetIncomingMessageType.DiscoveryResponse)
                {
                    int type = msg.ReadInt32(),
                        len = msg.ReadInt32();
                    byte[] bin = msg.ReadBytes(len);
                    if (!(DeserializeBinary<DiscoveryResponse>(bin) is DiscoveryResponse data)) return;

                    string itemText = msg.SenderEndPoint.Address.ToString() + ":" + data.Port;

                    List<UIMenuItem> matchedItems = new List<UIMenuItem>
                    {
                        _serverBrowser.Items.FirstOrDefault(i => i.Description == itemText),
                        _recentBrowser.Items.FirstOrDefault(i => i.Description == itemText),
                        _favBrowser.Items.FirstOrDefault(i => i.Description == itemText),
                        _lanBrowser.Items.FirstOrDefault(i => i.Description == itemText)
                    };

                    _currentOnlinePlayers += data.PlayerCount;

                    MainMenu.Money = "Servers Online: " + _currentOnlineServers + " | Players Online: " + _currentOnlinePlayers;

                    if (data.LAN)
                    {
                        UIMenuItem item = new UIMenuItem(data.ServerName);
                        string gamemode = data.Gamemode ?? "Unknown";

                        item.Text = data.ServerName;
                        item.Description = itemText;
                        item.SetRightLabel(gamemode + " | " + data.PlayerCount + "/" + data.MaxPlayers);

                        if (data.PasswordProtected)
                            item.SetLeftBadge(UIMenuItem.BadgeStyle.Lock);

                        int lastIndx = 0;
                        if (_serverBrowser.Items.Count > 0)
                            lastIndx = _serverBrowser.Index;

                        NetIncomingMessage gMsg = msg;
                        item.Activated += (sender, selectedItem) =>
                        {
                            if (IsOnServer())
                            {
                                _client.Disconnect("Switching servers.");

                                if (Opponents != null)
                                {
                                    Opponents.ToList().ForEach(pair => pair.Value.Clear());
                                    Opponents.Clear();
                                }

                                if (Npcs != null)
                                {
                                    Npcs.ToList().ForEach(pair => pair.Value.Clear());
                                    Npcs.Clear();
                                }

                                while (IsOnServer()) Yield();
                            }

                            if (data.PasswordProtected)
                            {
                                _password = Game.GetUserInput(256);
                            }

                            _connectTab.RefreshIndex();
                            ConnectToServer(gMsg.SenderEndPoint.Address.ToString(), data.Port);
                            MainMenu.TemporarilyHidden = true;
                            AddServerToRecent(item);
                        };

                        _lanBrowser.Items.Add(item);
                    }

                    foreach (UIMenuItem ourItem in matchedItems.Where(k => k != null))
                    {
                        string gamemode = data.Gamemode ?? "Unknown";

                        ourItem.Text = data.ServerName;
                        ourItem.SetRightLabel(gamemode + " | " + data.PlayerCount + "/" + data.MaxPlayers);
                        if (PlayerSettings.FavoriteServers.Contains(ourItem.Description))
                            ourItem.SetRightBadge(UIMenuItem.BadgeStyle.Star);

                        if (data.PasswordProtected)
                            ourItem.SetLeftBadge(UIMenuItem.BadgeStyle.Lock);

                        int lastIndx = 0;
                        if (_serverBrowser.Items.Count > 0)
                            lastIndx = _serverBrowser.Index;

                        NetIncomingMessage gMsg = msg;
                        ourItem.Activated += (sender, selectedItem) =>
                        {
                            if (IsOnServer())
                            {
                                _client.Disconnect("Switching servers.");

                                if (Opponents != null)
                                {
                                    Opponents.ToList().ForEach(pair => pair.Value.Clear());
                                    Opponents.Clear();
                                }

                                if (Npcs != null)
                                {
                                    Npcs.ToList().ForEach(pair => pair.Value.Clear());
                                    Npcs.Clear();
                                }

                                while (IsOnServer()) Script.Yield();
                            }

                            if (data.PasswordProtected)
                            {
                                _password = Game.GetUserInput(256);
                            }


                            ConnectToServer(gMsg.SenderEndPoint.Address.ToString(), data.Port);
                            MainMenu.TemporarilyHidden = true;
                            AddServerToRecent(ourItem);
                        };

                        _serverBrowser.Items.Remove(ourItem);
                        _serverBrowser.Items.Insert(0, ourItem);
                        _serverBrowser.MoveDown();
                    }
                }
            }
        }



        #region debug stuff

        private DateTime _artificialLagCounter = DateTime.MinValue;
        private SyncPed _debugSyncPed;
        private int _debugInterval = 30;
        private int _debugFluctuation = 0;
        private Random _r = new Random();

        private void Debug()
        {
            Ped player = Game.Player.Character;

            if (_debugSyncPed == null)
            {
                _debugSyncPed = new SyncPed(player.Model.Hash, player.Position, player.Rotation, false);
            }

            if (DateTime.Now.Subtract(_artificialLagCounter).TotalMilliseconds >= (_debugInterval))
            {
                _artificialLagCounter = DateTime.Now;
                _debugFluctuation = _r.Next(10) - 5;
                if (player.IsInVehicle())
                {
                    Vehicle veh = player.CurrentVehicle;
                    veh.Alpha = 50;

                    _debugSyncPed.VehiclePosition = veh.Position;
                    _debugSyncPed.VehicleRotation = veh.Rotation;
                    _debugSyncPed.VehicleVelocity = veh.Velocity;
                    _debugSyncPed.ModelHash = player.Model.Hash;
                    _debugSyncPed.VehicleHash = veh.Model.Hash;
                    _debugSyncPed.VehiclePrimaryColor = (int)veh.PrimaryColor;
                    _debugSyncPed.VehicleSecondaryColor = (int)veh.SecondaryColor;
                    _debugSyncPed.PedHealth = (int)(100 * ((player.Health < 0 ? 0 : player.Health) / (float)player.MaxHealth));
                    _debugSyncPed.VehicleHealth = veh.EngineHealth;
                    _debugSyncPed.VehicleSeat = Util.GetPedSeat(player);
                    _debugSyncPed.IsHornPressed = Game.Player.IsPressingHorn;
                    _debugSyncPed.Siren = veh.SirenActive;
                    _debugSyncPed.VehicleMods = CheckPlayerVehicleMods();
                    _debugSyncPed.Speed = veh.Speed;
                    _debugSyncPed.Steering = veh.SteeringAngle;
                    _debugSyncPed.IsInVehicle = true;
                    _debugSyncPed.LastUpdateReceived = Environment.TickCount;
                    _debugSyncPed.IsEngineRunning = veh.EngineRunning;
                    _debugSyncPed.Plate = veh.NumberPlate;
                    _debugSyncPed.RadioStation = (int)Game.RadioStation;
                    _debugSyncPed.IsInBurnout = veh.IsInBurnout();
                    _debugSyncPed.VehicleRPM = veh.CurrentRPM;
                    _debugSyncPed.LightsOn = veh.LightsOn;
                    _debugSyncPed.highbeamsOn = veh.HighBeamsOn;
                    _debugSyncPed.Latency = _debugInterval / 1000f;
                }
                else
                {
                    bool aiming = Game.IsControlPressed(0, GTA.Control.Aim);
                    bool shooting = player.IsShooting;

                    Vector3 aimCoord = new Vector3();
                    if (aiming || shooting)
                        aimCoord = ScreenRelToWorld(GameplayCamera.Position, GameplayCamera.Rotation,
                            new Vector2(0, 0));

                    _debugSyncPed.AimCoords = aimCoord;
                    _debugSyncPed.Position = player.Position;
                    _debugSyncPed.Rotation = player.Rotation;
                    _debugSyncPed.ModelHash = player.Model.Hash;
                    _debugSyncPed.CurrentWeapon = (int)player.Weapons.Current.Hash;
                    _debugSyncPed.PedHealth = (int)(100 * ((player.Health < 0 ? 0 : player.Health) / (float)player.MaxHealth));
                    _debugSyncPed.IsAiming = aiming;
                    _debugSyncPed.IsShooting = shooting || (player.IsInMeleeCombat && Game.IsControlJustPressed(0, Control.Attack));
                    _debugSyncPed.IsJumping = Function.Call<bool>(Hash.IS_PED_JUMPING, player.Handle);
                    _debugSyncPed.IsParachuteOpen = Function.Call<int>(Hash.GET_PED_PARACHUTE_STATE, Game.Player.Character.Handle) == 2;
                    _debugSyncPed.IsInVehicle = false;
                    _debugSyncPed.PedProps = CheckPlayerProps();
                    _debugSyncPed.LastUpdateReceived = Environment.TickCount;
                }
            }

            _debugSyncPed.DisplayLocally();

            if (_debugSyncPed.Character != null)
            {
                Function.Call(Hash.SET_ENTITY_NO_COLLISION_ENTITY, _debugSyncPed.Character.Handle, player.Handle, false);
                Function.Call(Hash.SET_ENTITY_NO_COLLISION_ENTITY, player.Handle, _debugSyncPed.Character.Handle, false);
            }


            if (_debugSyncPed.MainVehicle != null && player.IsInVehicle())
            {
                Function.Call(Hash.SET_ENTITY_NO_COLLISION_ENTITY, _debugSyncPed.MainVehicle.Handle, player.CurrentVehicle.Handle, false);
                Function.Call(Hash.SET_ENTITY_NO_COLLISION_ENTITY, player.CurrentVehicle.Handle, _debugSyncPed.MainVehicle.Handle, false);
            }

        }

        #endregion

        public void DecodeNativeCall(NativeData obj)
        {
            List<InputArgument> list = new List<InputArgument>();

            foreach (NativeArgument arg in obj.Arguments)
            {
                if (arg is IntArgument argument)
                {
                    list.Add(new InputArgument(argument.Data));
                }
                else if (arg is UIntArgument argument1)
                {
                    list.Add(new InputArgument(argument1.Data));
                }
                else if (arg is StringArgument argument2)
                {
                    list.Add(new InputArgument(argument2.Data));
                }
                else if (arg is FloatArgument argument3)
                {
                    list.Add(new InputArgument(argument3.Data));
                }
                else if (arg is BooleanArgument argument4)
                {
                    list.Add(new InputArgument(argument4.Data));
                }
                else if (arg is LocalPlayerArgument)
                {
                    list.Add(new InputArgument(Game.Player.Character.Handle));
                }
                else if (arg is OpponentPedHandleArgument argument5)
                {
                    long handle = argument5.Data;
                    lock (Opponents) if (Opponents.ContainsKey(handle) && Opponents[handle].Character != null) list.Add(new InputArgument(Opponents[handle].Character.Handle));
                }
                else if (arg is Vector3Argument tmp)
                {
                    list.Add(new InputArgument(tmp.X));
                    list.Add(new InputArgument(tmp.Y));
                    list.Add(new InputArgument(tmp.Z));
                }
                else if (arg is LocalGamePlayerArgument)
                {
                    list.Add(new InputArgument(Game.Player.Handle));
                }
            }

            NativeType nativeType = CheckNativeHash(obj.Hash);

            if ((int)nativeType >= 2)
            {
                if ((int)nativeType >= 3)
                {
                    NativeArgument modelObj = obj.Arguments[(int)nativeType - 3];
                    int modelHash = 0;

                    if (modelObj is UIntArgument argument)
                        modelHash = unchecked((int)argument.Data);
                    else if (modelObj is IntArgument argument1)
                        modelHash = argument1.Data;

                    Model model = new Model(modelHash);

                    if (model.IsValid)
                        model.Request(10000);
                }

                int entId = Function.Call<int>((Hash)obj.Hash, list.ToArray());

                lock (_entityCleanup)
                    _entityCleanup.Add(entId);

                if (obj.ReturnType is IntArgument)
                    SendNativeCallResponse(obj.Id, entId);

                return;
            }

            if (nativeType == NativeType.ReturnsBlip)
            {
                int blipId = Function.Call<int>((Hash)obj.Hash, list.ToArray());
                lock (_blipCleanup)
                    _blipCleanup.Add(blipId);

                if (obj.ReturnType is IntArgument)
                    SendNativeCallResponse(obj.Id, blipId);

                return;
            }

            if (obj.ReturnType == null)
            {
                Function.Call((Hash)obj.Hash, list.ToArray());
            }
            else
            {
                if (obj.ReturnType is IntArgument)
                    SendNativeCallResponse(obj.Id, Function.Call<int>((Hash)obj.Hash, list.ToArray()));
                else if (obj.ReturnType is UIntArgument)
                    SendNativeCallResponse(obj.Id, Function.Call<uint>((Hash)obj.Hash, list.ToArray()));
                else if (obj.ReturnType is StringArgument)
                    SendNativeCallResponse(obj.Id, Function.Call<string>((Hash)obj.Hash, list.ToArray()));
                else if (obj.ReturnType is FloatArgument)
                    SendNativeCallResponse(obj.Id, Function.Call<float>((Hash)obj.Hash, list.ToArray()));
                else if (obj.ReturnType is BooleanArgument)
                    SendNativeCallResponse(obj.Id, Function.Call<bool>((Hash)obj.Hash, list.ToArray()));
                else if (obj.ReturnType is Vector3Argument)
                    SendNativeCallResponse(obj.Id, Function.Call<Vector3>((Hash)obj.Hash, list.ToArray()));
            }
        }

        public void SendNativeCallResponse(string id, object response)
        {
            NativeResponse obj = new NativeResponse
            {
                Id = id
            };

            if (response is int int1)
                obj.Response = new IntArgument() { Data = int1 };
            else if (response is uint @int)
                obj.Response = new UIntArgument() { Data = @int };
            else if (response is string @string)
                obj.Response = new StringArgument() { Data = @string };
            else if (response is float single)
                obj.Response = new FloatArgument() { Data = single };
            else if (response is bool boolean)
                obj.Response = new BooleanArgument() { Data = boolean };
            else if (response is Vector3 tmp)
            {
                obj.Response = new Vector3Argument()
                {
                    X = tmp.X,
                    Y = tmp.Y,
                    Z = tmp.Z,
                };
            }

            NetOutgoingMessage msg = _client.CreateMessage();
            byte[] bin = SerializeBinary(obj);
            msg.Write((int)PacketType.NativeResponse);
            msg.Write(bin.Length);
            msg.Write(bin);
            _client.SendMessage(msg, NetDeliveryMethod.ReliableOrdered, 0);
        }

        private enum NativeType
        {
            Unknown = 0,
            ReturnsBlip = 1,
            ReturnsEntity = 2,
            ReturnsEntityNeedsModel1 = 3,
            ReturnsEntityNeedsModel2 = 4,
            ReturnsEntityNeedsModel3 = 5,
        }

        private NativeType CheckNativeHash(ulong hash)
        {
            switch (hash)
            {
                default:
                    return NativeType.Unknown;
                case 0xD49F9B0955C367DE:
                    return NativeType.ReturnsEntityNeedsModel2;
                case 0x7DD959874C1FD534:
                    return NativeType.ReturnsEntityNeedsModel3;
                case 0xAF35D0D2583051B0:
                case 0x509D5878EB39E842:
                case 0x9A294B2138ABB884:
                    return NativeType.ReturnsEntityNeedsModel1;
                case 0xEF29A16337FACADB:
                case 0xB4AC7D0CF06BFE8F:
                case 0x9B62392B474F44A0:
                case 0x63C6CCA8E68AE8C8:
                    return NativeType.ReturnsEntity;
                case 0x46818D79B1F7499A:
                case 0x5CDE92C702A8FCE7:
                case 0xBE339365C863BD36:
                case 0x5A039BB0BCA604B6:
                    return NativeType.ReturnsBlip;
            }
        }

        public static int GetPedSpeed(Vector3 firstVector, Vector3 secondVector)
        {
            float speed = (firstVector - secondVector).Length();

            if (speed >= 0.02f)
            {
                if (speed >= 0.12f)
                    return 3;
                else if (speed < 0.05f)
                    return 1;

                return 2;
            }

            return 0;
        }

        public static bool WorldToScreenRel(Vector3 worldCoords, out Vector2 screenCoords)
        {
            OutputArgument num1 = new OutputArgument(),
                num2 = new OutputArgument();

            screenCoords = new Vector2();

            if (!Function.Call<bool>(Hash._WORLD3D_TO_SCREEN2D, worldCoords.X, worldCoords.Y, worldCoords.Z, num1, num2))
                return false;

            screenCoords.X = (num1.GetResult<float>() - 0.5f) * 2;
            screenCoords.Y = (num2.GetResult<float>() - 0.5f) * 2;
            return true;
        }

        public static Vector3 ScreenRelToWorld(Vector3 camPos, Vector3 camRot, Vector2 coord)
        {
            Vector3 camForward = RotationToDirection(camRot),
                rotUp = camRot + new Vector3(10, 0, 0),
                rotDown = camRot + new Vector3(-10, 0, 0),
                rotLeft = camRot + new Vector3(0, 0, -10),
                rotRight = camRot + new Vector3(0, 0, 10),
                camRight = RotationToDirection(rotRight) - RotationToDirection(rotLeft),
                camUp = RotationToDirection(rotUp) - RotationToDirection(rotDown);

            double rollRad = -DegToRad(camRot.Y);

            Vector3 camRightRoll = camRight * (float)Math.Cos(rollRad) - camUp * (float)Math.Sin(rollRad),
                camUpRoll = camRight * (float)Math.Sin(rollRad) + camUp * (float)Math.Cos(rollRad);

            if (!WorldToScreenRel(camPos + camForward * 10.0f + camRightRoll + camUpRoll, out Vector2 point2D)) return camPos + camForward * 10.0f;
            if (!WorldToScreenRel(camPos + camForward * 10.0f, out Vector2 point2DZero)) return camPos + camForward * 10.0f;

            const double eps = 0.001;
            if (Math.Abs(point2D.X - point2DZero.X) < eps || Math.Abs(point2D.Y - point2DZero.Y) < eps)
                return camPos + camForward * 10.0f;

            return (camPos + camForward * 10.0f + camRightRoll * (coord.X - point2DZero.X) / (point2D.X - point2DZero.X) + camUpRoll * (coord.Y - point2DZero.Y) / (point2D.Y - point2DZero.Y));
        }

        public static Vector3 RotationToDirection(Vector3 rotation)
        {
            double z = DegToRad(rotation.Z),
                x = DegToRad(rotation.X),
                num = Math.Abs(Math.Cos(x));

            return new Vector3
            {
                X = (float)(-Math.Sin(z) * num),
                Y = (float)(Math.Cos(z) * num),
                Z = (float)Math.Sin(x)
            };
        }

        public static Vector3 DirectionToRotation(Vector3 direction)
        {
            direction.Normalize();

            double x = Math.Atan2(direction.Z, direction.Y);
            int y = 0;
            double z = -Math.Atan2(direction.X, direction.Y);

            return new Vector3
            {
                X = (float)RadToDeg(x),
                Y = (float)RadToDeg(y),
                Z = (float)RadToDeg(z)
            };
        }

        public static double DegToRad(double deg)
        {
            return deg * Math.PI / 180.0;
        }

        public static double RadToDeg(double deg)
        {
            return deg * 180.0 / Math.PI;
        }

        public static double BoundRotationDeg(double angleDeg)
        {
            int twoPi = (int)(angleDeg / 360);
            double res = angleDeg - twoPi * 360;

            return res < 0 ? res += 360 : res;
        }

        public static Vector3 RaycastEverything(Vector2 screenCoord)
        {
            Vector3 camPos = GameplayCamera.Position,
                camRot = GameplayCamera.Rotation;

            const float raycastToDist = 100.0f,
                raycastFromDist = 1f;

            Vector3 target3D = ScreenRelToWorld(camPos, camRot, screenCoord),
                source3D = camPos;

            Entity ignoreEntity = Game.Player.Character;
            if (Game.Player.Character.IsInVehicle())
                ignoreEntity = Game.Player.Character.CurrentVehicle;

            Vector3 dir = (target3D - source3D);
            dir.Normalize();

            RaycastResult raycastResults = World.Raycast(source3D + dir * raycastFromDist,
                source3D + dir * raycastToDist,
                (IntersectOptions)(1 | 16 | 256 | 2 | 4 | 8) // | peds + vehicles
                , ignoreEntity);

            return raycastResults.DitHitAnything ? raycastResults.HitCoords : camPos + dir * raycastToDist;
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
                catch (ProtoException)
                {
                    return null;
                }
            }
            return output;
        }

        public static byte[] SerializeBinary(object data)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                stream.SetLength(0);
                Serializer.Serialize(stream, data);
                return stream.ToArray();
            }
        }

        public int GetOpenUdpPort()
        {
            int startingAtPort = 5000,
                maxNumberOfPortsToCheck = 500;
            IEnumerable<int> range = Enumerable.Range(startingAtPort, maxNumberOfPortsToCheck),
                portsInUse = from p in range join used in System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners() on p equals used.Port select p;

            return range.Except(portsInUse).FirstOrDefault();
        }
    }

    public class MasterServerList
    {
        public List<string> List { get; set; }
    }
}
