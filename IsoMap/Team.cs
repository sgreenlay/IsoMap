using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IsoMap
{
    public class Team
    {
        public enum TeamID
        {
            TeamA,
            TeamB
        };

        public List<Index<Units>> units = new List<Index<Units>>();
        public TeamID team;

        public void Remove(Index<Units> idx)
        {
            units.Remove(idx);
        }
        public void RemoveAt(Index<Team> i)
        {
            units.RemoveAt(i);
        }

        public void Clear()
        {
            units.Clear();
        }
        public int Count { get { return units.Count; } }
        public Index<Units> this[Index<Team> i] { get { return units[i]; } }

        public Team(TeamID t)
        {
            team = t;
        }
    }
}
