using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TShockAPI;
using TShockAPI.DB;
using Terraria;

namespace Factions
{
    public class Faction
    {
        [Flags]
        public enum Settings
        {
            Color1 = 1,
            Color2 = 2,
            Private = 4,
            Hostile = 64
        }
        public int ID;
        private string name;
        public string Name
        {
            get { return this.name; }
            set { this.name = value; Factions.DoQuery("UPDATE factions_Factions SET Name = @0 WHERE ID = @1", this.name, this.ID); }
        }
        private string desc;
        public string Desc
        {
            get { return this.desc; }
            set { this.desc = value; Factions.DoQuery("UPDATE factions_Factions SET Description = @0 WHERE ID = @1", this.desc, this.ID); }
        }
        public List<int> Members;
        public List<int> Admins;
        public List<int> Invites;
        public List<Region> Regions;
        private Settings Flags;
        public int Power;
        public List<int> Allies;
        public List<int> AllyInvites;
        public List<int> Enemies;
        public Faction(int id, string name, string desc, Player player)
        {
            this.ID = id;
            this.name = name;
            this.desc = desc;
            this.Power = player.Power;
            this.Admins = new List<int>();
            this.Admins.Add(player.UserID);
            this.Members = new List<int>();
            this.Members.Add(player.UserID);
            this.Allies = new List<int>();
            this.AllyInvites = new List<int>();
            this.Enemies = new List<int>();
            this.Regions = new List<Region>();
            this.Invites = new List<int>();
            this.Flags = (Settings)0;
        }
        public Faction(int id, string name, string desc, List<int> members, List<int> admins, List<int> invites, int power, List<Region> regions) : this(id, name, desc, members, admins, invites, power, regions, 0, new List<int>(), new List<int>(), new List<int>()) { }
        public Faction(int id, string name, string desc, List<int> members, List<int> admins, List<int> invites, int power, List<Region> regions, byte flags, List<int> allies, List<int> allyinvites, List<int> enemies)
        {
            this.ID = id;
            this.name = name;
            this.desc = desc;
            this.Members = members;
            this.Admins = admins;
            this.Invites = invites;
            this.Regions = regions;
            this.Flags = (Settings)flags;
            this.Power = power;
            this.Allies = allies;
            this.AllyInvites = allyinvites;
            this.Enemies = enemies;
        }
        public bool IsAdmin(int userID)
        {
            if (this.Admins.Contains(userID))
                return true;
            return false;
        }
        public List<String> MemberNames() { return MemberNames(false); }
        public List<String> MemberNames(bool excludeAdmins)
        {
            List<String> ReturnList = new List<string>();
            try
            {
                foreach (int userid in (excludeAdmins) ? this.Members.Except(this.Admins) : this.Members)
                {
                    using (QueryResult reader = Factions.GetQuery("SELECT Name FROM factions_Players WHERE UserID = @0 AND WorldID = @1", userid, Main.worldID))
                    {
                        if (reader.Read())
                            ReturnList.Add(reader.Get<string>("Name"));
                    }
                }
            }
            catch (Exception ex) { Log.ConsoleError(ex.ToString()); }
            return ReturnList;
        }
        public List<String> AdminNames()
        {
            List<String> ReturnList = new List<string>();
            try
            {
                foreach (int userid in this.Admins)
                {
                    using (QueryResult reader = Factions.GetQuery("SELECT Name FROM factions_Players WHERE UserID = @0 AND WorldID = @1", userid, Main.worldID))
                    {
                        if (reader.Read())
                            ReturnList.Add(reader.Get<string>("Name"));
                    }
                }
            }
            catch (Exception ex) { Log.ConsoleError(ex.ToString()); }
            return ReturnList;
        }
        public String TeamColor
        {
            get
            {
                if (this.Flags.HasFlag(Settings.Color1))
                {
                    if (this.Flags.HasFlag(Settings.Color2))
                        return "Yellow";
                    else
                        return "Green";
                }
                else
                {
                    if (this.Flags.HasFlag(Settings.Color2))
                        return "Blue";
                    else
                        return "Red";
                }
            }
        }
        public byte Team
        {
            get
            {
                if (this.Flags.HasFlag(Settings.Color1))
                {
                    if (this.Flags.HasFlag(Settings.Color2))
                        return 4;
                    else
                        return 2;
                }
                else
                {
                    if (this.Flags.HasFlag(Settings.Color2))
                        return 3;
                    else
                        return 1;
                }
            }
            set
            {
                switch (value)
                {
                    case 4:
                        {
                            this.Flags |= Settings.Color1;
                            this.Flags |= Settings.Color2;
                            break;
                        }
                    case 3:
                        {
                            this.Flags &= ~Settings.Color1;
                            this.Flags |= Settings.Color2;
                            break;
                        }
                    case 2:
                        {
                            this.Flags |= Settings.Color1;
                            this.Flags &= ~Settings.Color2;
                            break;
                        }
                    default:
                        {
                            this.Flags &= ~Settings.Color1;
                            this.Flags &= ~Settings.Color2;
                            break;
                        }
                }
                Factions.DoQuery("UPDATE factions_Factions SET Flags = @0 WHERE ID = @1", this.Flags, this.ID);
                this.RefreshTeamStatus();
            }
        }
        public bool Hostile
        {
            get
            {
                return this.Flags.HasFlag(Settings.Hostile);
            }
            set
            {
                if (value)
                    this.Flags |= Settings.Hostile;
                else
                    this.Flags &= ~Settings.Hostile;
                Factions.DoQuery("UPDATE factions_Factions SET Flags = @0 WHERE ID = @1", this.Flags, this.ID);
                this.RefreshPVPStatus();
            }
        }
        public bool Private
        {
            get
            {
                return this.Flags.HasFlag(Settings.Private);
            }
            set
            {
                if (value)
                    this.Flags |= Settings.Private;
                else
                    this.Flags &= ~Settings.Private;
                Factions.DoQuery("UPDATE factions_Factions SET Flags = @0 WHERE ID = @1", this.Flags, this.ID);
            }
        }
        public bool InvitePlayer(Player player)
        {
            if (player == null || player.UserID == -1)
                return false;
            if (this.Invites.Contains(player.UserID))
            {
                player.TSplayer.SendMessage(String.Format("You have been invited to join faction '{0}'", this.Name), Color.BurlyWood);
                return true;
            }
            else
            {
                try
                {
                    this.Invites.Add(player.UserID);
                    Factions.DoQuery("UPDATE factions_Factions SET Invites = @0 WHERE ID = @1", String.Join(",", this.Invites), this.ID);
                    player.TSplayer.SendMessage(String.Format("You have been invited to join faction '{0}'", this.Name), Color.BurlyWood);
                    return true;
                }
                catch (Exception ex) { Log.ConsoleError(ex.ToString()); }
            }
            return false;
        }
        public List<Faction> GetAllies()
        {
            List<Faction> returnList = new List<Faction>();
            foreach (int fID in this.Allies)
            {
                var fact = GetFactionByID(fID);
                if (fact != null)
                    returnList.Add(fact);
            }
            return returnList;
        }
        public List<Faction> GetEnemies()
        {
            List<Faction> returnList = new List<Faction>();
            foreach (int fID in this.Enemies)
            {
                var fact = GetFactionByID(fID);
                if (fact != null)
                    returnList.Add(fact);
            }
            return returnList;
        }
        public String GetAlliesString() { return String.Join(", ", this.GetAllies().Select(i => i.Name)); }
        public String GetEnemiesString() { return String.Join(", ", this.GetEnemies().Select(i => i.Name)); }
        public List<Player> GetOnlineMembers()
        {
            List<Player> returnList = new List<Player>();
            foreach (Player ply in Factions.PlayerList)
            {
                if (ply != null && ply.Faction != null && ply.Faction.Equals(this))
                    returnList.Add(ply);
            }
            return returnList;
        }
        public bool AddMember(Player player)
        {
            if (player == null || player.ID == -1 || player.UserID == -1 || player.Faction != null)
                return false;
            try
            {
                player.Faction = this;
                if (this.Flags.HasFlag(Settings.Hostile) && !player.TSplayer.TPlayer.hostile)
                {
                    player.TSplayer.TPlayer.hostile = true;
                    TShockAPI.TSPlayer.All.SendData(PacketTypes.TogglePvp, "", player.ID);
                    player.TSplayer.SendMessage("Your PvP status has been changed due to Faction Hostile status", Color.BurlyWood);
                }
                else if (!this.Flags.HasFlag(Settings.Hostile) && player.TSplayer.TPlayer.hostile)
                {
                    player.TSplayer.TPlayer.hostile = false;
                    TShockAPI.TSPlayer.All.SendData(PacketTypes.TogglePvp, "", player.ID);
                    player.TSplayer.SendMessage("Your PvP status has been changed due to Faction Peaceful status", Color.BurlyWood);
                }
                this.Members.Add(player.UserID);
                this.Invites.Remove(player.UserID);
                this.Power += player.Power;
                Factions.DoQuery("UPDATE factions_Factions SET Power = @0, Members = @1 WHERE ID = @2", this.Power, String.Join(",", this.Members), this.ID);
                Factions.DoQuery("UPDATE factions_Players SET Faction = @0 WHERE UserID = @1 AND WorldID = @2", this.ID, player.UserID, Main.worldID);

                player.TSplayer.TPlayer.team = this.Team;
                player.TSplayer.SendData(PacketTypes.PlayerTeam, "", player.ID);
                foreach (Player member in this.GetOnlineMembers())
                {
                    member.TSplayer.SendData(PacketTypes.PlayerTeam, "", player.ID);
                    member.TSplayer.TPlayer.team = this.Team;
                    player.TSplayer.SendData(PacketTypes.PlayerTeam, "", member.ID);
                    member.TSplayer.SendMessage(String.Format("Player {0} has joined the faction", player.TSplayer.Name), Color.BurlyWood);
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.ConsoleError(ex.ToString());
                return false;
            }
        }
        public void RemoveMember(int userid)
        {
            try
            {
                this.Admins.Remove(userid);
                if (this.Members.Remove(userid))
                {
                    var player = Player.GetPlayerByUserID(userid);
                    if (player != null)
                        player.Faction = null;
                    Factions.DoQuery("UPDATE factions_Players SET Faction = @0 WHERE UserID = @1 AND WorldID = @2", 0, userid, Main.worldID);
                    if (this.Members.Count == 0) // delete faction                        
                        this.DeleteFaction();
                    else // recalculate power
                    {
                        if (this.Admins.Count == 0)
                            this.Admins = this.Members.ToList();
                        Factions.DoQuery("UPDATE factions_Factions SET Members = @0, Admins = @1 WHERE ID = @2", String.Join(",", this.Members), String.Join(",", this.Admins), this.ID);
                        if (player != null)
                        {
                            player.TSplayer.TPlayer.team = 0;
                            player.TSplayer.SendData(PacketTypes.PlayerTeam, "", player.ID);
                            foreach (Player member in this.GetOnlineMembers())
                            {
                                member.TSplayer.SendData(PacketTypes.PlayerTeam, "", player.ID);
                                member.TSplayer.TPlayer.team = 0;
                                player.TSplayer.SendData(PacketTypes.PlayerTeam, "", member.ID);
                                member.TSplayer.TPlayer.team = this.Team;
                                member.TSplayer.SendMessage(String.Format("Player {0} has left the faction", player.TSplayer.Name), Color.BurlyWood);
                            }
                        }
                        RefreshPower();
                    }
                }
            }
            catch (Exception ex) { Log.ConsoleError(ex.ToString()); }
        }
        public void DeleteFaction()
        {
            try
            {
                foreach (int userid in this.Members)
                {
                    var player = Player.GetPlayerByUserID(userid);
                    if (player != null)
                        player.Faction = null;
                    Factions.DoQuery("UPDATE factions_Players SET Faction = @0 WHERE UserID = @1 AND WorldID = @2", 0, userid, Main.worldID);
                }
                for (int i = this.Regions.Count - 1; i >= 0; i--)
                {
                    if (Factions.Regions.Remove(this.Regions[i]))
                    {
                        Factions.DoQuery("DELETE FROM factions_Regions WHERE ID = @0", this.Regions[i].ID);
                        this.Regions[i].Faction = 0;
                    }
                }
                if (Factions.FactionList.Remove(this))
                    Factions.DoQuery("DELETE FROM factions_Factions WHERE ID = @0", this.ID);
                TShockAPI.TSPlayer.All.SendMessage(String.Format("Faction {0} has been deleted.", this.Name), Color.LightSalmon);
            }
            catch (Exception ex) { Log.ConsoleError(ex.ToString()); }
        }
        public void RefreshTeamStatus()
        {
            var onlineMembers = this.GetOnlineMembers();
            foreach (Player player in onlineMembers)
            {
                if (player.TSplayer.TPlayer.team != this.Team)
                {
                    player.TSplayer.TPlayer.team = this.Team;
                    foreach (Player p2 in onlineMembers)
                    {
                        p2.TSplayer.SendData(PacketTypes.PlayerTeam, "", player.ID);
                    }
                }
            }
        }
        public void RefreshPVPStatus()
        {
            var onlineMembers = this.GetOnlineMembers();
            foreach (Player player in onlineMembers)
            {
                if ((this.Flags.HasFlag(Settings.Hostile) && !player.TSplayer.TPlayer.hostile) || (!this.Flags.HasFlag(Settings.Hostile) && player.TSplayer.TPlayer.hostile))
                {
                    player.TSplayer.TPlayer.hostile = !player.TSplayer.TPlayer.hostile;
                    NetMessage.SendData((int)PacketTypes.TogglePvp, -1, -1, "", player.ID);
                    player.TSplayer.SendMessage(String.Format("Faction PvP status changed to: {0}. Please ensure it matches the settings in your client.", this.Hostile ? "Hostile" : "Peaceful"), Color.LightSalmon);
                }
            }
        }
        public void RefreshPower()
        {
            try
            {
                int newPower = 0;

                foreach (int userid in this.Members)
                {
                    using (QueryResult reader = Factions.GetQuery("SELECT Power FROM factions_Players WHERE UserID = @0 AND WorldID = @1", userid, Main.worldID))
                    {
                        if (reader.Read())
                            newPower += reader.Get<int>("Power");
                    }
                }
                this.Power = newPower;
                Factions.DoQuery("UPDATE factions_Factions SET Power = @0 WHERE ID = @1", newPower, this.ID);

            }
            catch (Exception ex) { Log.ConsoleError(ex.ToString()); }
        }
        public bool RemovePower(int removeAmmount)
        {

            if (removeAmmount > this.Power)
                return false;
            List<Player> memberList = new List<Player>();
            try
            {
                foreach (int userid in this.Members)
                {
                    var player = Player.GetPlayerByUserID(userid);
                    if (player != null)
                        memberList.Add(player);
                    else
                    {
                        using (QueryResult reader = Factions.GetQuery("SELECT Power FROM factions_Players WHERE UserID = @0 AND WorldID = @1", userid, Main.worldID))
                        {
                            if (reader.Read())
                                memberList.Add(new Player(-1, reader.Get<int>("Power"), this, userid));
                        }
                    }
                }
            }
            catch (Exception ex) { Log.ConsoleError(ex.ToString()); }

            while (removeAmmount > 0 && memberList.Count > 0)
            {
                int share = removeAmmount / memberList.Count;
                if (share <= 0)
                    break;
                /*   if (share * memberList.Count != removeAmmount)
                       Console.WriteLine(" -> left: {0}", removeAmmount - share * memberList.Count);*/
                int leftover = 0;
                for (int i = memberList.Count - 1; i >= 0; i--)
                {
                    var player = memberList[i];
                    if (player.Power >= share)
                        player.Power -= share;
                    else
                    {
                        leftover += share - player.Power;
                        player.Power = 0;
                        memberList.Remove(player);
                    }
                    Factions.DoQuery("UPDATE factions_Players SET Power = @0 WHERE UserID = @1 AND WorldID = @2", player.Power, player.UserID, Main.worldID);
                }
                removeAmmount = leftover;
            }
            this.RefreshPower();
            return true;
        }
        public void MessageMembers(String message)
        {
            foreach (Player player in this.GetOnlineMembers())
            {
                player.TSplayer.SendMessage(message, Color.BurlyWood);
            }
        }
        public void MessageAdmins(String message)
        {
            foreach (Player player in this.GetOnlineMembers())
            {
                if (this.Admins.Contains(player.UserID))
                    player.TSplayer.SendMessage(message, Color.BurlyWood);
            }
        }
        public bool InFactionTerritory(Point point)
        {
            foreach (Region region in this.Regions)
            {
                if (region.InArea(point))
                    return true;
            }
            return false;
        }

        /*
         *    ---------------------------------------------- Static Faction methods ----------------------------------------
         */
        public static Faction GetFactionByID(int id)
        {
            foreach (Faction fact in Factions.FactionList)
            {
                if (fact.ID == id)
                    return fact;
            }
            return null;
        }
        public static Faction GetFactionByName(string name)
        {
            foreach (Faction fact in Factions.FactionList)
            {
                if (fact.Name == name)
                    return fact;
            }
            return null;
        }
        public static List<Faction> GetTopFactionsByPower() { return GetTopFactionsByPower(0); }
        public static List<Faction> GetTopFactionsByPower(int count)
        {
            var sorted = from fact in Factions.FactionList orderby fact.Power descending select fact;
            return sorted.ToList();
        }
        public static bool IsValidName(string name)
        {
            string validchars = " 1234567890aAbBcCdDeEfFgGhHiIjJkKlLmMnNoOpPqQrRsStTuUvVwWxXzZyY";
            foreach (char c in name.ToCharArray())
            {
                if (!validchars.Contains(c))
                    return false;
            }
            if (name.StartsWith(" "))
                return false;
            if (name.Length > 20)
                return false;
            return true;
        }
        public static void AllyFactions(Faction faction1, Faction faction2)
        {
            if (faction1.ID == faction2.ID)
                return;
            faction1.AllyInvites.Remove(faction2.ID);
            faction2.AllyInvites.Remove(faction1.ID);
            faction1.Enemies.Remove(faction2.ID);
            faction2.Enemies.Remove(faction1.ID);
            if (!faction1.Allies.Contains(faction2.ID))
                faction1.Allies.Add(faction2.ID);
            if (!faction2.Allies.Contains(faction1.ID))
                faction2.Allies.Add(faction1.ID);
            Factions.DoQuery("UPDATE factions_Factions SET Allies = @0, AllyInvites = @1, Enemies = @2 WHERE ID = @3", String.Join(",", faction1.Allies), String.Join(",", faction1.AllyInvites), String.Join(",", faction1.Enemies), faction1.ID);
            Factions.DoQuery("UPDATE factions_Factions SET Allies = @0, AllyInvites = @1, Enemies = @2 WHERE ID = @3", String.Join(",", faction2.Allies), String.Join(",", faction2.AllyInvites), String.Join(",", faction2.Enemies), faction2.ID);

        }
        public static void BreakAlly(Faction faction1, Faction faction2)
        {
            if (faction1.ID == faction2.ID)
                return;
            faction1.Allies.Remove(faction2.ID);
            faction2.Allies.Remove(faction1.ID);
            Factions.DoQuery("UPDATE factions_Factions SET Allies = @0 WHERE ID = @1", String.Join(",", faction1.Allies), faction1.ID);
            Factions.DoQuery("UPDATE factions_Factions SET Allies = @0 WHERE ID = @1", String.Join(",", faction2.Allies), faction2.ID);
        }

    }
}
