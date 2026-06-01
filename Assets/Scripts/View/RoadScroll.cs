// Drives the road shader's view-relative scroll. Sets two global shader values
// each frame: how far the texture has scrolled (from rider speed x multiplier)
// and the camera's forward (so the shader can keep every lane flowing toward
// the viewer). One of these in the scene covers all NightRider/Road materials.

using UnityEngine;
using NightRider.World;

namespace NightRider.View
{
    public class RoadScroll : MonoBehaviour
    {
        [Tooltip("Rider whose speed drives the scroll.")]
        public LaneFollower rider;
        [Tooltip("Viewpoint the road should flow toward. Defaults to the main camera.")]
        public Transform view;

        [Min(0f), Tooltip("Extra apparent speed on top of real motion — the vintage " +
                          "'road moves a touch too fast' desync. 0 = off.")]
        public float scrollMultiplier = 0.4f;

        float _scroll;
        static readonly int ScrollId = Shader.PropertyToID("_RoadScroll");
        static readonly int FlowId   = Shader.PropertyToID("_RoadFlowDir");

        void OnEnable()
        {
            if (view == null && Camera.main != null) view = Camera.main.transform;
        }

        void Update()
        {
            float speed = rider != null ? rider.speed : 0f;
            _scroll += speed * scrollMultiplier * Time.deltaTime;

            Shader.SetGlobalFloat(ScrollId, _scroll);
            Shader.SetGlobalVector(FlowId, view != null ? view.forward : Vector3.forward);
        }
    }
}
