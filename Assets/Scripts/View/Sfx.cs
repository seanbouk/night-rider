// Monophonic, NES-style sound effects. The music owns the pulse + triangle
// channels; every SOUND EFFECT is the noise channel — a 15-bit LFSR (long mode =
// hiss, short mode = a buzzy tone) shaped by an attack/decay envelope, with an
// optional pitch sweep. We take a few not-quite-NES liberties (free sweep rate,
// arbitrary envelope), but it's all programmatic noise. Clips are synthesized once
// at startup.
//
// One AudioSource plays one effect at a time. Priority == enum order (Gallop
// lowest, NavShop highest): a request interrupts anything of equal-or-lower
// priority, and is dropped while a strictly-higher effect is still playing.
//
// Auto-creates itself; needs an AudioListener in the scene (the Main Camera has
// one by default). Trigger from anywhere with Sfx.Play(SfxId.Xxx).

using UnityEngine;

namespace NightRider.View
{
    // Listed low -> high priority (highest last).
    public enum SfxId { Gallop, ChangeLane, Attack, Hit, Kill, OpenShop, CloseShop, NavShop, ShopDown, ShopUp, Purchase }

    [DefaultExecutionOrder(-100)]
    public class Sfx : MonoBehaviour
    {
        static Sfx _instance;
        public static Sfx Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<Sfx>();
                    if (_instance == null) _instance = new GameObject("Sfx").AddComponent<Sfx>();
                }
                return _instance;
            }
        }

        [Range(0f, 1f)] public float masterVolume = 0.6f;

        AudioSource _src;
        AudioClip[] _clips;
        int _curPriority = -1;
        double _busyUntil;     // dsp-clock time the current effect ends (timescale-independent)

        void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(this); return; }
            _instance = this;

            _src = gameObject.AddComponent<AudioSource>();
            _src.playOnAwake = false;
            _src.spatialBlend = 0f;            // 2D
            _src.ignoreListenerPause = true;   // shop SFX still play while the game is paused

            BuildClips();
        }

        void OnDestroy() { if (_instance == this) _instance = null; }

        public static void Play(SfxId id) => Instance.PlayInternal(id);

        void PlayInternal(SfxId id)
        {
            int p = (int)id;
            double now = AudioSettings.dspTime;
            if (now < _busyUntil && p < _curPriority) return;   // a higher effect is still sounding

            var clip = _clips[p];
            if (clip == null) return;
            _src.volume = masterVolume;
            _src.clip = clip;
            _src.Play();
            _curPriority = p;
            _busyUntil = now + clip.length;
        }

        // ---- synthesis ---------------------------------------------------------

        void BuildClips()
        {
            int sr = AudioSettings.outputSampleRate;
            if (sr <= 0) sr = 44100;
            _clips = new AudioClip[System.Enum.GetValues(typeof(SfxId)).Length];
            //                                         dur     f0     f1   tonal   atk     dec    vol
            _clips[(int)SfxId.Gallop]     = Build("gallop",    sr, 0.05f, 1800f,  350f,  false, 0.001f, 0.022f, 0.60f);
            _clips[(int)SfxId.ChangeLane] = Build("changelane",sr, 0.12f, 1500f, 4500f,  false, 0.005f, 0.070f, 0.45f);
            _clips[(int)SfxId.Attack]     = Build("attack",    sr, 0.16f, 7000f,  900f,  false, 0.001f, 0.100f, 0.55f);
            _clips[(int)SfxId.Hit]        = Build("hit",       sr, 0.10f, 3000f, 1500f,  false, 0.001f, 0.050f, 0.70f);
            _clips[(int)SfxId.Kill]       = Build("kill",      sr, 0.40f, 4000f,  250f,  false, 0.002f, 0.280f, 0.80f);
            _clips[(int)SfxId.OpenShop]   = Build("openshop",  sr, 0.16f,  500f, 1500f,  true,  0.005f, 0.130f, 0.50f);
            _clips[(int)SfxId.CloseShop]  = Build("closeshop", sr, 0.16f, 1500f,  500f,  true,  0.005f, 0.130f, 0.50f);
            _clips[(int)SfxId.NavShop]    = Build("navshop",   sr, 0.045f,1200f, 1200f,  true,  0.001f, 0.030f, 0.50f);
            // Adjust: short tonal blips that clearly sweep up (buy +) / down (sell -).
            _clips[(int)SfxId.ShopUp]     = Build("shopup",    sr, 0.07f, 1000f, 2400f,  true,  0.001f, 0.045f, 0.55f);
            _clips[(int)SfxId.ShopDown]   = Build("shopdown",  sr, 0.07f, 2400f, 1000f,  true,  0.001f, 0.045f, 0.55f);
            // Purchase: three ascending tonal hits — a little noise-fanfare.
            _clips[(int)SfxId.Purchase]   = BuildTriple("purchase", sr, new[] { 1100f, 1600f, 2300f }, 0.06f, 0.025f, 0.6f);
        }

        // dur seconds; f0->f1 LFSR clock sweep (Hz, exponential); tonal = short-mode
        // (buzzy) vs long-mode (hiss); atk/dec envelope seconds; vol 0..1.
        static AudioClip Build(string name, int sr, float dur, float f0, float f1,
                               bool tonal, float atk, float dec, float vol)
        {
            int n = Mathf.Max(1, (int)(sr * dur));
            var data = new float[n];
            RenderNoise(data, 0, n, sr, f0, f1, tonal, atk, dec, vol);
            var clip = AudioClip.Create(name, n, 1, sr, false);
            clip.SetData(data, 0);
            return clip;
        }

        // A sequence of ascending tonal bursts (gap seconds between them).
        static AudioClip BuildTriple(string name, int sr, float[] freqs, float burst, float gap, float vol)
        {
            int bn = Mathf.Max(1, (int)(sr * burst));
            int gn = Mathf.Max(0, (int)(sr * gap));
            int n = freqs.Length * bn + (freqs.Length - 1) * gn;
            var data = new float[n];
            int pos = 0;
            foreach (float f in freqs)
            {
                RenderNoise(data, pos, bn, sr, f, f * 1.06f, true, 0.001f, 0.035f, vol);   // tiny up-tick each hit
                pos += bn + gn;
            }
            var clip = AudioClip.Create(name, n, 1, sr, false);
            clip.SetData(data, 0);
            return clip;
        }

        // Render one LFSR-noise burst into data[start .. start+count), summed in.
        static void RenderNoise(float[] data, int start, int count, int sr, float f0, float f1,
                                bool tonal, float atk, float dec, float vol)
        {
            uint reg = 1;                  // 15-bit LFSR seed
            float phase = 0f;
            int outv = 1;
            float dur = count / (float)sr;
            const float fade = 0.004f;     // end fade to kill the final click
            float fadeStart = dur - fade;
            float ratio = Mathf.Max(f1, 1f) / Mathf.Max(f0, 1f);

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)sr;
                float freq = f0 * Mathf.Pow(ratio, dur > 0f ? t / dur : 0f);

                phase += freq / sr;
                while (phase >= 1f)
                {
                    phase -= 1f;
                    uint fb = tonal ? ((reg ^ (reg >> 6)) & 1u) : ((reg ^ (reg >> 1)) & 1u);
                    reg = (reg >> 1) | (fb << 14);
                    outv = (reg & 1u) != 0u ? 1 : -1;
                }

                float env = t < atk ? (atk > 0f ? t / atk : 1f)
                                    : Mathf.Exp(-(t - atk) / Mathf.Max(dec, 1e-4f));
                if (t > fadeStart) env *= Mathf.Clamp01((dur - t) / fade);

                int idx = start + i;
                if (idx >= 0 && idx < data.Length) data[idx] += outv * env * vol;
            }
        }
    }
}
