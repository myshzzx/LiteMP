using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Text.RegularExpressions;
using System.Xml.Serialization;
using LiteServer;
using Newtonsoft.Json;

namespace AdminTools
{
    public class Lists
    {
        internal static UserList _accounts = new UserList();
        internal static List<long> _authenticatedUsers = new List<long>();
    }

    [Serializable]
    public class TestServerScript : ServerScript
    {
        public static string Location { get { return AppDomain.CurrentDomain.BaseDirectory; } }
        public override string Name { get { return "Server Administration Tools"; } }

        public bool afk { get; private set; }

        public int ServerWeather;
        public TimeSpan ServerTime;

        private DateTime _lastCountdown;

        private string[] _weatherNames = new[]
        {
            "CLEAR",
            "EXTRASUNNY",
            "CLOUDS",
            "OVERCAST",
            "RAIN",
            "CLEARING",
            "THUNDER",
            "SMOG",
            "FOGGY",
            "XMAS",
            "SNOWLIGHT",
            "BLIZZARD",
        };

        public override void Start()
        {
            LoadAccounts(Location + "Accounts.xml");
            Console.WriteLine("Accounts have been loaded.");

            ServerWeather = 0;
            ServerTime = new TimeSpan(12, 0, 0);
        }

        public override void OnTick()
        {
            if((DateTime.Now.ToString("ss") == "20") && (DateTime.Now.ToString("ss") == "40")) { 
                if (Properties.Settings.Default.MaxPing != 0)
                {
                    for (var i = 0; i < Program.ServerInstance.Clients.Count; i++) {
                        if ((int)(Program.ServerInstance.Clients[i].Latency * 1000) > Properties.Settings.Default.MaxPing)
                        {
                            Program.ServerInstance.SendChatMessageToAll("SERVER", string.Format("Kicking {0} for Ping {1} too high! Max: {2}", Program.ServerInstance.Clients[i].DisplayName.ToString(), (Program.ServerInstance.Clients[i].Latency * 1000).ToString(), Properties.Settings.Default.MaxPing.ToString()));
                            Program.ServerInstance.KickPlayer(Program.ServerInstance.Clients[i], string.Format("Ping {0} too high! Max: {1}", (Program.ServerInstance.Clients[i].Latency * 1000).ToString(), Properties.Settings.Default.MaxPing.ToString()));
                        }
                    }

                }
            }
        }
        public override bool OnPlayerConnect(Client player)
        {
            try
            {
                Console.Write("Nickname: " + player.DisplayName.ToString() + " | ");
                Console.Write("Realname: " + player.Name.ToString() + " | ");
                Console.Write("Ping: " + Math.Round(player.Latency * 1000, MidpointRounding.AwayFromZero).ToString() + "ms | ");
                Console.Write("IP: " + player.NetConnection.RemoteEndPoint.Address.ToString() + " | ");
                Console.Write("Game Version: " + player.GameVersion.ToString() + " | ");
                Console.Write("Script Version: " + player.RemoteScriptVersion.ToString() + " | ");
                Console.Write("Vehicle Health: " + player.VehicleHealth.ToString() + " | ");
                Console.Write("Last Position: " + player.LastKnownPosition.ToString() + " | ");
                Console.Write("\n");
            }
            catch (Exception e) { }
            string response = String.Empty;
            try
            {
                using (var webClient = new System.Net.WebClient())
                {
                    response = webClient.DownloadString("http://ip-api.com/json/"+player.NetConnection.RemoteEndPoint.Address);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Could not contact IP API.");
            }
            if (!string.IsNullOrWhiteSpace(response)) {
                IPInfo dejson = JsonConvert.DeserializeObject<IPInfo>(response);
                if (dejson.list != null)
                {
                    if (dejson.status.Equals("success"))
                    {
                        string country = dejson.countryCode;
                        Console.WriteLine("Country Code: "+country);
                    }else
                    {
                        Console.WriteLine("Could not query IP infos from API.");
                    }
                }

            }

            if (!Properties.Settings.Default.ColoredNicknames) {
                player.DisplayName = Regex.Replace(player.DisplayName, "~.~", "", RegexOptions.IgnoreCase);
            }
            if (player.DisplayName == "SERVER")
            {
                Program.ServerInstance.SendChatMessageToAll("SERVER", string.Format("Kicking {0} for impersonating.", player.DisplayName.ToString()));
                Program.ServerInstance.KickPlayer(player, "Change your nickname to a proper one."); return false;
            }
            if (Properties.Settings.Default.KickOnDefaultName) {
                if (player.DisplayName.StartsWith("RLD!") || player.DisplayName.StartsWith("Player") || player.DisplayName.StartsWith("nosTEAM"))
                {
                    Program.ServerInstance.KickPlayer(player, "Change your nickname to a proper one. (F9 -> Settings -> Nickname)"); return false;
                }
            }
            if (Properties.Settings.Default.SocialClubOnly) {
                Console.WriteLine(player.Name);
                if (player.Name.ToString() == "RLD!" || player.Name.ToString() == "nosTEAM" || player.Name.ToString() == "Player")
                {
                    Program.ServerInstance.SendChatMessageToAll("SERVER", string.Format("Kicking {0} for cracked game.", player.DisplayName.ToString()));
                    Program.ServerInstance.KickPlayer(player, "Buy the game and sign in through social club!"); return false; }
            }
            if (Properties.Settings.Default.KickOnNameDifference)
            {
                if (!player.DisplayName.Equals(player.Name))
                {
                    Program.ServerInstance.SendChatMessageToAll("SERVER", string.Format("Kicking {0} for nickname differs from account name.", player.DisplayName.ToString()));
                    Program.ServerInstance.KickPlayer(player, string.Format("Change your nickname to {0}", player.Name)); return false; }
            }
            if (Properties.Settings.Default.KickOnOutdatedScript == true)
            {
                Console.WriteLine(string.Format("[Script Version Check] Got: {0} | Expected: {1}", player.RemoteScriptVersion.ToString(), Properties.Settings.Default.ScriptVersion));
                if (!player.RemoteScriptVersion.ToString().Equals(Properties.Settings.Default.ScriptVersion))
                {
                    Program.ServerInstance.SendChatMessageToAll("SERVER", string.Format("Kicking {0} for outdated mod.", player.DisplayName.ToString()));
                    Program.ServerInstance.KickPlayer(player, string.Format("Update your LiteMP mod to version {0}", Properties.Settings.Default.ScriptVersion)); return false;
                }
            }
            if (Properties.Settings.Default.KickOnOutdatedGame)
            {
                Console.WriteLine(string.Format("[Game Version Check] Got: {0} | Expected: {1}", player.GameVersion.ToString(), Properties.Settings.Default.GameVersion.ToString()));
                if (!player.GameVersion.ToString().Equals(Properties.Settings.Default.GameVersion.ToString()))
                {
                    Program.ServerInstance.SendChatMessageToAll("SERVER", string.Format("Kicking {0} for outdated game.", player.DisplayName.ToString()));
                    Program.ServerInstance.KickPlayer(player, "Update your GTA V to the newest version!"); return false;
                }
            }
            if (Properties.Settings.Default.OnlyAsciiNickName)
            {
                if(Encoding.UTF8.GetByteCount(player.DisplayName) != player.DisplayName.Length)
                {
                    Program.ServerInstance.KickPlayer(player, "Remove all non-ascii characters from your nickname."); return false;
                }
            }
            if (Properties.Settings.Default.OnlyAsciiUserName)
            {
                if (Encoding.UTF8.GetByteCount(player.Name) != player.Name.Length)
                {
                    Program.ServerInstance.KickPlayer(player, "Remove all non-ascii characters from your Social Club username.");return false;
                }
            }
            if (Properties.Settings.Default.LimitNickNames)
            {
                if (player.DisplayName.Length < 3 || player.DisplayName.Length > 33)
                {
                    Program.ServerInstance.KickPlayer(player, "Your nickname has to be between 3 and 33 chars long."); return false;
                }
            }

            Program.ServerInstance.SendChatMessageToPlayer(player, "SERVER", Properties.Settings.Default.MOTD);

            if (player.GetAccount(false) == null)
            {
                Program.ServerInstance.SendChatMessageToPlayer(player, "SERVER", "You can register an account using /register [password]");
            }
            else
            {
                Program.ServerInstance.SendChatMessageToPlayer(player, "SERVER", "Please authenticate to your account using /login [password]");
            }

            Program.ServerInstance.SendNativeCallToPlayer(player, 0x29B487C359E19889, _weatherNames[ServerWeather]);

            Program.ServerInstance.SendNativeCallToPlayer(player, 0x47C3B5848C3E45D8, ServerTime.Hours, ServerTime.Minutes, ServerTime.Seconds);
            Program.ServerInstance.SendNativeCallToPlayer(player, 0x4055E40BD2DBEC1D, true);
            return true;
        }

        public override bool OnChatMessage(Client sender, string message)
        {
            Account account = sender.GetAccount();
            if (message == "/q")
            {
                Program.ServerInstance.KickPlayer(sender, "You left the server.");
            }
            if (message == "/help")
            {
                Program.ServerInstance.SendChatMessageToPlayer(sender, "SERVER", "Available commands: /help, /info, /stop, /restart, /godmode, /weather, /time, /kill, /kick, /register, /login, /logout"); return false;
            }
            if (message == "/afk")
            {
                if (account == null || (int)account.Level < 1)
                {
                    Program.ServerInstance.SendChatMessageToPlayer(sender, "SERVER", "Register to use this command."); return false;
                }
                if (sender.afk) { Program.ServerInstance.SendChatMessageToPlayer(sender, "SERVER", "You are already AFK."); return false; }
                sender.afk = true;
                Program.ServerInstance.SendChatMessageToAll(sender.DisplayName, "has gone AFK.");return false;
            }
            if (message == "/back")
            {
                if (account == null || (int)account.Level < 1)
                {
                    Program.ServerInstance.SendChatMessageToPlayer(sender, "SERVER", "Register to use this command."); return false;
                }
                if (!sender.afk) { Program.ServerInstance.SendChatMessageToPlayer(sender, "SERVER", "You are not AFK."); return false; }
                Program.ServerInstance.SendChatMessageToAll(sender.DisplayName, "is now back.");sender.afk = false; return false;

            }
            if (message == "/l")
            {
                if (account == null || (int)account.Level < 3)
                {
                    Program.ServerInstance.SendChatMessageToPlayer(sender, "SERVER", "Insufficent privileges."); return false;
                }
                for (var i = 0; i < Program.ServerInstance.Clients.Count; i++)
                {
                    try {
                        Client target = Program.ServerInstance.Clients[i];
                        Console.WriteLine(string.Format("" +
                        "Nickname: {0} | " +
                        "Realname: {1} |" +
                        "Ping: {2}ms | " +
                        "IP: {3} | " +
                        "Game Version: {4} | " +
                        "Script Version: {5} | " +
                        "Vehicle Health: {6} | " +
                        "Last Position: {7} | ",
                        target.DisplayName.ToString(),
                        target.Name.ToString(),
                        Math.Round(target.Latency * 1000, MidpointRounding.AwayFromZero).ToString(),
                        target.NetConnection.RemoteEndPoint.Address.ToString(),
                        target.GameVersion.ToString(),
                        target.RemoteScriptVersion.ToString(),
                        target.VehicleHealth.ToString(),
                        target.LastKnownPosition.ToString()));
                    }catch {}
                }
                Program.ServerInstance.SendChatMessageToPlayer(sender, "SERVER", "Printed playerlist to console."); return false; return false;
            }
            if (message.StartsWith("/info"))
            {
                if (account == null || (int)account.Level < 2)
                {
                    Program.ServerInstance.SendChatMessageToPlayer(sender, "SERVER", "Insufficent privileges."); return false;
                }
                var args = message.Split();
                if (args.Length <= 1)
                {
                    Program.ServerInstance.SendChatMessageToPlayer(sender, "USAGE", "/info [Player Name]"); return false;
                }
                Client target = null;
                lock (Program.ServerInstance.Clients) target = Program.ServerInstance.Clients.FirstOrDefault(c => c.DisplayName.ToLower().StartsWith(args[1].ToLower()));

                if (target == null)
                {
                    Program.ServerInstance.SendChatMessageToPlayer(sender, "ERROR", "No such player found: " + args[1]);
                    return false;
                }
                Program.ServerInstance.SendChatMessageToPlayer(sender, "1/2", string.Format("" +
                    "Nickname: {0}\n" +
                    "Realname: {1}\n" +
                    "Ping: {2}ms\n" +
                    "IP: {3}",
                    target.DisplayName.ToString(),
                    target.Name.ToString(),
                    Math.Round(target.Latency * 1000, MidpointRounding.AwayFromZero).ToString(),
                    target.NetConnection.RemoteEndPoint.Address.ToString()));
                Program.ServerInstance.SendChatMessageToPlayer(sender, "2/2", string.Format("" +
                    "Game Version: {0}\n" +
                    "Script Version: {1}\n" +
                    "Vehicle Health: {2}\n" +
                    "Last Position: {3}\n",
                    target.GameVersion.ToString(),
                    target.RemoteScriptVersion.ToString(),
                    target.VehicleHealth.ToString(),
                    target.LastKnownPosition.ToString()));
                return false;
            }
            if (message.StartsWith("/nick"))
            {
                if (account == null || (int)account.Level < 2)
                {
                    Program.ServerInstance.SendChatMessageToPlayer(sender, "SERVER", "Insufficent privileges."); return false;
                }
                var args = message.Split();
                if (args.Length <= 1)
                {
                    Program.ServerInstance.SendChatMessageToPlayer(sender, "USAGE", "/tp [Player Name]"); return false;
                }
                sender.DisplayName = args[1];return false;
            }
            if (message.StartsWith("/stop"))
            {
                if (account == null || (int)account.Level < 2)
                {
                    Program.ServerInstance.SendChatMessageToPlayer(sender, "SERVER", "Insufficent privileges."); return false;
                }
                Program.ServerInstance.SendChatMessageToAll("SERVER", "This server will stop now!");
                Environment.Exit(-1);return false;
            }
            if (message.StartsWith("/restart"))
            {
                if (account == null || (int)account.Level < 2)
                {
                    Program.ServerInstance.SendChatMessageToPlayer(sender, "SERVER", "Insufficent privileges.");
                    return false;
                }
                Program.ServerInstance.SendChatMessageToAll("SERVER", "~p~This server will restart now. Please reconnect!~p~");
                
                return false;
            }

            if (message.StartsWith("/godmode"))
            {
                var args = message.Split();
                if (args.Length <= 1)
                {
                    Program.ServerInstance.SendChatMessageToPlayer(sender, "USAGE", "/godmode [Player Name]");
                    return false;
                }

                Client target = null;
                lock (Program.ServerInstance.Clients) target = Program.ServerInstance.Clients.FirstOrDefault(c => c.DisplayName.ToLower().StartsWith(args[1].ToLower()));

                if (target == null)
                {
                    Program.ServerInstance.SendChatMessageToPlayer(sender, "ERROR", "No such player found: " + args[1]);
                    return false;
                }

                return false;
            }

            if (message.StartsWith("/weather"))
            {
                if (account == null || (int)account.Level < 1)
                {
                    Program.ServerInstance.SendChatMessageToPlayer(sender, "SERVER", "Insufficent privileges.");
                    return false;
                }

                var args = message.Split();
                if (args.Length <= 1)
                {
                    Program.ServerInstance.SendChatMessageToPlayer(sender, "USAGE", "/weather [Weather ID]");
                    return false;
                }

                int newWeather;
                if (!int.TryParse(args[1], out newWeather))
                {
                    Program.ServerInstance.SendChatMessageToPlayer(sender, "USAGE", "/weather [Weather ID]");
                    return false;
                }

                if (newWeather < 0 || newWeather >= _weatherNames.Length)
                {
                    Program.ServerInstance.SendChatMessageToPlayer(sender, "USAGE", "Weather ID must be between 0 and " + (_weatherNames.Length-1));
                    return false;
                }

                ServerWeather = newWeather;
                Program.ServerInstance.SendNativeCallToAllPlayers(0x29B487C359E19889, _weatherNames[ServerWeather]);

                Console.WriteLine(string.Format("ADMINTOOLS: {0} has changed the weather to {1}", account.Name + " (" + sender.DisplayName + ")", ServerWeather));

                return false;
            }

            if (message.StartsWith("/time"))
            {
                if (account == null || (int)account.Level < 1)
                {
                    Program.ServerInstance.SendChatMessageToPlayer(sender, "SERVER", "Insufficent privileges.");
                    return false;
                }

                var args = message.Split();
                if (args.Length <= 1)
                {
                    Program.ServerInstance.SendChatMessageToPlayer(sender, "USAGE", "/time [hours]:[minutes]");
                    return false;
                }

                int hours;
                int minutes;
                var timeSplit = args[1].Split(':');

                if (timeSplit.Length < 2 || !int.TryParse(timeSplit[0], out hours) || !int.TryParse(timeSplit[1], out minutes))
                {
                    Program.ServerInstance.SendChatMessageToPlayer(sender, "USAGE", "/time [hours]:[minutes]");
                    return false;
                }

                if (hours < 0 || hours > 24 || minutes < 0 || minutes > 60)
                {
                    Program.ServerInstance.SendChatMessageToPlayer(sender, "USAGE", "/time [hours]:[minutes]");
                    return false;
                }

                ServerTime = new TimeSpan(hours, minutes, 0);

                Program.ServerInstance.SendNativeCallToAllPlayers(0x47C3B5848C3E45D8, ServerTime.Hours, ServerTime.Minutes, ServerTime.Seconds);
                Program.ServerInstance.SendNativeCallToAllPlayers(0x4055E40BD2DBEC1D, true);

                Console.WriteLine(string.Format("ADMINTOOLS: {0} has changed the time to {1}", account.Name + " (" + sender.DisplayName + ")", ServerTime));

                return false;
            }

            if (message.StartsWith("/kill"))
            {
                if (account == null || (int)account.Level < 1)
                {
                    Program.ServerInstance.SendChatMessageToPlayer(sender, "SERVER", "Insufficent privileges.");
                    return false;
                }

                var args = message.Split();
                if (args.Length <= 1)
                {
                    Program.ServerInstance.SendChatMessageToPlayer(sender, "USAGE", "/kill [Player Name]");
                    return false;
                }

                Client target = null;
                lock (Program.ServerInstance.Clients) target = Program.ServerInstance.Clients.FirstOrDefault(c => c.DisplayName.ToLower().StartsWith(args[1].ToLower()));

                if (target == null)
                {
                    Program.ServerInstance.SendChatMessageToPlayer(sender, "ERROR", "No such player found: " + args[1]);
                    return false;
                }

                Program.ServerInstance.SetPlayerHealth(target, -1);
                Console.WriteLine(string.Format("ADMINTOOLS: {0} has killed player {1}", account.Name + " (" + sender.DisplayName + ")", target.Name + " (" + target.DisplayName + ")"));
                return false;
            }

            if (message.StartsWith("/kick"))
            {
                if (account == null || (int)account.Level < 1)
                {
                    Program.ServerInstance.SendChatMessageToPlayer(sender, "SERVER", "Insufficent privileges.");
                    return false;
                }

                var args = message.Split();
                if (args.Length <= 2)
                {
                    Program.ServerInstance.SendChatMessageToPlayer(sender, "USAGE", "/kick [Player Name] [Reason]");
                    return false;
                }

                Client target = null;
                lock (Program.ServerInstance.Clients) target = Program.ServerInstance.Clients.FirstOrDefault(c => c.DisplayName.ToLower().StartsWith(args[1].ToLower()));

                if (target == null)
                {
                    Program.ServerInstance.SendChatMessageToPlayer(sender, "ERROR", "No such player found: " + args[1]);
                    return false;
                }

                Program.ServerInstance.KickPlayer(target, args[2]);
                Console.WriteLine(string.Format("SERVER: {0} has kicked player {1}", account.Name + " (" + sender.DisplayName + ")", target.Name + " (" + target.DisplayName + ")"));
                return false;
            }

            if (message.StartsWith("/register"))
            {
                account = sender.GetAccount(false);
                if (account != null)
                {
                    Program.ServerInstance.SendChatMessageToPlayer(sender, "SERVER", "You already have an account.");
                    return false;
                }

                var args = message.Split();
                if (args.Length <= 1)
                {
                    Program.ServerInstance.SendChatMessageToPlayer(sender, "USAGE", "/register [Password]");
                    return false;
                }

                var password = GetHashSha256(args[1]);
                var accObject = new Account()
                {
                    Level = Privilege.User,
                    Name = sender.DisplayName,
                    Password = password
                };
                lock (Lists._accounts.Accounts) Lists._accounts.Accounts.Add(accObject);
                SaveAccounts(Location + "Accounts.xml");
                lock (Lists._authenticatedUsers) Lists._authenticatedUsers.Add(sender.NetConnection.RemoteUniqueIdentifier);

                Program.ServerInstance.SendChatMessageToPlayer(sender, "SERVER", "Your account has been created!");
                Console.WriteLine(string.Format("SERVER: New player registered: {0}", accObject.Name));
                return false;
            }

            if (message.StartsWith("/login"))
            {
                if (account != null)
                {
                    Program.ServerInstance.SendChatMessageToPlayer(sender, "SERVER", "You are already authenticated.");
                    return false;
                }

                account = sender.GetAccount(false);

                if (account == null)
                {
                    Program.ServerInstance.SendChatMessageToPlayer(sender, "SERVER", "No accounts have been found with your name.");
                    return false;
                }

                var args = message.Split();
                if (args.Length <= 1)
                {
                    Program.ServerInstance.SendChatMessageToPlayer(sender, "USAGE", "/login [Password]");
                    return false;
                }

                var password = GetHashSha256(args[1]);

                if (password != account.Password)
                {
                    Program.ServerInstance.SendChatMessageToPlayer(sender, "SERVER", "Wrong password.");
                    return false;
                }

                lock (Lists._authenticatedUsers) if (!Lists._authenticatedUsers.Contains(sender.NetConnection.RemoteUniqueIdentifier)) Lists._authenticatedUsers.Add(sender.NetConnection.RemoteUniqueIdentifier);

                Program.ServerInstance.SendChatMessageToPlayer(sender, "SERVER", "Authentication successful!");
                if ((int)account.Level == 0) {
                    Console.WriteLine(string.Format("User \"{0}\" logged in.", account.Name + " (" + sender.DisplayName + ")"));
                    return false;
                } else if((int)account.Level == 1) {
                    Console.WriteLine(string.Format("Moderator \"{0}\" logged in.", account.Name + " (" + sender.DisplayName + ")"));
                    return false;
                }else if ((int)account.Level == 2) {
                    Console.WriteLine(string.Format("Administrator \"{0}\" logged in.", account.Name + " (" + sender.DisplayName + ")"));
                    return false;
                }
                else if ((int)account.Level == 3) {
                    Console.WriteLine(string.Format("Owner \"{0}\" logged in.", account.Name + " (" + sender.DisplayName + ")"));
                    return false;
                }
            }

            if (message == "/logout")
            {
                if (sender.IsAuthenticated())
                {
                    Console.WriteLine(string.Format("SERVER: Player has logged out: {0}", sender.Name));
                    Program.ServerInstance.SendChatMessageToPlayer(sender, "SERVER", "You have been logged out.");

                    lock (Lists._authenticatedUsers) if (Lists._authenticatedUsers.Contains(sender.NetConnection.RemoteUniqueIdentifier)) Lists._authenticatedUsers.Remove(sender.NetConnection.RemoteUniqueIdentifier);
                }
                else
                {
                    Program.ServerInstance.SendChatMessageToPlayer(sender, "SERVER", "You are not logged in.");
                }
                return false;
            }

            if (message == "/countdown")
            {
                if (DateTime.Now.Subtract(_lastCountdown).TotalSeconds < 30)
                {
                    Program.ServerInstance.SendChatMessageToPlayer(sender, "COUNTDOWN", "Please wait 30 seconds before starting another countdown.");
                    return false;
                }

                _lastCountdown = DateTime.Now;

                var cdThread = new Thread((ThreadStart) delegate
                {
                    for (int i = 3; i >= 0; i--)
                    {
                        Program.ServerInstance.SendChatMessageToAll("COUNTDOWN", i == 0 ? "Go!" : i.ToString());
                        Thread.Sleep(1000);
                    }
                });
                cdThread.Start();
                return false;
            }
            if (message.Contains("login") || message.Contains("register")) { return false; }
            return true;
        }

        public override void OnPlayerKilled(Client player)
        {
            Program.ServerInstance.SendNativeCallToPlayer(player, 0x29B487C359E19889, _weatherNames[ServerWeather]);

            Program.ServerInstance.SendNativeCallToPlayer(player, 0x47C3B5848C3E45D8, ServerTime.Hours, ServerTime.Minutes, ServerTime.Seconds);
            Program.ServerInstance.SendNativeCallToPlayer(player, 0x4055E40BD2DBEC1D, true);
        }

        private void LoadAccounts(string path)
        {
            XmlSerializer ser = new XmlSerializer(typeof(UserList));
            if (File.Exists(path))
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite))
                {
                    Lists._accounts = (UserList)ser.Deserialize(stream);
                }
            }
            else
            {
                Lists._accounts = new UserList();
                Lists._accounts.Accounts = new List<Account>();
                SaveAccounts(path);
            }
        }

        private void SaveAccounts(string path)
        {
            XmlSerializer ser = new XmlSerializer(typeof(UserList));
            using (var stream = new FileStream(path, File.Exists(path) ? FileMode.Truncate : FileMode.Create, FileAccess.ReadWrite))
            {
                ser.Serialize(stream, Lists._accounts);
            }
        }

        private string GetHashSha256(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            SHA256Managed hashstring = new SHA256Managed();
            byte[] hash = hashstring.ComputeHash(bytes);

            StringBuilder hashString = new StringBuilder();
            foreach (byte x in hash)
            {
                hashString.Append(string.Format("{0:x2}", x));
            }
            return hashString.ToString();
        }
    }

    public class IPInfo
    {
        public string countryCode { get; internal set; }
        public List<string> list { get; set; }
        public object status { get; internal set; }
    }

    public enum Privilege
    {
        User = 0,
        Moderator = 1,
        Administrator = 2,
        Owner = 3,
    }

    public class UserList
    {
        public List<Account> Accounts { get; set; }
    }

    public class Account
    {
        public string Name { get; set; }
        public string Password { get; set; }
        public Privilege Level { get; set; }
    }

    public static class Accounts
    {
        public static bool IsAuthenticated(this Client client)
        {
            lock (Lists._authenticatedUsers) return Lists._authenticatedUsers.Contains(client.NetConnection.RemoteUniqueIdentifier);
        }

        public static Account GetAccount(this Client client, bool checkauthentication = true)
        {
            if (!checkauthentication || client.IsAuthenticated()) lock (Lists._accounts.Accounts) return Lists._accounts.Accounts.FirstOrDefault(acc => acc.Name == client.DisplayName);

            return null;
        }

        public static Client GetClient(this Account account)
        {
            Client client = null;
            lock (Program.ServerInstance.Clients) Program.ServerInstance.Clients.Any(c =>
            {
                if (c.DisplayName == account.Name)
                {
                    client = c;

                    return true;
                }

                return false;
            });

            return client;
        }
    }
}