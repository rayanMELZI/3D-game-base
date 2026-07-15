using UnityEditor;
using UnityEngine;

namespace FpsBase.Editor
{
    [InitializeOnLoad]
    public static class RoadmapAssetSetup
    {
        private const string Destination = "Assets/Resources/Weapons/Attachments";
        static RoadmapAssetSetup() => EditorApplication.delayCall += EnsureAssets;

        [MenuItem("Tools/FPS Base/Prepare Roadmap Assets")]
        public static void EnsureAssets()
        {
            EnsureFolder("Assets/Resources/Weapons", "Attachments");
            Copy("Assets/Low Poly Weapon Pack 4_MW_1/Prefabs/Attachments/Optics/Optics/Optic_AQ.prefab", "Optic.prefab");
            Copy("Assets/Low Poly Weapon Pack 4_MW_1/Prefabs/Attachments/Barrels/Muzzles/Muzzle_Suppressor_L.prefab", "Suppressor.prefab");
            Copy("Assets/Low Poly Weapon Pack 4_MW_1/Prefabs/Previous/Attachments/UnderBarrels/ForwardGrips/ForwardGrip_A.prefab", "Foregrip.prefab");
            Copy("Assets/Low Poly Weapon Pack 4_MW_1/Prefabs/Attachments/Mags/Mag_A.prefab", "ExtendedMag.prefab");
            AssetDatabase.SaveAssets();
        }

        private static void EnsureFolder(string parent, string name)
        {
            string path = parent + "/" + name;
            if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, name);
        }

        private static void Copy(string source, string file)
        {
            string destination = Destination + "/" + file;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(destination) == null)
                AssetDatabase.CopyAsset(source, destination);
        }
    }
}
