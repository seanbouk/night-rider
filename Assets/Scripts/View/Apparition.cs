// A ghost copy of the rider, spawned by an attack. It mirrors the rider's current
// animation frame, shoots out one lane to the side (ease-out), flashes a couple
// of times, then vanishes — fast. The ghost look (monotone blue->white, NES
// column see-through) is the material's job (NightRider/Apparition).

using UnityEngine;

namespace NightRider.View
{
    [RequireComponent(typeof(SpriteRenderer))]
    public class Apparition : MonoBehaviour
    {
        SpriteRenderer _sr;
        SpriteSheetAnimator _source;
        Vector3 _startPos, _dir;
        float _reach, _life, _t;
        Camera _cam;

        public void Init(SpriteSheetAnimator source, Vector3 startPos, Vector3 dir, float reach, float life, int sortingOrder)
        {
            _sr = GetComponent<SpriteRenderer>();
            _sr.sortingOrder = sortingOrder;        // behind the rider
            _source = source;
            _startPos = startPos;
            _dir = dir.normalized;
            _reach = reach;
            _life = Mathf.Max(0.05f, life);
            transform.position = startPos;
        }

        void LateUpdate()
        {
            _t += Time.deltaTime;

            // Shoot out laterally, reaching full distance by half-life (ease-out).
            float shoot = Mathf.Clamp01(_t / (_life * 0.5f));
            float e = 1f - Mathf.Pow(1f - shoot, 3f);
            transform.position = _startPos + _dir * (_reach * e);

            // Mirror the rider's current frame.
            if (_source != null && _source.CurrentSprite != null) _sr.sprite = _source.CurrentSprite;

            // Billboard.
            if (_cam == null) _cam = Camera.main;
            if (_cam != null) transform.rotation = _cam.transform.rotation;

            // Solid while shooting, then blink for the second half.
            _sr.enabled = _t < _life * 0.5f || Mathf.Repeat(_t, 0.1f) < 0.05f;

            if (_t >= _life) Destroy(gameObject);
        }
    }
}
