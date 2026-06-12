// NES-style title screen. A pre-quantised image fills most of the screen (black
// around it); the title sits up high, a flashing "- PRESS START -" near the
// bottom, and a copyright line below that. Start / Select / A / B begin the game
// after a half second of black. Gameplay is frozen (timeScale 0) behind the title
// so the run starts fresh the moment the black clears.
//
// It lives on the UiCanvas TitleRoot (full-screen black + a 4:3 grid), so the CRT
// covers it just like the HUD. Add this to a scene object and assign the title
// image and the UI font (the same one the HUD uses); tweak the text/colours/rows.

using UnityEngine;
using UnityEngine.UI;
using NightRider.World;

namespace NightRider.View
{
    public class TitleScreen : MonoBehaviour
    {
        // True while the title is up (and during its black hold). The HUD checks this
        // so the Start press that begins the game doesn't also toggle pause.
        public static bool Active { get; private set; }

        [Header("Art")]
        [Tooltip("Pre-quantised title image (e.g. 256x240). Black fills the rest of the screen.")]
        public Texture2D titleImage;
        [Range(0.2f, 1f), Tooltip("Fraction of the 4:3 area the image fills (aspect preserved; black around it).")]
        public float imageScale = 1f;
        [Tooltip("Snap the title image to NES-legal colours (needs Read/Write on the texture).")]
        public bool snapToNes = true;

        [Header("Music")]
        [Tooltip("Track that loops on the title screen (stops for the black, then the game's default plays).")]
        public AudioClip titleTrack;

        [Header("Intro voice")]
        [Tooltip("Voice sample played over the black after the title. The black lasts at least Black Hold AND at least this clip's length.")]
        public AudioClip voiceClip;

        [Header("Font")]
        [Tooltip("UI font — assign the same one the HUD uses.")]
        public Font font;

        [Header("Title")]
        public string title = "Curse of the Night Rider";
        public int titleRow = 2;
        public Color titleColor = new(0.95f, 0.78f, 0.22f);   // warm gold

        [Header("Prompt (flashes)")]
        public string prompt = "- PRESS START -";
        public int promptRow = 25;
        public Color promptColor = Color.white;
        [Tooltip("Plate behind the prompt characters (only the text cells, only while flashed on).")]
        public Color promptBack = Color.black;

        [Header("Copyright")]
        public string copyright = "(C) 2026 DUBIT LTD";
        public int copyrightRow = 28;
        public Color copyrightColor = new(0.6f, 0.6f, 0.62f);

        [Header("Timing")]
        [Min(0.05f), Tooltip("PRESS START blink period (seconds, unscaled).")]
        public float blinkPeriod = 0.8f;
        [Range(0.1f, 0.9f), Tooltip("Fraction of the period the prompt is visible.")]
        public float blinkDuty = 0.6f;
        [Min(0f), Tooltip("Seconds of black between the title and the game.")]
        public float blackHold = 0.5f;

        enum Phase { Title, Black, Done }
        Phase _phase = Phase.Title;
        float _blackTimer;
        float _blackDuration;
        UiCanvas _ui;
        GlyphGrid _grid;
        Image _art;
        AudioSource _voice;

        void Awake()
        {
            Active = true;
            Time.timeScale = 0f;   // freeze the game behind the title
        }

        void Start()
        {
            _ui = UiCanvas.Instance;
            _ui.TitleRoot.gameObject.SetActive(true);

            // Title image (UI -> crisp pixel art; the CRT still gives it scanlines/blur).
            if (titleImage != null)
            {
                _art = UiCanvas.MakeImage(_ui.TitleFrame, Color.white);
                _art.name = "TitleArt";
                var tex = snapToNes ? Nes.SnapTexture(titleImage) : titleImage;
                _art.sprite = Sprite.Create(tex,
                    new Rect(0, 0, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f), 100f);
                _art.preserveAspect = true;
                float m = (1f - Mathf.Clamp01(imageScale)) * 0.5f;
                var rt = _art.rectTransform;
                rt.anchorMin = new Vector2(m, m);
                rt.anchorMax = new Vector2(1f - m, 1f - m);
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
            }

            // Default to the HUD's font so it matches without re-assigning.
            if (font == null)
            {
                var hud = FindAnyObjectByType<Hud>();
                if (hud != null) font = hud.hudFont;
            }

            // Text layers go on AFTER the art, so they sit on top of it.
            _grid = new GlyphGrid(_ui, _ui.TitleFrame, font);

            if (titleTrack != null) MusicDirector.Instance.PlayTitle(titleTrack);

            // 2D one-shot source for the intro voice (plays under the black).
            _voice = gameObject.AddComponent<AudioSource>();
            _voice.playOnAwake = false;
            _voice.spatialBlend = 0f;
        }

        void Update()
        {
            if (_phase == Phase.Done) return;

            if (_phase == Phase.Title)
            {
                bool showPrompt = Mathf.Repeat(Time.unscaledTime, blinkPeriod) < blinkPeriod * blinkDuty;
                DrawText(showPrompt);

                if (Controls.Start || Controls.Select || Controls.A || Controls.B)
                {
                    _phase = Phase.Black;
                    _blackTimer = 0f;
                    if (_art != null) _art.enabled = false;
                    _grid.Begin(); _grid.End();   // clear the text -> just the black backdrop
                    MusicDirector.Instance.Silence();   // no music during the black

                    // Play the intro voice; the black lasts at least blackHold and at
                    // least the clip's length.
                    _blackDuration = blackHold;
                    if (voiceClip != null)
                    {
                        _voice.PlayOneShot(voiceClip);
                        _blackDuration = Mathf.Max(blackHold, voiceClip.length);
                    }
                }
            }
            else if (_phase == Phase.Black)
            {
                _blackTimer += Time.unscaledDeltaTime;
                if (_blackTimer >= _blackDuration) Finish();
            }
        }

        void DrawText(bool showPrompt)
        {
            _grid.Begin();
            CenterRun(titleRow, title, Snap(titleColor));
            if (showPrompt && !string.IsNullOrEmpty(prompt))
            {
                int col = Mathf.Max(0, (_ui.cols - prompt.Length) / 2);
                _grid.Fill(col, promptRow, prompt.Length, 1, Snap(promptBack));   // plate behind the chars only
                _grid.Run(col, promptRow, prompt, Snap(promptColor));
            }
            CenterRun(copyrightRow, copyright, Snap(copyrightColor));
            _grid.End();
        }

        void CenterRun(int row, string s, Color c)
        {
            if (string.IsNullOrEmpty(s)) return;
            int col = Mathf.Max(0, (_ui.cols - s.Length) / 2);
            _grid.Run(col, row, s, c);
        }

        // Nearest NES-legal colour, keeping the original alpha (Nes.Snap forces opaque).
        static Color Snap(Color c)
        {
            var s = Nes.Snap(c);
            s.a = c.a;
            return s;
        }

        void Finish()
        {
            _phase = Phase.Done;
            Active = false;
            Time.timeScale = 1f;
            if (_ui != null && _ui.TitleRoot != null) _ui.TitleRoot.gameObject.SetActive(false);
            MusicDirector.Instance.StartGame();   // default track begins as the black clears
            enabled = false;
        }

        // Safety: never leave the game frozen if the title object is torn down.
        void OnDestroy()
        {
            if (Active) { Active = false; Time.timeScale = 1f; }
        }
    }
}
