using UnityEngine;

namespace FpsBase
{
    /// <summary>
    /// Shared IMGUI styling for all menus: dark panels, big accent buttons with
    /// hover states and click sounds, sliders and toggles. Everything is
    /// generated (1x1 textures), no assets required.
    /// </summary>
    public static class MenuWidgets
    {
        public static readonly Color Accent = new Color(1f, 0.62f, 0.22f); // sundown orange

        public static GUIStyle Title { get; private set; }
        public static GUIStyle Subtitle { get; private set; }
        public static GUIStyle Label { get; private set; }
        public static GUIStyle Small { get; private set; }
        public static GUIStyle Button { get; private set; }
        public static GUIStyle Input { get; private set; }

        private static Texture2D panelTex;
        private static bool ready;

        /// <summary>Must be called at the start of OnGUI before using any widget.</summary>
        public static void EnsureStyles()
        {
            if (ready)
                return;
            ready = true;

            panelTex = MakeTex(new Color(0.05f, 0.06f, 0.09f, 0.88f));

            Title = new GUIStyle(GUI.skin.label)
            {
                fontSize = 40, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
                clipping = TextClipping.Overflow, // never crop the game title
                wordWrap = false,
            };
            Title.normal.textColor = Accent;

            Subtitle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14, alignment = TextAnchor.MiddleCenter,
            };
            Subtitle.normal.textColor = new Color(1f, 1f, 1f, 0.55f);

            Label = new GUIStyle(GUI.skin.label) { fontSize = 15 };
            Label.normal.textColor = new Color(1f, 1f, 1f, 0.9f);

            Small = new GUIStyle(GUI.skin.label) { fontSize = 12 };
            Small.normal.textColor = new Color(1f, 1f, 1f, 0.5f);

            Button = new GUIStyle(GUI.skin.button)
            {
                fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
            };
            Button.normal.background = MakeTex(new Color(0.13f, 0.15f, 0.2f, 0.95f));
            Button.normal.textColor = new Color(1f, 1f, 1f, 0.9f);
            Button.hover.background = MakeTex(new Color(Accent.r, Accent.g, Accent.b, 0.9f));
            Button.hover.textColor = Color.black;
            Button.active.background = MakeTex(new Color(Accent.r * 0.8f, Accent.g * 0.8f, Accent.b * 0.8f, 1f));
            Button.active.textColor = Color.black;

            Input = new GUIStyle(GUI.skin.textField)
            {
                fontSize = 15, alignment = TextAnchor.MiddleLeft,
            };
        }

        /// <summary>Dark backdrop panel.</summary>
        public static void Panel(Rect rect)
        {
            GUI.color = Color.white;
            GUI.DrawTexture(rect, panelTex);
            // Accent line along the top edge.
            GUI.color = Accent;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 3), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        /// <summary>Styled button with a click sound.</summary>
        public static bool MenuButton(string text, float height = 42f)
        {
            if (GUILayout.Button(text, Button, GUILayout.Height(height)))
            {
                SfxSynth.Play2D(SfxSynth.UiClick(), 0.8f);
                return true;
            }
            return false;
        }

        /// <summary>Label + slider + value readout on one line.</summary>
        public static float Slider(string label, float value, float min, float max, string format = "0.0")
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, Label, GUILayout.Width(140));
            float result = GUILayout.HorizontalSlider(value, min, max, GUILayout.Height(22));
            GUILayout.Label(result.ToString(format), Label, GUILayout.Width(50));
            GUILayout.EndHorizontal();
            return result;
        }

        private static Texture2D MakeTex(Color color)
        {
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, color);
            tex.Apply();
            return tex;
        }
    }
}
