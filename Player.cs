using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TShockAPI;
using System.Threading;
using ChatAssistant;

namespace Factions
{
    public class Player
    {
        public int ID;
        public int UserID;
        public Faction Faction;
        public TSPlayer TSplayer;
        public int Power;
        public bool tempOutline = false;
        public Menu Menu = null;
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

            Factions.DoQuery("UPDATE factions_Players SET Power = @0 WHERE UserID = @1 AND WorldID = @2", newpower, this.UserID, Terraria.Main.worldID);
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

        //  ---------------------------------------  STATIC METHODS ---------------------------------------------------
        public static Player GetPlayerByUserID(int userid)
        {
            try
            {
                for (int i = 0; i < Factions.PlayerList.Length; i++)
                {
                    if (Factions.PlayerList[i] != null && Factions.PlayerList[i].UserID == userid)
                        return Factions.PlayerList[i];
                }
            }
            catch (Exception ex) { Log.ConsoleError(ex.ToString()); }
            return null;
        }
        public static Player GetPlayerByName(string name)
        {
            try
            {
                for (int i = 0; i < Factions.PlayerList.Length; i++)
                {
                    if (Factions.PlayerList[i] != null && Factions.PlayerList[i].TSplayer.Name == name)
                        return Factions.PlayerList[i];
                }
            }
            catch (Exception ex) { Log.ConsoleError(ex.ToString()); }
            return null;
        }


        // -------------------------------- UPDATER ----------------------------------------------------------
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
                    var player = Factions.PlayerList[who];
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
}
