using UnityEditor;
using UnityEngine;

namespace FpsBase.EditorTools
{
    /// <summary>
    /// Convenience menu: if you ever create a brand-new scene, use
    /// "Tools > FPS Base > Add Game Bootstrap" to make it playable instantly.
    /// </summary>
    public static class FpsBaseMenu
    {
        [MenuItem("Tools/FPS Base/Add Game Bootstrap")]
        public static void AddBootstrap()
        {
            if (Object.FindFirstObjectByType<GameBootstrap>() != null)
            {
                Debug.Log("This scene already has a GameBootstrap.");
                return;
            }

            var go = new GameObject("GameBootstrap");
            go.AddComponent<GameBootstrap>();
            Undo.RegisterCreatedObjectUndo(go, "Add Game Bootstrap");
            Selection.activeGameObject = go;
            Debug.Log("GameBootstrap added — press Play to build and run the game.");
        }
    }
}
