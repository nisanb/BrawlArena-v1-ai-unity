using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

namespace BrawlArena
{
    /// <summary>One playable brawler archetype, configured in the scene roster.</summary>
    [Serializable]
    public class BrawlerDefinition
    {
        public string id;
        public string displayName;
        public string role;
        public GameObject prefab;
        public string animSuffix;
        public string[] attackStates;
        public float maxHealth = 100f;
        public float damage = 20f;
        public float attackRange = 2.2f;
        public float attackRadius = 1.5f;
        public float cooldown = 0.9f;
        public float hitDelay = 0.35f;
        public float moveLock = 0.45f;
        public float moveSpeed = 5f;
        public float autoAimRange = 3.5f;
        public GameObject projectilePrefab;
        public float projectileSpeed = 16f;
        public GameObject swingVfx;
        public GameObject impactVfx;
        public GameObject koVfx;
        public GameObject spawnVfx;
    }

    /// <summary>
    /// Pre-match flow: character select screen -> loading screen -> spawn the
    /// picked brawler as the player plus randomized bots, then start the match.
    /// All UI is built in code, like BrawlHUD. When Automation/autopilot.flag
    /// exists (editor test harness), a random character is picked automatically
    /// and the player is bot-driven so the match can run unattended.
    /// </summary>
    public class GameFlow : MonoBehaviour
    {
        public BrawlerDefinition[] roster;

        Canvas canvas;
        GameObject selectPanel;
        GameObject loadingPanel;
        Image loadingFill;
        Text loadingTip;
        Font font;

        static readonly string[] Tips =
        {
            "TIP: HOLD ATTACK TO KEEP SWINGING",
            "TIP: SPRINT AWAY WHEN YOUR HEALTH RUNS LOW",
            "TIP: RANGED BRAWLERS MELT UP CLOSE — DIVE THEM",
            "TIP: KOs NEAR YOUR SPAWN ARE EASIER TO FOLLOW UP",
            "TIP: WATCH YOUR STAMINA — AN EMPTY BAR MEANS NO ESCAPE",
        };

        static bool AutopilotRequested
        {
            get
            {
                if (!Application.isEditor) return false;
                string flag = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Automation", "autopilot.flag");
                return File.Exists(flag);
            }
        }

        void Start()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildUi();
            if (BrawlHUD.Instance != null) BrawlHUD.Instance.SetGameplayVisible(false);
            if (AutopilotRequested) StartCoroutine(AutoPick());
        }

        IEnumerator AutoPick()
        {
            yield return new WaitForSeconds(0.8f);
            if (selectPanel != null && selectPanel.activeSelf)
                Pick(UnityEngine.Random.Range(0, roster.Length), true);
        }

        void Pick(int index, bool autopilot)
        {
            if (!selectPanel.activeSelf) return;
            selectPanel.SetActive(false);
            StartCoroutine(LoadAndSpawn(index, autopilot));
        }

        IEnumerator LoadAndSpawn(int index, bool autopilot)
        {
            loadingPanel.SetActive(true);
            loadingTip.text = Tips[UnityEngine.Random.Range(0, Tips.Length)];
            float t = 0f;
            const float duration = 1.8f;
            while (t < duration)
            {
                t += Time.deltaTime;
                loadingFill.fillAmount = Mathf.SmoothStep(0f, 1f, t / duration);
                yield return null;
            }

            SpawnAll(index, autopilot);
            loadingPanel.SetActive(false);
            if (BrawlHUD.Instance != null) BrawlHUD.Instance.SetGameplayVisible(true);
            if (MatchManager.Instance != null) MatchManager.Instance.BeginMatch();
        }

        void SpawnAll(int playerIndex, bool autopilot)
        {
            var mm = MatchManager.Instance;
            Vector3 SpawnPos(Transform[] set, int i) =>
                set != null && i < set.Length && set[i] != null ? set[i].position : Vector3.zero;

            // Random permutation of the remaining roster for the five bots.
            var others = new List<int>();
            for (int i = 0; i < roster.Length; i++)
                if (i != playerIndex) others.Add(i);
            for (int i = others.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (others[i], others[j]) = (others[j], others[i]);
            }

            var player = Spawn(roster[playerIndex], TeamId.Blue, SpawnPos(mm.blueSpawns, 0), !autopilot);
            Spawn(roster[others[0]], TeamId.Blue, SpawnPos(mm.blueSpawns, 1), false);
            Spawn(roster[others[1]], TeamId.Blue, SpawnPos(mm.blueSpawns, 2), false);
            Spawn(roster[others[2]], TeamId.Red, SpawnPos(mm.redSpawns, 0), false);
            Spawn(roster[others[3]], TeamId.Red, SpawnPos(mm.redSpawns, 1), false);
            Spawn(roster[others[4]], TeamId.Red, SpawnPos(mm.redSpawns, 2), false);

            var cam = UnityEngine.Object.FindFirstObjectByType<BrawlCamera>();
            if (cam != null) cam.SetTarget(player.transform);
        }

