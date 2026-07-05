using UnityEngine;

namespace FpsBase
{
    /// <summary>
    /// Floating billboard name above other players' heads, tinted by team color.
    /// Uses the built-in legacy TextMesh so no font assets are needed.
    /// </summary>
    public class Nametag : MonoBehaviour
    {
        private TextMesh textMesh;

        public static Nametag Create(Transform parent, string text, Color color)
        {
            var go = new GameObject("Nametag");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0, 2.15f, 0);

            var tag = go.AddComponent<Nametag>();
            tag.textMesh = go.AddComponent<TextMesh>();
            tag.textMesh.anchor = TextAnchor.MiddleCenter;
            tag.textMesh.alignment = TextAlignment.Center;
            tag.textMesh.characterSize = 0.06f;
            tag.textMesh.fontSize = 60;
            tag.textMesh.fontStyle = FontStyle.Bold;

            var font = GetBuiltinFont();
            if (font != null)
            {
                tag.textMesh.font = font;
                go.GetComponent<MeshRenderer>().material = font.material;
            }

            tag.SetText(text);
            tag.SetColor(color);
            return tag;
        }

        public void SetText(string text) => textMesh.text = string.IsNullOrEmpty(text) ? "Player" : text;

        public void SetColor(Color color) => textMesh.color = color;

        private void LateUpdate()
        {
            // Face the active camera.
            var cam = Camera.main;
            if (cam != null)
                transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
        }

        private static Font GetBuiltinFont()
        {
            try
            {
                return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); // Unity 2022.2+
            }
            catch
            {
                try
                {
                    return Resources.GetBuiltinResource<Font>("Arial.ttf"); // older Unity
                }
                catch
                {
                    return null;
                }
            }
        }
    }
}
