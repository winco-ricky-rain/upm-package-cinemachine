using UnityEngine;
using UnityEditor;
using static Unity.Cinemachine.Common.MathHelpers;
using Unity.Mathematics;
using Unity.Entities;
using Cinemachine.Utility;
using Cinemachine.Editor;

namespace Unity.Cinemachine3.Authoring.Editor
{
    [CustomEditor(typeof(CM_VcamComposerProxy))]
    internal class CM_VcamComposerEditor : BaseEditor<CM_VcamComposerProxy>
    {
        CinemachineScreenComposerGuides mScreenGuideEditor;

        protected int TopLevelChannel { get; private set; }

        protected virtual void OnEnable()
        {
            TopLevelChannel = Target.VirtualCamera.FindTopLevelChannel();

            mScreenGuideEditor = new CinemachineScreenComposerGuides();
            mScreenGuideEditor.GetHardGuide = () => { return ToRect(Target.Value.GetHardGuideRect()); };
            mScreenGuideEditor.GetSoftGuide = () => { return ToRect(Target.Value.GetSoftGuideRect()); };
            mScreenGuideEditor.SetHardGuide = (Rect r) =>
            {
                var v = Target.Value;
                v.SetHardGuideRect(FromRect(r));
                Target.Value = v;
                Target.Validate();
            };
            mScreenGuideEditor.SetSoftGuide = (Rect r) =>
            {
                var v = Target.Value;
                v.SetSoftGuideRect(FromRect(r));
                Target.Value = v;
                Target.Validate();
            };
            mScreenGuideEditor.Target = () => { return serializedObject; };

            CinemachineDebug.OnGUIHandlers -= OnGUI;
            CinemachineDebug.OnGUIHandlers += OnGUI;
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        static Rect ToRect(rect2d r) { return new Rect(r.pos + new float2(0.5f, 0.5f), r.size); }
        static rect2d FromRect(Rect r) { return new rect2d { pos = r.position - new Vector2(0.5f, 0.5f), size = r.size }; }

        protected virtual void OnDisable()
        {
            CinemachineDebug.OnGUIHandlers -= OnGUI;
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        public override void OnInspectorGUI()
        {
            BeginInspector();
#if false
            // GML this not working - CM_VcamLookAtTarget is created at runtime
            if (Target.SafeGetComponentData<CM_VcamLookAtTarget>().target == Entity.Null)
                EditorGUILayout.HelpBox(
                    "A LookAt target is required.  Behaviour will be undefined.  Remove this component you don't want a LookAt target.",
                    MessageType.Error);
#endif
            // First snapshot some settings
            Rect oldHard = ToRect(Target.Value.GetHardGuideRect());
            Rect oldSoft = ToRect(Target.Value.GetSoftGuideRect());

            // Draw the properties
            DrawRemainingPropertiesInInspector();
            mScreenGuideEditor.SetNewBounds(
                oldHard, oldSoft,
                ToRect(Target.Value.GetHardGuideRect()),
                ToRect(Target.Value.GetSoftGuideRect()));
        }

        // Oh gawd there has to be a nicer way to do this!
        Texture2D targetMarkerTex = null;
        Texture2D GetTargetMarkerTex()
        {
            if (targetMarkerTex == null)
            {
                const int size = 128;
                const float th = 1f;
                Color[] pix = new Color[size * size];
                Color c = CinemachineSettings.ComposerSettings.TargetColour;
                float radius = size / 2 - th;
                Vector2 center = new Vector2(size-1, size-1) / 2;
                for (int y = 0; y < size; ++y)
                {
                    for (int x = 0; x < size; ++x)
                    {
                        float d = Vector2.Distance(new Vector2(x, y), center);
                        d = Mathf.Abs((d - radius) / th);
                        var a = Mathf.Clamp01(1 - d);
                        pix[y * size + x] = new Color(1, 1, 1, a);
                    }
                }
                targetMarkerTex = new Texture2D(size, size);
                targetMarkerTex.SetPixels(pix);
                targetMarkerTex.Apply();
            }
            return targetMarkerTex;
        }

        protected CM_Brain FindBrain()
        {
            var ch = new ChannelHelper(TopLevelChannel);
            if (ch.HasComponent<CM_Brain>())
                return ch.EntityManager.GetComponentObject<CM_Brain>(ch.Entity);
            return null;
        }

        protected virtual void OnGUI()
        {
            if (Target == null || !Target.enabled)
                return;

            // Draw the camera guides
            var brain = FindBrain();
            if (brain == null || brain.OutputCamera == null || !brain.m_ShowGameViewGuides)
                return;

            // Screen guides
            var vcam = Target.VirtualCamera;
            var state = vcam.State;
            bool isLive = vcam.IsLive;
            mScreenGuideEditor.OnGUI_DrawGuides(isLive, brain.OutputCamera, state.Lens, true);

            // Draw an on-screen gizmo for the target
            if (state.HasLookAt && isLive)
            {
                Vector3 c = brain.OutputCamera.WorldToScreenPoint(state.ReferenceLookAt);
                if (c.z > 0)
                {
                    c.y = Screen.height - c.y;
                    var p2 = state.ReferenceLookAt
                        + state.FinalOrientation * new Vector3(state.ReferenceLookAtRadius, 0, 0);
                    p2 = brain.OutputCamera.WorldToScreenPoint(p2);
                    float radius = Mathf.Abs(p2.x - c.x);
                    Rect r = new Rect(c, Vector3.zero);
                    float minSize = CinemachineScreenComposerGuides.kGuideBarWidthPx;

                    var oldColor = GUI.color;
                    GUI.color = Color.black;
                    GUI.DrawTexture(r.Inflated(new Vector2(minSize, minSize)), Texture2D.whiteTexture, ScaleMode.StretchToFill);
                    var color = CinemachineSettings.ComposerSettings.TargetColour;
                    GUI.color = color;
                    GUI.DrawTexture(r.Inflated(new Vector2(minSize * 0.66f, minSize * 0.66f)), Texture2D.whiteTexture, ScaleMode.StretchToFill);
                    if (radius > minSize)
                    {
                        color.a = Mathf.Lerp(1f, CinemachineSettings.ComposerSettings.OverlayOpacity, (radius - 10f) / 50f);
                        GUI.color = color;
                        GUI.DrawTexture(r.Inflated(new Vector2(radius, radius)), GetTargetMarkerTex(), ScaleMode.StretchToFill);
                    }
                    GUI.color = oldColor;
                }
            }
        }
    }
}
