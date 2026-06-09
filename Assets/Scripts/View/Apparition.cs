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
        Transform _follow;
        float _side, _reach, _life, _t;
        Camera _cam;

        public void Init(SpriteSheetAnimator source, Transform follow, int side, float reach, float life, int sortingOrder)
        {
            _sr = GetComponent<SpriteRenderer>();
            _sr.sortingOrder = sortingOrder;        // behind the rider
            _source = source;
            _follow = follow;
            _side = side;
            _reach = reach;
            _life = Mathf.Max(0.05f, life);
            if (follow != null) transform.position = follow.position;
        }

        void LateUpdate()
        {
            _t += Time.deltaTime;

            // Shoot out laterally, reaching full distance by half-life (ease-out).
            // Tracks the rider's current pose, so it stays level / abreast.
            float shoot = Mathf.Clamp01(_t / (_life * 0.5f));
            float e = 1f - Mathf.Pow(1f - shoot, 3f);
            if (_follow != null)
                transform.position = _follow.position + _follow.right * (_side * _reach * e);

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
