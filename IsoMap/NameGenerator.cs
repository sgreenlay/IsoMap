using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IsoMap
{
    internal class NameGenerator
    {
        private static List<string> Syllables = new List<string>(){
                "ga","ka","sa","ta","na","ha","ma","ya","ra","wa",
                "ge","ke","se","te","ne","he","me",/* */"re",/* */
                "gi","ki","si","chi","ni","hi","mi",/* */"ri",/* */
                "go","ko","so","to","no","ho","mo","yo","ro","wo",
                "gu","ku","su","tsu","nu","hu","mu","yu","ru",
            };
        private static Random Rand = new Random();
        private static string randSyl()
        {
            return Syllables[Rand.Next(Syllables.Count)];
        }

        internal static string randName()
        {
            string name = "";
            for (var x = 0; x < 3; ++x)
            {
                name += randSyl();
            }
            name = char.ToUpper(name[0]) + name.Substring(1);
            return name;
        }
    }
}
