using System;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(KimodoBVHLoader))]
public class KimodoBVHLoaderEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        KimodoBVHLoader loader = (KimodoBVHLoader)target;

        GUILayout.Space(8);
        // LEGACY: BVH preview controls stay local/direct by design.
        // Do not route through KimodoEditorCommandManager.
        if (GUILayout.Button(new GUIContent("Build Preview From BVH", "Parse the configured BVH file and build a temporary preview rig + animation clip in the scene.")))
        {
            try
            {
                loader.BuildPreviewFromFile();
                Debug.Log("[KimodoBVHLoader] Preview built successfully.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[KimodoBVHLoader] Build failed: " + ex.Message + "\n" + ex);
            }
        }

        if (GUILayout.Button(new GUIContent("Play", "Play the built preview animation on the temporary preview object.")))
        {
            if (loader.builtRoot != null)
            {
                Animation anim = loader.builtRoot.GetComponent<Animation>();
                if (anim != null && anim.clip != null)
                {
                    anim.Play(anim.clip.name);
                }
            }
        }

        if (GUILayout.Button(new GUIContent("Stop", "Stop preview playback on the temporary preview object.")))
        {
            if (loader.builtRoot != null)
            {
                Animation anim = loader.builtRoot.GetComponent<Animation>();
                if (anim != null)
                {
                    anim.Stop();
                }
            }
        }

        if (GUILayout.Button(new GUIContent("Clear Preview", "Remove the temporary preview object and clip references created by this inspector.")))
        {
            if (loader.builtRoot != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(loader.builtRoot);
                }
                else
                {
                    DestroyImmediate(loader.builtRoot);
                }
                loader.builtRoot = null;
                loader.builtClip = null;
            }
        }
    }
}
