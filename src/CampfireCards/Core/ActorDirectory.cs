using System.Collections.Generic;

namespace CampfireCards.Core
{
    /// <summary>
    /// Every (actor number, published user id) pair seen this run. Photon hands a rejoiner a NEW actor number, so this
    /// map is what links them back to the state their departed actor left behind. Every client records the roster
    /// independently (a promoted host then already has it); entries are never removed on leave - that is the point -
    /// and the map restarts with the run. Unity-free.
    /// </summary>
    public sealed class ActorDirectory
    {
        private readonly Dictionary<int, string> _userIds = new Dictionary<int, string>();

        /// <summary>A usable published id. "" and "0" mean unknown - the game's own validity rule for user ids.</summary>
        public static bool ValidId(string userId) => !string.IsNullOrEmpty(userId) && userId != "0";

        public void Record(int actor, string userId)
        {
            if (ValidId(userId)) _userIds[actor] = userId;
        }

        public string UserIdOf(int actor)
        {
            string id;
            return _userIds.TryGetValue(actor, out id) ? id : null;
        }

        /// <summary>
        /// Departed actors that used this player's account: same user id, no longer in the room. Oldest first, so a
        /// leave-rejoin-leave-rejoin chain hands state forward one hop at a time.
        /// </summary>
        public List<int> DepartedTwins(int actor, string userId, ICollection<int> actorsInRoom)
        {
            var twins = new List<int>();
            if (!ValidId(userId)) return twins;
            foreach (var kv in _userIds)
                if (kv.Key != actor && kv.Value == userId && !actorsInRoom.Contains(kv.Key))
                    twins.Add(kv.Key);
            twins.Sort();
            return twins;
        }

        public void Reset() => _userIds.Clear();
    }
}
