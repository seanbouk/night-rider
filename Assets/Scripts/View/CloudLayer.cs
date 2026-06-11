// Carousel cloud band. Builds a frustum-filling quad in front of the sky (and
// behind the world) and drives NightRider/Cloud, which maps the cloud strip
// cylindrically by the camera's yaw — so the clouds scroll as you turn at the
// speed of an infinitely-distant backdrop. The clouds add over the sky gradient
// and snap to NES (see Cloud.shader). The strip wraps horizontally; it needn't
// line up perfectly after a full turn.
//
// Add to the Main Camera (alongside SkyBackground). Assign the cloud strip
// texture; set its wrap mode to Repeat (horizontal) so the carousel loops.

using UnityEngine;

namespace NightRider.View
{
    // Runs after ChaseCamera (default order 0) so we read the camera's FINAL pose
    // for this frame — otherwise the clouds lag the world by a frame and jolt when
    // the turn-rate changes (e.g. crossing bend <-> straight).
    [DefaultExecutionOrder(50)]
    [RequireComponent(typeof(Camera))]
    public class CloudLayer : MonoBehaviour
    {
        [Tooltip("Optional; falls back to Shader.Find(\"NightRider/Cloud\").")]
        public Shader cloudShader;
        [Tooltip("Greyscale cloud strip (several screens wide, a few rows tall). Set wrap mode to Repeat.")]
        public Texture2D cloudTexture;

        [Header("Placement")]
        [Min(1f), Tooltip("Distance for the backdrop quad; keep it just inside the sky's distance.")]
        public float distance = 95f;
        [Range(0f, 1f), Tooltip("Band centre up the screen (0 bottom .. 1 top).")]
        public float bandCenter = 0.62f;
        [Tooltip("Lock the band height so each texture row = a whole number of NES rows (keeps rows on the scanline grid). Uses the texture height.")]
        public bool pixelLockVertical = true;
        [Min(1), Tooltip("NES rows per texture row when pixel-locked (1 = chunkiest/native).")]
        public int verticalZoom = 1;
        [Min(2), Tooltip("NES rows on screen (match the CRT's Pixel Height, 240).")]
        public int nesRows = 240;
        [Range(0.01f, 1f), Tooltip("Manual band height (used only when pixel-lock is off).")]
        public float bandHeight = 0.18f;

        [Header("Look")]
        [Tooltip("Tint of the additive cloud light (white = neutral).")]
        public Color cloudTint = Color.white;
        [Range(0f, 2f), Tooltip("How strongly the cloud shades add onto the sky before snapping.")]
        public float strength = 1f;
        [Tooltip("Degrees of yaw per full texture loop — sets how BIG the cloud features read, not the speed. 360 = one loop per turn.")]
        public float degreesPerLoop = 360f;
        [Range(0f, 1f), Tooltip("Shades below this are treated as transparent (the sky shows).")]
        public float cloudCut = 0.04f;

        Camera _cam;
        Transform _quad;
        Material _mat;

        static readonly int CloudTexId = Shader.PropertyToID("_CloudTex");
        static readonly int TintId     = Shader.PropertyToID("_CloudTint");
        static readonly int StrengthId = Shader.PropertyToID("_Strength");
        static readonly int CenterId   = Shader.PropertyToID("_BandCenter");
        static readonly int HeightId   = Shader.PropertyToID("_BandHeight");
        static readonly int LoopId     = Shader.PropertyToID("_DegreesPerLoop");
        static readonly int CutId      = Shader.PropertyToID("_CloudCut");
        static readonly int YawId      = Shader.PropertyToID("_CamYaw");
        static readonly int HFovId     = Shader.PropertyToID("_HFovDeg");

        void Awake()
        {
            _cam = GetComponent<Camera>();
            BuildQuad();
        }

        void BuildQuad()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "CloudBand";
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            _quad = go.transform;
            _quad.SetParent(transform, false);

            var mr = go.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            var sh = cloudShader != null ? cloudShader : Shader.Find("NightRider/Cloud");
            _mat = new Material(sh);
            if (cloudTexture != null) _mat.SetTexture(CloudTexId, cloudTexture);
            mr.sharedMaterial = _mat;
        }

        void LateUpdate()
        {
            if (_cam == null || _mat == null) return;

            // Fill the frustum at `distance` (quad is a child, so it tracks the camera).
            float h = 2f * distance * Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            _quad.localPosition = new Vector3(0f, 0f, distance);
            _quad.localRotation = Quaternion.identity;
            _quad.localScale = new Vector3(h * _cam.aspect, h, 1f);

            // Carousel: world yaw + horizontal FOV feed the linear scroll map.
            Vector3 f = transform.forward;
            float yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            float hFovDeg = 2f * Mathf.Atan(Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * _cam.aspect) * Mathf.Rad2Deg;

            _mat.SetFloat(YawId, yaw);
            _mat.SetFloat(HFovId, hFovDeg);

            // Live-tunable look (cheap to push every frame).
            if (cloudTexture != null) _mat.SetTexture(CloudTexId, cloudTexture);
            _mat.SetColor(TintId, cloudTint);
            _mat.SetFloat(StrengthId, strength);
            _mat.SetFloat(CenterId, bandCenter);
            // Pixel-lock: map each texture row to a whole number of NES rows so the
            // strip sits exactly on the scanline grid (a row stays a row).
            float bh = bandHeight;
            if (pixelLockVertical && cloudTexture != null && nesRows > 0)
                bh = cloudTexture.height * Mathf.Max(1, verticalZoom) / (float)nesRows;
            _mat.SetFloat(HeightId, bh);
            _mat.SetFloat(LoopId, degreesPerLoop);
            _mat.SetFloat(CutId, cloudCut);
        }
    }
}
