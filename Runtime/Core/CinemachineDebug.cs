using UnityEngine;
using System.Collections.Generic;
using System.Text;
using Cinemachine.ECS;

namespace Cinemachine.Utility
{
    /// <summary>Manages onscreen positions for Cinemachine debugging output</summary>
    public class CinemachineDebug
    {
        static HashSet<Object> mClients;

        /// <summary>Release a screen rectangle previously obtained through GetScreenPos()</summary>
        /// <param name="client">The client caller.  Used as a handle.</param>
        public static void ReleaseScreenPos(Object client)
        {
            if (mClients != null && mClients.Contains(client))
                mClients.Remove(client);
        }

        /// <summary>Reserve an on-screen rectangle for debugging output.</summary>
        /// <param name="client">The client caller.  This is used as a handle.</param>
        /// <param name="text">Sample text, for determining rectangle size</param>
        /// <param name="style">What style will be used to draw, used here for
        /// determining rect size</param>
        /// <returns>An area on the game screen large enough to print the text
        /// in the style indicated</returns>
        public static Rect GetScreenPos(Object client, string text, GUIStyle style)
        {
            if (mClients == null)
                mClients = new HashSet<Object>();
            if (!mClients.Contains(client))
                mClients.Add(client);

            Vector2 pos = new Vector2(0, 0);
            Vector2 size = style.CalcSize(new GUIContent(text));
            if (mClients != null)
            {
                foreach (var c in mClients)
                {
                    if (c == client)
                        break;
                    pos.y += size.y;
                }
            }
            return new Rect(pos, size);
        }

        /// <summary>
        /// Delegate for OnGUI debugging.
        /// This will be called by the CinemachineBrain in its OnGUI (editor only)
        /// </summary>
        public delegate void OnGUIDelegate();

        /// <summary>
        /// Delegate for OnGUI debugging.
        /// This will be called by the CinemachineBrain in its OnGUI (editor only)
        /// </summary>
        public static OnGUIDelegate OnGUIHandlers;

        private static List<StringBuilder> mAvailableStringBuilders;

        /// <summary>Get a preallocated StringBuilder from the pool</summary>
        public static StringBuilder SBFromPool()
        {
            if (mAvailableStringBuilders == null || mAvailableStringBuilders.Count == 0)
                return new StringBuilder();
            var sb = mAvailableStringBuilders[mAvailableStringBuilders.Count - 1];
            mAvailableStringBuilders.RemoveAt(mAvailableStringBuilders.Count - 1);
            sb.Length = 0;
            return sb;
        }

        /// <summary>Return a StringBuilder to the preallocated pool</summary>
        public static void ReturnToPool(StringBuilder sb)
        {
            if (mAvailableStringBuilders == null)
                mAvailableStringBuilders = new List<StringBuilder>();
            mAvailableStringBuilders.Add(sb);
        }
    }

    public static class DebugHelpers
    {
        /// <summary>Text description of a blend, for debugging</summary>
        public static string Description(this CinemachineBlend blend)
        {
            var sb = CinemachineDebug.SBFromPool();
            if (blend.CamB == null || !blend.CamB.IsValid)
                sb.Append("(none)");
            else
            {
                sb.Append("[");
                sb.Append(blend.CamB.Name);
                sb.Append("]");
            }
            sb.Append(" ");
            sb.Append((int)(blend.BlendWeight * 100f));
            sb.Append("% from ");
            if (blend.CamA == null || !blend.CamA.IsValid)
                sb.Append("(none)");
            else
            {
                sb.Append("[");
                sb.Append(blend.CamA.Name);
                sb.Append("]");
            }
            string text = sb.ToString();
            CinemachineDebug.ReturnToPool(sb);
            return text;
        }

        /// <summary>Text description of a blend, for debugging</summary>
        public static string Description(this CM_BlendState blend)
        {
            var sb = CinemachineDebug.SBFromPool();
            var cam = CM_EntityVcam.GetEntityVcam(blend.cam);
            if (cam == null || !cam.IsValid)
                sb.Append("(none)");
            else
            {
                sb.Append("[");
                sb.Append(cam.Name);
                sb.Append("]");
            }
            sb.Append(" ");
            sb.Append((int)(blend.weight * 100f));
            sb.Append("% from ");
            var outgoingCam = CM_EntityVcam.GetEntityVcam(blend.outgoingCam);
            if (outgoingCam == null || !outgoingCam.IsValid)
                sb.Append("(none)");
            else
            {
                sb.Append("[");
                sb.Append(outgoingCam.Name);
                sb.Append("]");
            }
            string text = sb.ToString();
            CinemachineDebug.ReturnToPool(sb);
            return text;
        }
    }
}
