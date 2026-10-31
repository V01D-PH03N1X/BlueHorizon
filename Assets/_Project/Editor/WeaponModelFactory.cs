using BlueHorizon.Weapons;
using UnityEditor;
using UnityEngine;

namespace BlueHorizon.EditorTools
{
    /// <summary>
    /// Builds a weapon prefab from the files exported from Arsenal_Skinnable.blend
    /// (Art/Weapons/&lt;name&gt;: Weapon_&lt;name&gt;.fbx, T_&lt;name&gt;_BC.png, T_&lt;name&gt;_MS.png).
    /// T_*_MS holds metallic in RGB and smoothness in alpha, the layout URP Lit reads.
    /// </summary>
    public static class WeaponModelFactory
    {
        const string Art = ProjectSetup.Root + "/Art/Weapons";
        static readonly string[] Clips = { WeaponModel.IdleClip, WeaponModel.FireClip, WeaponModel.ReloadClip, WeaponModel.EquipClip };

        /// <summary>The FBX barrel points along -Z; the game expects it along +Z.</summary>
        static readonly Quaternion ModelRotation = Quaternion.Euler(0f, 180f, 0f);

        public static WeaponModel Build(string name, bool builtInOptic = false)
        {
            string folder = $"{Art}/{name}";
            string fbxPath = $"{folder}/Weapon_{name}.fbx";
            ConfigureModel(fbxPath);
            Material skin = CreateSkin(folder, name);
            Material glass = builtInOptic ? CreateGlass() : null;

            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (fbx == null) throw new System.IO.FileNotFoundException("Weapon model missing", fbxPath);

            var root = new GameObject($"Weapon_{name}");
            try
            {
                var mesh = (GameObject)PrefabUtility.InstantiatePrefab(fbx, root.transform);
                mesh.transform.localPosition = Vector3.zero;
                mesh.transform.localRotation = ModelRotation;

                foreach (Renderer r in mesh.GetComponentsInChildren<Renderer>())
                {
                    var materials = new Material[r.sharedMaterials.Length];
                    for (int i = 0; i < materials.Length; i++) materials[i] = i == 0 ? skin : glass;
                    r.sharedMaterials = materials;
                }

                var model = root.AddComponent<WeaponModel>();
                model.builtInOptic = builtInOptic;
                model.clips = mesh.GetComponent<Animation>();
                // The imported default pose is the first frame of a take; anchors are measured at rest.
                model.clips.GetClip(WeaponModel.IdleClip).SampleAnimation(mesh, 0f);
                Vector3 muzzle = Bone(mesh.transform, "MuzzlePoint", root.transform);
                Vector3 sight = Bone(mesh.transform, "Reflex", root.transform);
                model.mount = CreateMount(FindBone(mesh.transform, "Root"), root.transform);
                model.muzzle = Anchor(model.mount, "Muzzle", muzzle);
                model.sight = Anchor(model.mount, "Sight", sight);
                model.underbarrel = Anchor(model.mount, "Underbarrel", new Vector3(0f, muzzle.y - 0.045f, muzzle.z * 0.6f));

                string path = $"{folder}/Weapon_{name}.prefab";
                return PrefabUtility.SaveAsPrefabAsset(root, path).GetComponent<WeaponModel>();
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        static Transform FindBone(Transform search, string bone)
        {
            foreach (Transform t in search.GetComponentsInChildren<Transform>())
            {
                if (t.name == bone) return t;
            }
            throw new System.InvalidOperationException($"{search.name} has no bone '{bone}'");
        }

        static Vector3 Bone(Transform search, string bone, Transform space) => space.InverseTransformPoint(FindBone(search, bone).position);

        /// <summary>A child of the bone that sits exactly on the prefab root at rest.</summary>
        static Transform CreateMount(Transform bone, Transform root)
        {
            var mount = new GameObject("Mount").transform;
            mount.SetParent(bone, false);
            mount.SetPositionAndRotation(root.position, root.rotation);
            Vector3 scale = bone.lossyScale;
            mount.localScale = new Vector3(1f / scale.x, 1f / scale.y, 1f / scale.z);
            return mount;
        }

        static Transform Anchor(Transform parent, string name, Vector3 position)
        {
            var anchor = new GameObject(name).transform;
            anchor.SetParent(parent, false);
            anchor.localPosition = position;
            return anchor;
        }

        static void ConfigureModel(string path)
        {
            if (AssetImporter.GetAtPath(path) is not ModelImporter importer) return;
            if (importer.animationType == ModelImporterAnimationType.Legacy && importer.clipAnimations.Length == Clips.Length) return;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            importer.animationType = ModelImporterAnimationType.Legacy;
            importer.importAnimation = true;
            importer.SaveAndReimport();

            // The FBX takes are named after the Blender actions (A_PISTOL_Fire); the game asks for "Fire".
            var clips = new ModelImporterClipAnimation[Clips.Length];
            ModelImporterClipAnimation[] takes = importer.defaultClipAnimations;
            for (int i = 0; i < Clips.Length; i++)
            {
                string suffix = "_" + Clips[i];
                clips[i] = System.Array.Find(takes, t => t.takeName.EndsWith(suffix))
                           ?? throw new System.InvalidOperationException($"{path} has no {Clips[i]} take");
                clips[i].name = Clips[i];
                clips[i].wrapMode = Clips[i] == WeaponModel.IdleClip ? WrapMode.Loop : WrapMode.Once;
            }
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
        }

        static Texture2D LoadTexture(string path, bool srgb)
        {
            if (AssetImporter.GetAtPath(path) is TextureImporter importer && importer.sRGBTexture != srgb)
            {
                importer.sRGBTexture = srgb;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Material LoadOrCreate(string path)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static Material CreateSkin(string folder, string name)
        {
            Material m = LoadOrCreate($"{folder}/M_Weapon_{name}.mat");
            m.SetColor("_BaseColor", Color.white);
            m.SetTexture("_BaseMap", LoadTexture($"{folder}/T_{name}_BC.png", true));
            m.SetTexture("_MetallicGlossMap", LoadTexture($"{folder}/T_{name}_MS.png", false));
            m.SetFloat("_Smoothness", 1f);
            m.EnableKeyword("_METALLICSPECGLOSSMAP");
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material CreateGlass()
        {
            Material m = LoadOrCreate($"{Art}/M_Weapon_Glass.mat");
            m.SetColor("_BaseColor", new Color(0.01f, 0.023f, 0.033f));
            m.SetFloat("_Smoothness", 0.98f);
            m.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