        public static BrawlerController Spawn(BrawlerDefinition def, TeamId team, Vector3 pos, bool asHumanPlayer)
        {
            var go = UnityEngine.Object.Instantiate(def.prefab, pos, Quaternion.Euler(0f, team == TeamId.Blue ? 0f : 180f, 0f));
            go.name = def.displayName;

            var health = go.GetComponent<Health>();
            if (health == null) health = go.AddComponent<Health>();
            health.SetMax(def.maxHealth);

            if (asHumanPlayer)
            {
                var cc = go.AddComponent<CharacterController>();
                cc.center = new Vector3(0f, 1f, 0f);
                cc.radius = 0.4f;
                cc.height = 1.8f;
            }
            else
            {
                var capsule = go.AddComponent<CapsuleCollider>();
                capsule.center = new Vector3(0f, 1f, 0f);
                capsule.radius = 0.4f;
                capsule.height = 1.9f;
                var agent = go.AddComponent<NavMeshAgent>();
                agent.radius = 0.45f;
                agent.height = 1.9f;
                agent.speed = def.moveSpeed;
                agent.acceleration = 40f;
                agent.angularSpeed = 720f;
                agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;
            }

            var ctrl = go.AddComponent<BrawlerController>();
            ctrl.displayName = def.displayName;
            ctrl.team = team;
            ctrl.animSuffix = def.animSuffix;
            ctrl.attackStates = def.attackStates;
            ctrl.moveSpeed = def.moveSpeed;
            ctrl.attackDamage = def.damage;
            ctrl.attackRange = def.attackRange;
            ctrl.attackRadius = def.attackRadius;
            ctrl.attackCooldown = def.cooldown;
            ctrl.attackHitDelay = def.hitDelay;
            ctrl.attackMoveLock = def.moveLock;
            ctrl.autoAimRange = def.autoAimRange;
            ctrl.projectilePrefab = def.projectilePrefab;
            ctrl.projectileSpeed = def.projectileSpeed;
            ctrl.swingVfx = def.swingVfx;
            ctrl.impactVfx = def.impactVfx;
            ctrl.koVfx = def.koVfx;
            ctrl.spawnVfx = def.spawnVfx;

            if (asHumanPlayer) go.AddComponent<PlayerBrawlerInput>();
            else go.AddComponent<AIBrawler>();
            return ctrl;
        }

        // ---------------- UI construction ----------------

        void BuildUi()
        {
            var canvasGo = new GameObject("FlowCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            BuildSelectPanel(canvasGo.transform);
            BuildLoadingPanel(canvasGo.transform);
        }

        void BuildSelectPanel(Transform root)
        {
            selectPanel = NewRect("CharacterSelect", root);
            Stretch((RectTransform)selectPanel.transform);
            var dim = selectPanel.AddComponent<Image>();
            dim.color = new Color(0.05f, 0.06f, 0.1f, 0.88f);

            var title = MakeText("Title", selectPanel.transform, "CHOOSE YOUR BRAWLER", 84, new Color(1f, 0.9f, 0.45f));
            var trt = title.rectTransform;
            trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.9f);
            trt.sizeDelta = new Vector2(1600f, 120f);

            for (int i = 0; i < roster.Length; i++)
            {
                int index = i;
                var def = roster[i];
                var card = NewRect("Card_" + def.id, selectPanel.transform);
                var rt = (RectTransform)card.transform;
                int col = i % 3;
                int row = i / 3;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2((col - 1) * 470f, 90f - row * 380f);
                rt.sizeDelta = new Vector2(420f, 340f);

                var bg = card.AddComponent<Image>();
                bg.sprite = null;
                bg.color = new Color(0.13f, 0.16f, 0.24f, 0.98f);
                var button = card.AddComponent<Button>();
                var colors = button.colors;
                colors.highlightedColor = new Color(1.2f, 1.2f, 1.2f, 1f);
                colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
                button.colors = colors;
                button.onClick.AddListener(() => Pick(index, false));

                var name = MakeText("Name", card.transform, def.displayName, 52, Color.white);
                var nrt = name.rectTransform;
                nrt.anchorMin = nrt.anchorMax = new Vector2(0.5f, 0.82f);
                nrt.sizeDelta = new Vector2(400f, 70f);

                var role = MakeText("Role", card.transform, def.role, 30, new Color(0.7f, 0.85f, 1f));
                var rrt = role.rectTransform;
                rrt.anchorMin = rrt.anchorMax = new Vector2(0.5f, 0.65f);
                rrt.sizeDelta = new Vector2(400f, 50f);

                AddStatBar(card.transform, "HP", def.maxHealth / 150f, 0.48f, new Color(0.4f, 0.9f, 0.45f));
                AddStatBar(card.transform, "DMG", def.damage / 30f, 0.34f, new Color(1f, 0.55f, 0.35f));
                AddStatBar(card.transform, "SPD", (def.moveSpeed - 4f) / 1.6f, 0.2f, new Color(0.45f, 0.75f, 1f));

                string kind = def.projectilePrefab != null ? "RANGED" : "MELEE";
                var kindText = MakeText("Kind", card.transform, kind, 26, new Color(1f, 0.9f, 0.5f));
                var krt = kindText.rectTransform;
                krt.anchorMin = krt.anchorMax = new Vector2(0.5f, 0.07f);
                krt.sizeDelta = new Vector2(400f, 40f);
            }
        }

