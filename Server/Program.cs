using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Xml.Serialization;

namespace LiteServer
{
    public static class Program
    {
        public static string Location { get { return AppDomain.CurrentDomain.BaseDirectory; } }
        public static GameServer ServerInstance { get; set; }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteFile(string name);

        const int tickRate = 60;

        static void Main(string[] args)
        {
            var settings = ReadSettings(Program.Location + "Settings.xml");
            Console.WriteLine("Starting LiteServer...");

            Console.WriteLine("[=======================================]");
            Console.WriteLine("||");
            Console.WriteLine("||\tName: " + settings.Name);
            Console.WriteLine("||\tPort: " + settings.Port);
            Console.WriteLine("||\tPlayer Limit: " + settings.MaxPlayers);
            Console.WriteLine("||");
            Console.WriteLine("[=======================================]");

            if (settings.Port != 4499)
                Log.LogToConsole(3, "Server", "WARN: Port is not the default one, players on your local network won't be able to automatically detect you!");


            ServerInstance = new GameServer(settings.Port, settings.Name, settings.Gamemode);
            ServerInstance.PasswordProtected = settings.PasswordProtected;
            ServerInstance.Password = settings.Password;
            ServerInstance.AnnounceSelf = settings.Announce;
            ServerInstance.MasterServer = "http://masterserver.lite-mp.com/";
            ServerInstance.MaxPlayers = settings.MaxPlayers;
            ServerInstance.AllowDisplayNames = settings.AllowDisplayNames;

            ServerInstance.Start(settings.Filterscripts);

            Log.LogToConsole(2, "Server", "Started! Waiting for connections.");

            while (true)
            {
                ServerInstance.Tick();
                System.Threading.Thread.Sleep(1000 / tickRate);
            }
        }

        static ServerSettings ReadSettings(string path)
        {
            var ser = new XmlSerializer(typeof(ServerSettings));

            ServerSettings settings = null;

            if (File.Exists(path))
            {
                using (var stream = File.OpenRead(path)) settings = (ServerSettings)ser.Deserialize(stream);

                using (var stream = new FileStream(path, File.Exists(path) ? FileMode.Truncate : FileMode.Create, FileAccess.ReadWrite)) ser.Serialize(stream, settings);
            }
            else
            {
                using (var stream = File.OpenWrite(path)) ser.Serialize(stream, settings = new ServerSettings());
            }

            return settings;
        }
    }
}
