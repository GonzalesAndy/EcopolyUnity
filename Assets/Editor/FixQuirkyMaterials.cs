using UnityEngine;
using UnityEditor;
using System.IO;

public class FixQuirkyMaterials : EditorWindow
{
    private const string QUIRKY_FOLDER  = "Assets/Quirky Series Ultimate";
    private const string SHADER_NAME    = "Toon/SoftSurface";
    private const float  DEFAULT_COLOR  = 0.4f;
    private const float  DEFAULT_EMISSION = 0.5f;

    [MenuItem("Tools/Fix Quirky Series Materials")]
    static void Run()
    {
        Shader shader = Shader.Find(SHADER_NAME);
        if (shader == null)
        {
            Debug.LogError($"[FixQuirky] Shader '{SHADER_NAME}' introuvable. Vérifie que SoftSurface.shader est bien dans le projet.");
            return;
        }

        string[] matGuids = AssetDatabase.FindAssets("t:Material", new[] { QUIRKY_FOLDER });
        int fixed_ = 0, skipped = 0;

        foreach (string guid in matGuids)
        {
            string matPath = AssetDatabase.GUIDToAssetPath(guid);
            Material mat   = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null) { skipped++; continue; }

            // Nom de la texture attendue : M_Tiger → T_Tiger
            string matName  = Path.GetFileNameWithoutExtension(matPath); // ex: M_Tiger
            string texName  = "T_" + matName.Substring(2);               // ex: T_Tiger

            // Cherche la texture correspondante dans tout le dossier Quirky
            string[] texGuids = AssetDatabase.FindAssets($"{texName} t:Texture2D", new[] { QUIRKY_FOLDER });
            Texture2D tex = null;
            if (texGuids.Length > 0)
                tex = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(texGuids[0]));

            // Récupère la texture déjà assignée si on n'en a pas trouvé par nom
            if (tex == null && mat.HasProperty("_MainTex"))
                tex = mat.GetTexture("_MainTex") as Texture2D;
            if (tex == null && mat.HasProperty("_BaseMap"))
                tex = mat.GetTexture("_BaseMap") as Texture2D;

            // Applique le shader et reconnecte la texture
            mat.shader = shader;

            if (tex != null)
                mat.SetTexture("_MainTex", tex);

            // Remet les valeurs par défaut si elles ont disparu
            if (!mat.HasProperty("_Color") || mat.GetColor("_Color") == Color.black)
                mat.SetColor("_Color", new Color(DEFAULT_COLOR, DEFAULT_COLOR, DEFAULT_COLOR, 1f));
            if (mat.HasProperty("_Emission"))
                mat.SetFloat("_Emission", DEFAULT_EMISSION);

            EditorUtility.SetDirty(mat);
            fixed_++;
            Debug.Log($"[FixQuirky] {matName} → shader assigné, texture : {(tex != null ? tex.name : "non trouvée")}");
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[FixQuirky] Terminé — {fixed_} matériaux corrigés, {skipped} ignorés.");
        EditorUtility.DisplayDialog("Fix Quirky Materials", $"{fixed_} matériaux corrigés.", "OK");
    }
}
