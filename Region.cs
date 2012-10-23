using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TShockAPI;
namespace Factions
{
    public class Region
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

        public static Region GetRegionByID(int id)
        {
            foreach (Region reg in Factions.Regions)
            {
                if (reg.ID == id)
                    return reg;
            }
            return new Region(-1, 0, 0);
        }
        public static List<Region> GetRegionsByUserID(int userid)
        {
            List<Region> ReturnList = new List<Region>();
            foreach (Region reg in Factions.Regions)
            {
                if (reg.Owner != -1 && reg.Owner == userid)
                    ReturnList.Add(reg);
            }
            return ReturnList;
        }
        public static Region GetRegionFromLocation(int x, int y)
        {
            foreach (Region reg in Factions.Regions)
            {
                if (reg.InArea(x, y))
                    return reg;
            }
            var newregion = new Region(-1, x, y);
            newregion.X = (int)(newregion.X / newregion.Width) * newregion.Width;
            newregion.Y = (int)(newregion.Y / newregion.Height) * newregion.Height;
            return newregion;
        }
        public static List<TShockAPI.DB.Region> GetTSRegionsFromLocation(int x, int y)
        {
            List<TShockAPI.DB.Region> returnList = new List<TShockAPI.DB.Region>();
            foreach (TShockAPI.DB.Region region in TShock.Regions.Regions)
            {
                if (region.InArea(x, y))
                    returnList.Add(region);
            }
            return returnList;
        }
    }
}
