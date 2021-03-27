using System.Collections.Generic;
using System.Windows.Forms;

namespace LiteClient
{
    public class PlayerSettings
    {
        public string DisplayName { get; set; }
        public Keys ActivationKey { get; set; }
        public int MaxStreamedNpcs { get; set; }
        public List<string> FavoriteServers { get; set; }
        public List<string> RecentServers { get; set; }

        public PlayerSettings()
        {
            DisplayName = string.IsNullOrWhiteSpace(GTA.Game.Player.Name) ? "Player" : GTA.Game.Player.Name;
            ActivationKey = Keys.F9;
            MaxStreamedNpcs = 10;
            FavoriteServers = new List<string>();
            RecentServers = new List<string>();
        }
    }
}