using UnityEngine;

namespace BIS
{
    public partial class MenuUI
    {
        void ConquestSetup() { Title("Galactic Conquest"); if (UI.Button(new Rect(UI.W / 2 - 160, 900, 320, 64), "BACK")) screen = Screen.Main; }
        void ConquestMapScreen() { ConquestSetup(); }
    }
}
