namespace LiteServer
{
    public class ServerSettings
    {
        public string MasterServer { get; set; }
        public string Name { get; set; }
        public int MaxPlayers { get; set; }
        public int Port { get; set; }
        public bool PasswordProtected { get; set; }
        public string Password { get; set; }
        public bool Announce { get; set; }

        public bool AllowDisplayNames { get; set; }

        public string Gamemode { get; set; }
        public string[] Filterscripts { get; set; }

        public ServerSettings()
        {
            MasterServer = "http://litemp.000webhostapp.com/";
            Port = 4499;
            MaxPlayers = 16;
            Name = "Simple GTA Server";
            Password = "changeme";
            PasswordProtected = false;
            Gamemode = "freeroam";
            Announce = true;
            AllowDisplayNames = true;
            Filterscripts = new string[] { "" };
        }
    }
}