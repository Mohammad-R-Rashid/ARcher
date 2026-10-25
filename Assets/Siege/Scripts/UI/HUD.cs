using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Siege
{
    // Runtime-built overlay UI: health banners, crosshair + draw ring, hit markers, floating hit
    // text, off-screen arrows for the catapult and incoming fireballs, prompts and big banners.
    public class HUD : MonoBehaviour
    {
        public Texture2D crosshairTex, ringTex, vignetteTex, panelTex, dotTex;
        public ScreenFX screenFX;
        [HideInInspector] public Camera cam;

        static readonly Color Gold = new Color(1f, 0.83f, 0.42f);
        static readonly Color Ink = new Color(0.08f, 0.06f, 0.05f, 0.75f);

        GameManager m_Game;
        Castle m_Castle;
        Trebuchet m_Catapult;
        Font m_Font;
        Sprite m_Cross, m_Ring, m_Vig, m_Panel, m_Tri, m_Dot;
        RectTransform m_Root, m_Safe;
        Rect m_LastSafe;
        Image m_Crosshair, m_DrawRing, m_HitMarker;
        Text m_Prompt, m_Warning, m_Title, m_Subtitle;
        CanvasGroup m_BannerGroup, m_BarsGroup;
        Bar m_CastleBar, m_CatBar;
        Button m_AutoBtn, m_AgainBtn, m_MoveBtn;
        readonly List<Image> m_Arrows = new List<Image>();
        readonly List<Text> m_Floaters = new List<Text>();
        Coroutine m_BannerCo;
        float m_HitFlash;

        class Bar
        {
            public RectTransform root;
            public Image fill, ghost;
            public float value = 1f, ghostValue = 1f, shake, baseScale = 1f;
        }

        void Awake()
        {
            m_Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            m_Cross = Spr(crosshairTex); m_Ring = Spr(ringTex); m_Vig = Spr(vignetteTex); m_Panel = Spr(panelTex, 60); m_Dot = Spr(dotTex);
            m_Tri = Spr(MakeTriangle());
            Build();
        }

        static Sprite Spr(Texture2D t, float border = 0)
        {
            if (!t) return null;
            return Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, Vector4.one * border);
        }

        static Texture2D MakeTriangle()
        {
            var t = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    float u = (x + 0.5f) / 64f, v = (y + 0.5f) / 64f;
                    float half = (1f - v) * 0.5f;
                    float a = Mathf.Clamp01((half - Mathf.Abs(u - 0.5f)) * 40f) * Mathf.Clamp01(v * 40f);
                    t.SetPixel(x, y, new Color(1, 1, 1, a));
                }
            t.Apply();
            return t;
        }

        // ---------------------------------------------------------------- construction
        void Build()
        {
            var canvasGo = new GameObject("HUDCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1f;
            m_Root = canvasGo.GetComponent<RectTransform>();
            // Everything readable lives inside the safe area (Dynamic Island / rounded corners).
            m_Safe = new GameObject("SafeArea", typeof(RectTransform)).GetComponent<RectTransform>();
            m_Safe.SetParent(m_Root, false);
            Stretch(m_Safe);
            if (!FindAnyObjectByType<EventSystem>())
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            // Full-screen overlays driven by ScreenFX
            var heat = Img("Heat", m_Root, m_Vig, new Color(1, 0.5f, 0.1f, 0));
            Stretch(heat.rectTransform);
            var dmg = Img("Damage", m_Root, m_Vig, new Color(1, 0, 0, 0));
            Stretch(dmg.rectTransform);
            heat.raycastTarget = dmg.raycastTarget = false;
            if (screenFX) { screenFX.heatOverlay = heat; screenFX.damageOverlay = dmg; }

            // Crosshair
            m_DrawRing = Img("DrawRing", m_Root, m_Ring, new Color(1, 0.85f, 0.45f, 0.9f));
            Size(m_DrawRing.rectTransform, 120, 120);
            m_DrawRing.type = Image.Type.Filled;
            m_DrawRing.fillMethod = Image.FillMethod.Radial360;
            m_DrawRing.fillOrigin = (int)Image.Origin360.Top;
            m_DrawRing.fillAmount = 0;
            m_Crosshair = Img("Crosshair", m_Root, m_Cross, new Color(1, 1, 1, 0.85f));
            Size(m_Crosshair.rectTransform, 64, 64);
            m_HitMarker = Img("HitMarker", m_Root, m_Cross, new Color(1, 0.25f, 0.15f, 0));
            Size(m_HitMarker.rectTransform, 90, 90);
            m_HitMarker.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);

            // Health bars
            m_BarsGroup = new GameObject("Bars", typeof(RectTransform), typeof(CanvasGroup)).GetComponent<CanvasGroup>();
            m_BarsGroup.transform.SetParent(m_Safe, false);
            Stretch((RectTransform)m_BarsGroup.transform);
            m_BarsGroup.blocksRaycasts = false;
            m_CastleBar = MakeBar("YOUR CASTLE", new Color(0.35f, 0.62f, 1f), true);
            // Enemy health floats above the catapult itself (Clash Royale style), not in a corner.
            m_CatBar = MakeBar("SIEGE CATAPULT", new Color(0.95f, 0.3f, 0.2f), false);
            m_CatBar.root.SetParent(m_Root, false);
            m_CatBar.root.anchorMin = m_CatBar.root.anchorMax = new Vector2(0.5f, 0.5f);
            m_CatBar.baseScale = 0.5f;

            // Prompt + warnings
            m_Prompt = Label("Prompt", m_Safe, 44, Gold, TextAnchor.MiddleCenter);
            Anchor(m_Prompt.rectTransform, new Vector2(0.5f, 0f), new Vector2(0, 120), new Vector2(1400, 90));
            m_Warning = Label("Warning", m_Safe, 34, new Color(1f, 0.55f, 0.4f), TextAnchor.MiddleCenter);
            Anchor(m_Warning.rectTransform, new Vector2(0.5f, 0f), new Vector2(0, 200), new Vector2(1400, 60));

            // Banner
            var banner = new GameObject("Banner", typeof(RectTransform), typeof(CanvasGroup));
            banner.transform.SetParent(m_Safe, false);
            m_BannerGroup = banner.GetComponent<CanvasGroup>();
            m_BannerGroup.alpha = 0;
            m_BannerGroup.blocksRaycasts = false;
            var brt = (RectTransform)banner.transform;
            Anchor(brt, new Vector2(0.5f, 0.76f), Vector2.zero, new Vector2(1500, 260));
            var plate = Img("Plate", brt, m_Panel, new Color(0f, 0f, 0f, 0f));   // no plate: keep the view clean
            plate.type = Image.Type.Sliced;
            Stretch(plate.rectTransform);
            m_Title = Label("Title", brt, 96, Gold, TextAnchor.MiddleCenter);
            Anchor(m_Title.rectTransform, new Vector2(0.5f, 0.62f), Vector2.zero, new Vector2(1500, 150));
            m_Title.fontStyle = FontStyle.Bold;
            m_Subtitle = Label("Subtitle", brt, 40, Color.white, TextAnchor.MiddleCenter);
            Anchor(m_Subtitle.rectTransform, new Vector2(0.5f, 0.2f), Vector2.zero, new Vector2(1500, 70));

            // Buttons
            m_AutoBtn = MakeButton("PLACE IT FOR ME", new Vector2(1f, 0f), new Vector2(-260, 90), new Vector2(440, 100), () => m_Game.PlaceCatapultAuto());
            m_AgainBtn = MakeButton("PLAY AGAIN", new Vector2(0.5f, 0.4f), Vector2.zero, new Vector2(480, 120), () => m_Game.PlayAgain());
            m_MoveBtn = MakeButton("MOVE CASTLE", new Vector2(0.5f, 0.4f), new Vector2(0, -120), new Vector2(340, 80), () => m_Game.Replace());

            for (int i = 0; i < 4; i++)
            {
                var a = Img("Arrow" + i, m_Root, m_Tri, Color.white);
                Size(a.rectTransform, 56, 56);
                a.raycastTarget = false;
                a.enabled = false;
                m_Arrows.Add(a);
            }
            for (int i = 0; i < 6; i++)
            {
                var f = Label("Float" + i, m_Root, 48, Gold, TextAnchor.MiddleCenter);
                f.fontStyle = FontStyle.Bold;
                Size(f.rectTransform, 600, 80);
                f.enabled = false;
                m_Floaters.Add(f);
            }
        }

        // Slim health bar: dark rounded track, white damage ghost, coloured fill. No labels.
        Bar MakeBar(string title, Color color, bool left)
        {
            var root = new GameObject(title, typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(m_BarsGroup.transform, false);
            var anchor = left ? new Vector2(0f, 1f) : new Vector2(1f, 1f);
            Anchor(root, anchor, new Vector2(left ? 230 : -230, -50), new Vector2(380, 26));
            var white = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), Vector2.one * 0.5f);
            var track = Img("Track", root, m_Panel, new Color(0.05f, 0.04f, 0.04f, 0.6f));
            track.type = Image.Type.Sliced;
            track.pixelsPerUnitMultiplier = 6f;
            Stretch(track.rectTransform);
            var inner = new GameObject("Inner", typeof(RectTransform)).GetComponent<RectTransform>();
            inner.SetParent(root, false);
            Stretch(inner);
            inner.offsetMin = new Vector2(5, 5); inner.offsetMax = new Vector2(-5, -5);
            var ghost = Img("Ghost", inner, white, new Color(1, 1, 1, 0.85f));
            Stretch(ghost.rectTransform);
            ghost.type = Image.Type.Filled; ghost.fillMethod = Image.FillMethod.Horizontal; ghost.fillOrigin = 0;
            var fill = Img("Fill", inner, white, color);
            Stretch(fill.rectTransform);
            fill.type = Image.Type.Filled; fill.fillMethod = Image.FillMethod.Horizontal; fill.fillOrigin = 0;
            return new Bar { root = root, fill = fill, ghost = ghost };
        }

        Button MakeButton(string text, Vector2 anchor, Vector2 pos, Vector2 size, UnityEngine.Events.UnityAction onClick)
        {
            var img = Img(text, m_Safe, m_Panel, new Color(0.45f, 0.12f, 0.06f, 0.92f));
            img.raycastTarget = true;   // buttons must receive taps
            img.type = Image.Type.Sliced;
            Anchor(img.rectTransform, anchor, pos, size);
            var btn = img.gameObject.AddComponent<Button>();
            var cb = btn.colors; cb.highlightedColor = new Color(1, 0.9f, 0.8f); cb.pressedColor = new Color(0.8f, 0.7f, 0.6f); btn.colors = cb;
            btn.onClick.AddListener(() => { Haptics.UI(); Sfx.Play2D(Sound.UiTick); onClick(); });
            var t = Label("Text", img.rectTransform, (int)(size.y * 0.36f), Gold, TextAnchor.MiddleCenter);
            t.fontStyle = FontStyle.Bold;
            Stretch(t.rectTransform);
            t.text = text;
            btn.gameObject.SetActive(false);
            return btn;
        }

        Image Img(string name, Transform parent, Sprite s, Color c)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var i = go.GetComponent<Image>();
            i.sprite = s;
            i.color = c;
            i.raycastTarget = false;
            return i;
        }

        Text Label(string name, Transform parent, int size, Color c, TextAnchor align)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(Outline), typeof(Shadow));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = m_Font;
            t.fontSize = size;
            t.color = c;
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            var o = go.GetComponent<Outline>(); o.effectColor = new Color(0.12f, 0.05f, 0.02f, 0.95f); o.effectDistance = new Vector2(2.5f, -2.5f);
            var sh = go.GetComponent<Shadow>(); sh.effectColor = new Color(0, 0, 0, 0.6f); sh.effectDistance = new Vector2(3, -5);
            return t;
        }

        static void Stretch(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }
        static void Size(RectTransform r, float w, float h) { r.sizeDelta = new Vector2(w, h); }
        static void Anchor(RectTransform r, Vector2 a, Vector2 pos, Vector2 size)
        {
            r.anchorMin = r.anchorMax = a;
            r.pivot = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = pos;
            r.sizeDelta = size;
        }

        // ---------------------------------------------------------------- game hooks
        public void Bind(GameManager g) { m_Game = g; }

        public void SetTargets(Castle castle, Trebuchet catapult)
        {
            m_Castle = castle;
            m_Catapult = catapult;
            m_CastleBar.value = m_CastleBar.ghostValue = 1f;
            m_CatBar.value = m_CatBar.ghostValue = 1f;
        }

        public void OnPhase(GameManager.Phase p)
        {
            m_AutoBtn.gameObject.SetActive(p == GameManager.Phase.PlaceCatapult);
            m_AgainBtn.gameObject.SetActive(false);
            m_MoveBtn.gameObject.SetActive(false);
            m_BarsGroup.alpha = p >= GameManager.Phase.Intro ? 1 : 0;
            bool aim = p == GameManager.Phase.Battle;
            m_Crosshair.enabled = aim || p == GameManager.Phase.PlaceCastle || p == GameManager.Phase.PlaceCatapult;
            m_DrawRing.enabled = aim;
            switch (p)
            {
                case GameManager.Phase.Scanning: m_Prompt.text = "Scan the floor"; break;
                case GameManager.Phase.PlaceCastle: m_Prompt.text = "Tap to raise your castle"; break;
                case GameManager.Phase.PlaceCatapult: m_Prompt.text = "Tap a spot across the room for the enemy catapult"; break;
                default: m_Prompt.text = ""; break;
            }
            if (p == GameManager.Phase.Victory || p == GameManager.Phase.Defeat) StartCoroutine(ShowAgainLater());
            m_BarsGroup.alpha = p >= GameManager.Phase.Intro && p <= GameManager.Phase.Battle ? 1 : 0;
            SetPlacementWarning(null);
        }

        IEnumerator ShowAgainLater()
        {
            yield return new WaitForSecondsRealtime(2.5f);
            m_AgainBtn.gameObject.SetActive(true);
            m_AgainBtn.transform.localScale = Vector3.zero;
            for (float t = 0; t < 0.35f; t += Time.unscaledDeltaTime)
            {
                m_AgainBtn.transform.localScale = Vector3.one * Ease.OutBack(t / 0.35f);
                yield return null;
            }
            m_AgainBtn.transform.localScale = Vector3.one;
            if (!m_Game.Simulated) m_MoveBtn.gameObject.SetActive(true);
        }

        IEnumerator ClearPromptLater(float t)
        {
            yield return new WaitForSecondsRealtime(t);
            if (m_Game && m_Game.State == GameManager.Phase.Battle) m_Prompt.text = "";
        }

        public void SetStatus(string msg) { if (m_Warning) m_Warning.text = msg ?? ""; }

        public void SetPlacementWarning(string msg) { if (m_Warning) m_Warning.text = msg ?? ""; }

        public void Banner(string title, string subtitle, Color color, float hold)
        {
            if (m_BannerCo != null) StopCoroutine(m_BannerCo);
            m_BannerCo = StartCoroutine(BannerRoutine(title, subtitle, color, hold));
        }

        IEnumerator BannerRoutine(string title, string subtitle, Color color, float hold)
        {
            m_Title.text = title;
            m_Title.color = color;
            m_Subtitle.text = "";
            var rt = (RectTransform)m_BannerGroup.transform;
            for (float t = 0; t < 0.4f; t += Time.unscaledDeltaTime)
            {
                float k = t / 0.4f;
                m_BannerGroup.alpha = k;
                rt.localScale = Vector3.one * Mathf.Lerp(1.6f, 1f, Ease.OutBack(k));
                yield return null;
            }
            m_BannerGroup.alpha = 1; rt.localScale = Vector3.one;
            if (hold <= 0) yield break;
            yield return new WaitForSecondsRealtime(hold);
            for (float t = 0; t < 0.5f; t += Time.unscaledDeltaTime)
            {
                m_BannerGroup.alpha = 1 - t / 0.5f;
                yield return null;
            }
            m_BannerGroup.alpha = 0;
        }

        public void OnCatapultHit(float damage, Vector3 point, bool knight)
        {
            m_HitFlash = 1f;
            m_CatBar.shake = 1f;
            string msg = damage >= Tune.TNTDamage ? "KABOOM!" : knight ? "KNIGHT HIT!" : "HIT!";
            // No floating words: the crosshair hit-flash, splinters and haptics carry the feedback.
        }

        public void OnCastleDamaged()
        {
            m_CastleBar.shake = 1f;
        }

        void Float(Vector3 world, string msg, Color c, float scale)
        {
            if (!cam) return;
            var sp = cam.WorldToScreenPoint(world);
            if (sp.z < 0) return;
            Text f = null;
            foreach (var x in m_Floaters) if (!x.enabled) { f = x; break; }
            if (f == null) f = m_Floaters[0];
            StartCoroutine(FloatRoutine(f, sp, msg, c, scale));
        }

        IEnumerator FloatRoutine(Text f, Vector3 screen, string msg, Color c, float scale)
        {
            f.enabled = true;
            f.text = msg;
            var rt = f.rectTransform;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(m_Root, screen, null, out var local);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            for (float t = 0; t < 1f; t += Time.unscaledDeltaTime)
            {
                rt.anchoredPosition = local + new Vector2(0, 60 + t * 120);
                rt.localScale = Vector3.one * scale * (t < 0.15f ? Mathf.Lerp(1.8f, 1f, t / 0.15f) : 1f);
                f.color = new Color(c.r, c.g, c.b, 1f - Mathf.Clamp01((t - 0.6f) / 0.4f));
                yield return null;
            }
            f.enabled = false;
        }

        // ---------------------------------------------------------------- per frame
        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            var bow = m_Game ? m_Game.bow : null;
            float draw = bow ? bow.CurrentDraw : 0f;
            if (m_DrawRing.enabled)
            {
                m_DrawRing.fillAmount = draw;
                m_DrawRing.color = Color.Lerp(new Color(1, 0.85f, 0.45f, 0.9f), new Color(1f, 0.4f, 0.2f, 1f), Mathf.InverseLerp(0.8f, 1f, draw));
            }
            bool locked = bow && bow.LockedPoint.HasValue;
            m_Crosshair.rectTransform.localScale = Vector3.one * (1f - draw * 0.35f) * (locked ? 0.85f + 0.08f * Mathf.Sin(Time.unscaledTime * 14f) : 1f);
            m_Crosshair.color = locked ? new Color(1f, 0.3f, 0.2f, 1f) : new Color(1, 1, 1, 0.85f);
            m_HitFlash = Mathf.Max(0, m_HitFlash - dt * 3f);
            m_HitMarker.color = new Color(1f, 0.3f, 0.15f, m_HitFlash);
            m_HitMarker.rectTransform.localScale = Vector3.one * (1f + (1f - m_HitFlash) * 0.4f);

            if (m_Castle) UpdateBar(m_CastleBar, CastleHealth(), dt);
            if (m_Catapult) UpdateBar(m_CatBar, m_Catapult.Health / m_Catapult.MaxHealth, dt);
            else UpdateBar(m_CatBar, 0, dt);
            FollowCatapult();
            ApplySafeArea();
            UpdateHint();
            UpdateArrows();
        }

        // What actually loses you the game: the gate (raid) or the keep (bombardment), whichever is worse.
        float CastleHealth()
        {
            float f = 1f;
            var gate = m_Castle.Gate;
            if (gate) f = Mathf.Min(f, gate.Health / gate.maxHealth);
            if (m_Castle.keep) f = Mathf.Min(f, m_Castle.keep.Health / m_Castle.keep.maxHealth);
            return f;
        }

        // One-time onboarding: shown in battle until the first arrow is loosed, then never again this session.
        static bool s_HasFired;
        void UpdateHint()
        {
            if (!m_Game) return;
            if (!s_HasFired && m_Game.bow && m_Game.bow.CurrentDraw > 0.5f) s_HasFired = true;
            if (m_Game.State == GameManager.Phase.Battle)
            {
                string tracking = m_Game.TrackingProblem;
                m_Prompt.text = tracking ?? (s_HasFired ? "" : "HOLD TO DRAW  -  RELEASE TO FIRE");
            }
        }

        void ApplySafeArea()
        {
            var sa = Screen.safeArea;
            if (sa == m_LastSafe || Screen.width == 0) return;
            m_LastSafe = sa;
            m_Safe.anchorMin = new Vector2(sa.xMin / Screen.width, sa.yMin / Screen.height);
            m_Safe.anchorMax = new Vector2(sa.xMax / Screen.width, sa.yMax / Screen.height);
            m_Safe.offsetMin = m_Safe.offsetMax = Vector2.zero;
        }

        void FollowCatapult()
        {
            bool show = cam && m_Catapult && m_Catapult.Alive && m_Game && m_Game.State >= GameManager.Phase.Intro;
            Vector3 sp = Vector3.zero;
            if (show)
            {
                var top = m_Catapult.AimCenter + Vector3.up * 13f;
                sp = cam.WorldToScreenPoint(top);
                show = sp.z > 0 && sp.x > 0 && sp.x < Screen.width && sp.y > 0 && sp.y < Screen.height;
            }
            m_CatBar.root.gameObject.SetActive(show);
            if (!show) return;
            var canvas = m_Root.GetComponent<Canvas>();
            var uiCam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(m_Root, sp, uiCam, out var local);
            m_CatBar.root.anchoredPosition = local;
        }

        void UpdateBar(Bar b, float v, float dt)
        {
            b.value = MathExt.Damp(b.value, v, 18f, dt);
            b.ghostValue = b.ghostValue > b.value ? Mathf.Max(b.value, b.ghostValue - dt * 0.35f) : b.value;
            b.fill.fillAmount = b.value;
            b.ghost.fillAmount = b.ghostValue;
            b.shake = Mathf.Max(0, b.shake - dt * 3f);
            b.root.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(Time.unscaledTime * 60f) * b.shake * 3f);
            b.root.localScale = Vector3.one * b.baseScale * (1f + b.shake * 0.08f);
        }

        void UpdateArrows()
        {
            int n = 0;
            if (cam && m_Game && m_Game.State == GameManager.Phase.Battle)
            {
                if (m_Catapult && m_Catapult.Alive) Point(ref n, m_Catapult.AimCenter, new Color(1f, 0.35f, 0.25f, 0.9f), 1f);
                foreach (var f in Fireball.Active)
                    if (f && !f.NearMiss) Point(ref n, f.transform.position, new Color(1f, 0.6f, 0.1f, 1f), 1.2f + 0.2f * Mathf.Sin(Time.time * 20f));
            }
            for (int i = n; i < m_Arrows.Count; i++) m_Arrows[i].enabled = false;
        }

        void Point(ref int n, Vector3 world, Color c, float scale)
        {
            if (n >= m_Arrows.Count) return;
            var vp = cam.WorldToViewportPoint(world);
            bool behind = vp.z < 0;
            if (!behind && vp.x > 0.03f && vp.x < 0.97f && vp.y > 0.05f && vp.y < 0.95f) return;
            var dir = new Vector2(vp.x - 0.5f, vp.y - 0.5f);
            if (behind) dir = -dir;
            if (dir.sqrMagnitude < 1e-4f) dir = Vector2.down;
            dir.Normalize();
            var a = m_Arrows[n++];
            a.enabled = true;
            a.color = c;
            var size = m_Root.rect.size;
            float k = Mathf.Min((size.x * 0.5f - 70) / Mathf.Max(0.001f, Mathf.Abs(dir.x)), (size.y * 0.5f - 70) / Mathf.Max(0.001f, Mathf.Abs(dir.y)));
            a.rectTransform.anchorMin = a.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            a.rectTransform.anchoredPosition = dir * k;
            a.rectTransform.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f);
            a.rectTransform.localScale = Vector3.one * scale;
        }
    }
}
