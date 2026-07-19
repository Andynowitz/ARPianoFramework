using UnityEngine;
using ARPIANO.Scripts.Models;

namespace ARPIANO.Scripts.Piano
{
    public static class PianoPrefabLoader
    {
        public static PianoKey LoadWhiteKey()
        {
            return Resources.Load<PianoKey>("Prefabs/WhiteKey");
        }

        public static PianoKey LoadBlackKey()
        {
            return Resources.Load<PianoKey>("Prefabs/BlackKey");
        }
    }
}
