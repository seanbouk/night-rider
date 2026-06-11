// Scene-view editor for TradingPost. A post lives at lane + t (not at its
// GameObject's transform), so the normal move tool is useless for it. This puts a
// position handle at the post's REAL spot and, as you drag, projects the dragged
// point back onto the lane to set t — so you just slide the post along the road.
//
// Assign the post's Lane first; then drag the handle. Switch lanes by reassigning
// the Lane field.

using UnityEngine;
using UnityEditor;
using NightRider.World;

namespace NightRider.EditorTools
{
    [CustomEditor(typeof(TradingPost))]
    public class TradingPostEditor : Editor
    {
        void OnSceneGUI()
        {
            var post = (TradingPost)target;
            if (post.lane == null || !post.lane.IsValid) return;

            float tt = Mathf.Clamp01(post.t);
            post.lane.EvaluateWorld(tt, out var pos, out _, out _);
            Vector3 handlePos = pos + Vector3.up * post.heightOffset;

            Handles.color = post.ghostColor;
            Handles.Label(handlePos + Vector3.up * 1.6f, $"{post.postName}\n(drag to move along the road)");

            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.PositionHandle(handlePos, Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                // Snap the dragged point back onto the lane -> new t.
                float newT = post.lane.ProjectWorldPoint(moved, out _);
                Undo.RecordObject(post, "Move Trading Post");
                post.t = post.lane.Closed ? Mathf.Repeat(newT, 1f) : Mathf.Clamp01(newT);
                EditorUtility.SetDirty(post);
            }
        }
    }
}
