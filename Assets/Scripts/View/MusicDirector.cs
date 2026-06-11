// Background music + the "head" jukebox. One looping AudioSource plays exactly one
// track at a time:
//   - the title track (driven by TitleScreen)
//   - the default game track (the "no head" slot, never lost)
//   - one track per collected head — each trading post grants a head (32x32 image)
//     and a music track when you buy it
//
// Buying a head switches to its track immediately and shows its head in the HUD.
// SELECT cycles default -> head0 -> head1 -> ... -> default. Music loops, and audio
// runs on real time so it keeps playing through the timeScale-0 pause/shop.
//
// Place one in the scene and assign Default Track; heads + tracks live on the
// TradingPosts. Auto-creates a silent fallback if absent.

using System.Collections.Generic;
using UnityEngine;
using NightRider.World;

namespace NightRider.View
{
    [DefaultExecutionOrder(-90)]
    public class MusicDirector : MonoBehaviour
    {
        static MusicDirector _instance;
        public static MusicDirector Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<MusicDirector>();
                    if (_instance == null) _instance = new GameObject("MusicDirector").AddComponent<MusicDirector>();
                }
                return _instance;
            }
        }

        [Tooltip("Plays when no head is selected (the default — never lost).")]
        public AudioClip defaultTrack;
        [Range(0f, 1f)] public float volume = 0.6f;

        AudioSource _src;
        readonly List<TradingPost> _heads = new();              // collected heads, in order
        readonly Dictionary<Texture2D, Sprite> _spriteCache = new();
        int _index = -1;                                        // -1 = default / no head

        public TradingPost CurrentHead => (_index < 0 || _index >= _heads.Count) ? null : _heads[_index];

        // 32x32 head sprite for the HUD (null while on the default slot).
        public Sprite CurrentHeadSprite
        {
            get
            {
                var h = CurrentHead;
                if (h == null || h.headImage == null) return null;
                if (!_spriteCache.TryGetValue(h.headImage, out var sp))
                {
                    var t = h.headImage;
                    sp = Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100f);
                    _spriteCache[h.headImage] = sp;
                }
                return sp;
            }
        }

        void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(this); return; }
            _instance = this;
            _src = gameObject.AddComponent<AudioSource>();
            _src.playOnAwake = false;
            _src.loop = true;
            _src.spatialBlend = 0f;   // 2D
            _src.volume = volume;
        }

        void OnDestroy() { if (_instance == this) _instance = null; }

        void Start()
        {
            // If no title screen is driving us, start the default track straight away.
            if (!TitleScreen.Active) StartGame();
        }

        void Update()
        {
            _src.volume = volume;
            // SELECT cycles head/track during gameplay (not on the title or in the shop).
            if (!TitleScreen.Active && !TradingMenu.Active && Controls.Select) Next();
        }

        // ---- transport ---------------------------------------------------------

        public void PlayTitle(AudioClip clip) => Play(clip);   // title screen track
        public void Silence() => Play(null);                   // the half-second black

        public void StartGame()                                 // game begins -> default
        {
            _index = -1;
            PlayCurrent();
        }

        public bool Has(TradingPost post) => post != null && _heads.Contains(post);

        // Buy a head: collect it (once per post) and switch to it immediately.
        public void Acquire(TradingPost post)
        {
            if (post == null) return;
            int at = _heads.IndexOf(post);
            if (at < 0) { _heads.Add(post); at = _heads.Count - 1; }
            _index = at;
            PlayCurrent();
        }

        // SELECT: default -> head0 -> ... -> headN-1 -> default.
        public void Next()
        {
            _index++;
            if (_index >= _heads.Count) _index = -1;
            PlayCurrent();
        }

        void PlayCurrent()
        {
            var h = CurrentHead;
            Play(h != null ? h.headTrack : defaultTrack);
        }

        void Play(AudioClip clip)
        {
            if (clip == null) { _src.Stop(); _src.clip = null; return; }
            if (_src.clip == clip && _src.isPlaying) return;   // already on this track
            _src.clip = clip;
            _src.loop = true;
            _src.Play();
        }
    }
}
