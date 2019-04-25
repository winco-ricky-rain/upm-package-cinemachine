using UnityEngine;
using UnityEditor;

namespace Unity.Cinemachine33.Authoring.Editor
{
    [InitializeOnLoad]
    internal sealed class EditorGlue
    {
        private static Texture2D sCinemachineLogoTexture = null;

        internal static Texture2D CinemachineLogoTexture
        {
            get
            {
                if (sCinemachineLogoTexture == null)
                    sCinemachineLogoTexture = Resources.Load<Texture2D>("cm_logo_sm");
                if (sCinemachineLogoTexture != null)
                    sCinemachineLogoTexture.hideFlags = HideFlags.DontSaveInEditor;
                return sCinemachineLogoTexture;
            }
        }

        private static void OnHierarchyGUI(int instanceID, Rect selectionRect)
        {
            GameObject instance = EditorUtility.InstanceIDToObject(instanceID) as GameObject;
            if (instance == null)
            {
                // Object in process of being deleted?
                return;
            }

            if (instance.GetComponent<Unity.Cinemachine3.Authoring.CM_Brain>() != null)
            {
                Rect texRect = new Rect(
                    selectionRect.xMax - selectionRect.height, selectionRect.yMin,
                    selectionRect.height, selectionRect.height);
                GUI.DrawTexture(texRect, CinemachineLogoTexture, ScaleMode.ScaleAndCrop);
            }
        }

        static EditorGlue()
        {
            if (CinemachineLogoTexture != null)
            {
                EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyGUI;
            }
        }
    }
}
