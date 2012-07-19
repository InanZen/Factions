using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Data;
using System.ComponentModel;
using Terraria;
using Hooks;
using TShockAPI;
using TShockAPI.DB;
using Mono.Data.Sqlite;
using MySql.Data.MySqlClient;
using ChatAssistant;
using System.Threading;

namespace Factions
{
    [APIVersion(1, 12)]
    public class Factions : TerrariaPlugin
    {
        private class Player
        {
            public int ID;
            public int UserID;
            public Faction Faction;
            public TSPlayer TSplayer;
            public int Power;
            public bool tempOutline = false;
            public Chat.Menu Menu = null;
            public bool changingMoney = false;
            public Thread UpdateThread = null;
            public byte LastState = 0;
            public byte IdleCount = 0;
            public Player(int id, int power, Faction faction) : this(id, power, faction, -1) { }
            public Player(int id, int power, Faction faction, int userID)
            {
                this.ID = id;
                this.UserID = userID;
                if (id >= 0 && id < TShock.Players.Length)
                {
                    this.TSplayer = TShock.Players[id];
                    if (this.TSplayer != null)
                        this.UserID = this.TSplayer.UserID;
                }
                this.Faction = faction;
                this.Power = power;
            }
            public bool ChangePower(int change)
            {
                if (change == 0)
                    return true;
                int newpower = this.Power + change;
                if (newpower < -100 || newpower > 100)
                    return false;
                this.Power = newpower;

                db.Query("UPDATE factions_Players SET Power = @0 WHERE UserID = @1 AND WorldID = @2", newpower, this.UserID, Main.worldID);
                if (this.Faction != null)
                    this.Faction.RefreshPower();
                this.TSplayer.SendData(PacketTypes.Status, String.Format("Power: {0}\n", this.Power), -1);
                return true;
            }
            public void CloseMenu()
            {
                this.ClearOutline();
                if (this.Menu != null)
                    this.Menu.Close();
            }
            public void ClearOutline()
            {
                if (this.tempOutline)
                {
                    this.tempOutline = false;
                    this.TSplayer.SendTileSquare(this.TSplayer.TileX, this.TSplayer.TileY, 100);
                }
            }
            public void StartUpdating()
            {
                try
                {
                    if (this.UpdateThread == null || !this.UpdateThread.IsAlive)
                    {
                        var updater = new Updater(this.ID);
                        this.UpdateThread = new Thread(updater.UpdatePower);
                        this.UpdateThread.Start();
                        this.TSplayer.SendData(PacketTypes.Status, String.Format("Power: {0}\n", this.Power), -1);
                    }
                }
                catch (Exception ex) { Log.ConsoleError(ex.ToString()); }
            }
            public void StopUpdating()
            {
                try
                {
                    if (this.UpdateThread != null)
                        this.UpdateThread.Abort();
                }
                catch (Exception ex) { Log.ConsoleError(ex.ToString()); }
            }
            private class Updater
            {
                int who;                
                byte PowerCount = 0;
                public Updater(int who)
                {
                    this.who = who;
                }
                public void UpdatePower()
                {
                    while (Thread.CurrentThread.IsAlive)
                    {                        
                        var player = PlayerList[who];
                        if (player != null)
                        {
                            try
                            {
                                if (player.IdleCount < 3)
                                {
                                    player.IdleCount++;
                                    if (this.PowerCount == 10)                                    
                                        player.ChangePower(3);                                    
                                    this.PowerCount++;
                                    if (this.PowerCount > 10)
                                        this.PowerCount = 1;
                                }
                            }
                            catch (Exception ex) { Log.ConsoleError(ex.ToString()); }
                            Thread.Sleep(60000);
                        }
                        else
                            Thread.CurrentThread.Abort();
                    }
                }
            }

        }
        private class Region
        {
            public int ID;
            public int X;
            public int Y;
            public int Width;
            public int Height;
            public byte Flags;
            public int Owner;
            public int Faction;
            public Region(int id, int x, int y) : this(id, x, y, 0, 0, 0) { }
            public Region(int id, int x, int y, int owner) : this(id, x, y, owner, 0, 0) { }
            public Region(int id, int x, int y, int owner, int faction) : this(id, x, y, owner, faction, 0) { }
            public Region(int id, int x, int y, int owner, int faction, byte flags)
            {
                this.ID = id;
                this.X = x;
                this.Y = y;
                this.Width = 20;
                this.Height = 20;
                this.Flags = flags;
                this.Owner = owner;
                this.Faction = faction;
            }
            public bool InArea(Point point)
            {
                return InArea(point.X, point.Y);
            }
            public bool InArea(int x, int y)
            {
                if (x >= this.X && x < (this.X + this.Width) && y >= this.Y && y < (this.Y + this.Height))
                    return true;
                return false;
            }


        }
        private class Faction
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
                set { this.name = value; db.Query("UPDATE factions_Factions SET Name = @0 WHERE ID = @1", this.name, this.ID); }
            }
            private string desc;
            public string Desc
            {
                get { return this.desc; }
                set { this.desc = value; db.Query("UPDATE factions_Factions SET Description = @0 WHERE ID = @1", this.desc, this.ID); }
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
                        using (QueryResult reader = db.QueryReader("SELECT Name FROM factions_Players WHERE UserID = @0 AND WorldID = @1", userid, Main.worldID))
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
                        using (QueryResult reader = db.QueryReader("SELECT Name FROM factions_Players WHERE UserID = @0 AND WorldID = @1", userid, Main.worldID))
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
                    db.Query("UPDATE factions_Factions SET Flags = @0 WHERE ID = @1", this.Flags, this.ID);
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
                        db.Query("UPDATE factions_Factions SET Flags = @0 WHERE ID = @1", this.Flags, this.ID);
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
                        db.Query("UPDATE factions_Factions SET Flags = @0 WHERE ID = @1", this.Flags, this.ID);
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
                        db.Query("UPDATE factions_Factions SET Invites = @0 WHERE ID = @1", String.Join(",", this.Invites), this.ID);
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
                foreach (Player ply in PlayerList)
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
                    db.Query("UPDATE factions_Factions SET Power = @0, Members = @1 WHERE ID = @2", this.Power, String.Join(",", this.Members), this.ID);
                    db.Query("UPDATE factions_Players SET Faction = @0 WHERE UserID = @1 AND WorldID = @2", this.ID, player.UserID, Main.worldID);
                    
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
                        var player = GetPlayerByUserID(userid);
                        if (player != null)                        
                            player.Faction = null;                        
                        db.Query("UPDATE factions_Players SET Faction = @0 WHERE UserID = @1 AND WorldID = @2", 0, userid, Main.worldID);
                        if (this.Members.Count == 0) // delete faction                        
                            this.DeleteFaction();
                        else // recalculate power
                        {
                            if (this.Admins.Count == 0)
                                this.Admins = this.Members.ToList();
                            db.Query("UPDATE factions_Factions SET Members = @0, Admins = @1 WHERE ID = @2", String.Join(",", this.Members), String.Join(",", this.Admins), this.ID);
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
                        var player = GetPlayerByUserID(userid);
                        if (player != null)
                            player.Faction = null;
                        db.Query("UPDATE factions_Players SET Faction = @0 WHERE UserID = @1 AND WorldID = @2", 0, userid, Main.worldID);
                    }
                    for (int i = this.Regions.Count - 1; i >= 0; i--)
                    {
                        if (Factions.Regions.Remove(this.Regions[i]))
                        {
                            db.Query("DELETE FROM factions_Regions WHERE ID = @0", this.Regions[i].ID);
                            this.Regions[i].Faction = 0;
                        }
                    }
                    if (FactionList.Remove(this))                    
                        db.Query("DELETE FROM factions_Factions WHERE ID = @0", this.ID);        
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
                            using (QueryResult reader = db.QueryReader("SELECT Power FROM factions_Players WHERE UserID = @0 AND WorldID = @1", userid, Main.worldID))
                            {
                                if (reader.Read())
                                    newPower += reader.Get<int>("Power");
                            }
                        }
                        this.Power = newPower;
                        db.Query("UPDATE factions_Factions SET Power = @0 WHERE ID = @1", newPower, this.ID);
                    
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
                        var player = GetPlayerByUserID(userid);
                        if (player != null)
                            memberList.Add(player);
                        else
                        {
                            using (QueryResult reader = db.QueryReader("SELECT Power FROM factions_Players WHERE UserID = @0 AND WorldID = @1", userid, Main.worldID))
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
                        db.Query("UPDATE factions_Players SET Power = @0 WHERE UserID = @1 AND WorldID = @2", player.Power, player.UserID, Main.worldID);
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
                db.Query("UPDATE factions_Factions SET Allies = @0, AllyInvites = @1, Enemies = @2 WHERE ID = @3", String.Join(",", faction1.Allies), String.Join(",", faction1.AllyInvites), String.Join(",", faction1.Enemies), faction1.ID);
                db.Query("UPDATE factions_Factions SET Allies = @0, AllyInvites = @1, Enemies = @2 WHERE ID = @3", String.Join(",", faction2.Allies), String.Join(",", faction2.AllyInvites), String.Join(",", faction2.Enemies), faction2.ID); 

            }
            public static void BreakAlly(Faction faction1, Faction faction2)
            {
                if (faction1.ID == faction2.ID)
                    return;
                faction1.Allies.Remove(faction2.ID);
                faction2.Allies.Remove(faction1.ID);
                db.Query("UPDATE factions_Factions SET Allies = @0 WHERE ID = @1", String.Join(",", faction1.Allies), faction1.ID);
                db.Query("UPDATE factions_Factions SET Allies = @0 WHERE ID = @1", String.Join(",", faction2.Allies), faction2.ID); 
            }
        }


        private static string savepath = Path.Combine(TShock.SavePath, "Factions/");
        private static IDbConnection db;

        private static List<Faction> FactionList = new List<Faction>();
        private static List<Region> Regions = new List<Region>();
        private static Player[] PlayerList = new Player[256];
        private static Thread UpdatePowerThread = new Thread(UpdatePower);
        private static void UpdatePower()
        {
            while (UpdatePowerThread.IsAlive)
            {
                if (Main.worldID > 0)
                {
                    try
                    {
                        db.Query("UPDATE factions_Players SET OfflineCount = OfflineCount + 10 WHERE Online = 0 AND Power > -100 AND WorldID = @0", Main.worldID);
                        db.Query("UPDATE factions_Players SET Power = Power - 1, OfflineCount = 0 WHERE OfflineCount >= @1 AND Power > -100 AND WorldID = @0", Main.worldID, 120);

                        foreach (Faction fact in FactionList)
                        {
                            fact.RefreshPower();
                        }
                    }
                    catch (Exception ex) { Log.ConsoleError(ex.ToString()); }
                }
                Thread.Sleep(600000);
            }
        }

        public override string Name
        {
            get { return "Factions"; }
        }
        public override string Author
        {
            get { return "by InanZen"; }
        }
        public override string Description
        {
            get { return "factions"; }
        }
        public override Version Version
        {
            get { return new Version("0.6"); }
        }
        public Factions(Main game)
            : base(game)
        {
            Order = 1;
        }
        public override void Initialize()
        {
            NetHooks.GetData += GetData;
            NetHooks.SendData += SendData;
            NetHooks.GreetPlayer += OnJoin;    
            ServerHooks.Leave += OnLeave;
            ServerHooks.Chat += OnChat;
            GameHooks.Update += OnUpdate;
            Commands.ChatCommands.Add(new Command("factions.command", CommandMethod, "factions", "faction", "fact", "f"));
            if (!Directory.Exists(savepath))
                Directory.CreateDirectory(savepath);
            SetupDb();
            UpdatePowerThread.Start();
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try
                {
                    NetHooks.GetData -= GetData;
                    NetHooks.SendData -= SendData;
                    NetHooks.GreetPlayer -= OnJoin;
                    ServerHooks.Leave -= OnLeave;
                    ServerHooks.Chat -= OnChat;
                    GameHooks.Update -= OnUpdate;

                }
                catch (Exception ex) { Log.ConsoleError(ex.ToString()); }
                try
                {
                    UpdatePowerThread.Abort();
                    db.Query("UPDATE factions_Players SET Online = 0 WHERE Online = 1 AND WorldID = @0", Main.worldID);
                    for (int i = 0; i < PlayerList.Length; i++)
                    {
                        if (PlayerList[i] != null && PlayerList[i].UserID != -1)                        
                            PlayerList[i].StopUpdating();   
                    }
                }
                catch (Exception ex) { Log.ConsoleError(ex.ToString()); }
            }
            base.Dispose(disposing);
        }
        public void OnChat(messageBuffer buffer, int who, string text, HandledEventArgs args)
        {
            var player = PlayerList[who];
            if (player != null)
            {
                if (text.StartsWith("/login") && player.TSplayer.UserID != player.UserID)
                {
                   // Console.WriteLine("player.userID: {0}, TSplayer.userID: {1}", player.UserID, player.TSplayer.UserID);
                    if (player.UserID != -1)
                    {
                        int factid = 0;
                        if (player.Faction != null)
                            factid = player.Faction.ID;
                        db.Query("UPDATE factions_Players SET Power = @0, Faction = @1 WHERE UserID = @2 AND WorldID = @3", player.Power, factid, player.UserID, Main.worldID);
                        player.StopUpdating();
                    }
                    using (QueryResult reader = db.QueryReader("SELECT * FROM factions_Players WHERE UserID = @0 AND WorldID = @1", player.TSplayer.UserID, Main.worldID))
                    {
                        if (reader.Read())
                        {
                            var faction = GetFactionByID(reader.Get<int>("Faction"));
                            PlayerList[who] = new Player(who, reader.Get<int>("Power"), faction);
                            reader.Dispose();
                            db.Query("UPDATE factions_Players SET Online = 1 WHERE UserID = @0 AND WorldID = @1", player.TSplayer.UserID, Main.worldID);
                        }
                        else
                        {
                            reader.Dispose();
                            db.Query("INSERT INTO factions_Players (UserID, Name, Power, Online, OfflineCount, Faction, WorldID) VALUES (@0, @1, 10, 1, 0, 0, @2)", player.TSplayer.UserID, player.TSplayer.UserAccountName, Main.worldID);
                            PlayerList[who] = new Player(who, 10, null);
                        }
                    }
                    PlayerList[who].StartUpdating();
                }
            }            
        }
  /*      private static void CheckPlayer(Player player)
        {
            if (text.StartsWith("/login") && player.TSplayer.UserID != player.UserID)
            {
                Console.WriteLine("player.userID: {0}, TSplayer.userID: {1}", player.UserID, player.TSplayer.UserID);
                if (player.UserID != -1)
                {
                    int factid = 0;
                    if (player.Faction != null)
                        factid = player.Faction.ID;
                    db.Query("UPDATE factions_Players SET Power = @0, Faction = @1 WHERE UserID = @2 AND WorldID = @3", player.Power, factid, player.UserID, Main.worldID);

                }
                using (QueryResult reader = db.QueryReader("SELECT * FROM factions_Players WHERE UserID = @0 AND WorldID = @1", player.TSplayer.UserID, Main.worldID))
                {
                    if (reader.Read())
                    {
                        var faction = GetFactionByID(reader.Get<int>("Faction"));
                        PlayerList[who] = new Player(who, reader.Get<int>("Power"), faction);
                        db.Query("UPDATE factions_Players SET Online = 1 WHERE UserID = @0 AND WorldID = @1", player.TSplayer.UserID, Main.worldID);
                    }
                    else
                    {
                        db.Query("INSERT INTO factions_Players (UserID, Name, Power, Online, OfflineCount, Faction, WorldID) VALUES (@0, @1, 10, 1, 0, 0, @2)", player.TSplayer.UserID, player.TSplayer.UserAccountName, Main.worldID);
                        PlayerList[who] = new Player(who, 10, null);
                    }
                }
                Console.WriteLine("playerlist[who].userid: {0}", PlayerList[who].UserID);
                PlayerList[who].StartUpdating();
            }
        }*/
        public void OnJoin(int who, HandledEventArgs e)
        {
            var player = TShock.Players[who];
            if (player != null)
            {
                if (player.UserID != -1)
                {
                    try
                    {
                        QueryResult reader = db.QueryReader("SELECT * FROM factions_Players WHERE UserID = @0 AND WorldID = @1", player.UserID, Main.worldID);
                        if (reader.Read())
                        {                            
                            var faction = GetFactionByID(reader.Get<int>("Faction"));
                            PlayerList[who] = new Player(who, reader.Get<int>("Power"), faction);
                            if (faction != null)
                                player.SetTeam(faction.Team);
                            reader.Dispose();
                            db.Query("UPDATE factions_Players SET Online = 1 WHERE UserID = @0 AND WorldID = @1", player.UserID, Main.worldID);
                        }                            
                        else
                        {
                            reader.Dispose();
                            db.Query("INSERT INTO factions_Players (UserID, Name, Power, Online, OfflineCount, Faction, WorldID) VALUES (@0, @1, 10, 1, 0, 0, @2)", player.UserID, player.UserAccountName, Main.worldID);
                            PlayerList[who] = new Player(who, 10, null);
                        }
                        PlayerList[who].StartUpdating();
                    }
                    catch (Exception ex) { Log.ConsoleError(ex.ToString()); }
                }
                else                
                    PlayerList[who] = new Player(who, 0, null);                
            }
        }
        public void OnLeave(int who)
        {
            var player = PlayerList[who];
            if (player != null)
            {
                int factid = 0;
                if (player.Faction != null)
                    factid = player.Faction.ID;
                db.Query("UPDATE factions_Players SET Power = @0, Faction = @1, Online = 0 WHERE UserID = @2 AND WorldID = @3", player.Power, factid, player.UserID, Main.worldID);
                player.StopUpdating();
            }
            PlayerList[who] = null;
        }
        private static void DisplayRegionInfo(Player player)
        {
            OutlineRegion(player);
            var region = GetRegionFromLocation(player.TSplayer.TileX, player.TSplayer.TileY);
            List<MenuItem> menuData = new List<MenuItem>();
            menuData.Add(new MenuItem(String.Format("Plot ({0}, {1}) - ({2}, {3})", region.X, region.Y, region.X + region.Width - 1, region.Y + region.Height - 1), 0, false, Color.Gray));
            if (region.ID == -1)
            {
                menuData.Add(new MenuItem("Plot has no owner", 0, false, Color.LightGray));
                int price = GetPlotPrice(player, new Point(player.TSplayer.TileX, player.TSplayer.TileY));
                if (price == -1)
                    menuData.Add(new MenuItem("This land is unclaimable", 0, false, Color.Gray));
                else if (player.Faction == null)
                {
                    if (price == -2)
                        menuData.Add(new MenuItem("You cannot claim any more plots at the moment", 0, false, Color.Gray));
                    else
                    {
                        menuData.Add(new MenuItem(string.Format("Price for you: {0} power", price), 0, false, Color.LightGray));
                        menuData.Add(new MenuItem(String.Format("You have: {0} power", player.Power), 0, false, (player.Power - price >= 0) ? Color.DarkGreen : Color.DarkRed));
                        if (player.Power - price >= 0)
                            menuData.Add(new MenuItem("[ Claim it for yourself ]", 20, Color.BurlyWood));
                    }
                }
                else
                {
                    if (price == -2)
                        menuData.Add(new MenuItem("Your faction cannot claim any more plots at the moment", 0, false, Color.Gray));
                    else
                    {
                        menuData.Add(new MenuItem(string.Format("Price for your faction: {0} power", price), 0, false, Color.LightGray));
                        menuData.Add(new MenuItem(String.Format("Your faction has: {0} power", player.Faction.Power), 0, false, (player.Faction.Power - price >= 0) ? Color.DarkGreen : Color.DarkRed));
                        if (player.Faction.Power - price >= 0 && player.Faction.IsAdmin(player.UserID))
                            menuData.Add(new MenuItem("[ Claim it for your faction ]", 20, Color.BurlyWood));
                    }
                }
            }
            else
            {
                if (region.Owner != 0)
                {
                    if (region.Owner == player.UserID)
                    {
                        menuData.Add(new MenuItem("This is your land", 0, false, Color.LightGray));
                        menuData.Add(new MenuItem("[ Unclaim ]", 22, Color.DarkRed));
                    }
                    else
                    {
                            using (QueryResult reader = db.QueryReader("SELECT * FROM factions_Players WHERE UserID = @0", region.Owner))
                            {
                                if (reader.Read())
                                    menuData.Add(new MenuItem(String.Format("This land belongs to: {0}", reader.Get<String>("Name")), 0, false, Color.LightGray));
                            }
                        
                    }
                }
                else if (region.Faction != 0)
                {
                    var faction = GetFactionByID(region.Faction);
                    if (player.Faction != null && player.Faction.Equals(faction))
                    {
                        menuData.Add(new MenuItem("This land belongs to your Faction", 0, false, Color.LightGray));
                        if (faction.IsAdmin(player.UserID))
                            menuData.Add(new MenuItem("[ Unclaim ]", 22, Color.DarkRed));                        
                    }
                    else
                    {
                        var menuItem = new MenuItem("This land belongs to Faction: [ @0 ]", 310, Color.LightGray);
                        menuItem.Input = faction.Name;
                        menuData.Add(menuItem);
                    }

                }
                else
                    menuData.Add(new MenuItem("Abnormal", -1, false, Color.LightGray));
            }
            if (player.Menu == null)
                player.Menu = Chat.CreateMenu(player.ID, "Plot manager", menuData, new Chat.MenuAction(OnMenu));
            else
            {
                player.Menu.title = "Plot manager";
                player.Menu.index = 0;
                menuData.Add(new MenuItem("[ <- Back ]", 1, Color.BurlyWood));
                player.Menu.contents = menuData;
                player.Menu.DisplayMenu();
            }
        }
        private static void OutlineRegion(Player player)
        {
            int x = player.TSplayer.TileX;
            int y = player.TSplayer.TileY;            
            int width = 20;
            int height = 20;
            int x1 = (int)(x / width) * width;
            int y1 = (int)(y / height) * height;
            for (int j = y1; j < y1 + height; j++)
            {
                for (int i = x1; i < x1 + width; i++)
                {
                    if (i == x1 || i == x1 + width - 1 || j == y1 || j == y1 + height - 1)
                    {
                        byte tempType = Main.tile[i, j].type;
                        bool tempActive = Main.tile[i, j].active;
                        Main.tile[i, j].type = 70;
                        Main.tile[i, j].active = true;
                        player.TSplayer.SendTileSquare(i, j, 1);
                        Main.tile[i, j].type = tempType;
                        Main.tile[i, j].active = tempActive;
                    }
                }
            }
            player.tempOutline = true;
        }
        public static void CommandMethod(CommandArgs args)
        {
            var player = PlayerList[args.Player.Index];
            if (player == null)
                return;
            if (args.Parameters.Count > 0)
            {
                if (args.Parameters[0] == "invite")
                {
                    if (player.Faction == null || !player.Faction.IsAdmin(player.UserID))
                    {
                        player.TSplayer.SendMessage("Only faction admins can invite players to the faction", Color.LightSalmon);
                        return;
                    }
                    if (args.Parameters.Count < 2)
                    {
                        player.TSplayer.SendMessage("Syntax: /faction invite PlayerName", Color.LightSalmon);
                        return;
                    }
                    var targetName = String.Join(" ", args.Parameters.GetRange(1, args.Parameters.Count-1));
                    var target = GetPlayerByName(targetName);
                    if (target == null)                    
                        player.TSplayer.SendMessage(String.Format("Could not find player '{0}'", targetName), Color.LightSalmon);
                    else if (target.UserID == -1)
                        player.TSplayer.SendMessage(String.Format("Player '{0}' is not registered or logged in", targetName), Color.LightSalmon);
                    else if (player.Faction.Invites.Contains(target.UserID))
                        player.TSplayer.SendMessage(String.Format("Player '{0}' is already invited to join your faction", targetName), Color.LightSalmon);
                    else if (player.Faction.Members.Contains(target.UserID))
                        player.TSplayer.SendMessage(String.Format("Player '{0}' is already in your faction", targetName), Color.LightSalmon);
                    else if (player.Faction.InvitePlayer(target))
                        player.TSplayer.SendMessage(String.Format("Player '{0}' has been invited to join your faction", targetName), Color.DarkGreen);
                    else
                        player.TSplayer.SendMessage(String.Format("Could not invite '{0}' to join your faction", targetName), Color.LightSalmon);
                }
            }
            else
            {

                List<MenuItem> menuData = new List<MenuItem>();
                menuData.Add(new MenuItem("[ Plot information & management ]", 2, Color.LightGray));
                menuData.Add(new MenuItem("[ Faction information & management ]", 3, Color.LightGray));
                menuData.Add(new MenuItem("[ Player information & management ]", 4, Color.LightGray));
                menuData.Add(new MenuItem("[ Exit ]", -1, Color.Gray));
                Chat.CreateMenu(player.ID, "Factions", menuData, new Chat.MenuAction(OnMenu));

            }
        }

        public static void OnMenu(Object menu, MenuEventArgs args)
        {
            var player = PlayerList[args.PlayerID];
            if (player != null)
            {
                if (args.Status == 0)
                {
                    player.ClearOutline();
                    player.Menu = null;
                    return;
                }
                else if (args.Status == 1 && args.Selected >= 0 && args.Selected < args.Data.Count)
                {
                    player.Menu = (Chat.Menu)menu;                        
                    int value = args.Data[args.Selected].Value;
                    switch (value)
                    {
                        case -1:
                            {
                                player.CloseMenu();
                                break;
                            }
                        case 1: // Main Menu
                            {
                                player.Menu.title = "Factions";
                                player.Menu.contents.Clear();
                                player.Menu.index = 0;
                                player.Menu.contents.Add(new MenuItem("[ Plot information & management ]", 2, Color.LightGray));
                                player.Menu.contents.Add(new MenuItem("[ Faction information & management ]", 3, Color.LightGray));
                                player.Menu.contents.Add(new MenuItem("[ Player information & management ]", 4, Color.LightGray));
                              /*  if (player.TSplayer.Group.Name == "superadmin" || player.TSplayer.Group.HasPermission("factions.admin"))
                                    player.Menu.contents.Add(new MenuItem("Factions Administration", 9, Color.LightGray));*/
                                player.Menu.contents.Add(new MenuItem("[ Exit ]", -1, Color.Gray));
                                args.Handled = true;
                                player.Menu.DisplayMenu();
                                break;
                            }
                        case 2: // plot manager
                            {
                                DisplayRegionInfo(player);
                                args.Handled = true;
                                break;
                            }
                        case 20: // buying plot
                            {
                                int price = GetPlotPrice(player, new Point(player.TSplayer.TileX, player.TSplayer.TileY));
                                if (price < 0)
                                    return;
                                player.Menu.contents.Clear();
                                OutlineRegion(player);
                                player.Menu.index = 0;
                                player.Menu.contents.Add(new MenuItem(String.Format("Buy this plot for {0} power?", price), 0, false, Color.LightGray));
                                player.Menu.contents.Add(new MenuItem("[ Yes ]", 21, Color.DarkGreen));
                                player.Menu.contents.Add(new MenuItem("[ No ]", 2, Color.DarkRed));
                                args.Handled = true;
                                player.Menu.DisplayMenu();
                                break;
                            }
                        case 21: // claim plot 
                            {
                                int price = GetPlotPrice(player, new Point(player.TSplayer.TileX, player.TSplayer.TileY));
                                if (price < 0)
                                    return;
                                if ((player.Faction == null && player.ChangePower(-1 * price)) || (player.Faction != null && player.Faction.IsAdmin(player.UserID) && player.Faction.RemovePower(price)))
                                {
                                    AddRegion(player);
                                    DisplayRegionInfo(player);
                                    args.Handled = true;
                                }
                                break;
                            }
                        case 22: // unclaim plot
                            {
                                player.Menu.contents.Clear();
                                player.Menu.index = 0;
                                player.Menu.contents.Add(new MenuItem("Are you sure you want to unclaim this land?", 0, false, Color.LightGray));
                                player.Menu.contents.Add(new MenuItem("[ Yes ]", 23, Color.DarkGreen));
                                player.Menu.contents.Add(new MenuItem("[ No ]", 2, Color.DarkRed));
                                args.Handled = true;
                                player.Menu.DisplayMenu();
                                break;
                            }
                        case 23: // unclaim plot confirmation
                            {
                                var region = GetRegionFromLocation(player.TSplayer.TileX, player.TSplayer.TileY);
                                if (region.ID != -1)
                                {
                                    if ((region.Owner != 0 && player.UserID == region.Owner) || (region.Faction != 0 && player.Faction != null && player.Faction.ID == region.Faction && player.Faction.IsAdmin(player.UserID)))
                                    {
                                        if (UnclaimRegion(region))
                                        {
                                            DisplayRegionInfo(player);
                                            args.Handled = true;
                                        }
                                        else
                                            player.TSplayer.SendMessage("Error: Could not unclaim region", Color.Red);                                        
                                    }
                                }
                                break;
                            }

                        case 3: // faction management
                            {
                                player.Menu.title = "Faction Manager";
                                player.Menu.contents.Clear();
                                player.Menu.index = 0;
                                player.Menu.contents.Add(new MenuItem("[ Faction List ]", 31, Color.LightGray));
                                if (player.Faction != null)
                                    player.Menu.contents.Add(new MenuItem("[ My Faction ]", 33, Color.LightGray));
                                else
                                    player.Menu.contents.Add(new MenuItem("[ New Faction ]", 32, Color.LightGray));
                                
                                player.Menu.contents.Add(new MenuItem("[ <- Back ]", 1, Color.BurlyWood));
                                args.Handled = true;
                                player.Menu.DisplayMenu();
                                break;
                            }
                        case 4: //player management
                            {
                                player.changingMoney = false;
                                player.Menu.title = "Player information";
                                player.Menu.contents.Clear();
                                player.Menu.index = 0;
                                player.Menu.contents.Add(new MenuItem(String.Format("Power: {0}", player.Power), 0, false, Color.LightGray));
                                player.Menu.contents.Add(new MenuItem(String.Format("[ Convert gold to Power ]", player.Power), 41, Color.LightGray));
                                if (player.Faction != null)
                                    player.Menu.contents.Add(new MenuItem(String.Format("[ My faction: {0} ]", player.Faction.Name), 33, Color.LightGray));
                                player.Menu.contents.Add(new MenuItem("[ <- Back ]", 1, Color.BurlyWood));
                               
                                args.Handled = true;
                                player.Menu.DisplayMenu();
                                break;
                            }
                        case 41: // convert money to power
                            {
                                player.changingMoney = true;
                                player.Menu.title = "Player information";
                                player.Menu.contents.Clear();
                                player.Menu.index = 0;
                                player.Menu.contents.Add(new MenuItem("Drop gold from your inventory", 0, false, Color.LightGray));
                                player.Menu.contents.Add(new MenuItem("to exchange it for Power.", 0, false, Color.LightGray));
                                player.Menu.contents.Add(new MenuItem("1 Gold = 2 Power", 0, false, Color.Goldenrod));
                                player.Menu.contents.Add(new MenuItem("Click [Back] when you're done", 0, false, Color.LightGray));
                                player.Menu.contents.Add(new MenuItem("[ <- Back ]", 4, Color.BurlyWood));
                                args.Handled = true;
                                player.Menu.DisplayMenu();
                                break;
                            }
                        /*case 9: //admin
                            {
                                break;
                            */

                        case 31: // List Factions
                            {
                                player.Menu.title = "Faction List";
                                var factname = player.Menu.GetItemByValue(3101);
                                player.Menu.contents.Clear();
                                player.Menu.index = 0;
                                foreach (Faction fact in GetTopFactionsByPower())
                                {
                                    if (factname != null && factname.Text == fact.Name)
                                        player.Menu.index = player.Menu.contents.Count;
                                    var menuI = new MenuItem("- @0", 310, Color.Gray);
                                    menuI.Input = fact.Name;
                                    player.Menu.contents.Add(menuI);                                    
                                    player.Menu.contents.Add(new MenuItem(String.Format("     Power: {0}, Members: {1}", fact.Power, fact.Members.Count), 0, false, Color.LightGray));
                                }
                                player.Menu.contents.Add(new MenuItem("[ <- Back ]", 3, Color.BurlyWood));
                                args.Handled = true;
                                player.Menu.DisplayMenu();
                                break;
                            }
                        case 310: // Faction Information
                            {
                                var faction = GetFactionByName(args.Data[args.Selected].Input);
                                if (faction != null)
                                {
                                    player.Menu.title = "Faction information";
                                    player.Menu.contents.Clear();
                                    player.Menu.index = 0;
                                    player.Menu.contents.Add(new MenuItem(faction.Name, 3101, false, Color.YellowGreen));
                                    player.Menu.contents.Add(new MenuItem(String.Format(" \"{0}\"", faction.Desc), 0, false, Color.Gray));
                                    player.Menu.contents.Add(new MenuItem("", 0, false, Color.White));
                                    player.Menu.contents.Add(new MenuItem(String.Format("Join status: {0}", faction.Private ? "Invite Only" : "Open to all"), 0, false, Color.LightGray));
                                    player.Menu.contents.Add(new MenuItem(String.Format("PVP status: {0}", faction.Hostile ? "Hostile" : "Peaceful"), 0, false, Color.LightGray));
                                    player.Menu.contents.Add(new MenuItem(String.Format("Team color: {0}", faction.TeamColor), 0, false, Color.LightGray));
                                    player.Menu.contents.Add(new MenuItem(String.Format("Admins ({0}):", faction.Admins.Count), 0, false, Color.LightGray));
                                    player.Menu.contents.Add(new MenuItem(String.Join(", ", faction.AdminNames()), 0, false, Color.DarkGreen));
                                    player.Menu.contents.Add(new MenuItem(String.Format("Members ({0}):", faction.Members.Except(faction.Admins).ToList().Count), 0, false, Color.LightGray));
                                    player.Menu.contents.Add(new MenuItem(String.Join(", ", faction.MemberNames(true)), 0, false, Color.DarkBlue));
                                    player.Menu.contents.Add(new MenuItem(String.Format("Power: {0}:", faction.Power), 0, false, Color.LightGray));
                                    if (faction.Allies.Count > 0)
                                        player.Menu.contents.Add(new MenuItem(String.Format("Allies: {0}:", faction.GetAlliesString()), 0, false, Color.LightGray));
                                    if (faction.Enemies.Count > 0)
                                        player.Menu.contents.Add(new MenuItem(String.Format("Enemies: {0}:", faction.GetEnemiesString()), 0, false, Color.LightGray));
                                    player.Menu.contents.Add(new MenuItem("", 0, false, Color.White));
                                    if (player.Faction == null)
                                    {
                                        if (faction.Private)
                                        {
                                            if (faction.Invites.Contains(player.UserID))
                                                player.Menu.contents.Add(new MenuItem("[ Join ] (You have been invited to join)", 3100, Color.DarkGreen));
                                            else
                                                player.Menu.contents.Add(new MenuItem("[ Join ] (To join request invitation from faction Admins)", 0, false, Color.Gray));
                                        }
                                        else
                                            player.Menu.contents.Add(new MenuItem("[ Join ]", 3100, Color.DarkGreen));
                                    }
                                    else if (player.Faction.ID != faction.ID && player.Faction.IsAdmin(player.UserID))
                                    {
                                        if (player.Faction.Allies.Contains(faction.ID))
                                        {
                                            player.Menu.contents.Add(new MenuItem("[ Revoke Ally status ]", 3103, Color.DarkRed));
                                        }
                                        else if (faction.AllyInvites.Contains(player.Faction.ID))
                                        {
                                            player.Menu.contents.Add(new MenuItem("Avaiting Ally confirmation", 0, false, Color.LightGray));
                                        }
                                        else if (player.Faction.AllyInvites.Contains(faction.ID))
                                        {
                                            player.Menu.contents.Add(new MenuItem("[ Confirm Alliance ]", 3102, Color.DarkGreen));
                                            player.Menu.contents.Add(new MenuItem("[ Decline Alliance ]", 3103, Color.DarkRed));
                                        }
                                        else if (player.Faction.Enemies.Contains(faction.ID))
                                        {
                                            player.Menu.contents.Add(new MenuItem("[ Revoke Enemy status ]", 3105, Color.DarkRed));
                                        }
                                        else
                                        {
                                            player.Menu.contents.Add(new MenuItem("[ Request Alliance ]", 3102, Color.DarkGreen));
                                            player.Menu.contents.Add(new MenuItem("[ Declare War ]", 3104, Color.DarkRed));
                                        }
                                    }
                                    if (player.Menu.GetItemByValue(1) != null)
                                        player.Menu.contents.Add(new MenuItem("[ <- Back ]", 1, Color.BurlyWood));
                                    else
                                        player.Menu.contents.Add(new MenuItem("[ <- Back ]", 31, Color.BurlyWood));
                                    args.Handled = true;
                                    player.Menu.DisplayMenu();
                                }
                                break;
                            }
                        case 3100: // Join Faction
                            {
                                var faction = GetFactionByName(player.Menu.GetItemByValue(3101).Text);
                                if (faction != null)
                                {

                                    if ((faction.Private && faction.Invites.Contains(player.UserID)) || !faction.Private)
                                    {
                                        player.Menu.title = "Faction information";
                                        player.Menu.contents.Clear();
                                        player.Menu.index = 0;
                                        if (faction.AddMember(player))
                                        {
                                            player.Menu.contents.Add(new MenuItem(String.Format("Success, you have joined the {0}", faction.Name), 0, false, Color.DarkGreen));
                                            player.Menu.contents.Add(new MenuItem("[ My faction ]", 33, Color.BurlyWood));
                                        }
                                        else
                                            player.Menu.contents.Add(new MenuItem(String.Format("Error, could not join faction: {0}", faction.Name), 0, false, Color.DarkRed));
                                        
                                        player.Menu.contents.Add(new MenuItem("[ <- Back ]", 3, Color.BurlyWood));
                                        args.Handled = true;
                                        player.Menu.DisplayMenu();
                                    }                                    
                                }                                
                                break;
                            }
                        case 3102: // request alliance
                            {
                                try
                                {
                                    var faction = GetFactionByName(player.Menu.GetItemByValue(3101).Text);
                                    if (faction != null)
                                    {
                                        player.Menu.contents.Clear();
                                        player.Menu.index = 0;

                                        if (player.Faction.AllyInvites.Contains(faction.ID))
                                        {
                                            Faction.AllyFactions(faction, player.Faction);
                                            string allyString = String.Format("{0} and {1} are now Allies", player.Faction.Name, faction.Name);
                                            player.Menu.contents.Add(new MenuItem(allyString, 0, false, Color.DarkGreen));
                                            TShock.Utils.Broadcast(allyString, Color.DarkGreen);
                                        }
                                        else
                                        {
                                            faction.AllyInvites.Add(player.Faction.ID);
                                            db.Query("UPDATE factions_Factions SET AllyInvites = @0 WHERE ID = @1", String.Join(",", faction.AllyInvites), faction.ID);
                                            player.Menu.contents.Add(new MenuItem("Ally request sent", 0, false, Color.DarkGreen));
                                            faction.MessageMembers(String.Format("Faction '{0}' requests an Alliance with you", player.Faction.Name));
                                        }
                                        var backbutton = new MenuItem("[ <- Back to @0 overview ]", 310, Color.BurlyWood);
                                        backbutton.Input = faction.Name;
                                        player.Menu.contents.Add(backbutton);
                                        player.Menu.contents.Add(new MenuItem("[ Exit ]", -1, Color.Gray));
                                        args.Handled = true;
                                        player.Menu.DisplayMenu();
                                    }
                                }
                                catch (Exception ex) { Log.ConsoleError(ex.ToString()); }
                                break;
                            }
                        case 3103: // revoke alliance
                            {
                                try
                                {
                                    var faction = GetFactionByName(player.Menu.GetItemByValue(3101).Text);
                                    if (faction != null)
                                    {
                                        player.Menu.contents.Clear();
                                        player.Menu.index = 0;

                                        Faction.BreakAlly(faction, player.Faction);
                                        TShock.Utils.Broadcast(String.Format("Factions {0} and {1} have broken off the Alliance", faction.Name, player.Faction.Name), Color.LightSalmon);
                                        player.Menu.contents.Add(new MenuItem(String.Format("You've broken off the alliance with {0}", faction.Name), 0, false, Color.LightGray));
                                        var backbutton = new MenuItem("[ <- Back to @0 overview ]", 310, Color.BurlyWood);
                                        backbutton.Input = faction.Name;
                                        player.Menu.contents.Add(backbutton);
                                        player.Menu.contents.Add(new MenuItem("[ Exit ]", -1, Color.Gray));
                                        args.Handled = true;
                                        player.Menu.DisplayMenu();
                                    }
                                }
                                catch (Exception ex) { Log.ConsoleError(ex.ToString()); }
                                break;
                            }
                        case 3104: // declare war
                            {
                                try
                                {
                                    var faction = GetFactionByName(player.Menu.GetItemByValue(3101).Text);
                                    if (faction != null)
                                    {
                                        player.Menu.contents.Clear();
                                        player.Menu.index = 0;
                                        player.Faction.Enemies.Add(faction.ID);
                                        db.Query("UPDATE factions_Factions SET Enemies = @0 WHERE ID = @1", String.Join(",", player.Faction.Enemies), player.Faction.ID);
                                        TShock.Utils.Broadcast(String.Format("Faction {0} has declared war on {1}", player.Faction.Name, faction.Name), Color.LightSalmon);
                                        player.Menu.contents.Add(new MenuItem(String.Format("You are now at war with {0}", faction.Name), 0, false, Color.LightGray));
                                        var backbutton = new MenuItem("[ <- Back to @0 overview ]", 310, Color.BurlyWood);
                                        backbutton.Input = faction.Name;
                                        player.Menu.contents.Add(backbutton);
                                        player.Menu.contents.Add(new MenuItem("[ Exit ]", -1, Color.Gray));
                                        args.Handled = true;
                                        player.Menu.DisplayMenu();
                                    }
                                }
                                catch (Exception ex) { Log.ConsoleError(ex.ToString()); }
                                break;
                            }
                        case 3105: // revoke enemy
                            {
                                try
                                {
                                    var faction = GetFactionByName(player.Menu.GetItemByValue(3101).Text);
                                    if (faction != null)
                                    {
                                        player.Menu.contents.Clear();
                                        player.Menu.index = 0;
                                        player.Faction.Enemies.Remove(faction.ID);
                                        db.Query("UPDATE factions_Factions SET Enemies = @0 WHERE ID = @1", String.Join(",", player.Faction.Enemies), player.Faction.ID);
                                        TShock.Utils.Broadcast(String.Format("Faction {0} has declared neutral towards {1}", player.Faction.Name, faction.Name), Color.LightSalmon);
                                        player.Menu.contents.Add(new MenuItem(String.Format("You are now at neutral towards {0}", faction.Name), 0, false, Color.LightGray));
                                        var backbutton = new MenuItem("[ <- Back to @0 overview ]", 310, Color.BurlyWood);
                                        backbutton.Input = faction.Name;
                                        player.Menu.contents.Add(backbutton);
                                        player.Menu.contents.Add(new MenuItem("[ Exit ]", -1, Color.Gray));
                                        args.Handled = true;
                                        player.Menu.DisplayMenu();
                                    }
                                }
                                catch (Exception ex) { Log.ConsoleError(ex.ToString()); }
                                break;
                            }
                        case 32: // New Faction
                            {
                                player.Menu.title = "Faction Creator";
                                player.Menu.contents.Clear();
                                player.Menu.index = 0;
                                player.Menu.contents.Add(new MenuItem("Name: ", 321, false, true, Color.LightGray));
                                player.Menu.contents.Add(new MenuItem("Description: ", 322, false, true, Color.LightGray));
                                player.Menu.contents.Add(new MenuItem("[ Create  ]", 320, false, Color.BurlyWood));
                                player.Menu.contents.Add(new MenuItem("[ <- Back ]", 3, true, Color.BurlyWood));
                                args.Handled = true;
                                player.Menu.DisplayMenu();
                                break;
                            }
                        case 320: // Create faction
                            {
                                var nameitem = player.Menu.GetItemByValue(321);
                                var descitem = player.Menu.GetItemByValue(322);
                                if (descitem == null || nameitem == null)
                                    break;
                                MenuItem newNameItem = new MenuItem(nameitem);
                                MenuItem newDescItem = new MenuItem(descitem);
                                player.Menu.contents.Clear();
                                player.Menu.index = 0;

                                player.Menu.contents.Add(new MenuItem("Notice:", 0, false, Color.Red));
                                player.Menu.contents.Add(new MenuItem("+------------------------------------------------+", 0, false, Color.LightSalmon));
                                player.Menu.contents.Add(new MenuItem("|            If you already own any regions, they will get deleted            |", 0, false, Color.LightSalmon));
                                player.Menu.contents.Add(new MenuItem("|                  and be made available for anyone to aquire.               |", 0, false, Color.LightSalmon));
                                player.Menu.contents.Add(new MenuItem("| Number of allowed regions for Factions = number of faction members  |", 0, false, Color.LightSalmon));
                                player.Menu.contents.Add(new MenuItem("+------------------------------------------------+", 0, false, Color.LightSalmon));
                                player.Menu.contents.Add(new MenuItem("", 0, false, Color.LightGray));
                                newNameItem.Text = " Name: <@0> ";
                                newNameItem.Color = Color.LightGray;
                                newNameItem.Writable = false;
                                player.Menu.contents.Add(newNameItem);
                                newDescItem.Text = " Description: <@0> ";
                                newDescItem.Color = Color.LightGray;
                                newDescItem.Writable = false;
                                player.Menu.contents.Add(newDescItem);
                                player.Menu.contents.Add(new MenuItem("", 0, false, Color.LightGray));
                                player.Menu.contents.Add(new MenuItem("Accept & Create a faction:", 0, false, Color.DarkGreen));
                                player.Menu.contents.Add(new MenuItem("Yes", 325, Color.BurlyWood));
                                player.Menu.contents.Add(new MenuItem("No", -1, Color.BurlyWood));

                                args.Handled = true;
                                player.Menu.DisplayMenu();
                                break;
                            }
                        case 325: // faction create confirmation
                            {
                                String newFactName = player.Menu.GetItemByValue(321).Input;
                                String newFactDesc = player.Menu.GetItemByValue(322).Input;
                                if (GetFactionByName(newFactName) == null)
                                {
                                    
                                    db.Query("INSERT INTO factions_Factions (Name, Description, Members, Admins, Power, Flags, WorldID) VALUES (@0, @1, @2, @3, @4, @5, @6)", newFactName, newFactDesc, player.TSplayer.UserID, player.TSplayer.UserID, player.Power, 0, Main.worldID);
                                    int newFactID = -1;
                                    using (QueryResult reader = db.QueryReader("SELECT ID FROM factions_Factions WHERE Name = @0", newFactName))
                                    {
                                        if (reader.Read())
                                        {
                                            newFactID = reader.Get<int>("ID");
                                        }
                                    }
                                    if (newFactID != -1)
                                    {
                                        Faction newFaction = new Faction(newFactID, newFactName, newFactDesc, player);
                                        FactionList.Add(newFaction);
                                        player.Faction = newFaction;
                                        player.TSplayer.SendMessage("Faction created");
                                        var playerregions = GetRegionsByUserID(player.UserID);
                                        foreach (Region region in playerregions)
                                        {
                                            UnclaimRegion(region);
                                        }
                                        newFaction.RefreshPVPStatus();
                                        newFaction.RefreshTeamStatus();
                                    }
                                }
                                break;
                            }

                        case 33: // Your Faction
                            {
                                if (player.Faction != null)
                                {
                                    player.Menu.title = "Your Faction";
                                    player.Menu.contents.Clear();
                                    player.Menu.index = 0;
                                    player.Menu.contents.Add(new MenuItem(player.Faction.Name, 0, false, false, Color.GreenYellow));
                                    player.Menu.contents.Add(new MenuItem(String.Format(" \"{0}\"", player.Faction.Desc), 0, false, false, Color.Gray));
                                    player.Menu.contents.Add(new MenuItem("", 0, false, false));
                                    player.Menu.contents.Add(new MenuItem(String.Format("Join status: {0}", player.Faction.Private ? "Invite Only" : "Open to all"), 0, false, Color.LightGray));
                                    player.Menu.contents.Add(new MenuItem(String.Format("PVP status: {0}", player.Faction.Hostile ? "Hostile" : "Peaceful"), 0, false, Color.LightGray));
                                    player.Menu.contents.Add(new MenuItem(String.Format("Team color: {0}", player.Faction.TeamColor), 0, false, Color.LightGray));
                                    player.Menu.contents.Add(new MenuItem(String.Format("Admins ({0}):", player.Faction.Admins.Count), 0, false, Color.LightGray));
                                    player.Menu.contents.Add(new MenuItem(String.Join(", ", player.Faction.AdminNames()), 0, false, false, Color.DarkGreen));
                                    player.Menu.contents.Add(new MenuItem(String.Format("Members ({0}):", player.Faction.Members.Except(player.Faction.Admins).ToList().Count), 0, false, Color.LightGray));
                                    player.Menu.contents.Add(new MenuItem(String.Join(", ", player.Faction.MemberNames(true)), 0, false, Color.DarkBlue));
                                    player.Menu.contents.Add(new MenuItem(String.Format("Power: {0}", player.Faction.Power), 0, false, Color.LightGray));
                                    player.Menu.contents.Add(new MenuItem(String.Format("Territory: {0}/{1}", player.Faction.Regions.Count, player.Faction.Members.Count), 0, false, Color.LightGray));
                                    if (player.Faction.Allies.Count > 0)
                                        player.Menu.contents.Add(new MenuItem(String.Format("Allies: {0}:", player.Faction.GetAlliesString()), 0, false, Color.LightGray));
                                    if (player.Faction.Enemies.Count > 0)
                                        player.Menu.contents.Add(new MenuItem(String.Format("Enemies: {0}:", player.Faction.GetEnemiesString()), 0, false, Color.LightGray));
                                    player.Menu.contents.Add(new MenuItem("", 0, false, Color.White));

                                    if (player.Faction.IsAdmin(player.TSplayer.UserID))
                                        player.Menu.contents.Add(new MenuItem("[ Faction Settings ]", 30, Color.LightGray));
                                    player.Menu.contents.Add(new MenuItem("[ Leave Faction ]", 35, Color.LightGray));
                                    player.Menu.contents.Add(new MenuItem("[ <- Back ]", 3, Color.BurlyWood));
                                    args.Handled = true;
                                    player.Menu.DisplayMenu();
                                }
                                break;
                            }
                        case 35: // leave faction
                            {
                                player.Menu.contents.Clear();
                                player.Menu.index = 0;
                                player.Menu.contents.Add(new MenuItem("Are you sure you want to leave the faction?", 0, false, Color.LightGray));
                                player.Menu.contents.Add(new MenuItem("[ Yes ]", 36, Color.DarkGreen));
                                player.Menu.contents.Add(new MenuItem("[ No ]", 33, Color.DarkRed));
                                args.Handled = true;
                                player.Menu.DisplayMenu();
                                break;

                            }
                        case 36: // leave faction confirmation
                            {
                                if (player.Faction != null)                                
                                    player.Faction.RemoveMember(player.UserID);
                                break;
                            }
                        case 30: // Faction Settings (Admins only)
                            {
                                if (!player.Faction.IsAdmin(player.TSplayer.UserID))
                                    break;
                                player.Menu.title = "Faction Settings";
                                player.Menu.contents.Clear();
                                player.Menu.index = 0;
                                var nameitem = new MenuItem("Name: @0", 301, false, true, Color.LightGray);
                                nameitem.Input = player.Faction.Name;
                                var descitem = new MenuItem("Desctiption: @0", 302, false, true, Color.LightGray);
                                descitem.Input = player.Faction.Desc;

                                player.Menu.contents.Add(nameitem);
                                player.Menu.contents.Add(descitem);
                                player.Menu.contents.Add(new MenuItem(String.Format("Join status: [{0}]", player.Faction.Private ? "Invite Only" : "Open to all"), 306, Color.LightGray));
                                player.Menu.contents.Add(new MenuItem(String.Format("PvP status: [{0}]", player.Faction.Hostile ? "Hostile" : "Peaceful"), 304, Color.LightGray));
                                player.Menu.contents.Add(new MenuItem(String.Format("Team color: [{0}]", player.Faction.TeamColor), 305, Color.LightGray));
                                player.Menu.contents.Add(new MenuItem("[ Manage members ]", 303, true, false, Color.LightGray));
                                player.Menu.contents.Add(new MenuItem("[ <- Back ]", 3, true, false, Color.BurlyWood));

                                args.Handled = true;
                                player.Menu.DisplayMenu();
                                break;
                            }
                        case 303: // Faction Member management
                            {
                                if (!player.Faction.IsAdmin(player.TSplayer.UserID))
                                    break;
                                player.Menu.title = String.Format("{0} Members", player.Faction.Name);
                                player.Menu.contents.Clear();
                                player.Menu.index = 0;

                                foreach (int userid in player.Faction.Members)
                                {
                                    using (QueryResult reader = db.QueryReader("SELECT Name, Power FROM factions_Players WHERE UserID = @0 ORDER BY Name ASC", userid))
                                    {
                                        if (reader.Read())
                                        {
                                            player.Menu.contents.Add(new MenuItem(String.Format("- (@0) {0} [Power: {1}] {2}", reader.Get<string>("Name"), reader.Get<int>("Power"), (player.Faction.IsAdmin(userid)) ? "<Admin>" : ""), 3030, (player.UserID == userid) ? false : true, Color.BurlyWood));
                                            player.Menu.contents.Last().Input = userid.ToString();
                                        }
                                    }
                                }
                                player.Menu.contents.Add(new MenuItem("[ <- Back ]", 30, true, false, Color.BurlyWood));
                                args.Handled = true;
                                player.Menu.DisplayMenu();
                                break;
                            }
                        case 3030:
                            {
                                if (!player.Faction.IsAdmin(player.TSplayer.UserID))
                                    break;
                                player.Menu.title = String.Format("{0} Members", player.Faction.Name);
                                int userid = 0;
                                if (int.TryParse(args.Data[args.Selected].Input, out userid))
                                {
                                    player.Menu.contents.Clear();
                                    player.Menu.index = 0;
                             
                                        using (QueryResult reader = db.QueryReader("SELECT Name, Power FROM factions_Players WHERE UserID = @0", userid))
                                        {
                                            if (reader.Read())
                                            {
                                                player.Menu.contents.Add(new MenuItem(String.Format("Name: {0} (userID: @0)", reader.Get<string>("Name")), 3035, false, Color.GreenYellow));
                                                player.Menu.contents.Last().Input = userid.ToString();
                                                player.Menu.contents.Add(new MenuItem(String.Format("Power: {0}", reader.Get<int>("Power")), 0, false, Color.LightGray));
                                                if (player.Faction.Admins.Contains(userid))
                                                {
                                                    player.Menu.contents.Add(new MenuItem("Status: Admin", 0, false, Color.DarkOrange));
                                                    player.Menu.contents.Add(new MenuItem("[ Demote to default ]", 3032, Color.BurlyWood));
                                                }
                                                else
                                                {
                                                    player.Menu.contents.Add(new MenuItem("Status: Default", 0, false, Color.Gray));
                                                    player.Menu.contents.Add(new MenuItem("[ Promote to admin ]", 3032, Color.BurlyWood));
                                                }
                                                player.Menu.contents.Add(new MenuItem("[ Kick from Faction ]", 3031, Color.BurlyWood));
                                                player.Menu.contents.Add(new MenuItem("[ <- Back ]", 303, Color.BurlyWood));
                                            }
                                        }
                                    
                                    args.Handled = true;
                                    player.Menu.DisplayMenu();
                                }
                                break;
                            }
                        case 3031: //Kick from Faction
                            {
                                int userid = -1;
                                var useridItem = player.Menu.GetItemByValue(3035);
                                if (useridItem != null && player.Faction != null && player.Faction.IsAdmin(player.UserID) && int.TryParse(useridItem.Input, out userid) && player.UserID != userid && player.Faction.Members.Contains(userid))
                                {
                                    player.Faction.RemoveMember(userid);
                                    player.Menu.contents.Clear();
                                    player.Menu.index = 0;
                                    player.Menu.contents.Add(new MenuItem("Player has been kicked from faction.", 0, false, Color.Green));
                                    player.Menu.contents.Add(new MenuItem("[ <- Back ]", 303, Color.BurlyWood));
                                    args.Handled = true;
                                    player.Menu.DisplayMenu();
                                }
                                break;
                            }
                        case 3032: // promote /demote 
                            {
                                int userid = -1;
                                var useridItem = player.Menu.GetItemByValue(3035);
                                if (useridItem != null && player.Faction != null && player.Faction.IsAdmin(player.UserID) && int.TryParse(useridItem.Input, out userid) && player.UserID != userid && player.Faction.Members.Contains(userid))
                                {
                                    player.Menu.contents.Clear();
                                    player.Menu.index = 0;
                                    if (player.Faction.Admins.Remove(userid))                                    
                                        player.Menu.contents.Add(new MenuItem("Member demoted to default.", 0, false, Color.LightGray));                                   
                                    else
                                    {
                                        player.Faction.Admins.Add(userid);
                                        player.Menu.contents.Add(new MenuItem("Member promoted to Admin.", 0, false, Color.LightGray));
                                    }
                                        db.Query("UPDATE factions_Factions SET Admins = @0 WHERE ID = @1", String.Join(",", player.Faction.Admins), player.Faction.ID);
                                    player.Menu.contents.Add(new MenuItem("[ <- Back ]", 303, Color.BurlyWood));
                                    args.Handled = true;
                                    player.Menu.DisplayMenu();
                                }
                                break;
                            }
                        case 304: // faction pvp status
                            {
                                player.Faction.Hostile = !player.Faction.Hostile;                                
                                player.Menu.contents.Clear();
                                player.Menu.index = 0;
                                player.Menu.contents.Add(new MenuItem("Faction PVP status has been changed.", 0, false, Color.Green));
                                player.Menu.contents.Add(new MenuItem("[ <- Back ]", 30, Color.BurlyWood));
                                args.Handled = true;
                                player.Menu.DisplayMenu();
                                break;
                            }
                        case 305: //faction team color
                            {
                                player.Faction.Team = (byte)(player.Faction.Team + 1);
                                player.Menu.contents.Clear();
                                player.Menu.index = 0;
                                player.Menu.contents.Add(new MenuItem("Faction team color been changed.", 0, false, Color.Green));
                                player.Menu.contents.Add(new MenuItem("[ <- Back ]", 30, Color.BurlyWood));
                                args.Handled = true;
                                player.Menu.DisplayMenu();
                                break;
                            }
                        case 306: //faction Join status
                            {
                                player.Faction.Private = !player.Faction.Private;
                                player.Menu.contents.Clear();
                                player.Menu.index = 0;
                                player.Menu.contents.Add(new MenuItem("Faction Join status has been changed.", 0, false, Color.Green));
                                player.Menu.contents.Add(new MenuItem("[ <- Back ]", 30, Color.BurlyWood));
                                args.Handled = true;
                                player.Menu.DisplayMenu();
                                break;
                            }
                        case 309: // Save faction name & desc
                            {
                                player.Faction.Name = player.Menu.GetItemByValue(301).Input;
                                player.Faction.Desc = player.Menu.GetItemByValue(302).Input;

                                player.Menu.contents.Clear();
                                player.Menu.index = 0;                                
                                player.Menu.contents.Add(new MenuItem("Faction settings saved.", 0, false, Color.Green));
                                player.Menu.contents.Add(new MenuItem("[ <- Back ]", 30, Color.BurlyWood));
                                player.Menu.contents.Add(new MenuItem("[ Exit ]", -1, Color.Gray));
                                args.Handled = true;
                                player.Menu.DisplayMenu();
                                break;
                            }

                        default:
                            {
                                Console.WriteLine("Got value: {0} from menu. Closing", value);
                                player.CloseMenu();
                                break;
                            }
                    }                    

                }
                else if (args.Status == 2)
                {
                    player.Menu = (Chat.Menu)menu;
                    int value = args.Data[args.Selected].Value;
                    string input = args.Data[args.Selected].Input;
                    switch (value)
                    {
                        case 301: // Faction name
                            {
                                var saveButton = player.Menu.GetItemByValue(309);
                                if (Faction.IsValidName(input))
                                {
                                    args.Data[args.Selected].Color = Color.Green;
                                    if (saveButton == null && player.Menu.GetItemByValue(302).Color != Color.Red)
                                        player.Menu.contents.Insert(2, new MenuItem("[ Save & Exit ]", 309, Color.DarkGreen));
                                }
                                else
                                {
                                    args.Data[args.Selected].Color = Color.Red;
                                    if (saveButton != null)
                                        player.Menu.contents.Remove(saveButton);
                                }                                                                
                                break;
                            }
                        case 302: // Faction description
                            {
                                var saveButton = player.Menu.GetItemByValue(309);
                                if (input.Length > 0 && input[0] != ' ')
                                {
                                    args.Data[args.Selected].Color = Color.Green;
                                    if (saveButton == null && player.Menu.GetItemByValue(301).Color != Color.Red)
                                        player.Menu.contents.Insert(2, new MenuItem("[ Save & Exit ]", 309, Color.DarkGreen));
                                }
                                else
                                {
                                    args.Data[args.Selected].Color = Color.Red;
                                    if (saveButton != null)
                                        player.Menu.contents.Remove(saveButton);
                                }
                                break;
                            }
                        case 321: // new faction name
                            {
                                if (!Faction.IsValidName(input))
                                {
                                    player.Menu.contents[args.Selected].Color = Color.Red;
                                    player.Menu.contents[args.Selected].Text = "Name: @0 [invalid]";                                    
                                }
                                else if (GetFactionByName(input) == null)
                                {
                                    player.Menu.contents[args.Selected].Color = Color.Green;
                                    player.Menu.contents[args.Selected].Text = "Name: @0 [ok]";
                                    if (player.Menu.contents[args.Selected + 1].Color != Color.Red)
                                    {
                                        player.Menu.contents[args.Selected + 2].Color = Color.BurlyWood;
                                        player.Menu.contents[args.Selected + 2].Selectable = true;
                                        break;
                                    }
                                }
                                else
                                {
                                    player.Menu.contents[args.Selected].Color = Color.Red;
                                    player.Menu.contents[args.Selected].Text = "Name: @0 [taken]";
                                }
                                player.Menu.contents[args.Selected + 2].Color = Color.Gray;
                                player.Menu.contents[args.Selected + 2].Selectable = false;
                                break;
                            }
                        case 322: // new faction description
                            {
                                if (input.Length > 0 && !input.StartsWith(" "))
                                {
                                    player.Menu.contents[args.Selected].Color = Color.Green;
                                    if (player.Menu.contents[args.Selected - 1].Color != Color.Red)
                                    {
                                        player.Menu.contents[args.Selected + 1].Color = Color.BurlyWood;
                                        player.Menu.contents[args.Selected + 1].Selectable = true;
                                        break;
                                    }
                                }
                                else
                                    player.Menu.contents[args.Selected].Color = Color.Red;
                                player.Menu.contents[args.Selected + 1].Color = Color.Gray;
                                player.Menu.contents[args.Selected + 1].Selectable = false;
                                break;
                            }
                        default:
                            {
                                Console.WriteLine("Value: {0}, Input: {1}", value, input);
                                break;
                            }
                    }                    
                }
                else
                {
                    Log.ConsoleError(String.Format("Got Status {0} from menu", args.Status));
                }
            }
        }

        void OnUpdate()
        {
            if (Main.worldID != 0)
            {
                Console.WriteLine("Loading data for plugin Factions...");
                int count = 0;
                using (QueryResult reader = db.QueryReader("SELECT * FROM factions_Factions WHERE WorldID = @0", Main.worldID))
                {
                    while (reader.Read())
                    {
                        var membs = reader.Get<string>("Members");
                        List<int> members = new List<int>();
                        if (membs != null)
                        {
                            foreach (String memb in membs.Split(','))
                            {
                                int membid;
                                if (int.TryParse(memb, out membid) && !members.Contains(membid))                                
                                    members.Add(membid);                                
                            }
                        }
                        var adms = reader.Get<string>("Admins");
                        List<int> admins = new List<int>();
                        if (adms != null)
                        {
                            foreach (String adm in adms.Split(','))
                            {
                                int admid;
                                if (int.TryParse(adm, out admid) && !admins.Contains(admid))
                                    admins.Add(admid);
                            }
                        }
                        var invitees = reader.Get<string>("Invites");
                        List<int> invites = new List<int>();
                        if (invitees != null)
                        {
                            foreach (String invitee in invitees.Split(','))
                            {
                                int inviteeID;
                                if (int.TryParse(invitee, out inviteeID) && !invites.Contains(inviteeID))
                                    invites.Add(inviteeID);
                            }
                        }
                        var alliesString = reader.Get<string>("Allies");
                        List<int> allies = new List<int>();
                        if (alliesString != null)
                        {
                            foreach (String ally in alliesString.Split(','))
                            {
                                int ID;
                                if (int.TryParse(ally, out ID) && !allies.Contains(ID))
                                    allies.Add(ID);
                            }
                        }
                        var allyInvitesString = reader.Get<string>("AllyInvites");
                        List<int> allyinvites = new List<int>();
                        if (allyInvitesString != null)
                        {
                            foreach (String allyinvite in allyInvitesString.Split(','))
                            {
                                int ID;
                                if (int.TryParse(allyinvite, out ID) && !allyinvites.Contains(ID))
                                    allyinvites.Add(ID);
                            }
                        }
                        var enemiesString = reader.Get<string>("Enemies");
                        List<int> enemies = new List<int>();
                        if (enemiesString != null)
                        {
                            foreach (String enemy in enemiesString.Split(','))
                            {
                                int ID;
                                if (int.TryParse(enemy, out ID) && !enemies.Contains(ID))
                                    enemies.Add(ID);
                            }
                        }
                        FactionList.Add(new Faction(reader.Get<int>("ID"), reader.Get<string>("Name"), reader.Get<string>("Description"), members, admins, invites, reader.Get<int>("Power"), new List<Region>(), (byte)reader.Get<int>("Flags"), allies, allyinvites, enemies));
                        count++;
                    }
                }
                Console.WriteLine("--> {0} factions loaded", count);
                count = 0;
                using (QueryResult reader = db.QueryReader("SELECT * FROM factions_Regions WHERE WorldID = @0", Main.worldID))
                {
                    while (reader.Read())
                    {
                        var newregion = new Region(reader.Get<int>("ID"), reader.Get<int>("X"), reader.Get<int>("Y"), reader.Get<int>("Owner"), reader.Get<int>("Faction"), (byte)reader.Get<int>("Flags"));
                        Regions.Add(newregion);
                        if (newregion.Faction != 0)
                        {
                            var faction = GetFactionByID(newregion.Faction);
                            if (faction != null)
                                faction.Regions.Add(newregion);
                        }
                        count++;
                    }
                }
                Console.WriteLine("--> {0} regions loaded", count);
                GameHooks.Update -= OnUpdate;
                db.Query("UPDATE factions_Players SET Online = 0 WHERE Online = 1 AND WorldID = @0", Main.worldID);
            }
        }
        private void SetupDb()
        {
            if (TShock.Config.StorageType.ToLower() == "sqlite")
            {
                //Console.WriteLine("sqlite - {0}", TShock.Config.StorageType);
                db = new SqliteConnection(string.Format("uri=file://{0},Version=3", Path.Combine(savepath, "Factions.sqlite")));
            }
            else if (TShock.Config.StorageType.ToLower() == "mysql")
            {
                //Console.WriteLine("mysql - {0}", TShock.Config.StorageType);
                try
                {
                    var host = TShock.Config.MySqlHost.Split(':');
                    db = new MySqlConnection();
                    db.ConnectionString = String.Format("Server={0}; Port={1}; Database={2}; Uid={3}; Pwd={4};",
                        host[0],
                        host.Length == 1 ? "3306" : host[1],
                        TShock.Config.MySqlDbName,
                        TShock.Config.MySqlUsername,
                        TShock.Config.MySqlPassword
                    );
                }
                catch (MySqlException ex)
                {
                    Log.Error(ex.ToString());
                    throw new Exception("MySql not setup correctly");
                }
            }
            else
            {
                throw new Exception("Invalid storage type");
            }

            SqlTableCreator SQLcreator = new SqlTableCreator(db, db.GetSqlType() == SqlType.Sqlite ? (IQueryBuilder)new SqliteQueryCreator() : new MysqlQueryCreator());
            SqlTableEditor SQLeditor = new SqlTableEditor(db, db.GetSqlType() == SqlType.Sqlite ? (IQueryBuilder)new SqliteQueryCreator() : new MysqlQueryCreator());
            SqlTable table = new SqlTable("factions_Factions",
                new SqlColumn("ID", MySqlDbType.Int32) { Primary = true, AutoIncrement = true, NotNull = true },
                new SqlColumn("Name", MySqlDbType.VarChar) { Length = 30 },
                new SqlColumn("Description", MySqlDbType.Text),
                new SqlColumn("Members", MySqlDbType.Text),
                new SqlColumn("Admins", MySqlDbType.Text),
                new SqlColumn("Invites", MySqlDbType.Text),
                new SqlColumn("Power", MySqlDbType.Int32),
                new SqlColumn("Territory", MySqlDbType.Text),
                new SqlColumn("Allies", MySqlDbType.Text),
                new SqlColumn("AllyInvites", MySqlDbType.Text),
                new SqlColumn("Enemies", MySqlDbType.Text),
                new SqlColumn("Flags", MySqlDbType.Int32),
                new SqlColumn("WorldID", MySqlDbType.Int32)
            );
            SQLcreator.EnsureExists(table);
            table = new SqlTable("factions_Players",
                new SqlColumn("UserID", MySqlDbType.Int32) { Primary = true, NotNull = true },
                new SqlColumn("Name", MySqlDbType.VarChar) { Length = 30 },
                new SqlColumn("Faction", MySqlDbType.Int32),
                new SqlColumn("Power", MySqlDbType.Int32),
                new SqlColumn("Online", MySqlDbType.Int32),
                new SqlColumn("OfflineCount", MySqlDbType.Int32),
                new SqlColumn("WorldID", MySqlDbType.Int32)
            );
            SQLcreator.EnsureExists(table);
            table = new SqlTable("factions_Regions",
                new SqlColumn("ID", MySqlDbType.Int32) { Primary = true, AutoIncrement = true, NotNull = true },
                new SqlColumn("X", MySqlDbType.Int32),
                new SqlColumn("Y", MySqlDbType.Int32),
                new SqlColumn("Owner", MySqlDbType.Int32),
                new SqlColumn("Faction", MySqlDbType.Int32),
                new SqlColumn("Flags", MySqlDbType.Int32),
                new SqlColumn("WorldID", MySqlDbType.Int32)
            );
            SQLcreator.EnsureExists(table);            
        }


        public static void GetData(GetDataEventArgs e) {
            try
            {
                switch (e.MsgID)
                {
                    #region Item Drop
                    case PacketTypes.ItemDrop:
                        {
                            using (var data = new MemoryStream(e.Msg.readBuffer, e.Index, e.Length))
                            {
                                var reader = new BinaryReader(data);
                                int itemID = reader.ReadInt16();
                                float posX = reader.ReadSingle();
                                float posY = reader.ReadSingle();
                                float velX = reader.ReadSingle();
                                float velY = reader.ReadSingle();
                                byte stacks = reader.ReadByte();
                                byte prefix = reader.ReadByte();
                                int type = reader.ReadInt16();
                                reader.Dispose();
                                //Console.WriteLine("ItemUpdate: id: {0}, posx: {1}, posy: {2}, velX: {3}, velY: {4}, stack: {5}, prefix: {6}, type: {7}", itemID, posX, posY, velX, velY, stacks, prefix, type);
                                var player = PlayerList[e.Msg.whoAmI];
                                if (player != null)
                                {
                                    if (player.changingMoney && type == 73)
                                    {
                                        int gainpower = stacks * 2;
                                        bool converted = false;
                                        if (gainpower + player.Power > 100)
                                        {
                                            int realgain = 100 - player.Power;
                                            int extragain = gainpower - realgain;
                                            if (realgain != 0 && player.ChangePower(realgain))
                                            {
                                                if (extragain % 2 != 0) // drop 50 silver
                                                {
                                                    extragain--;
                                                    int silverid = Item.NewItem((int)posX, (int)posY, 1, 1, 72, 50, true, 0);
                                                    NetMessage.SendData((int)PacketTypes.ItemDrop, -1, -1, "", silverid);
                                                }
                                                if (extragain > 0) // drop extra gold
                                                {
                                                    int goldid = Item.NewItem((int)posX, (int)posY, 1, 1, 73, (int)(extragain / 2), true, 0);
                                                    NetMessage.SendData((int)PacketTypes.ItemDrop, -1, -1, "", goldid);
                                                }
                                                converted = true;
                                                player.changingMoney = false;
                                                gainpower = realgain;
                                            }
                                        }
                                        else if (player.ChangePower(gainpower))
                                            converted = true;
                                        if (converted)
                                        {
                                            e.Handled = true;
                                            if (player.Menu != null)
                                            {
                                                var convtext = player.Menu.GetItemByValue(410);
                                                if (convtext != null)
                                                    convtext.Text = String.Format("> Converted {0} gold into {1} power", gainpower / 2, gainpower);
                                                else
                                                    player.Menu.contents.Insert(3, new MenuItem(String.Format("> Converted {0} gold into {1} power", gainpower / 2, gainpower), 410, false, Color.DarkGreen));
                                                player.Menu.index = 3;
                                                player.Menu.DisplayMenu();
                                            }
                                        }
                                    }
                                }
                            }
                            break;
                        }
                    #endregion
                    #region Player Update
                    case PacketTypes.PlayerUpdate:
                        {
                            byte plyID, flags;
                            using (var data = new MemoryStream(e.Msg.readBuffer, e.Index, e.Length))
                            {
                                var reader = new BinaryReader(data);
                                plyID = reader.ReadByte();
                                flags = reader.ReadByte();
                                reader.Close();
                                reader.Dispose();
                            }
                            var player = PlayerList[plyID];
                            if (player != null)
                            {
                                if (player.LastState != flags)
                                {
                                    player.LastState = flags;
                                    player.IdleCount = 0;
                                }
                                if ((flags & 32) == 32)
                                    player.CloseMenu();                                
                            }
                            break;
                        }
                    #endregion
                    #region Tile Edit
                    case PacketTypes.Tile:
                        {
                            byte type, tileType, style;
                            Int32 x, y;
                            bool fail;
                            using (var data = new MemoryStream(e.Msg.readBuffer, e.Index, e.Length))
                            {
                                var reader = new BinaryReader(data);
                                type = reader.ReadByte();
                                x = reader.ReadInt32();
                                y = reader.ReadInt32();
                                tileType = reader.ReadByte();
                                fail = reader.ReadBoolean();
                                style = reader.ReadByte();
                                reader.Close();
                                reader.Dispose();
                            }
                            //    Console.WriteLine("Tileinfo: type: {0}, frameX: {1}, frameY: {2}, frameNum: {3}", tile.type, tile.frameX, tile.frameY, tile.frameNumber);
                            //   Console.WriteLine("Tiledata: type: {0}, frameX: {1}, frameY: {2}, frameNum: {3}", tile.Data.type, tile.Data.frameX, tile.Data.frameY, tile.Data.frameNumber);
                            //    Console.WriteLine("type: {0}, Main.tile[].active: {1}, fail: {2}", type, Main.tile[x, y].Data.active, fail);

                            var player = PlayerList[e.Msg.whoAmI];
                            if (player == null)
                                return;

                            if (y < Main.worldSurface)
                            {
                                if (Main.tile[x, y].type == 5)
                                {
                                    if (tileType == 0 && Main.tile[x, y + 1].type == 2)
                                    {
                                        e.Handled = true;
                                        WorldGen.KillTile(x, y);
                                        Main.tile[x, y].type = 20;
                                        WorldGen.PlaceTile(x, y, 20, false, true);
                                        TSPlayer.All.SendTileSquare(x, y, 6);
                                    }
                                }
                                else if (!new int[] { 3, 24, 28, 32, 37, 51, 52, 61, 62, 69, 71, 73, 74, 80, 81, 82, 83, 84, 85, 110, 113, 115, 138 }.Contains(Main.tile[x, y].type))
                                {
                                    var perm = CanBuild(player, new Point(x, y)); 
                                    if (perm < 0)
                                    {
                                        e.Handled = true;                                        
                                        //player.TSplayer.SendMessage("You can't build on the surface unless you claim the land");                                        
                                        if (perm == -2)
                                        {
                                            player.TSplayer.SendTileSquare(x, y, 4);
                                            player.TSplayer.Disable("tileedit in someone's region");
                                        }
                                    }
                                }
                            }
                            else
                            {
                                if (CanBuild(player, new Point(x, y)) < 0)
                                {
                                    e.Handled = true;
                                    player.TSplayer.SendTileSquare(x, y, 4);
                                    player.TSplayer.SendMessage("You can't build on this land, it does not belong to you.");
                                    player.TSplayer.Disable("tileedit in someone's region");
                                }
                            }
                            break;
                        }
                    #endregion
                    #region Liquid set
                    case PacketTypes.LiquidSet:
                        {                            
                            int x, y;
                            byte liquid;
                            //bool lava;
                            using (var data = new MemoryStream(e.Msg.readBuffer, e.Index, e.Length))
                            {
                                var reader = new BinaryReader(data);
                                x = reader.ReadInt32();
                                y = reader.ReadInt32();
                                liquid = reader.ReadByte();
                                //lava = reader.ReadBoolean();
                                reader.Close();
                                reader.Dispose();
                            }
                            //Console.WriteLine("(GetData) Liquid set: x:{0} y:{1} liquid:{2}", x, y, liquid);
                            var player = PlayerList[e.Msg.whoAmI];
                            if (player == null)
                                return;
                            if (CanBuild(player, new Point(x, y)) < 0)
                            {
                                e.Handled = true;
                                player.TSplayer.SendTileSquare(x, y, 4);
                                player.TSplayer.SendMessage("You cannot use bucket in this region");
                                player.TSplayer.Disable("using bucket in no-build zone");
                            }
                            break;
                        }
                    #endregion
                    #region Chest kill
                    case PacketTypes.TileKill:
                        {
                            int x, y;
                            using (var data = new MemoryStream(e.Msg.readBuffer, e.Index, e.Length))
                            {
                                var reader = new BinaryReader(data);
                                x = reader.ReadInt32();
                                y = reader.ReadInt32();
                                reader.Close();
                                reader.Dispose();
                            }
                            var player = PlayerList[e.Msg.whoAmI];
                            if (player == null)
                                break;
                            if (CanBuild(player, new Point(x, y)) < 0)
                            {
                                e.Handled = true;
                                player.TSplayer.SendMessage("You cannot remove this chest");
                                player.TSplayer.Disable("trying to remove chest");
                                player.TSplayer.SendTileSquare(x, y, 4);
                            }
                            break;
                        }
                    #endregion
                    #region Player damage
                    case PacketTypes.PlayerDamage:
                        {
                            using (var data = new MemoryStream(e.Msg.readBuffer, e.Index, e.Length))
                            {
                                var reader = new BinaryReader(data);
                                var pID = reader.ReadByte();
                                var hitDirection = reader.ReadByte();
                                var dmg = reader.ReadInt16();
                                var PVP = reader.ReadByte();
                                //var crit = reader.ReadByte();
                                //var death = reader.ReadBytes(e.Msg.messageLength - 6);
                                //var deathtext = Encoding.UTF8.GetString(death);
                                //Console.WriteLine("PlayerDamage: pID: {0}, e.whoami: {1}, PVP: {2}", pID, e.Msg.whoAmI, PVP);
                                if (PVP == 1)
                                {
                                    try
                                    {
                                        var player = PlayerList[pID];
                                        var attacker = PlayerList[e.Msg.whoAmI];
                                        if (player != null)
                                        {
                                            if (player.Faction != null && player.Faction.InFactionTerritory(new Point(player.TSplayer.TileX, player.TSplayer.TileY)))
                                            {
                                                if (attacker.Faction == null || (!attacker.Faction.Enemies.Contains(player.Faction.ID) && !player.Faction.Enemies.Contains(attacker.Faction.ID)))
                                                {
                                                    e.Handled = true;
                                                    attacker.TSplayer.SendMessage(String.Format("You cannot attack {0} in their faction territory unless your faction declares war.", player.TSplayer.Name), Color.LightSalmon);
                                                    attacker.TSplayer.Disable("PvP on neutral facton grounds");
                                                }                                                
                                            }
                                        }
                                    }
                                    catch (Exception ex) { Log.ConsoleError(ex.ToString()); }
                                }
                                //Console.WriteLine("deathtext: {0}", deathtext);
                                //e.Handled = true;
                            }
                            break;
                        }
                    #endregion
                    #region Player Death
                    case PacketTypes.PlayerKillMe:
                        {
                            byte pID, hitDirection;
                            //Int16 dmg;
                           // bool PVP;
                           // byte[] death;
                            //string deathtext;
                            using (var data = new MemoryStream(e.Msg.readBuffer, e.Index, e.Length))
                            {
                                var reader = new BinaryReader(data);
                                pID = reader.ReadByte();
                                hitDirection = reader.ReadByte();
                                //dmg = reader.ReadInt16();
                                //PVP = reader.ReadBoolean();
                                //death = reader.ReadBytes(e.Msg.messageLength - 5);
                                //deathtext = Encoding.UTF8.GetString(death);
                                reader.Close();
                                reader.Dispose();
                            }
                            var player = PlayerList[pID];
                            if (player != null)
                            {
                                player.ChangePower(-1);
                            }
                            // Console.WriteLine("PlayerKillMe: who: {0}, dir: {1}, dmg: {2}, PVP: {3}, e.msg.whoami: {4}", pID, hitDirection, dmg, PVP, e.Msg.whoAmI);
                            //Console.WriteLine("deathtext: {0}", deathtext);
                            break;
                        }
                    #endregion
                    #region Player Team change
                    case PacketTypes.PlayerTeam:
                        {
                            if (e.Handled)
                                return;
                            byte pID, teamID;
                            using (var data = new MemoryStream(e.Msg.readBuffer, e.Index, e.Length))
                            {
                                var reader = new BinaryReader(data);
                                pID = reader.ReadByte();
                                teamID = reader.ReadByte();
                                reader.Close();
                                reader.Dispose();
                            }
                            try
                            {
                                var player = PlayerList[pID];
                                if (player != null)
                                {
                                    if (player.Faction != null)
                                    {
                                        player.TSplayer.SendMessage("You cannot change team while in Faction.", Color.LightSalmon);
                                        player.TSplayer.TPlayer.team = player.Faction.Team;
                                        player.TSplayer.SendData(PacketTypes.PlayerTeam, "", player.ID);
                                        e.Handled = true;
                                    }
                                    /*else
                                    {
                                        player.TSplayer.TPlayer.team = teamID;
                                        NetMessage.SendData((int)PacketTypes.PlayerTeam, -1, -1, "", player.ID);
                                    }
                                    e.Handled = true;*/
                                }
                            }
                            catch (Exception ex)
                            {
                                Log.ConsoleError(ex.ToString());
                                Log.ConsoleError(String.Format("playerID: {0}", pID));
                            }
                            break;
                        }
                    #endregion
                    #region Player PVP toggle
                    case PacketTypes.TogglePvp:
                        {
                            byte pID;
                            bool pvp;
                            using (var data = new MemoryStream(e.Msg.readBuffer, e.Index, e.Length))
                            {
                                var reader = new BinaryReader(data);
                                pID = reader.ReadByte();
                                pvp = reader.ReadBoolean();
                                reader.Close();
                                reader.Dispose();
                            }
                            var player = PlayerList[pID];
                            if (player != null && player.Faction != null)
                            {
                                if (!pvp && player.Faction.Hostile)
                                {
                                    player.TSplayer.TPlayer.hostile = true;
                                    player.TSplayer.SendMessage("You cannot turn off your PvP, your Faction status is Hostile.", Color.LightSalmon);
                                    player.TSplayer.SendData(PacketTypes.TogglePvp, "", e.Msg.whoAmI);
                                    e.Handled = true;
                                }
                                else if (pvp && !player.Faction.Hostile)
                                {
                                    player.TSplayer.TPlayer.hostile = false;
                                    player.TSplayer.SendMessage("You cannot turn on your PvP, your Faction status is Peaceful.", Color.LightSalmon);
                                    player.TSplayer.SendData(PacketTypes.TogglePvp, "", e.Msg.whoAmI);
                                    e.Handled = true;
                                }
                            }
                            break;
                        }
                    #endregion
                    case PacketTypes.Status:
                        {
                            Int32 Number;
                            String Status;
                            using (var data = new MemoryStream(e.Msg.readBuffer, e.Index, e.Length))
                            {
                                var reader = new BinaryReader(data);
                                Number = reader.ReadInt32();
                                Status = Encoding.UTF8.GetString(reader.ReadBytes(e.Msg.messageLength - 4));
                                reader.Close();
                                reader.Dispose();
                            }
                            Console.WriteLine("(Get Data) Status: number: {0}, text: {1}", Number, Status);
                            break;
                        }
                }
            }
            catch (Exception ex)
            {
                Log.ConsoleError(ex.ToString());
            }
        }
        public void SendData(SendDataEventArgs e)
        {
            try
            {
                switch (e.MsgID)
                {
                    case PacketTypes.Status:
                        {
                            Console.WriteLine("(SendData) Status ->  1: {0}, 2: {4}, 3: {5}, 4: {6}, 5: {1}, remote: {2}, ignore: {3}", e.number, e.number5, e.remoteClient, e.ignoreClient, e.number2, e.number3, e.number4);
                            break;   
                        }
                    case PacketTypes.TogglePvp:
                        {
                            //Console.WriteLine("(SendData) PvP ->  1: {0}, 2: {4}, 3: {5}, 4: {6}, 5: {1}, remote: {2}, ignore: {3}", e.number, e.number5, e.remoteClient, e.ignoreClient, e.number2, e.number3, e.number4);
                            if (e.remoteClient == -1)
                            {
                                var player = PlayerList[e.number];
                                if (player != null && player.Faction != null)
                                {
                                    for (int i = 0; i < PlayerList.Length; i++)
                                    {
                                        if (PlayerList[i] != null)
                                        {
                                            if (!player.Faction.Members.Contains(PlayerList[i].UserID) && PlayerList[i].Faction != null && player.Faction.Allies.Contains(PlayerList[i].Faction.ID))
                                            {
                                                player.TSplayer.TPlayer.hostile = false;
                                               // Console.WriteLine("Player {0} is an ally", PlayerList[i].ID);
                                            }
                                            else
                                            {
                                                player.TSplayer.TPlayer.hostile = player.Faction.Hostile;
                                                //Console.WriteLine("Player {0} is outsider", PlayerList[i].ID);
                                            }
                                            PlayerList[i].TSplayer.SendData(PacketTypes.TogglePvp, "", e.number);
                                        }
                                    }
                                    e.Handled = true;

                                    /*if (e.ignoreClient != -1)
                                    {
                                        e.Handled = true;
                                        NetMessage.SendData((int)PacketTypes.TogglePvp, -1, -1, "", e.number);
                                    }*/
                                }
                            }
                            break;
                        }
                    case PacketTypes.PlayerTeam:
                        {
                            //Console.WriteLine("team change -> 1: {0}, 2: {4}, 3: {5}, 4: {6}, 5: {1}, remote: {2}, ignore: {3}", e.number, e.number5, e.remoteClient, e.ignoreClient, e.number2, e.number3, e.number4);
                            if (e.remoteClient == -1)
                            {
                                var player = PlayerList[e.number];
                                if (player != null)
                                {
                                    int oldteam = player.TSplayer.TPlayer.team;                                    
                                    for (int i = 0; i < PlayerList.Length; i++)
                                    {
                                        if (PlayerList[i] != null)
                                        {
                                            if (PlayerList[i].Faction != null)
                                            {
                                                if (PlayerList[i].Faction.Members.Contains(player.UserID))
                                                    player.TSplayer.TPlayer.team = PlayerList[i].Faction.Team;
                                                else
                                                    player.TSplayer.TPlayer.team = 0;
                                            }
                                            else if (player.Faction != null)
                                                player.TSplayer.TPlayer.team = 0;
                                            else
                                                player.TSplayer.TPlayer.team = oldteam;
                                            //Console.WriteLine("(Send Data) Sending Team Change to {0} for player {1}", PlayerList[i].ID, e.number);
                                            PlayerList[i].TSplayer.SendData(PacketTypes.PlayerTeam, "", e.number);
                                        }
                                    }
                                    player.TSplayer.TPlayer.team = oldteam;
                                }
                                e.Handled = true;
                            }
                            break;
                        }
                }
            }
            catch (Exception ex) { Log.ConsoleError(ex.ToString()); }
        }

        private static Faction GetFactionByID(int id)
        {
            foreach (Faction fact in FactionList)
            {
                if (fact.ID == id)
                    return fact;
            }
            return null;
        }
        private static Faction GetFactionByName(string name)
        {
            foreach (Faction fact in FactionList)
            {
                if (fact.Name == name)
                    return fact;
            }
            return null;
        }
        private static List<Faction> GetTopFactionsByPower() { return GetTopFactionsByPower(0); }
        private static List<Faction> GetTopFactionsByPower(int count)
        {
            var sorted = from fact in FactionList orderby fact.Power descending select fact;
            return sorted.ToList();
        }
        private static Region GetRegionByID(int id)
        {
            foreach (Region reg in Regions)
            {
                if (reg.ID == id)
                    return reg;
            }
            return new Region(-1, 0, 0);
        }
        private static List<Region> GetRegionsByUserID(int userid)
        {
            List<Region> ReturnList = new List<Region>();
            foreach (Region reg in Regions)
            {
                if (reg.Owner != -1 && reg.Owner == userid)
                    ReturnList.Add(reg);
            }
            return ReturnList;
        }
        private static Region GetRegionFromLocation(int x, int y)
        {
            foreach (Region reg in Regions)
            {
                if (reg.InArea(x, y))
                    return reg;
            }
            var newregion = new Region(-1, x, y);
            newregion.X = (int)(newregion.X / newregion.Width) * newregion.Width;
            newregion.Y = (int)(newregion.Y / newregion.Height) * newregion.Height;
            return newregion;
        }
        private static List<TShockAPI.DB.Region> GetTSRegionsFromLocation(int x, int y)
        {
            List<TShockAPI.DB.Region> returnList = new List<TShockAPI.DB.Region>();
            foreach (TShockAPI.DB.Region region in TShock.Regions.Regions)
            {
                if (region.InArea(x, y))
                    returnList.Add(region);
            }
            return returnList;
        }
        private static Player GetPlayerByUserID(int userid)
        {
            try
            {
                for (int i = 0; i < PlayerList.Length; i++)
                {
                    if (PlayerList[i] != null && PlayerList[i].UserID == userid)
                        return PlayerList[i];
                }
            }
            catch (Exception ex) { Log.ConsoleError(ex.ToString()); }
            return null;
        }
        private static Player GetPlayerByName(string name)
        {
            try
            {
                for (int i = 0; i < PlayerList.Length; i++)
                {
                    if (PlayerList[i] != null && PlayerList[i].TSplayer.Name == name)
                        return PlayerList[i];
                }
            }
            catch (Exception ex) { Log.ConsoleError(ex.ToString()); }
            return null;
        }
        private static bool UnclaimRegion(Region region)
        {
            try
            {
                if (Regions.Remove(region))
                {
                    db.Query("DELETE FROM factions_Regions WHERE ID = @0", region.ID);
                    region.Owner = 0;
                    if (region.Faction != 0)
                    {
                        var faction = GetFactionByID(region.Faction);
                        if (faction != null)
                            faction.Regions.Remove(region);
                    }
                    region.Faction = 0;
                    return true;
                }
            }
            catch (Exception ex) { Log.ConsoleError(ex.ToString()); }
            return false;
        }
        private static bool RectInRegion(Rectangle rect)    
        {
            foreach (TShockAPI.DB.Region region in TShock.Regions.Regions)
            {
                if (region.Area.Intersects(rect))                
                    return true;                
            }
            return false;
        }
        private static bool PlotInRegion(Region plot)
        {
            return RectInRegion(new Rectangle(plot.X, plot.Y, plot.Width, plot.Height));
        }
        private static int CanBuild(Player player, Point point)
        {
            if (player.TSplayer.Group.Name == "superadmin" || player.TSplayer.Group.HasPermission("factions.bypass.regions"))
                return 1;

            var region = GetRegionFromLocation(point.X, point.Y);
            if (region.ID != -1)
            {
                if ((region.Owner != 0 && region.Owner == player.UserID) || (region.Faction != 0 && player.Faction != null && region.Faction == player.Faction.ID))
                    return 1;
                else
                    return -2;
            }
            else
            {
                var tsRegions = GetTSRegionsFromLocation(point.X, point.Y);
                foreach (TShockAPI.DB.Region reg in tsRegions)
                {
                    if (!reg.HasPermissionToBuildInRegion(player.TSplayer))
                        return -2;
                }
                if (tsRegions.Count > 0)
                    return 1;
                else if (point.Y < Main.worldSurface)                
                    return -1;                
                else
                    return 1;
            }
        }
        private static Region AddRegion(Player player)
        {
            int width = 20;
            int height = 20;
            int x = (int)(player.TSplayer.TileX / width) * width;
            int y = (int)(player.TSplayer.TileY / height) * height;
            if (GetRegionFromLocation(player.TSplayer.TileX, player.TSplayer.TileY).ID != -1 || RectInRegion(new Rectangle(x, y, width, height)))
                return null;
            int owner = 0;
            int faction = 0;
            if (player.Faction == null)
                owner = player.UserID;
            else
                faction = player.Faction.ID;

            db.Query("INSERT INTO factions_Regions (X, Y, Owner, Faction, Flags, WorldID) VALUES (@0, @1, @2, @3, @4, @5)", x, y, owner, faction, 0, Main.worldID);
            using (QueryResult reader = db.QueryReader("SELECT ID FROM factions_Regions WHERE X = @0 AND Y = @1 AND WorldID = @2", x, y, Main.worldID))
            {
                if (reader.Read())
                {
                    var newregion = new Region(reader.Get<int>("ID"), x, y, owner, faction);
                    Regions.Add(newregion);
                    if (player.Faction != null)
                        player.Faction.Regions.Add(newregion);
                    return newregion;
                }
            }
            
            return null;
        }
        private static int GetPlotPrice(Player player, Point p)
        {            
            int width = 20;
            int height = 20;
            int x = (int)(p.X / width) * width;
            int y = (int)(p.Y / height) * height;
            if (GetRegionFromLocation(p.X, p.Y).ID != -1 || RectInRegion(new Rectangle(x, y, width, height)))
                return -1;
            
            int baseprice = 20;
            float surmultiply = 1f;
            float apartmultiply = 1f;
            if (player.Faction == null)
            {
                var regions = GetRegionsByUserID(player.UserID);
                if (regions.Count >= 3)
                    return -2;
                if (p.Y <= Main.worldSurface)
                    surmultiply = 1.5f;
                if (!PointNextToAnyRegion(regions, p))
                    apartmultiply = 1.5f;
                return (int)(baseprice * (regions.Count + 1) * surmultiply * apartmultiply);                
            }
            else
            {
                if (player.Faction.Regions.Count >= player.Faction.Members.Count)
                    return -2;
                if (p.Y <= Main.worldSurface)
                    surmultiply = 1.5f;
                if (!PointNextToAnyRegion(player.Faction.Regions, p))
                    apartmultiply = 1.5f;
                //Console.WriteLine("surface: {0}, apart: {1}, membMulti: {2}, reg.count: {3}, mem.count: {4}", surmultiply, apartmultiply, 1 + (float)player.Faction.Members.Count / 5, player.Faction.Regions.Count, player.Faction.Members.Count);
                return (int)(baseprice * (player.Faction.Regions.Count + 1) * surmultiply * apartmultiply * (1 + (float)player.Faction.Members.Count / 5f)); // f(x) = base * region.number * above * apart * (1 + member.count/5)    
            }
        }
        private static bool PointNextToRegion(Region region, Point point)
        {
            //Console.WriteLine("Region: {0},{1}  Point: {2},{3}", region.X, region.Y, point.X, point.Y);
            if (region.X - point.X > 20 || region.X - point.X < -40 || region.Y - point.Y > 20 || region.Y - point.Y < -40)
                return false;
            return true;
        }
        private static bool PointNextToAnyRegion(List<Region> regionList, Point point)
        {
            if (regionList.Count == 0)
                return true;
            foreach (Region region in regionList)
            {
                if (PointNextToRegion(region, point))
                {
                    Console.WriteLine("Its next to it!");
                    return true;

                }
            }
            return false;
        }
    }

}