        void AddStatBar(Transform card, string label, float value01, float anchorY, Color color)
        {
            var text = MakeText(label, card, label, 24, new Color(1f, 1f, 1f, 0.8f));
            var trt = text.rectTransform;
            trt.anchorMin = trt.anchorMax = new Vector2(0.14f, anchorY);
            trt.sizeDelta = new Vector2(90f, 34f);

            var barBg = NewRect(label + "Bg", card);
            var brt = (RectTransform)barBg.transform;
            brt.anchorMin = brt.anchorMax = new Vector2(0.6f, anchorY);
            brt.sizeDelta = new Vector2(240f, 16f);
            barBg.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

            var fill = NewRect(label + "Fill", barBg.transform);
            var frt = (RectTransform)fill.transform;
            frt.anchorMin = new Vector2(0f, 0f);
            frt.anchorMax = new Vector2(Mathf.Clamp01(value01), 1f);
            frt.offsetMin = new Vector2(2f, 2f);
            frt.offsetMax = new Vector2(-2f, -2f);
            fill.AddComponent<Image>().color = color;
        }

        void BuildLoadingPanel(Transform root)
        {
            loadingPanel = NewRect("Loading", root);
            Stretch((RectTransform)loadingPanel.transform);
            var dim = loadingPanel.AddComponent<Image>();
            dim.color = new Color(0.03f, 0.04f, 0.07f, 1f);

            var title = MakeText("Title", loadingPanel.transform, "ENTERING THE ARENA...", 72, Color.white);
            var trt = title.rectTransform;
            trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.58f);
            trt.sizeDelta = new Vector2(1600f, 110f);

            var barBg = NewRect("BarBg", loadingPanel.transform);
            var brt = (RectTransform)barBg.transform;
            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.45f);
            brt.sizeDelta = new Vector2(900f, 26f);
            barBg.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.12f);

            var fillGo = NewRect("BarFill", barBg.transform);
            Stretch((RectTransform)fillGo.transform);
            loadingFill = fillGo.AddComponent<Image>();
            loadingFill.sprite = WhiteSprite();
            loadingFill.color = new Color(1f, 0.85f, 0.3f);
            loadingFill.type = Image.Type.Filled;
            loadingFill.fillMethod = Image.FillMethod.Horizontal;
            loadingFill.fillAmount = 0f;

            loadingTip = MakeText("Tip", loadingPanel.transform, "", 34, new Color(1f, 1f, 1f, 0.75f));
            var tiprt = loadingTip.rectTransform;
            tiprt.anchorMin = tiprt.anchorMax = new Vector2(0.5f, 0.34f);
            tiprt.sizeDelta = new Vector2(1500f, 60f);

            loadingPanel.SetActive(false);
        }

        static GameObject NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        Text MakeText(string name, Transform parent, string content, int size, Color color)
        {
            var go = NewRect(name, parent);
            var txt = go.AddComponent<Text>();
            txt.font = font;
            txt.text = content;
            txt.fontSize = size;
            txt.fontStyle = FontStyle.Bold;
            txt.color = color;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            txt.verticalOverflow = VerticalWrapMode.Overflow;
            txt.raycastTarget = false;
            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.7f);
            outline.effectDistance = new Vector2(2f, -2f);
            return txt;
        }

        static Sprite whiteSprite;

        static Sprite WhiteSprite()
        {
            if (whiteSprite != null) return whiteSprite;
            var tex = Texture2D.whiteTexture;
            whiteSprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            return whiteSprite;
        }
    }
}
