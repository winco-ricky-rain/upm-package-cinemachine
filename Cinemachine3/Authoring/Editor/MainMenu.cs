using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using Unity.Cinemachine3.Authoring;

namespace Unity.Cinemachine33.Authoring.Editor
{
    internal static class MainMenu
    {
        public const string kCinemachineRootMenu = "Assets/Create/Cinemachine/";

        [MenuItem("Cinemachine/Create Vcam", false, 1)]
        public static CM_Vcam CM_CreateVcam()
        {
            return CM_InternalCreateVcam(
                "CM vcam", true, typeof(CM_VcamComposerProxy), typeof(CM_VcamTransposerProxy));
        }

        [MenuItem("Cinemachine/Create FreeLook", false, 1)]
        private static void CM_CreateFreeLook()
        {
            CM_CreateBrainOnCameraIfAbsent();
            GameObject go = ObjectFactory.CreateGameObject(
                    GenerateUniqueObjectName(typeof(CM_BasicFreeLook), "CM FreeLook"),
                    typeof(CM_BasicFreeLook), typeof(CM_VcamComposerProxy), typeof(CM_VcamOrbitalProxy));
            if (SceneView.lastActiveSceneView != null)
                go.transform.position = SceneView.lastActiveSceneView.pivot;
            Undo.RegisterCreatedObjectUndo(go, "create FreeLook camera");
            Selection.activeGameObject = go;
        }

        [MenuItem("Cinemachine/Create ClearShot Vcam", false, 1)]
        private static void CM_CreateClearShotVcam()
        {
            CM_CreateBrainOnCameraIfAbsent();
            GameObject go = ObjectFactory.CreateGameObject(
                    GenerateUniqueObjectName(typeof(CM_ClearShot), "CM ClearShot"),
                    typeof(CM_ClearShot));
            if (SceneView.lastActiveSceneView != null)
                go.transform.position = SceneView.lastActiveSceneView.pivot;
            Undo.RegisterCreatedObjectUndo(go, "create ClearShot camera");
            Selection.activeGameObject = go;
        }

        /// <summary>
        /// Create a Virtual Camera, with dots components
        /// </summary>
        static CM_Vcam CM_InternalCreateVcam(string name, bool selectIt, params Type[] components)
        {
            // Create a new vcam
            CM_CreateBrainOnCameraIfAbsent();
            GameObject go = ObjectFactory.CreateGameObject(
                    GenerateUniqueObjectName(typeof(CM_Vcam), name),
                    typeof(CM_Vcam));
            if (SceneView.lastActiveSceneView != null)
                go.transform.position = SceneView.lastActiveSceneView.pivot;
            Undo.RegisterCreatedObjectUndo(go, "create " + name);
            CM_Vcam vcam = go.GetComponent<CM_Vcam>();
            foreach (Type t in components)
                Undo.AddComponent(go, t);
            if (selectIt)
                Selection.activeObject = go;
            return vcam;
        }

        /// <summary>
        /// If there is no CinemachineBrain in the scene, try to create one on the main camera
        /// </summary>
        static void CM_CreateBrainOnCameraIfAbsent()
        {
            CM_Brain[] brains = UnityEngine.Object.FindObjectsOfType(typeof(CM_Brain)) as CM_Brain[];
            if (brains == null || brains.Length == 0)
            {
                Camera cam = Camera.main;
                if (cam == null)
                {
                    Camera[] cams = UnityEngine.Object.FindObjectsOfType(
                            typeof(Camera)) as Camera[];
                    if (cams != null && cams.Length > 0)
                        cam = cams[0];
                }
                if (cam != null)
                    Undo.AddComponent<CM_Brain>(cam.gameObject);
            }
        }

        /// <summary>
        /// Generate a unique name with the given prefix by adding a suffix to it
        /// </summary>
        public static string GenerateUniqueObjectName(Type type, string prefix)
        {
            int count = 0;
            UnityEngine.Object[] all = Resources.FindObjectsOfTypeAll(type);
            foreach (UnityEngine.Object o in all)
            {
                if (o != null && o.name.StartsWith(prefix))
                {
                    string suffix = o.name.Substring(prefix.Length);
                    int i;
                    if (Int32.TryParse(suffix, out i) && i > count)
                        count = i;
                }
            }
            return prefix + (count + 1);
        }
    }
}
