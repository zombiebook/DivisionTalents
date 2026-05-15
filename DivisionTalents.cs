using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Duckov.Modding;
using HarmonyLib;
using ItemStatsSystem;

namespace DivisionTalents
{
    /// <summary>
    /// Division 2 Weapon Talents Mod for Escape from Duckov
    /// Real-time talent system using Harmony patches (Duckov-totem style)
    /// </summary>
    public class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private static readonly string ModVersion = "2.0.0";
        private TalentManager? _talentManager;
        private static Harmony? _harmony;

        protected override void OnAfterSetup()
        {
            try
            {
                Debug.Log($"[DivisionTalents] ★ Initializing v{ModVersion} ★");

                GameObject go = new GameObject("DivisionTalentsRoot");
                UnityEngine.Object.DontDestroyOnLoad(go);

                _talentManager = go.AddComponent<TalentManager>();
                Debug.Log("[DivisionTalents] TalentManager created");

                // Harmony 패치 적용
                _harmony = new Harmony("com.divisiontalents.mod");
                _harmony.PatchAll();

                var patchedMethods = _harmony.GetPatchedMethods();
                Debug.Log($"[DivisionTalents] ★ Harmony patches applied: {patchedMethods.Count()} methods ★");

                Debug.Log("[DivisionTalents] ★ Mod initialized successfully! ★");
                Debug.Log("[DivisionTalents] Press T to open talent selector");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DivisionTalents] Initialization failed: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }

    public enum TalentType
    {
        Offensive,
        Defensive,
        Utility
    }

    public class WeaponTalent
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public TalentType Type { get; set; }
        public bool IsPassive { get; set; }
        public bool IsActive { get; set; }
        public float Duration { get; set; }
        public float Cooldown { get; set; }
        public float LastProcTime { get; set; }
        public Dictionary<string, float> Stats { get; set; } = new Dictionary<string, float>();

        public bool CanProc(float currentTime) => (currentTime - LastProcTime) >= Cooldown;
        public bool IsExpired(float currentTime) => !IsPassive && IsActive && (currentTime - LastProcTime) >= Duration;

        public void Activate(float currentTime)
        {
            IsActive = true;
            LastProcTime = currentTime;
        }

        public void Deactivate() => IsActive = false;
    }

    /// <summary>
    /// 탤런트 관리자 - 게임 이벤트를 받아 탤런트를 처리
    /// </summary>
    public partial class TalentManager : MonoBehaviour
    {
        public static TalentManager? Instance { get; private set; }

        private Dictionary<string, WeaponTalent> _talents = new Dictionary<string, WeaponTalent>();
        private string? _equippedTalentId = null;

        // UI 상태
        private bool _showTalentSelector = false;
        private bool _showBuffIcons = true;
        private bool _debugMode = false;

        // UI Rect
        private Rect _selectorRect = new Rect(0, 0, 600, 700);
        private Vector2 _scrollPosition = Vector2.zero;

        // 통계
        private int _killCount = 0;
        private int _critCount = 0;
        private int _reloadCount = 0;
        private int _emptyReloadCount = 0;
        private int _headshotKillCount = 0;

        // Fast Hands 스택
        private int _fastHandsStacks = 0;
        private float _fastHandsLastStackTime = 0f;
        private const float FAST_HANDS_DECAY_TIME = 5f; // 5초 후 스택 감소

        // First Blood: 재장전 후 첫 사격 부스트 사용 가능 여부
        private bool _firstBloodAvailable = false;

        // Actum Est: 적 명중 스택 (100 도달 시 다음 탄창 부스트)
        private int _actumEstStacks = 0;
        private bool _actumEstChargeReady = false; // 100스택 도달, 다음 재장전 대기
        private bool _actumEstActive = false;       // 다음 탄창 사용 중 (재장전 후 활성)

        // Septic Shock: 적별 중첩 추적 (Health 인스턴스 ID → 스택/타이머)
        private Dictionary<int, int> _septicStacks = new Dictionary<int, int>();
        private Dictionary<int, float> _septicTimers = new Dictionary<int, float>();

        // 버프 아이콘 텍스처
        private Texture2D? _bgTexture;
        private Texture2D? _activeBgTexture;
        private Texture2D? _buffActiveBgTexture;
        private Texture2D? _glowTexture;
        private Dictionary<string, Texture2D> _talentIcons = new Dictionary<string, Texture2D>();
        private Dictionary<string, Color> _talentColors = new Dictionary<string, Color>();

        // GUI 스타일 (캐싱)
        private GUIStyle? _labelStyle;
        private GUIStyle? _headerStyle;
        private GUIStyle? _buttonStyle;
        private GUIStyle? _selectedButtonStyle;
        private GUIStyle? _cooldownStyle;
        private GUIStyle? _stackStyle;

        private void Awake()
        {
            Instance = this;
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
            
            InitializeTalentColors();
            InitializeTalents();
            CreateBuffTextures();
            LoadGameIcons();

            // 화면 크기에 맞게 UI 위치 조정
            _selectorRect = new Rect(Screen.width / 2 - 300, Screen.height / 2 - 350, 600, 700);

            Debug.Log("[DivisionTalents] TalentManager initialized");
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void InitializeTalentColors()
        {
            _talentColors["close_and_personal"] = new Color(1f, 0.3f, 0.3f);
            _talentColors["frenzy"] = new Color(1f, 0.5f, 0.2f);
            _talentColors["ranger"] = new Color(1f, 0.4f, 0.4f);
            _talentColors["optimist"] = new Color(1f, 0.6f, 0.3f);
            _talentColors["strained"] = new Color(0.9f, 0.2f, 0.2f);
            _talentColors["preservation"] = new Color(0.3f, 0.6f, 1f);
            _talentColors["reformation"] = new Color(0.4f, 0.7f, 1f);
            _talentColors["vindictive"] = new Color(0.5f, 0.8f, 1f);
            _talentColors["fast_hands"] = new Color(0.4f, 1f, 0.4f);
            _talentColors["measured"] = new Color(1f, 1f, 0.3f);
            _talentColors["allegro"] = new Color(0.6f, 1f, 0.5f);
            _talentColors["extra"] = new Color(1f, 0.55f, 0.15f); // 주황색 (이미지와 동일)
            _talentColors["electromagnetic_accelerator"] = new Color(0.5f, 0.7f, 1f); // 푸른 전자기 느낌

            // === Division 2 추가 탤런트 색상 ===
            // 공격형 - 빨강 계열
            _talentColors["boomerang"] = new Color(1f, 0.45f, 0.35f);
            _talentColors["outsider_edge"] = new Color(1f, 0.35f, 0.45f);
            _talentColors["killer"] = new Color(0.95f, 0.25f, 0.35f);
            _talentColors["first_blood"] = new Color(1f, 0.65f, 0.4f);

            // 방어형 - 파랑 계열
            _talentColors["stable"] = new Color(0.55f, 0.75f, 1f);
            _talentColors["perpetuation"] = new Color(0.4f, 0.55f, 0.95f);
            _talentColors["quickstep"] = new Color(0.3f, 0.9f, 0.9f);
            _talentColors["septic_shock"] = new Color(0.6f, 0.9f, 0.2f); // 독 느낌

            // 유틸리티 - 초록/노랑 계열
            _talentColors["actum_est"] = new Color(1f, 0.85f, 0.2f); // 노란색 (전기 느낌)
        }

        private void InitializeTalents()
        {
            // === 공격형 탤런트 ===
            AddTalent(new WeaponTalent
            {
                Id = "close_and_personal",
                Name = "Close & Personal",
                Description = L("근거리 킬: 5초간 +50% 데미지", "Close kill: +50% DMG for 5s", "近距離キル: 5秒間+50%ダメージ"),
                Type = TalentType.Offensive,
                IsPassive = false,
                Duration = 5f,
                Cooldown = 0f,
                Stats = { { "damage_bonus", 0.5f }, { "trigger_range", 7f } }
            });

            AddTalent(new WeaponTalent
            {
                Id = "frenzy",
                Name = "Frenzy",
                Description = L("빈 탄창 재장전: 7초간 +20% 데미지, +35% 연사력", "Empty reload: +20% DMG, +35% RPM for 7s", "空マガジンリロード: 7秒間+20%ダメージ, +35%連射"),
                Type = TalentType.Offensive,
                IsPassive = false,
                Duration = 7f,
                Cooldown = 0f,
                Stats = { { "damage_bonus", 0.2f }, { "fire_rate_bonus", 0.35f } }
            });

            AddTalent(new WeaponTalent
            {
                Id = "ranger",
                Name = "Ranger",
                Description = L("거리 데미지: 5m당 +2% (최대 +40%)", "Distance DMG: +2% per 5m (max +40%)", "距離ダメージ: 5mごとに+2% (最大+40%)"),
                Type = TalentType.Offensive,
                IsPassive = true,
                IsActive = true,
                Stats = { { "bonus_per_5m", 0.02f }, { "max_bonus", 0.4f } }
            });

            AddTalent(new WeaponTalent
            {
                Id = "optimist",
                Name = "Optimist",
                Description = L("탄창이 비워질수록 데미지 증가 (최대 +25%)", "More DMG as mag empties (max +25%)", "マガジンが空になるほどダメージ増加 (最大+25%)"),
                Type = TalentType.Offensive,
                IsPassive = true,
                IsActive = true,
                Stats = { { "max_bonus", 0.25f } }
            });

            AddTalent(new WeaponTalent
            {
                Id = "strained",
                Name = "Strained",
                Description = L("체력 5% 잃을 때마다 +10% 크리티컬 데미지", "+10% crit DMG per 5% HP lost", "体力5%減少ごとに+10%クリティカルダメージ"),
                Type = TalentType.Offensive,
                IsPassive = true,
                IsActive = true,
                Stats = { { "crit_per_5_percent", 0.1f } }
            });

            // === 방어형 탤런트 ===
            AddTalent(new WeaponTalent
            {
                Id = "preservation",
                Name = "Preservation",
                Description = L("킬: 3초간 체력 5% 회복", "Kill: Heal 5% HP over 3s", "キル: 3秒間体力5%回復"),
                Type = TalentType.Defensive,
                IsPassive = false,
                Duration = 3f,
                Cooldown = 1f,
                Stats = { { "heal_amount", 0.05f } }
            });

            AddTalent(new WeaponTalent
            {
                Id = "reformation",
                Name = "Reformation",
                Description = L("헤드샷 킬: 체력 5% 즉시 회복", "Headshot kill: Instant 5% HP", "ヘッドショットキル: 即座に体力5%回復"),
                Type = TalentType.Defensive,
                IsPassive = false,
                Duration = 0f,
                Cooldown = 0.5f,
                Stats = { { "heal_amount", 0.05f } }
            });

            AddTalent(new WeaponTalent
            {
                Id = "vindictive",
                Name = "Vindictive",
                Description = L("킬: 5초간 +20% 크리티컬 확률", "Kill: +20% crit chance for 5s", "キル: 5秒間+20%クリティカル率"),
                Type = TalentType.Defensive,
                IsPassive = false,
                Duration = 5f,
                Cooldown = 0f,
                Stats = { { "crit_bonus", 0.2f } }
            });

            // === 유틸리티 탤런트 ===
            AddTalent(new WeaponTalent
            {
                Id = "fast_hands",
                Name = "Fast Hands",
                Description = L("크리티컬: 재장전 속도 -1% (최대 30스택)", "Crit: -1% reload time (max 30 stacks)", "クリティカル: リロード速度-1% (最大30スタック)"),
                Type = TalentType.Utility,
                IsPassive = false,
                Duration = 5f,
                Cooldown = 0f,
                Stats = { { "reload_reduction", 0.01f }, { "max_stacks", 30f } }
            });

            AddTalent(new WeaponTalent
            {
                Id = "measured",
                Name = "Measured",
                Description = L("탄창 상단 +15% 연사력, 하단 +20% 데미지", "Top half: +15% RPM, Bottom: +20% DMG", "マガジン上半分+15%連射, 下半分+20%ダメージ"),
                Type = TalentType.Utility,
                IsPassive = true,
                IsActive = true,
                Stats = { { "top_fire_rate", 0.15f }, { "bottom_damage", 0.2f } }
            });

            AddTalent(new WeaponTalent
            {
                Id = "allegro",
                Name = "Allegro",
                Description = L("+10% 연사력 (항상 적용)", "+10% fire rate (passive)", "+10%連射速度 (パッシブ)"),
                Type = TalentType.Utility,
                IsPassive = true,
                IsActive = true,
                Stats = { { "fire_rate_bonus", 0.1f } }
            });

            AddTalent(new WeaponTalent
            {
                Id = "extra",
                Name = "Extra",
                Description = L("+50% 탄창 용량 (항상 적용)", "+50% magazine capacity (passive)", "+50%マガジン容量 (パッシブ)"),
                Type = TalentType.Utility,
                IsPassive = true,
                IsActive = true,
                Stats = { { "mag_capacity_bonus", 0.5f } }
            });

            AddTalent(new WeaponTalent
            {
                Id = "electromagnetic_accelerator",
                Name = "Electromagnetic Accelerator",
                Description = L("조준(우클릭) 중 +50% 데미지", "ADS (right click): +50% DMG", "エイム(右クリック)中+50%ダメージ"),
                Type = TalentType.Utility,
                IsPassive = true,
                IsActive = true,
                Stats = { { "damage_bonus", 0.5f } }
            });

            // === Division 2 추가 탤런트 ===

            // ▶ 공격형 추가
            AddTalent(new WeaponTalent
            {
                Id = "boomerang",
                Name = "Boomerang",
                Description = L("크리티컬 시 탄환 1발 복구 + 5초간 +50% 데미지", "Crit: Return 1 bullet + +50% DMG for 5s", "クリティカル: 弾丸1発回復 + 5秒間+50%ダメージ"),
                Type = TalentType.Offensive,
                IsPassive = false,
                Duration = 5f,
                Cooldown = 0f,
                Stats = { { "damage_bonus", 0.5f } }
            });

            AddTalent(new WeaponTalent
            {
                Id = "outsider_edge",
                Name = "Outsider Edge",
                Description = L("헤드샷 시 +25% 데미지 (4초)", "Headshot: +25% DMG for 4s", "ヘッドショット: 4秒間+25%ダメージ"),
                Type = TalentType.Offensive,
                IsPassive = false,
                Duration = 4f,
                Cooldown = 0f,
                Stats = { { "damage_bonus", 0.25f } }
            });

            AddTalent(new WeaponTalent
            {
                Id = "killer",
                Name = "Killer",
                Description = L("킬: +50% 크리티컬 데미지 (5초)", "Kill: +50% crit DMG for 5s", "キル: 5秒間+50%クリティカルダメージ"),
                Type = TalentType.Offensive,
                IsPassive = false,
                Duration = 5f,
                Cooldown = 0f,
                Stats = { { "crit_dmg_bonus", 0.5f } }
            });

            AddTalent(new WeaponTalent
            {
                Id = "first_blood",
                Name = "First Blood",
                Description = L("첫 사격 +30% 데미지 (재장전 후 리셋)", "First shot +30% DMG (resets on reload)", "初弾+30%ダメージ (リロードでリセット)"),
                Type = TalentType.Offensive,
                IsPassive = false,
                Duration = 60f,
                Cooldown = 0f,
                Stats = { { "damage_bonus", 0.3f } }
            });

            // ▶ 방어형 추가
            AddTalent(new WeaponTalent
            {
                Id = "stable",
                Name = "Stable",
                Description = L("반동 제어 +30% (항상 적용)", "+30% recoil control (passive)", "+30%反動制御 (パッシブ)"),
                Type = TalentType.Defensive,
                IsPassive = true,
                IsActive = true,
                Stats = { { "recoil_bonus", 0.30f } }
            });

            AddTalent(new WeaponTalent
            {
                Id = "perpetuation",
                Name = "Perpetuation",
                Description = L("헤드샷 킬: +25% 다음 데미지 (3초)", "Headshot kill: +25% DMG for 3s", "ヘッドショットキル: 3秒間+25%ダメージ"),
                Type = TalentType.Defensive,
                IsPassive = false,
                Duration = 3f,
                Cooldown = 0f,
                Stats = { { "damage_bonus", 0.25f } }
            });

            AddTalent(new WeaponTalent
            {
                Id = "septic_shock",
                Name = "Septic Shock",
                Description = L("같은 적 명중 시 중첩. 3중첩 스턴, 6중첩 쇼크, 7중첩 +20% 데미지", "Hit same enemy: 3=stun, 6=shock, 7=+20% DMG", "同じ敵に命中で重複. 3スタン, 6ショック, 7で+20%ダメージ"),
                Type = TalentType.Defensive,
                IsPassive = true,
                IsActive = true,
                Stats = { { "max_stacks", 7f }, { "damage_bonus_at_max", 0.20f }, { "duration", 10f } }
            });

            AddTalent(new WeaponTalent
            {
                Id = "quickstep",
                Name = "Quickstep",
                Description = L("이동 속도 +20% (항상 적용)", "+20% movement speed (passive)", "+20%移動速度 (パッシブ)"),
                Type = TalentType.Defensive,
                IsPassive = true,
                IsActive = true,
                Stats = { { "move_speed_bonus", 0.20f } }
            });

            // ▶ 유틸리티 추가
            AddTalent(new WeaponTalent
            {
                Id = "actum_est",
                Name = "Actum Est",
                Description = L("100발 명중 후 다음 탄창 +25% 데미지 + 전기탄", "100 hits: next mag +25% DMG + shock ammo", "100発命中後、次のマガジン+25%ダメージ+電撃弾"),
                Type = TalentType.Utility,
                IsPassive = false,
                Duration = 999f,
                Cooldown = 0f,
                Stats = {
                    { "damage_bonus", 0.25f },
                    { "max_stacks", 100f }
                }
            });

            Debug.Log($"[DivisionTalents] {_talents.Count}개 탤런트 초기화 완료");
        }

        private void AddTalent(WeaponTalent talent) => _talents[talent.Id] = talent;

        /// <summary>
        /// 시스템 언어에 따라 한국어/영어/일본어 텍스트 반환
        /// </summary>
        private static string L(string korean, string english, string japanese)
        {
            var lang = Application.systemLanguage;
            if (lang == SystemLanguage.Japanese) return japanese;
            if (lang == SystemLanguage.Korean) return korean;
            return english;
        }

        public WeaponTalent? GetTalent(string id) => _talents.TryGetValue(id, out var t) ? t : null;
        public IEnumerable<WeaponTalent> GetAllTalents() => _talents.Values;

        public bool IsTalentEquipped(string talentId) => _equippedTalentId == talentId;
        public string? GetEquippedTalentId() => _equippedTalentId;

        // ========== 텍스처 / 아이콘 ==========

        private void CreateBuffTextures()
        {
            _bgTexture = MakeTex(1, 1, new Color(0.1f, 0.1f, 0.1f, 0.7f));
            _activeBgTexture = MakeTex(1, 1, new Color(0.05f, 0.05f, 0.05f, 0.9f));
        }

        private Texture2D MakeTex(int w, int h, Color c)
        {
            var pixels = new Color[w * h];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = c;
            var tex = new Texture2D(w, h);
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        private void LoadGameIcons()
        {
            try
            {
                string modFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";
                string iconFolder = Path.Combine(modFolder, "icons");

                if (!Directory.Exists(iconFolder))
                {
                    Directory.CreateDirectory(iconFolder);
                }

                foreach (var kvp in _talents)
                {
                    string pngPath = Path.Combine(iconFolder, $"{kvp.Key}.png");
                    Color iconColor = _talentColors.TryGetValue(kvp.Key, out var c) ? c : Color.white;

                    Texture2D? tex = null;
                    if (File.Exists(pngPath))
                    {
                        tex = LoadTextureFromFile(pngPath);
                    }

                    if (tex == null)
                    {
                        tex = CreateTalentIcon(kvp.Value, iconColor);
                        try
                        {
                            File.WriteAllBytes(pngPath, tex.EncodeToPNG());
                        }
                        catch { }
                    }

                    if (tex != null) _talentIcons[kvp.Key] = tex;
                }

                Debug.Log($"[DivisionTalents] {_talentIcons.Count}개 아이콘 로드 완료");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DivisionTalents] Icon load failed: {ex.Message}");
            }
        }

        private Texture2D? LoadTextureFromFile(string path)
        {
            try
            {
                byte[] data = File.ReadAllBytes(path);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (tex.LoadImage(data)) return tex;
            }
            catch { }
            return null;
        }

        private Texture2D CreateTalentIcon(WeaponTalent talent, Color baseColor)
        {
            int size = 64;
            var tex = new Texture2D(size, size);
            float center = size / 2f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                    float norm = dist / center;

                    if (norm <= 1f)
                    {
                        var c = Color.Lerp(baseColor, baseColor * 0.3f, norm);
                        c.a = 1f - (norm * 0.3f);
                        tex.SetPixel(x, y, c);
                    }
                    else
                    {
                        tex.SetPixel(x, y, new Color(0, 0, 0, 0));
                    }
                }
            }

            tex.Apply();
            return tex;
        }
    }
}

namespace DivisionTalents
{
    public partial class TalentManager
    {
        // ========== Update ==========

        private void Update()
        {
            float currentTime = Time.time;

            // 재장전 감지 (매 프레임 탄약 폴링)
            try
            {
                var player = CharacterMainControl.Main;
                if (player != null)
                {
                    ReloadDetector.CheckReload(player);
                }
            }
            catch { }

            // 만료된 탤런트 비활성화
            foreach (var talent in _talents.Values)
            {
                if (talent.IsExpired(currentTime))
                {
                    talent.Deactivate();
                    OnTalentExpired(talent);
                    if (_debugMode)
                        Debug.Log($"[DivisionTalents] {talent.Name} 만료");
                }
            }

            // Fast Hands 스택 감소 (5초 동안 크리티컬 없으면 스택 감소)
            if (_fastHandsStacks > 0 && (currentTime - _fastHandsLastStackTime) >= FAST_HANDS_DECAY_TIME)
            {
                // Fast Hands 부스트 제거
                RemoveFastHandsBoost();
                _fastHandsStacks = 0;
                if (_debugMode)
                    Debug.Log("[DivisionTalents] Fast Hands 스택 초기화");
            }

            // 단축키
            if (Input.GetKeyDown(KeyCode.T))
            {
                _showTalentSelector = !_showTalentSelector;
            }
            if (Input.GetKeyDown(KeyCode.F9))
            {
                _debugMode = !_debugMode;
                Debug.Log($"[DivisionTalents] Debug: {(_debugMode ? "ON" : "OFF")}");
            }
            if (Input.GetKeyDown(KeyCode.F10))
            {
                _showBuffIcons = !_showBuffIcons;
            }
        }

        /// <summary>
        /// 탤런트 만료 시 호출 - 게임 스탯 부스트 제거
        /// </summary>
        private void OnTalentExpired(WeaponTalent talent)
        {
            try
            {
                var player = CharacterMainControl.Main;
                if (player == null) return;

                var weapons = StatBoostManager.GetAllEquippedWeapons(player);
                var characterItem = StatBoostManager.GetCharacterItem(player);

                switch (talent.Id)
                {
                    case "frenzy":
                        // 연사력 부스트 제거
                        foreach (var weapon in weapons)
                        {
                            StatBoostManager.RemoveWeaponBoost(weapon, "ShootSpeed");
                            StatBoostManager.RemoveWeaponBoost(weapon, "AttackSpeed");
                        }
                        break;

                    case "vindictive":
                        // 크리티컬 확률 부스트 제거 (있다면)
                        foreach (var weapon in weapons)
                        {
                            StatBoostManager.RemoveWeaponBoost(weapon, "CriticalChance");
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                if (_debugMode)
                    Debug.LogWarning($"[DivisionTalents] OnTalentExpired error: {ex.Message}");
            }
        }

        /// <summary>
        /// 탤런트 활성화 시 호출 - 게임 스탯 부스트 적용
        /// </summary>
        private void OnTalentActivated(WeaponTalent talent)
        {
            try
            {
                var player = CharacterMainControl.Main;
                if (player == null) return;

                var weapons = StatBoostManager.GetAllEquippedWeapons(player);

                switch (talent.Id)
                {
                    case "frenzy":
                        // +35% 연사력
                        foreach (var weapon in weapons)
                        {
                            StatBoostManager.ApplyWeaponBoost(weapon, "ShootSpeed", talent.Stats["fire_rate_bonus"]);
                            StatBoostManager.ApplyWeaponBoost(weapon, "AttackSpeed", talent.Stats["fire_rate_bonus"]);
                        }
                        if (_debugMode)
                            Debug.Log($"[DivisionTalents] Frenzy: +{talent.Stats["fire_rate_bonus"] * 100}% 연사력 적용");
                        break;
                }
            }
            catch (Exception ex)
            {
                if (_debugMode)
                    Debug.LogWarning($"[DivisionTalents] OnTalentActivated error: {ex.Message}");
            }
        }

        /// <summary>
        /// Fast Hands 재장전 속도 부스트 적용 (스택 기반)
        /// </summary>
        private void ApplyFastHandsBoost()
        {
            try
            {
                var player = CharacterMainControl.Main;
                if (player == null) return;

                var characterItem = StatBoostManager.GetCharacterItem(player);
                if (characterItem == null) return;

                // 기존 부스트 제거 후 다시 적용
                StatBoostManager.RemoveCharacterBoost(characterItem, "ReloadSpeedGain");

                var talent = GetTalent("fast_hands");
                if (talent == null) return;

                float boostValue = _fastHandsStacks * talent.Stats["reload_reduction"];
                StatBoostManager.ApplyCharacterBoost(characterItem, "ReloadSpeedGain", boostValue);

                if (_debugMode)
                    Debug.Log($"[DivisionTalents] Fast Hands: ReloadSpeedGain +{boostValue:F2} ({_fastHandsStacks} stacks)");
            }
            catch (Exception ex)
            {
                if (_debugMode)
                    Debug.LogWarning($"[DivisionTalents] ApplyFastHandsBoost error: {ex.Message}");
            }
        }

        private void RemoveFastHandsBoost()
        {
            try
            {
                var player = CharacterMainControl.Main;
                if (player == null) return;

                var characterItem = StatBoostManager.GetCharacterItem(player);
                if (characterItem == null) return;

                StatBoostManager.RemoveCharacterBoost(characterItem, "ReloadSpeedGain");
            }
            catch { }
        }

        /// <summary>
        /// 패시브 탤런트의 항상-적용 부스트 관리
        /// </summary>
        public void ApplyPassiveBoosts()
        {
            try
            {
                if (_equippedTalentId == null) return;
                
                var talent = GetTalent(_equippedTalentId);
                if (talent == null) return;

                var player = CharacterMainControl.Main;
                if (player == null) return;

                var weapons = StatBoostManager.GetAllEquippedWeapons(player);
                var characterItem = StatBoostManager.GetCharacterItem(player);

                if (talent.Id == "allegro")
                {
                    foreach (var weapon in weapons)
                    {
                        StatBoostManager.ApplyWeaponBoost(weapon, "ShootSpeed", talent.Stats["fire_rate_bonus"]);
                        StatBoostManager.ApplyWeaponBoost(weapon, "AttackSpeed", talent.Stats["fire_rate_bonus"]);
                    }
                }
                else if (talent.Id == "stable" && characterItem != null)
                {
                    // Stable: 반동 제어 +30%
                    StatBoostManager.ApplyCharacterBoost(characterItem, "RecoilControl", talent.Stats["recoil_bonus"]);
                }
                else if (talent.Id == "quickstep" && characterItem != null)
                {
                    // Quickstep: 이동 속도 +20%
                    StatBoostManager.ApplyWeaponBoost(characterItem, "WalkSpeed", talent.Stats["move_speed_bonus"]);
                    StatBoostManager.ApplyWeaponBoost(characterItem, "RunSpeed", talent.Stats["move_speed_bonus"]);
                }
                else if (talent.Id == "extra")
                {
                    // Extra: 탄창 용량 +50%
                    Debug.Log($"[DivisionTalents] Extra 적용 시도 - GetAllEquippedWeapons returned {weapons.Count} weapons");

                    // 인벤토리 포함 모든 무기 가져오기
                    var allWeapons = StatBoostManager.GetAllInventoryGuns(player);
                    Debug.Log($"[DivisionTalents] Extra: GetAllInventoryGuns returned {allWeapons.Count} weapons");

                    foreach (var weapon in allWeapons)
                    {
                        StatBoostManager.ApplyMagCapacityBoost(weapon, talent.Stats["mag_capacity_bonus"]);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DivisionTalents] ApplyPassiveBoosts error: {ex.Message}");
            }
        }

        public void RemoveAllPassiveBoosts()
        {
            try
            {
                var player = CharacterMainControl.Main;
                if (player == null) return;

                var weapons = StatBoostManager.GetAllEquippedWeapons(player);
                foreach (var weapon in weapons)
                {
                    StatBoostManager.RemoveWeaponBoost(weapon, "ShootSpeed");
                    StatBoostManager.RemoveWeaponBoost(weapon, "AttackSpeed");
                }

                // Extra 부스트 제거 - 모든 인벤토리 무기에서
                var allWeapons = StatBoostManager.GetAllInventoryGuns(player);
                foreach (var weapon in allWeapons)
                {
                    StatBoostManager.RemoveMagCapacityBoost(weapon);
                }

                var characterItem = StatBoostManager.GetCharacterItem(player);
                if (characterItem != null)
                {
                    StatBoostManager.RemoveCharacterBoost(characterItem, "ReloadSpeedGain");
                    StatBoostManager.RemoveCharacterBoost(characterItem, "RecoilControl");
                    StatBoostManager.RemoveWeaponBoost(characterItem, "WalkSpeed");
                    StatBoostManager.RemoveWeaponBoost(characterItem, "RunSpeed");
                }
            }
            catch { }
        }

        // ========== 탤런트 이벤트 핸들러 ==========

        /// <summary>
        /// 적 처치 시 호출 (Harmony 패치에서 호출)
        /// </summary>
        public void OnEnemyKilled(float distance, bool isHeadshot)
        {
            if (_equippedTalentId == null) return;

            float now = Time.time;
            _killCount++;
            if (isHeadshot) _headshotKillCount++;

            var talent = GetTalent(_equippedTalentId);
            if (talent == null) return;

            switch (talent.Id)
            {
                case "close_and_personal":
                    if (distance <= talent.Stats["trigger_range"])
                    {
                        talent.Activate(now);
                        OnTalentActivated(talent);
                        Debug.Log($"[DivisionTalents] ★ Close & Personal 발동! ({distance:F1}m) ★");
                    }
                    break;

                case "preservation":
                    if (talent.CanProc(now))
                    {
                        talent.Activate(now);
                        HealPlayer(talent.Stats["heal_amount"]);
                        Debug.Log($"[DivisionTalents] ★ Preservation 발동! 체력 +{talent.Stats["heal_amount"] * 100}% ★");
                    }
                    break;

                case "reformation":
                    if (isHeadshot && talent.CanProc(now))
                    {
                        talent.Activate(now);
                        HealPlayer(talent.Stats["heal_amount"]);
                        Debug.Log($"[DivisionTalents] ★ Reformation 발동! (헤드샷 킬) ★");
                    }
                    break;

                case "vindictive":
                    talent.Activate(now);
                    OnTalentActivated(talent);
                    Debug.Log($"[DivisionTalents] ★ Vindictive 발동! 5초간 +20% 크리티컬 확률 ★");
                    break;

                // === Division 2 추가 탤런트 ===
                case "killer":
                    talent.Activate(now);
                    Debug.Log($"[DivisionTalents] ★ Killer 발동! 5초간 +50% 크리티컬 데미지 ★");
                    break;

                case "perpetuation":
                    if (isHeadshot && talent.CanProc(now))
                    {
                        talent.Activate(now);
                        Debug.Log($"[DivisionTalents] ★ Perpetuation 발동! 3초간 +25% 데미지 (헤드샷 킬) ★");
                    }
                    break;
            }
        }

        /// <summary>
        /// 크리티컬 히트 시 호출 (Harmony 패치에서 호출)
        /// </summary>
        public void OnCriticalHit()
        {
            _critCount++;

            if (_equippedTalentId == null) return;

            float now = Time.time;

            // Boomerang: 크리티컬 시 다음 사격 데미지 부스트 + 탄환 1발 복구
            if (_equippedTalentId == "boomerang")
            {
                var bTalent = GetTalent("boomerang");
                if (bTalent != null)
                {
                    bTalent.Activate(now);

                    // 탄환 1발 복구 (CapacityHash stat +1)
                    try
                    {
                        var player = CharacterMainControl.Main;
                        if (player != null)
                        {
                            var allGuns = StatBoostManager.GetAllInventoryGuns(player);
                            foreach (var weapon in allGuns)
                            {
                                if (weapon == null) continue;
                                var gun = weapon.GetComponent<ItemSetting_Gun>();
                                if (gun == null) continue;

                                // _bulletCountCache 직접 +1
                                var cacheField = typeof(ItemSetting_Gun).GetField("_bulletCountCache",
                                    BindingFlags.NonPublic | BindingFlags.Instance);
                                if (cacheField != null)
                                {
                                    int current = (int)cacheField.GetValue(gun);
                                    if (current >= 0)
                                    {
                                        cacheField.SetValue(gun, current + 1);
                                        if (_debugMode)
                                            Debug.Log($"[DivisionTalents] Boomerang: 탄환 복구 {current}→{current + 1}");
                                        break; // 활성 무기 하나만
                                    }
                                }
                            }
                        }
                    }
                    catch { }

                    if (_debugMode)
                        Debug.Log($"[DivisionTalents] ★ Boomerang 발동! 5초간 +50% 데미지 + 탄환 1발 복구 ★");
                }
                return;
            }

            // Fast Hands: 크리티컬 시 재장전 속도 스택
            if (_equippedTalentId != "fast_hands") return;

            var talent = GetTalent("fast_hands");
            if (talent == null) return;

            int maxStacks = (int)talent.Stats["max_stacks"];
            if (_fastHandsStacks < maxStacks)
            {
                _fastHandsStacks++;
                _fastHandsLastStackTime = now;
                talent.Activate(now);

                // 실제 재장전 속도 적용
                ApplyFastHandsBoost();

                if (_debugMode)
                    Debug.Log($"[DivisionTalents] Fast Hands 스택: {_fastHandsStacks}/{maxStacks}");
            }
        }

        /// <summary>
        /// 재장전 시 호출 (Harmony 패치에서 호출)
        /// </summary>
        public void OnReload(bool wasEmpty)
        {
            _reloadCount++;
            if (wasEmpty) _emptyReloadCount++;

            if (_equippedTalentId == null) return;

            float now = Time.time;
            var talent = GetTalent(_equippedTalentId);
            if (talent == null) return;

            if (talent.Id == "frenzy" && wasEmpty)
            {
                talent.Activate(now);
                OnTalentActivated(talent);
                Debug.Log($"[DivisionTalents] ★ Frenzy 발동! 7초간 +20% 데미지, +35% 연사력 ★");
            }

            // === Division 2 추가 탤런트 ===

            // First Blood: 재장전 시 리셋 (다음 첫 사격 부스트 활성화)
            if (talent.Id == "first_blood")
            {
                talent.Activate(now);
                _firstBloodAvailable = true;
                Debug.Log($"[DivisionTalents] ★ First Blood: 재장전 후 첫 사격 +30% 준비됨 ★");
            }

            // Actum Est: 충전 완료 상태에서 재장전하면 활성화
            if (talent.Id == "actum_est")
            {
                if (_actumEstActive)
                {
                    // 부스트 탄창 사용 중에 재장전 → 비활성화 + 스택 초기화
                    _actumEstActive = false;
                    _actumEstStacks = 0;
                    talent.Deactivate();
                    ElectricAmmoApplier.RemoveAllElectric();
                    Debug.Log($"[DivisionTalents] Actum Est 부스트 탄창 종료 (재장전)");
                }
                else if (_actumEstChargeReady)
                {
                    // 100스택 충전 후 첫 재장전 → 부스트 탄창 활성화
                    _actumEstActive = true;
                    _actumEstChargeReady = false;
                    talent.Activate(now);
                    Debug.Log($"[DivisionTalents] ★ Actum Est 발동! 이번 탄창 +25% 데미지 + 전기탄 ★");
                }
            }
        }

        /// <summary>
        /// 플레이어 체력 회복
        /// </summary>
        private void HealPlayer(float ratio)
        {
            try
            {
                var player = CharacterMainControl.Main;
                if (player == null || player.Health == null) return;

                var health = player.Health;
                var healthType = health.GetType();

                // MaxHealth 가져오기
                float maxHealth = 100f;
                var maxHealthProp = healthType.GetProperty("MaxHealth",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (maxHealthProp != null)
                {
                    var v = maxHealthProp.GetValue(health);
                    if (v is float f) maxHealth = f;
                }

                // AddHealth 호출
                var addHealthMethod = healthType.GetMethod("AddHealth",
                    BindingFlags.Public | BindingFlags.Instance);
                if (addHealthMethod != null)
                {
                    addHealthMethod.Invoke(health, new object[] { maxHealth * ratio });
                }
            }
            catch (Exception ex)
            {
                if (_debugMode)
                    Debug.LogWarning($"[DivisionTalents] HealPlayer error: {ex.Message}");
            }
        }

        // ========== 데미지 계산 ==========

        /// <summary>
        /// 현재 활성 탤런트의 데미지 보너스 계산 (Harmony 패치에서 호출)
        /// </summary>
        public float GetDamageMultiplier(float distance, float currentAmmoRatio)
        {
            if (_equippedTalentId == null) return 1f;

            float multiplier = 1f;
            float now = Time.time;
            var talent = GetTalent(_equippedTalentId);
            if (talent == null) return 1f;

            // 패시브가 아닌 경우 IsActive 체크
            if (!talent.IsPassive && !talent.IsActive) return 1f;

            switch (talent.Id)
            {
                case "close_and_personal":
                    if (talent.IsActive && (now - talent.LastProcTime) < talent.Duration)
                    {
                        multiplier *= (1f + talent.Stats["damage_bonus"]);
                    }
                    break;

                case "frenzy":
                    if (talent.IsActive && (now - talent.LastProcTime) < talent.Duration)
                    {
                        multiplier *= (1f + talent.Stats["damage_bonus"]);
                    }
                    break;

                case "ranger":
                    {
                        int stacks = Mathf.FloorToInt(distance / 5f);
                        float bonus = Mathf.Min(stacks * talent.Stats["bonus_per_5m"], talent.Stats["max_bonus"]);
                        multiplier *= (1f + bonus);
                    }
                    break;

                case "optimist":
                    {
                        // 탄창이 비워질수록 데미지 증가
                        float emptyRatio = 1f - currentAmmoRatio; // 0~1
                        float bonus = emptyRatio * talent.Stats["max_bonus"];
                        multiplier *= (1f + bonus);
                    }
                    break;

                case "measured":
                    // 탄창 하단 절반에서 +20% 데미지
                    if (currentAmmoRatio < 0.5f)
                    {
                        multiplier *= (1f + talent.Stats["bottom_damage"]);
                    }
                    break;

                // === Division 2 추가 탤런트 ===

                case "boomerang":
                    // 크리티컬 후 활성화된 동안 데미지 부스트
                    if (talent.IsActive && (now - talent.LastProcTime) < talent.Duration)
                    {
                        multiplier *= (1f + talent.Stats["damage_bonus"]);
                    }
                    break;

                case "outsider_edge":
                    if (talent.IsActive && (now - talent.LastProcTime) < talent.Duration)
                    {
                        multiplier *= (1f + talent.Stats["damage_bonus"]);
                    }
                    break;

                case "perpetuation":
                    if (talent.IsActive && (now - talent.LastProcTime) < talent.Duration)
                    {
                        multiplier *= (1f + talent.Stats["damage_bonus"]);
                    }
                    break;

                case "first_blood":
                    // 재장전 후 첫 사격만 부스트
                    if (_firstBloodAvailable)
                    {
                        multiplier *= (1f + talent.Stats["damage_bonus"]);
                        _firstBloodAvailable = false; // 한 번 사용 후 리셋
                        if (_debugMode)
                            Debug.Log($"[DivisionTalents] First Blood 적용! +30%");
                    }
                    break;

                case "actum_est":
                    // 부스트 탄창 사용 중일 때만 데미지 증가
                    if (_actumEstActive)
                    {
                        multiplier *= (1f + talent.Stats["damage_bonus"]);
                    }
                    break;

                case "electromagnetic_accelerator":
                    {
                        // 우클릭 조준 중일 때만 데미지 부스트
                        if (Input.GetMouseButton(1))
                        {
                            multiplier *= (1f + talent.Stats["damage_bonus"]);
                        }
                    }
                    break;
            }

            return multiplier;
        }

        public int GetFastHandsStacks() => _fastHandsStacks;

        // Actum Est getters (UI 표시용)
        public int GetActumEstStacks() => _actumEstStacks;
        public bool IsActumEstChargeReady() => _actumEstChargeReady;
        public bool IsActumEstActive() => _actumEstActive;

        /// <summary>
        /// 적 명중 시 호출 (Actum Est 스택 누적용)
        /// </summary>
        public void OnEnemyHit()
        {
            if (_equippedTalentId != "actum_est") return;

            var talent = GetTalent("actum_est");
            if (talent == null) return;

            // 이미 충전 완료 상태면 더 이상 쌓이지 않음 (재장전 대기)
            if (_actumEstChargeReady) return;
            if (_actumEstActive) return;

            int maxStacks = (int)talent.Stats["max_stacks"];
            if (_actumEstStacks < maxStacks)
            {
                _actumEstStacks++;

                if (_actumEstStacks >= maxStacks)
                {
                    _actumEstChargeReady = true;
                    Debug.Log($"[DivisionTalents] ★ Actum Est 충전 완료! 다음 재장전 시 발동! ★");
                }
                else if (_debugMode && _actumEstStacks % 10 == 0)
                {
                    Debug.Log($"[DivisionTalents] Actum Est 스택: {_actumEstStacks}/{maxStacks}");
                }
            }
        }

        /// <summary>
        /// 헤드샷 히트 시 호출 (적이 죽지 않아도)
        /// </summary>
        public void OnHeadshotHit()
        {
            if (_equippedTalentId == null) return;

            float now = Time.time;
            var talent = GetTalent(_equippedTalentId);
            if (talent == null) return;

            switch (talent.Id)
            {
                case "outsider_edge":
                    talent.Activate(now);
                    if (_debugMode)
                        Debug.Log($"[DivisionTalents] ★ Outsider Edge 발동! 4초간 +25% 데미지 (헤드샷) ★");
                    break;
            }
        }

        /// <summary>
        /// Septic Shock: 적별 스택 관리
        /// </summary>
        public void OnEnemyHitForSeptic(Health victimHealth)
        {
            if (_equippedTalentId != "septic_shock") return;
            if (victimHealth == null) return;

            var talent = GetTalent("septic_shock");
            if (talent == null) return;

            int victimId = victimHealth.GetInstanceID();
            float now = Time.time;
            int maxStacks = (int)talent.Stats["max_stacks"];
            float duration = talent.Stats["duration"];

            // 타이머 만료 체크
            if (_septicTimers.TryGetValue(victimId, out float lastTime))
            {
                if (now - lastTime > duration)
                {
                    _septicStacks[victimId] = 0; // 10초 지나면 리셋
                }
            }

            // 스택 증가 (최대 7)
            if (!_septicStacks.ContainsKey(victimId))
                _septicStacks[victimId] = 0;

            if (_septicStacks[victimId] < maxStacks)
            {
                _septicStacks[victimId]++;
                _septicTimers[victimId] = now;

                // 1중첩: 맹독 적용
                if (_septicStacks[victimId] == 1)
                {
                    ApplyPoisonToEnemy(victimHealth);
                    Debug.Log($"[DivisionTalents] ★ Septic Shock 1중첩: 맹독! ★");
                }
                // 3중첩: 스턴 (전기 쇼크로 감전/경직)
                else if (_septicStacks[victimId] == 3)
                {
                    ApplyElectricShockToEnemy(victimHealth);
                    Debug.Log($"[DivisionTalents] ★ Septic Shock 3중첩: 스턴! ★");
                }
                // 6중첩: 강한 전기 쇼크
                else if (_septicStacks[victimId] == 6)
                {
                    ApplyElectricShockToEnemy(victimHealth);
                    Debug.Log($"[DivisionTalents] ★ Septic Shock 6중첩: 전기 쇼크! ★");
                }

                if (_debugMode)
                    Debug.Log($"[DivisionTalents] Septic Shock: 중첩 {_septicStacks[victimId]}/{maxStacks}");
            }
        }

        /// <summary>
        /// 적에게 전기 쇼크 적용 (전기 무기의 buff를 적에게 부여)
        /// </summary>
        private void ApplyElectricShockToEnemy(Health victimHealth)
        {
            try
            {
                if (victimHealth == null) return;

                // 전기 참조 로드 (ElectricAmmoApplier가 이미 로드했을 수 있음)
                ElectricAmmoApplier.LoadReferences();

                // 적의 GameObject에서 buffManager 찾기
                var victimGO = victimHealth.gameObject;
                if (victimGO == null) return;

                // 적의 CharacterMainControl 또는 Character 컴포넌트에서 buffManager 찾기
                var components = victimGO.GetComponents<Component>();
                object? buffManager = null;
                object? character = null;

                foreach (var comp in components)
                {
                    if (comp == null) continue;
                    var typeName = comp.GetType().Name;
                    if (typeName == "CharacterMainControl" || typeName == "Character")
                    {
                        character = comp;
                        // buffManager 필드 찾기
                        var bmField = comp.GetType().GetField("buffManager",
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (bmField != null)
                        {
                            buffManager = bmField.GetValue(comp);
                        }
                        break;
                    }
                }

                if (buffManager == null || character == null) return;

                // 전기 buff 프리팹 가져오기 (ItemSetting_Gun의 buff 필드에서)
                var buffField = typeof(ItemSetting_Gun).GetField("buff",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (buffField == null) return;

                // 전기 무기에서 buff 추출 (ElectricAmmoApplier가 이미 로드한 참조 사용)
                // 간단하게: 전기 무기 인스턴스화해서 buff 가져오기
                Item? electricWeapon = ItemAssetsCollection.InstantiateSync(733); // 전기 무기
                if (electricWeapon == null) return;

                var gunSetting = electricWeapon.GetComponent<ItemSetting_Gun>();
                if (gunSetting == null)
                {
                    UnityEngine.Object.Destroy(electricWeapon.gameObject);
                    return;
                }

                var electricBuff = buffField.GetValue(gunSetting);
                UnityEngine.Object.Destroy(electricWeapon.gameObject);

                if (electricBuff == null) return;

                // AddBuff 호출
                var addBuffMethod = buffManager.GetType().GetMethod("AddBuff",
                    BindingFlags.Public | BindingFlags.Instance);
                if (addBuffMethod != null)
                {
                    addBuffMethod.Invoke(buffManager, new object[] { electricBuff, character, -1 });
                    if (_debugMode)
                        Debug.Log($"[DivisionTalents] Electric shock applied to enemy!");
                }
            }
            catch (Exception ex)
            {
                if (_debugMode)
                    Debug.LogWarning($"[DivisionTalents] ApplyElectricShockToEnemy error: {ex.Message}");
            }
        }

        /// <summary>
        /// 적에게 맹독 적용 (독 무기 TypeID 899의 buff 사용)
        /// </summary>
        private void ApplyPoisonToEnemy(Health victimHealth)
        {
            try
            {
                if (victimHealth == null) return;

                var victimGO = victimHealth.gameObject;
                if (victimGO == null) return;

                var components = victimGO.GetComponents<Component>();
                object? buffManager = null;
                object? character = null;

                foreach (var comp in components)
                {
                    if (comp == null) continue;
                    var typeName = comp.GetType().Name;
                    if (typeName == "CharacterMainControl" || typeName == "Character")
                    {
                        character = comp;
                        var bmField = comp.GetType().GetField("buffManager",
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (bmField != null)
                            buffManager = bmField.GetValue(comp);
                        break;
                    }
                }

                if (buffManager == null || character == null) return;

                var buffField = typeof(ItemSetting_Gun).GetField("buff",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (buffField == null) return;

                // 독 무기 (TypeID 899) 인스턴스화해서 buff 추출
                Item? poisonWeapon = ItemAssetsCollection.InstantiateSync(899);
                if (poisonWeapon == null) return;

                var gunSetting = poisonWeapon.GetComponent<ItemSetting_Gun>();
                if (gunSetting == null)
                {
                    UnityEngine.Object.Destroy(poisonWeapon.gameObject);
                    return;
                }

                var poisonBuff = buffField.GetValue(gunSetting);
                UnityEngine.Object.Destroy(poisonWeapon.gameObject);

                if (poisonBuff == null) return;

                var addBuffMethod = buffManager.GetType().GetMethod("AddBuff",
                    BindingFlags.Public | BindingFlags.Instance);
                if (addBuffMethod != null)
                {
                    addBuffMethod.Invoke(buffManager, new object[] { poisonBuff, character, -1 });
                    if (_debugMode)
                        Debug.Log($"[DivisionTalents] Poison applied to enemy!");
                }
            }
            catch (Exception ex)
            {
                if (_debugMode)
                    Debug.LogWarning($"[DivisionTalents] ApplyPoisonToEnemy error: {ex.Message}");
            }
        }

        /// <summary>
        /// Septic Shock: 현재 적에 대한 데미지 보너스 반환
        /// </summary>
        public float GetSepticDamageBonus(Health victimHealth)
        {
            if (_equippedTalentId != "septic_shock") return 0f;
            if (victimHealth == null) return 0f;

            var talent = GetTalent("septic_shock");
            if (talent == null) return 0f;

            int victimId = victimHealth.GetInstanceID();
            int maxStacks = (int)talent.Stats["max_stacks"];

            if (!_septicStacks.TryGetValue(victimId, out int stacks)) return 0f;
            if (stacks < maxStacks) return 0f; // 7스택 미만이면 보너스 없음

            // 7스택 도달 시 +20% 데미지
            return talent.Stats["damage_bonus_at_max"];
        }
    }
}

namespace DivisionTalents
{
    public partial class TalentManager
    {
        // ========== OnGUI ==========

        private void OnGUI()
        {
            InitializeStyles();

            if (_showBuffIcons && !_showTalentSelector)
            {
                DrawBuffIcons();
            }

            if (_showTalentSelector)
            {
                DrawTalentSelector();
            }

            if (_debugMode)
            {
                DrawDebugInfo();
            }
        }

        private void InitializeStyles()
        {
            if (_labelStyle != null) return;

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                normal = { textColor = Color.white }
            };

            _headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.yellow }
            };

            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                padding = new RectOffset(10, 10, 5, 5)
            };

            _selectedButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal =
                {
                    textColor = Color.yellow,
                    background = MakeTex(2, 2, new Color(0.3f, 0.6f, 0.3f, 0.8f))
                },
                padding = new RectOffset(10, 10, 5, 5)
            };

            _cooldownStyle = new GUIStyle
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1f, 0.5f, 0.5f) }
            };

            _stackStyle = new GUIStyle
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.LowerRight,
                normal = { textColor = Color.white }
            };
        }

        // ========== 버프 아이콘 그리기 ==========

        private void DrawBuffIcons()
        {
            if (_equippedTalentId == null) return;

            var talent = GetTalent(_equippedTalentId);
            if (talent == null) return;

            float currentTime = Time.time;
            bool shouldShow = false;
            float remainingTime = 0f;
            string subText = "";
            string extraInfo = ""; // 추가 정보 (스택 보너스 등)

            if (talent.IsPassive)
            {
                // 패시브는 항상 표시
                shouldShow = true;
                subText = "PASSIVE";
            }
            else if (talent.IsActive && (currentTime - talent.LastProcTime) < talent.Duration)
            {
                // 액티브는 발동 중일 때만 표시
                shouldShow = true;
                remainingTime = talent.Duration - (currentTime - talent.LastProcTime);
                subText = $"{remainingTime:F1}s";
            }
            else if (talent.Id == "fast_hands" && _fastHandsStacks > 0)
            {
                // Fast Hands는 스택이 있을 때만 표시
                shouldShow = true;
                int maxStacks = (int)talent.Stats["max_stacks"];
                subText = $"{_fastHandsStacks}/{maxStacks}";
                float reduction = _fastHandsStacks * talent.Stats["reload_reduction"] * 100f;
                extraInfo = $"-{reduction:F0}%";

                // Fast Hands는 스택 감소까지 남은 시간도 표시
                float decayRemaining = FAST_HANDS_DECAY_TIME - (currentTime - _fastHandsLastStackTime);
                if (decayRemaining > 0 && decayRemaining < FAST_HANDS_DECAY_TIME)
                {
                    extraInfo += $" ({decayRemaining:F1}s)";
                }
            }
            else if (talent.Id == "actum_est")
            {
                // Actum Est: 스택이 있거나 충전 완료 또는 활성화 중일 때 표시
                if (_actumEstStacks > 0 || _actumEstChargeReady || _actumEstActive)
                {
                    shouldShow = true;
                    int maxStacks = (int)talent.Stats["max_stacks"];

                    if (_actumEstActive)
                    {
                        subText = "ACTIVE";
                        extraInfo = "+25% DMG";
                    }
                    else if (_actumEstChargeReady)
                    {
                        subText = "READY";
                        extraInfo = "RELOAD!";
                    }
                    else
                    {
                        subText = $"{_actumEstStacks}/{maxStacks}";
                        extraInfo = $"{(_actumEstStacks * 100 / maxStacks)}%";
                    }
                }
            }
            else if (talent.Id == "electromagnetic_accelerator")
            {
                // EMA는 패시브: 우클릭 조준 중에만 활성 표시
                shouldShow = true;
                if (Input.GetMouseButton(1))
                {
                    subText = "ACTIVE";
                    extraInfo = "+50% DMG";
                }
                else
                {
                    subText = "PASSIVE";
                    extraInfo = L("우클릭 조준", "Hold RMB", "右クリック");
                }
            }
            else if (talent.Id == "septic_shock")
            {
                // Septic Shock: 현재 가장 높은 스택 표시
                shouldShow = true;
                int highestStack = 0;
                foreach (var kvp in _septicStacks)
                {
                    if (kvp.Value > highestStack) highestStack = kvp.Value;
                }
                int maxStacks = (int)talent.Stats["max_stacks"];
                subText = $"{highestStack}/{maxStacks}";
                if (highestStack >= maxStacks)
                    extraInfo = "+20% DMG";
                else if (highestStack >= 6)
                    extraInfo = "SHOCK";
                else if (highestStack >= 3)
                    extraInfo = "STUN";
                else if (highestStack >= 1)
                    extraInfo = "POISON";
                else
                    extraInfo = L("명중 시 중첩", "Hit to stack", "命中で重複");
            }

            if (!shouldShow) return;

            // 화면 오른쪽 하단에 표시 (Duckov-totem 스타일)
            float boxWidth = 80;  // 살짝 더 크게
            float boxHeight = 80;
            float boxX = Screen.width - boxWidth - 20;
            float boxY = Screen.height * 0.85f - boxHeight / 2;

            // 배경 박스 (Fast Hands 스택이 많을수록 더 빛나게)
            if (talent.Id == "fast_hands" && _fastHandsStacks > 0)
            {
                int maxStacks = (int)talent.Stats["max_stacks"];
                float intensity = (float)_fastHandsStacks / maxStacks; // 0~1
                
                // 글로우 효과 - 외곽선
                Color glowColor = Color.Lerp(new Color(0.3f, 1f, 0.3f, 0.4f), 
                                             new Color(0.2f, 1f, 0.2f, 0.9f), intensity);
                if (_glowTexture == null)
                {
                    _glowTexture = MakeTex(1, 1, Color.white);
                }
                GUI.color = glowColor;
                GUI.DrawTexture(new Rect(boxX - 4, boxY - 4, boxWidth + 8, boxHeight + 8), _glowTexture);
                GUI.color = Color.white;
            }
            // Actum Est 글로우 - 충전 완료 시 노란색으로 빛남
            else if (talent.Id == "actum_est" && (_actumEstStacks > 0 || _actumEstChargeReady || _actumEstActive))
            {
                int maxStacks = (int)talent.Stats["max_stacks"];
                
                Color glowColor;
                if (_actumEstActive)
                {
                    // 활성 중: 강한 노란색 펄스
                    float pulse = 0.5f + Mathf.Sin(Time.time * 6f) * 0.3f;
                    glowColor = new Color(1f, 0.9f, 0.2f, pulse);
                }
                else if (_actumEstChargeReady)
                {
                    // 충전 완료: 강한 노란색 펄스 (재장전 알림)
                    float pulse = 0.6f + Mathf.Sin(Time.time * 8f) * 0.4f;
                    glowColor = new Color(1f, 0.6f, 0.1f, pulse);
                }
                else
                {
                    // 충전 중
                    float intensity = (float)_actumEstStacks / maxStacks;
                    glowColor = Color.Lerp(new Color(0.5f, 0.5f, 0.2f, 0.4f),
                                          new Color(1f, 0.85f, 0.2f, 0.9f), intensity);
                }

                if (_glowTexture == null) _glowTexture = MakeTex(1, 1, Color.white);
                GUI.color = glowColor;
                GUI.DrawTexture(new Rect(boxX - 4, boxY - 4, boxWidth + 8, boxHeight + 8), _glowTexture);
                GUI.color = Color.white;
            }
            // EMA 글로우 - 우클릭 조준 중일 때 푸른색
            else if (talent.Id == "electromagnetic_accelerator" && Input.GetMouseButton(1))
            {
                float pulse = 0.6f + Mathf.Sin(Time.time * 8f) * 0.3f;
                Color glowColor = new Color(0.6f, 0.85f, 1f, pulse);

                if (_glowTexture == null) _glowTexture = MakeTex(1, 1, Color.white);
                GUI.color = glowColor;
                GUI.DrawTexture(new Rect(boxX - 4, boxY - 4, boxWidth + 8, boxHeight + 8), _glowTexture);
                GUI.color = Color.white;
            }

            if (_bgTexture != null)
            {
                GUI.DrawTexture(new Rect(boxX, boxY, boxWidth, boxHeight), _bgTexture);
            }

            // 아이콘
            if (_talentIcons.TryGetValue(talent.Id, out var icon))
            {
                float iconSize = boxWidth * 0.65f;
                float iconX = boxX + (boxWidth - iconSize) / 2;
                float iconY = boxY + (boxHeight - iconSize) / 2 - 6; // 살짝 위로
                GUI.DrawTexture(new Rect(iconX, iconY, iconSize, iconSize), icon);
            }

            // 탤런트 이름 (아이콘 위)
            var nameStyle = new GUIStyle(_labelStyle)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.cyan }
            };
            GUI.Label(new Rect(boxX, boxY - 20, boxWidth, 18), talent.Name, nameStyle);

            // === Fast Hands 전용: 큰 스택 표시 ===
            if (talent.Id == "fast_hands" && _fastHandsStacks > 0)
            {
                // 우측 상단에 큰 스택 숫자
                var bigStackStyle = new GUIStyle
                {
                    fontSize = 28,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.UpperRight,
                    normal = { textColor = new Color(0.4f, 1f, 0.4f) }
                };
                // 그림자 효과 (검은색 뒤로)
                var shadowStyle = new GUIStyle(bigStackStyle);
                shadowStyle.normal.textColor = Color.black;
                GUI.Label(new Rect(boxX + 1, boxY - 7 + 1, boxWidth - 4, 36), $"{_fastHandsStacks}", shadowStyle);
                GUI.Label(new Rect(boxX, boxY - 7, boxWidth - 4, 36), $"{_fastHandsStacks}", bigStackStyle);

                // 하단에 보너스 정보 (-30% 등)
                var bonusStyle = new GUIStyle
                {
                    fontSize = 13,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = new Color(1f, 1f, 0.4f) }
                };
                var bonusShadow = new GUIStyle(bonusStyle);
                bonusShadow.normal.textColor = Color.black;
                
                // "RELOAD" 라벨
                GUI.Label(new Rect(boxX + 1, boxY + boxHeight - 36 + 1, boxWidth, 14), "RELOAD", bonusShadow);
                GUI.Label(new Rect(boxX, boxY + boxHeight - 36, boxWidth, 14), "RELOAD", bonusStyle);

                // 보너스 % 와 감소시간
                GUI.Label(new Rect(boxX + 1, boxY + boxHeight - 22 + 1, boxWidth, 16), extraInfo, bonusShadow);
                GUI.Label(new Rect(boxX, boxY + boxHeight - 22, boxWidth, 16), extraInfo, bonusStyle);

                // 스택 진행 바 (아이콘 아래 가는 막대)
                int maxStacks = (int)talent.Stats["max_stacks"];
                float fillRatio = (float)_fastHandsStacks / maxStacks;
                
                if (_buffActiveBgTexture == null)
                {
                    _buffActiveBgTexture = MakeTex(1, 1, new Color(0.05f, 0.3f, 0.05f, 0.8f));
                }
                if (_glowTexture == null)
                {
                    _glowTexture = MakeTex(1, 1, Color.white);
                }
                
                // 바 배경
                GUI.color = new Color(0, 0, 0, 0.6f);
                GUI.DrawTexture(new Rect(boxX + 4, boxY + boxHeight - 5, boxWidth - 8, 3), _glowTexture);
                // 바 채움
                GUI.color = Color.Lerp(new Color(0.4f, 1f, 0.4f, 0.9f), new Color(1f, 1f, 0.3f, 0.9f), fillRatio);
                GUI.DrawTexture(new Rect(boxX + 4, boxY + boxHeight - 5, (boxWidth - 8) * fillRatio, 3), _glowTexture);
                GUI.color = Color.white;
            }
            // === Actum Est 전용: 큰 스택 표시 ===
            else if (talent.Id == "actum_est" && (_actumEstStacks > 0 || _actumEstChargeReady || _actumEstActive))
            {
                int maxStacks = (int)talent.Stats["max_stacks"];

                // 우측 상단 큰 표시
                Color textColor;
                string mainText;

                if (_actumEstActive)
                {
                    textColor = new Color(1f, 1f, 0.3f);
                    mainText = "⚡";
                }
                else if (_actumEstChargeReady)
                {
                    textColor = new Color(1f, 0.6f, 0.1f);
                    mainText = "100";
                }
                else
                {
                    textColor = new Color(1f, 0.85f, 0.2f);
                    mainText = $"{_actumEstStacks}";
                }

                var bigStackStyle = new GUIStyle
                {
                    fontSize = 26,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.UpperRight,
                    normal = { textColor = textColor }
                };
                var shadowStyle = new GUIStyle(bigStackStyle);
                shadowStyle.normal.textColor = Color.black;
                GUI.Label(new Rect(boxX + 1, boxY - 7 + 1, boxWidth - 4, 36), mainText, shadowStyle);
                GUI.Label(new Rect(boxX, boxY - 7, boxWidth - 4, 36), mainText, bigStackStyle);

                // 하단 라벨
                var bonusStyle = new GUIStyle
                {
                    fontSize = 12,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = textColor }
                };
                var bonusShadow = new GUIStyle(bonusStyle);
                bonusShadow.normal.textColor = Color.black;

                // "SHOCK" / "CHARGED!" / "STACK" 라벨
                string label = _actumEstActive ? "SHOCK" : _actumEstChargeReady ? "CHARGED!" : "STACK";
                GUI.Label(new Rect(boxX + 1, boxY + boxHeight - 36 + 1, boxWidth, 14), label, bonusShadow);
                GUI.Label(new Rect(boxX, boxY + boxHeight - 36, boxWidth, 14), label, bonusStyle);

                // extraInfo (예: "+25% DMG", "RELOAD!", "50%")
                GUI.Label(new Rect(boxX + 1, boxY + boxHeight - 22 + 1, boxWidth, 16), extraInfo, bonusShadow);
                GUI.Label(new Rect(boxX, boxY + boxHeight - 22, boxWidth, 16), extraInfo, bonusStyle);

                // 진행 바 (충전 중일 때만)
                if (!_actumEstChargeReady && !_actumEstActive)
                {
                    float fillRatio = (float)_actumEstStacks / maxStacks;
                    if (_glowTexture == null) _glowTexture = MakeTex(1, 1, Color.white);

                    GUI.color = new Color(0, 0, 0, 0.6f);
                    GUI.DrawTexture(new Rect(boxX + 4, boxY + boxHeight - 5, boxWidth - 8, 3), _glowTexture);
                    GUI.color = Color.Lerp(new Color(1f, 0.85f, 0.2f, 0.9f), new Color(1f, 0.4f, 0.1f, 0.9f), fillRatio);
                    GUI.DrawTexture(new Rect(boxX + 4, boxY + boxHeight - 5, (boxWidth - 8) * fillRatio, 3), _glowTexture);
                    GUI.color = Color.white;
                }
            }
            else
            {
                // 일반 탤런트: 하단에 시간/PASSIVE 표시
                if (!string.IsNullOrEmpty(subText))
                {
                    var bottomStyle = new GUIStyle
                    {
                        fontSize = 14,
                        fontStyle = FontStyle.Bold,
                        alignment = TextAnchor.MiddleCenter,
                        normal = { textColor = talent.IsPassive ? new Color(0.7f, 0.7f, 1f) : new Color(1f, 0.7f, 0.3f) }
                    };
                    var bottomShadow = new GUIStyle(bottomStyle);
                    bottomShadow.normal.textColor = Color.black;
                    
                    GUI.Label(new Rect(boxX + 1, boxY + boxHeight - 22 + 1, boxWidth, 18), subText, bottomShadow);
                    GUI.Label(new Rect(boxX, boxY + boxHeight - 22, boxWidth, 18), subText, bottomStyle);
                }

                // 액티브 탤런트의 시간 진행 바
                if (talent.IsActive && !talent.IsPassive && remainingTime > 0)
                {
                    float fillRatio = remainingTime / talent.Duration;
                    if (_glowTexture == null) _glowTexture = MakeTex(1, 1, Color.white);
                    
                    GUI.color = new Color(0, 0, 0, 0.6f);
                    GUI.DrawTexture(new Rect(boxX + 4, boxY + boxHeight - 5, boxWidth - 8, 3), _glowTexture);
                    GUI.color = new Color(1f, 0.6f, 0.2f, 0.9f);
                    GUI.DrawTexture(new Rect(boxX + 4, boxY + boxHeight - 5, (boxWidth - 8) * fillRatio, 3), _glowTexture);
                    GUI.color = Color.white;
                }
            }
        }

        // ========== 탤런트 선택 UI ==========

        private void DrawTalentSelector()
        {
            // 반투명 배경
            GUI.color = new Color(0, 0, 0, 0.7f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.Box(_selectorRect, "");
            GUILayout.BeginArea(_selectorRect);

            GUILayout.Space(15);
            GUILayout.Label("=== Division 2 Weapon Talents ===", _headerStyle);
            GUILayout.Space(10);

            string equippedName = _equippedTalentId != null && _talents.ContainsKey(_equippedTalentId)
                ? _talents[_equippedTalentId].Name
                : L("없음", "None", "なし");
            GUILayout.Label($"{L("장착됨", "Equipped", "装備中")}: {equippedName}", _labelStyle);
            GUILayout.Space(5);
            GUILayout.Label($"T: {L("닫기", "Close", "閉じる")}  |  F9: {L("디버그", "Debug", "デバッグ")}  |  F10: {L("아이콘 토글", "Icon Toggle", "アイコン切替")}", _labelStyle);
            GUILayout.Space(10);

            _scrollPosition = GUILayout.BeginScrollView(_scrollPosition, GUILayout.Height(550));

            DrawTalentCategory(L("⚔ 공격형 (Offensive)", "⚔ Offensive", "⚔ 攻撃型"), TalentType.Offensive);
            DrawTalentCategory(L("🛡 방어형 (Defensive)", "🛡 Defensive", "🛡 防御型"), TalentType.Defensive);
            DrawTalentCategory(L("⚙ 유틸리티 (Utility)", "⚙ Utility", "⚙ ユーティリティ"), TalentType.Utility);

            GUILayout.EndScrollView();

            GUILayout.Space(10);
            if (GUILayout.Button(L("탤런트 해제", "Unequip", "タレント解除"), _buttonStyle, GUILayout.Height(35)))
            {
                _equippedTalentId = null;
                Debug.Log("[DivisionTalents] 탤런트 해제됨");
            }

            GUILayout.EndArea();
        }

        private void DrawTalentCategory(string categoryName, TalentType type)
        {
            GUILayout.Space(5);
            GUILayout.Label(categoryName, _headerStyle);
            GUILayout.Space(3);

            foreach (var kvp in _talents)
            {
                if (kvp.Value.Type != type) continue;

                bool isEquipped = _equippedTalentId == kvp.Key;
                var style = isEquipped ? _selectedButtonStyle : _buttonStyle;

                GUILayout.BeginHorizontal();

                // 아이콘
                if (_talentIcons.TryGetValue(kvp.Key, out var icon))
                {
                    GUILayout.Label(icon, GUILayout.Width(40), GUILayout.Height(40));
                }
                else
                {
                    GUILayout.Space(40);
                }

                // 버튼 (이름 + 설명)
                if (GUILayout.Button($"{(isEquipped ? "✓ " : "")}{kvp.Value.Name}\n{kvp.Value.Description}", 
                    style, GUILayout.Height(45)))
                {
                    if (isEquipped)
                    {
                        // 해제 - 패시브 부스트 제거
                        RemoveAllPassiveBoosts();
                        _equippedTalentId = null;
                        Debug.Log($"[DivisionTalents] {kvp.Value.Name} 해제됨");
                    }
                    else
                    {
                        // 이전 탤런트 부스트 제거
                        RemoveAllPassiveBoosts();
                        _equippedTalentId = kvp.Key;
                        // Fast Hands 스택 초기화
                        _fastHandsStacks = 0;
                        // Actum Est 상태 초기화 + 전기 속성 제거
                        _actumEstStacks = 0;
                        _actumEstChargeReady = false;
                        _actumEstActive = false;
                        ElectricAmmoApplier.RemoveAllElectric();
                        // First Blood 초기화
                        _firstBloodAvailable = false;
                        // 새 패시브 부스트 적용
                        ApplyPassiveBoosts();
                        Debug.Log($"[DivisionTalents] {kvp.Value.Name} 장착됨");
                    }
                }

                GUILayout.EndHorizontal();
                GUILayout.Space(2);
            }
        }

        // ========== 디버그 정보 ==========

        private void DrawDebugInfo()
        {
            var rect = new Rect(10, Screen.height - 200, 400, 190);
            GUI.Box(rect, "");

            GUILayout.BeginArea(rect);
            GUILayout.Space(5);
            GUILayout.Label("=== DivisionTalents Debug ===", _headerStyle);
            GUILayout.Label($"Equipped: {_equippedTalentId ?? "None"}", _labelStyle);
            GUILayout.Label($"Kills: {_killCount} (Headshots: {_headshotKillCount})", _labelStyle);
            GUILayout.Label($"Crits: {_critCount}", _labelStyle);
            GUILayout.Label($"Reloads: {_reloadCount} (Empty: {_emptyReloadCount})", _labelStyle);
            GUILayout.Label($"Fast Hands Stacks: {_fastHandsStacks}", _labelStyle);

            if (_equippedTalentId != null)
            {
                var t = GetTalent(_equippedTalentId);
                if (t != null && !t.IsPassive)
                {
                    float remaining = t.IsActive ? Mathf.Max(0, t.Duration - (Time.time - t.LastProcTime)) : 0;
                    GUILayout.Label($"Active: {t.IsActive} | Remaining: {remaining:F1}s", _labelStyle);
                }
            }
            GUILayout.EndArea();
        }
    }
}

namespace DivisionTalents
{
    /// <summary>
    /// Health.Hurt 패치 - 데미지 적용 시점에 후킹
    /// 게임의 정확한 DamageInfo 구조를 사용
    /// </summary>
    [HarmonyPatch(typeof(Health), "Hurt")]
    public static class Health_Hurt_Patch
    {
        // Reflection 캐시 (성능 최적화)
        // 게임의 실제 필드 이름:
        // - damageValue (float) : 데미지 값
        // - fromCharacter (CharacterMainControl) : 공격자
        // - crit (Int32) : 크리티컬 플래그 (-1 = 일반, 그 외 = 크리티컬)
        // - critDamageFactor (float) : 크리티컬 데미지 배율
        // - critRate (float) : 크리티컬 확률
        // - damagePoint (Vector3) : 히트 위치
        // - damageNormal (Vector3) : 히트 노멀
        // - toDamageReceiver (DamageReceiver) : 데미지 받는 곳
        // - fromWeaponItemID (int) : 무기 ID
        // - damageType (DamageTypes enum) : normal/fire/electric 등
        // - finalDamage (float) : 최종 데미지
        private static FieldInfo? _damageField;
        private static FieldInfo? _attackerField;
        private static FieldInfo? _critField;
        private static FieldInfo? _critDamageFactorField;
        private static FieldInfo? _critRateField;
        private static FieldInfo? _damagePointField;
        private static FieldInfo? _damageNormalField;
        private static FieldInfo? _toDamageReceiverField;
        private static FieldInfo? _finalDamageField;
        private static PropertyInfo? _currentHealthProp;
        private static PropertyInfo? _maxHealthProp;
        private static Type? _damageInfoType;
        private static bool _cacheInitialized = false;

        // 디버그: 한 번만 출력
        private static bool _debugFieldsLogged = false;
        // 디버그: 처음 30번의 데미지 이벤트 자세히 로깅
        private static int _debugDamageCount = 0;
        private const int MAX_DEBUG_DAMAGE_LOGS = 30;
        private static bool _damageReceiverLogged = false;

        private static void InitializeCache(object damageInfo)
        {
            if (_cacheInitialized || damageInfo == null) return;

            try
            {
                _damageInfoType = damageInfo.GetType();
                var bf = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

                _damageField = _damageInfoType.GetField("damageValue", bf);
                _attackerField = _damageInfoType.GetField("fromCharacter", bf);
                _critField = _damageInfoType.GetField("crit", bf);
                _critDamageFactorField = _damageInfoType.GetField("critDamageFactor", bf);
                _critRateField = _damageInfoType.GetField("critRate", bf);
                _damagePointField = _damageInfoType.GetField("damagePoint", bf);
                _damageNormalField = _damageInfoType.GetField("damageNormal", bf);
                _toDamageReceiverField = _damageInfoType.GetField("toDamageReceiver", bf);
                _finalDamageField = _damageInfoType.GetField("finalDamage", bf);

                _currentHealthProp = typeof(Health).GetProperty("CurrentHealth", bf);
                _maxHealthProp = typeof(Health).GetProperty("MaxHealth", bf);

                _cacheInitialized = true;

                Debug.Log($"[DivisionTalents] ★ Cache initialized - damage:{_damageField != null}, attacker:{_attackerField != null}, crit:{_critField != null}, damagePoint:{_damagePointField != null}, critDmgFactor:{_critDamageFactorField != null}, critRate:{_critRateField != null} ★");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DivisionTalents] Cache init failed: {ex.Message}");
            }
        }

        /// <summary>
        /// 디버그: damageInfo의 모든 필드를 출력 (한 번만)
        /// </summary>
        private static void LogDamageInfoFields(object damageInfo)
        {
            if (_debugFieldsLogged || damageInfo == null) return;
            _debugFieldsLogged = true;

            try
            {
                var t = damageInfo.GetType();
                Debug.Log("===== [DivisionTalents] DAMAGE INFO ANALYSIS =====");
                Debug.Log($"DamageInfo Type: {t.FullName}");

                Debug.Log("--- Fields ---");
                var fields = t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (var f in fields)
                {
                    object value = "?";
                    try { value = f.GetValue(damageInfo) ?? "null"; } catch { }
                    Debug.Log($"  {f.FieldType.Name} {f.Name} = {value}");
                }

                Debug.Log("==================================================");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DivisionTalents] LogDamageInfoFields error: {ex.Message}");
            }
        }

        /// <summary>
        /// 디버그: DamageReceiver의 모든 필드/프로퍼티 출력 (한 번만)
        /// 헤드샷/약점 정보를 어떻게 찾을 수 있는지 분석
        /// </summary>
        private static void LogDamageReceiverFields(object receiver)
        {
            try
            {
                var t = receiver.GetType();
                Debug.Log("===== [DivisionTalents] DAMAGE RECEIVER ANALYSIS =====");
                Debug.Log($"DamageReceiver Type: {t.FullName}");

                Debug.Log("--- Fields ---");
                var fields = t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (var f in fields)
                {
                    object value = "?";
                    try { value = f.GetValue(receiver) ?? "null"; } catch { }
                    Debug.Log($"  {f.FieldType.Name} {f.Name} = {value}");
                }

                Debug.Log("--- Properties ---");
                var props = t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (var p in props)
                {
                    if (p.GetIndexParameters().Length > 0) continue;
                    object value = "?";
                    try { value = p.GetValue(receiver) ?? "null"; } catch { }
                    Debug.Log($"  {p.PropertyType.Name} {p.Name} = {value}");
                }

                // GameObject 이름 / 부모 정보
                if (receiver is Component comp)
                {
                    Debug.Log($"--- GameObject ---");
                    Debug.Log($"  Name: {comp.gameObject.name}");
                    Debug.Log($"  Parent: {comp.transform.parent?.name ?? "null"}");
                    Debug.Log($"  All Components on this GO:");
                    foreach (var c in comp.gameObject.GetComponents<Component>())
                    {
                        Debug.Log($"    - {c.GetType().Name}");
                    }
                }
                Debug.Log("======================================================");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DivisionTalents] LogDamageReceiverFields error: {ex.Message}");
            }
        }

        /// <summary>
        /// 크리티컬 감지 - crit 필드 사용
        /// 게임 로그 분석 결과:
        ///   crit = -1 : 일반 데미지
        ///   crit = 0  : 크리티컬 (확인됨!)
        ///   crit > 0  : 약점/헤드샷 (인덱스로 부위 표시 가능성)
        /// </summary>
        private static bool IsCriticalHit(object damageInfo)
        {
            if (_critField == null) return false;
            try
            {
                var v = _critField.GetValue(damageInfo);
                if (v is int critValue)
                {
                    // -1이 아니면 모두 크리티컬 (0=일반 크리, 1+=약점/헤드샷)
                    return critValue != -1;
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// 헤드샷/약점 감지 - crit > 0 이거나 damagePoint가 적의 머리 위치인 경우
        /// </summary>
        private static bool IsHeadshotByCrit(object damageInfo)
        {
            if (_critField == null) return false;
            try
            {
                var v = _critField.GetValue(damageInfo);
                if (v is int critValue)
                {
                    // crit > 0 이면 약점/헤드샷일 가능성
                    return critValue > 0;
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// 헤드샷 감지 - crit 값이 0보다 크면 약점/헤드샷, 또는 damagePoint 위치로 판단
        /// </summary>
        private static bool IsHeadshot(object damageInfo, Health victim)
        {
            try
            {
                // 방법 1: crit > 0 이면 헤드샷/약점일 가능성
                if (IsHeadshotByCrit(damageInfo)) return true;

                // 방법 2: damagePoint를 적 위치와 비교
                if (_damagePointField != null && victim != null)
                {
                    var damagePoint = _damagePointField.GetValue(damageInfo);
                    if (damagePoint is Vector3 hitPos)
                    {
                        Vector3 enemyPos = victim.transform.position;
                        float heightDiff = hitPos.y - enemyPos.y;
                        if (heightDiff > 1.4f) return true;
                    }
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Prefix: 데미지 적용 전에 호출됨
        /// 플레이어가 가하는 데미지에 탤런트 보너스 적용
        /// </summary>
        [HarmonyPrefix]
        static void Prefix(Health __instance, object damageInfo, ref object[] __state)
        {
            __state = new object[] { 0f, false }; // [원래 데미지, 데미지 변경 여부]

            try
            {
                if (damageInfo == null) return;
                if (TalentManager.Instance == null) return;

                InitializeCache(damageInfo);

                // 첫 호출 시 모든 필드 디버그 출력
                LogDamageInfoFields(damageInfo);

                if (_damageField == null || _attackerField == null) return;

                // 공격자 확인
                var attacker = _attackerField.GetValue(damageInfo);
                if (attacker == null) return;

                // 플레이어가 공격자인지 확인
                var player = CharacterMainControl.Main;
                if (player == null) return;

                bool isPlayerAttacker = ReferenceEquals(attacker, player);
                if (!isPlayerAttacker)
                {
                    if (attacker is MonoBehaviour atkMono && atkMono.gameObject == player.gameObject)
                    {
                        isPlayerAttacker = true;
                    }
                }

                if (!isPlayerAttacker) return;

                // 피해자가 플레이어 본인이면 무시
                var victimMono = __instance.GetComponent<CharacterMainControl>();
                if (victimMono != null && victimMono == player) return;

                // 현재 데미지 값
                float currentDamage = (float)_damageField.GetValue(damageInfo);

                // === 디버그: 처음 N번의 데미지 이벤트 자세히 로깅 ===
                if (_debugDamageCount < MAX_DEBUG_DAMAGE_LOGS)
                {
                    _debugDamageCount++;
                    int critValue = -999;
                    float critRate = 0, critDmgFactor = 0;
                    string damageType = "?", receiverInfo = "?";

                    try
                    {
                        if (_critField != null) critValue = (int)_critField.GetValue(damageInfo);
                        if (_critRateField != null) critRate = (float)_critRateField.GetValue(damageInfo);
                        if (_critDamageFactorField != null) critDmgFactor = (float)_critDamageFactorField.GetValue(damageInfo);
                        var dtField = _damageInfoType?.GetField("damageType",
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (dtField != null) damageType = dtField.GetValue(damageInfo)?.ToString() ?? "null";
                        if (_toDamageReceiverField != null)
                        {
                            var receiver = _toDamageReceiverField.GetValue(damageInfo);
                            if (receiver != null)
                            {
                                receiverInfo = $"{receiver.GetType().Name}";
                                // DamageReceiver의 모든 필드/프로퍼티 한 번만 출력
                                if (!_damageReceiverLogged)
                                {
                                    _damageReceiverLogged = true;
                                    LogDamageReceiverFields(receiver);
                                }
                                // 추가로 GameObject 이름 가져오기
                                if (receiver is Component recvComp)
                                {
                                    receiverInfo += $" on '{recvComp.gameObject.name}'";
                                }
                            }
                        }
                    }
                    catch { }

                    Debug.Log($"[DivisionTalents][Hit#{_debugDamageCount}] dmg={currentDamage:F1}, crit={critValue}, critRate={critRate:F2}, critFactor={critDmgFactor:F2}, dmgType={damageType}, receiver={receiverInfo}");
                }
                // === 디버그 끝 ===

                // 크리티컬 감지 (crit 필드 사용: -1=일반, 0+=크리티컬)
                bool isCritical = IsCriticalHit(damageInfo);
                if (isCritical)
                {
                    int critVal = -1;
                    try { if (_critField != null) critVal = (int)_critField.GetValue(damageInfo); } catch { }
                    Debug.Log($"[DivisionTalents] ★ CRITICAL HIT DETECTED! (crit={critVal}, dmg={currentDamage:F1}) ★");
                    TalentManager.Instance.OnCriticalHit();
                }

                // 헤드샷 감지 (crit > 0)
                bool isHeadshotHit = false;
                if (_critField != null)
                {
                    try
                    {
                        var v = _critField.GetValue(damageInfo);
                        if (v is int cv && cv > 0) isHeadshotHit = true;
                    }
                    catch { }
                }
                if (isHeadshotHit)
                {
                    TalentManager.Instance.OnHeadshotHit();
                }

                // 적 명중 감지 (Actum Est용)
                TalentManager.Instance.OnEnemyHit();

                // Septic Shock: 적별 스택 관리 + 데미지 부스트
                TalentManager.Instance.OnEnemyHitForSeptic(__instance);

                // 거리 계산 (damagePoint 사용)
                float distance = 0f;
                if (_damagePointField != null)
                {
                    var hitPos = _damagePointField.GetValue(damageInfo);
                    if (hitPos is Vector3 vPos)
                    {
                        distance = Vector3.Distance(player.transform.position, vPos);
                    }
                }
                else
                {
                    distance = Vector3.Distance(player.transform.position, __instance.transform.position);
                }

                // 현재 탄약 비율 가져오기
                float ammoRatio = GetCurrentAmmoRatio(player);

                // 데미지 보너스 계산
                float multiplier = TalentManager.Instance.GetDamageMultiplier(distance, ammoRatio);

                // Septic Shock: 7스택 도달 시 +20% 추가
                float septicBonus = TalentManager.Instance.GetSepticDamageBonus(__instance);
                if (septicBonus > 0)
                {
                    multiplier *= (1f + septicBonus);
                }

                // Vindictive: 활성화 중이면 critRate 부스트 적용
                ApplyCritRateBoost(damageInfo);

                // Strained: 체력에 따라 critDamageFactor 부스트
                ApplyCritDamageBoost(damageInfo);

                if (multiplier > 1f)
                {
                    float originalDamage = currentDamage;
                    float newDamage = originalDamage * multiplier;
                    _damageField.SetValue(damageInfo, newDamage);

                    __state[0] = originalDamage;
                    __state[1] = true;

                    var manager = TalentManager.Instance;
                    if (manager != null)
                    {
                        var debugField = typeof(TalentManager).GetField("_debugMode",
                            BindingFlags.NonPublic | BindingFlags.Instance);
                        if (debugField != null && (bool)debugField.GetValue(manager))
                        {
                            Debug.Log($"[DivisionTalents] Damage: {originalDamage:F1} → {newDamage:F1} (x{multiplier:F2}, dist:{distance:F1}m, crit:{isCritical})");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DivisionTalents] Prefix error: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// Vindictive: 크리티컬 확률 부스트
        /// </summary>
        private static void ApplyCritRateBoost(object damageInfo)
        {
            try
            {
                if (_critRateField == null) return;
                var manager = TalentManager.Instance;
                if (manager == null) return;
                if (manager.GetEquippedTalentId() != "vindictive") return;

                var talent = manager.GetTalent("vindictive");
                if (talent == null || !talent.IsActive) return;
                if ((Time.time - talent.LastProcTime) >= talent.Duration) return;

                // 현재 critRate에 보너스 추가
                var currentObj = _critRateField.GetValue(damageInfo);
                if (currentObj == null) return;
                float currentCritRate = (float)currentObj;
                float newCritRate = currentCritRate + talent.Stats["crit_bonus"];
                _critRateField.SetValue(damageInfo, newCritRate);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DivisionTalents] ApplyCritRateBoost error: {ex.Message}");
            }
        }

        /// <summary>
        /// Strained: 체력 부족 시 크리티컬 데미지 증가
        /// Killer: 킬 후 크리티컬 데미지 증가
        /// </summary>
        private static void ApplyCritDamageBoost(object damageInfo)
        {
            try
            {
                if (_critDamageFactorField == null) return;
                var manager = TalentManager.Instance;
                if (manager == null) return;

                var equipped = manager.GetEquippedTalentId();
                if (equipped == null) return;

                float bonus = 0f;

                // Strained: 체력에 비례
                if (equipped == "strained")
                {
                    var talent = manager.GetTalent("strained");
                    if (talent == null) return;

                    var player = CharacterMainControl.Main;
                    if (player == null || player.Health == null) return;

                    if (_currentHealthProp == null || _maxHealthProp == null) return;

                    var curObj = _currentHealthProp.GetValue(player.Health);
                    var maxObj = _maxHealthProp.GetValue(player.Health);
                    if (curObj == null || maxObj == null) return;

                    float currentHp = (float)curObj;
                    float maxHp = (float)maxObj;
                    if (maxHp <= 0) return;

                    float missingPercent = 1f - (currentHp / maxHp);
                    int stacks = Mathf.FloorToInt(missingPercent * 100f / 5f);
                    bonus = stacks * talent.Stats["crit_per_5_percent"];
                }
                // Killer: 활성화 동안 +50% 크리티컬 데미지
                else if (equipped == "killer")
                {
                    var talent = manager.GetTalent("killer");
                    if (talent == null || !talent.IsActive) return;
                    if ((Time.time - talent.LastProcTime) >= talent.Duration) return;
                    bonus = talent.Stats["crit_dmg_bonus"];
                }

                if (bonus > 0)
                {
                    var currentFactorObj = _critDamageFactorField.GetValue(damageInfo);
                    if (currentFactorObj == null) return;
                    float currentFactor = (float)currentFactorObj;
                    _critDamageFactorField.SetValue(damageInfo, currentFactor + bonus);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DivisionTalents] ApplyCritDamageBoost error: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix: 데미지 적용 후 호출됨
        /// 적이 죽었는지 확인하고 OnEnemyKilled 호출
        /// </summary>
        [HarmonyPostfix]
        static void Postfix(Health __instance, object damageInfo)
        {
            try
            {
                if (TalentManager.Instance == null) return;
                if (_currentHealthProp == null) return;

                // 피해자의 현재 체력 확인
                var currentHealthObj = _currentHealthProp.GetValue(__instance);
                if (currentHealthObj == null) return;

                float currentHealth = (float)currentHealthObj;
                if (currentHealth > 0) return; // 아직 살아있음

                // 죽은 대상이 플레이어인지 확인
                var player = CharacterMainControl.Main;
                if (player == null) return;

                var victim = __instance.GetComponent<CharacterMainControl>();
                if (victim != null && victim == player) return; // 플레이어 본인은 제외

                // 공격자가 플레이어인지 확인
                if (_attackerField == null || damageInfo == null) return;

                var attacker = _attackerField.GetValue(damageInfo);
                if (attacker == null) return;

                bool isPlayerKill = ReferenceEquals(attacker, player);
                if (!isPlayerKill && attacker is MonoBehaviour atkMono && atkMono.gameObject == player.gameObject)
                {
                    isPlayerKill = true;
                }

                if (!isPlayerKill) return;

                // 거리 계산 (damagePoint 사용)
                Vector3 hitPosition = __instance.transform.position;
                if (_damagePointField != null)
                {
                    var posObj = _damagePointField.GetValue(damageInfo);
                    if (posObj is Vector3 p) hitPosition = p;
                }
                float distance = Vector3.Distance(player.transform.position, hitPosition);

                // 헤드샷 여부 확인
                bool isHeadshot = IsHeadshot(damageInfo, __instance);

                // 킬 이벤트 호출
                TalentManager.Instance.OnEnemyKilled(distance, isHeadshot);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DivisionTalents] Postfix error: {ex.Message}");
            }
        }

        /// <summary>
        /// 현재 탄약 비율 가져오기 (0~1)
        /// </summary>
        private static float GetCurrentAmmoRatio(CharacterMainControl player)
        {
            try
            {
                if (player == null) return 1f;

                // 자식 컴포넌트에서 ItemSetting_Gun 찾기
                var components = player.GetComponentsInChildren<Component>(true);
                foreach (var comp in components)
                {
                    if (comp == null) continue;
                    var compType = comp.GetType();
                    if (compType.Name != "ItemSetting_Gun" && !compType.Name.Contains("Gun")) continue;

                    // 현재 탄약 / 최대 탄약 필드 찾기
                    var currentAmmoField = compType.GetField("currentAmmo",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        ?? compType.GetField("ammo",
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                    var maxAmmoField = compType.GetField("maxAmmo",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        ?? compType.GetField("magazineSize",
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                    if (currentAmmoField != null && maxAmmoField != null)
                    {
                        var current = Convert.ToSingle(currentAmmoField.GetValue(comp));
                        var max = Convert.ToSingle(maxAmmoField.GetValue(comp));
                        if (max > 0) return current / max;
                    }
                }
            }
            catch { }

            return 1f;
        }
    }

    /// <summary>
    /// 재장전 감지 - ammo-check 모드의 검증된 방법 사용
    /// 객체 구조:
    ///   CharacterMainControl.agentHolder.CurrentHoldGun  → Gun 객체
    ///     ├─ gunState (int)              ★ 5 = 재장전 중
    ///     └─ Item (property)             → Item 객체
    ///          └─ GetComponent<ItemSetting_Gun>()
    ///               └─ _bulletCountCache (int)  ★ 현재 탄약
    /// </summary>
    public static class ReloadDetector
    {
        private const int RELOAD_STATE_VALUE = 5;

        // CharacterMainControl 캐시
        private static FieldInfo? _agentHolderField;
        private static PropertyInfo? _agentHolderProp;
        private static MemberInfo? _currentHoldGunMember;
        private static bool _holderCached = false;

        // Gun 객체 캐시 (gunState는 여기에 있음)
        private static FieldInfo? _gunStateField;
        private static PropertyInfo? _gunItemProp;
        private static bool _gunObjCached = false;

        // ItemSetting_Gun 캐시 (_bulletCountCache는 여기에 있음)
        private static Type? _itemSettingGunType;
        private static MethodInfo? _getComponentMethod;
        private static FieldInfo? _bulletCountField;
        private static bool _ammoCacheReady = false;

        // 추적 상태
        private static bool _wasReloading = false;
        private static int _ammoBeforeReload = -1;
        private static int _lastAmmo = -1;
        private static int _lastGunInstanceId = 0;

        // ─────────── CharacterMainControl ↔ agentHolder ↔ CurrentHoldGun ───────────

        private static void CacheHolderReflection(object mainObj)
        {
            if (_holderCached) return;
            _holderCached = true;

            try
            {
                var t = mainObj.GetType();
                var bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                _agentHolderField = t.GetField("agentHolder", bf);
                if (_agentHolderField == null)
                {
                    _agentHolderProp = t.GetProperty("agentHolder", bf)
                        ?? t.GetProperty("AgentHolder", bf);
                }

                Debug.Log($"[DivisionTalents] AgentHolder cached - field:{_agentHolderField != null}, prop:{_agentHolderProp != null}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DivisionTalents] CacheHolderReflection error: {ex.Message}");
            }
        }

        private static void CacheCurrentHoldGunMember(object holderObj)
        {
            if (_currentHoldGunMember != null) return;

            try
            {
                var ht = holderObj.GetType();
                var bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                PropertyInfo? p = ht.GetProperty("CurrentHoldGun", bf)
                    ?? ht.GetProperty("currentHoldGun", bf);
                FieldInfo? f = ht.GetField("CurrentHoldGun", bf)
                    ?? ht.GetField("currentHoldGun", bf);

                _currentHoldGunMember = (MemberInfo?)p ?? (MemberInfo?)f;

                Debug.Log($"[DivisionTalents] CurrentHoldGun cached: {(_currentHoldGunMember != null ? _currentHoldGunMember.Name : "NOT FOUND")}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DivisionTalents] CacheCurrentHoldGunMember error: {ex.Message}");
            }
        }

        private static object? GetCurrentGun(CharacterMainControl player)
        {
            try
            {
                if (player == null) return null;

                CacheHolderReflection(player);

                object? holder = null;
                if (_agentHolderField != null) holder = _agentHolderField.GetValue(player);
                else if (_agentHolderProp != null) holder = _agentHolderProp.GetValue(player);
                if (holder == null) return null;

                CacheCurrentHoldGunMember(holder);
                if (_currentHoldGunMember == null) return null;

                if (_currentHoldGunMember is FieldInfo fi)
                    return fi.GetValue(holder);
                if (_currentHoldGunMember is PropertyInfo pi)
                    return pi.GetValue(holder);
            }
            catch { }
            return null;
        }

        // ─────────── Gun 객체 (gunState, Item 프로퍼티) ───────────

        private static void CacheGunObjectFields(object gunObj)
        {
            if (_gunObjCached) return;
            _gunObjCached = true;

            try
            {
                var t = gunObj.GetType();
                var bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                _gunStateField = t.GetField("gunState", bf);
                _gunItemProp = t.GetProperty("Item", bf) ?? t.GetProperty("item", bf);

                Debug.Log($"[DivisionTalents] ★ Gun obj cached - type:{t.Name}, gunState:{_gunStateField != null}, item:{_gunItemProp != null} ★");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DivisionTalents] CacheGunObjectFields error: {ex.Message}");
            }
        }

        // ─────────── ItemSetting_Gun (_bulletCountCache) ───────────

        private static void CacheAmmoReflection(object gunObj)
        {
            if (_ammoCacheReady) return;

            try
            {
                if (_gunItemProp == null) return;

                var item = _gunItemProp.GetValue(gunObj);
                if (item == null) return;

                var itemType = item.GetType();
                var getComponentRaw = itemType.GetMethod("GetComponent", Type.EmptyTypes);
                if (getComponentRaw == null) return;

                _itemSettingGunType = typeof(ItemSetting_Gun);
                _getComponentMethod = getComponentRaw.MakeGenericMethod(_itemSettingGunType);

                _bulletCountField = _itemSettingGunType.GetField(
                    "_bulletCountCache",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                if (_bulletCountField == null)
                {
                    Debug.LogWarning("[DivisionTalents] _bulletCountCache field not found!");
                    return;
                }

                _ammoCacheReady = true;
                Debug.Log($"[DivisionTalents] ★ Ammo reflection ready ★");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DivisionTalents] CacheAmmoReflection error: {ex.Message}");
            }
        }

        /// <summary>
        /// 현재 탄약 가져오기 - Gun.Item.GetComponent&lt;ItemSetting_Gun&gt;()._bulletCountCache
        /// </summary>
        private static int ReadCurrentAmmo(object gunObj)
        {
            try
            {
                if (!_ammoCacheReady) return -1;
                if (_gunItemProp == null || _getComponentMethod == null || _bulletCountField == null) return -1;

                var item = _gunItemProp.GetValue(gunObj);
                if (item == null) return -1;

                var setting = _getComponentMethod.Invoke(item, null);
                if (setting == null) return -1;

                var v = _bulletCountField.GetValue(setting);
                if (v == null) return -1;

                int ammo = Convert.ToInt32(v);
                return ammo < 0 ? 0 : ammo;
            }
            catch { }
            return -1;
        }

        // ─────────── 매 프레임 호출 ───────────

        public static void CheckReload(CharacterMainControl player)
        {
            try
            {
                if (player == null) return;

                var gun = GetCurrentGun(player);
                if (gun == null) return;

                CacheGunObjectFields(gun);
                CacheAmmoReflection(gun);

                if (_gunStateField == null) return;

                int gunState;
                try { gunState = Convert.ToInt32(_gunStateField.GetValue(gun)); }
                catch { return; }

                bool isReloading = (gunState == RELOAD_STATE_VALUE);
                int currentAmmo = ReadCurrentAmmo(gun);

                int gunId = (gun as Component)?.GetInstanceID() ?? gun.GetHashCode();

                // 무기가 바뀌면 상태 리셋
                if (gunId != _lastGunInstanceId)
                {
                    _lastGunInstanceId = gunId;
                    _wasReloading = isReloading;
                    _lastAmmo = currentAmmo;
                    _ammoBeforeReload = currentAmmo;
                    return;
                }

                // 평상시(재장전 중 아님) 탄약 추적
                if (!isReloading && currentAmmo >= 0)
                {
                    _lastAmmo = currentAmmo;
                }

                // === 재장전 시작 ===
                if (!_wasReloading && isReloading)
                {
                    _ammoBeforeReload = _lastAmmo;
                    Debug.Log($"[DivisionTalents] >>> Reload START - ammo before: {_ammoBeforeReload} <<<");
                }
                // === 재장전 종료 ===
                else if (_wasReloading && !isReloading)
                {
                    bool wasEmpty = (_ammoBeforeReload == 0);
                    Debug.Log($"[DivisionTalents] >>> Reload END - ammo {_ammoBeforeReload}→{currentAmmo}, wasEmpty={wasEmpty} <<<");

                    if (TalentManager.Instance != null)
                    {
                        TalentManager.Instance.OnReload(wasEmpty);
                    }
                }

                _wasReloading = isReloading;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DivisionTalents] CheckReload error: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}

namespace DivisionTalents
{
    /// <summary>
    /// 무기 / 캐릭터 스탯 매니저 - 탤런트가 활성화될 때 게임의 실제 스탯을 변경
    /// Duckov-totem과 동일한 방식으로 ItemSetting_Gun의 stat을 직접 수정
    /// </summary>
    public static class StatBoostManager
    {
        // 원본 스탯 저장 (탤런트 해제 시 복원용)
        private static Dictionary<string, float> _originalStats = new Dictionary<string, float>();
        // 현재 적용된 부스트 추적
        private static HashSet<string> _appliedBoosts = new HashSet<string>();

        /// <summary>
        /// 무기 스탯에 퍼센트 부스트 적용
        /// </summary>
        public static void ApplyWeaponBoost(Item weapon, string statKey, float percentageBoost)
        {
            try
            {
                if (weapon == null) return;

                var stat = weapon.GetStat(statKey);
                if (stat == null) return;

                var baseValueProperty = stat.GetType().GetProperty("BaseValue");
                if (baseValueProperty == null) return;

                string key = $"Weapon_{weapon.GetInstanceID()}_{statKey}";
                
                // 이미 적용되어 있으면 스킵
                if (_appliedBoosts.Contains(key)) return;

                float originalValue = (float)baseValueProperty.GetValue(stat);
                _originalStats[key] = originalValue;
                _appliedBoosts.Add(key);

                float newValue = originalValue * (1f + percentageBoost);
                baseValueProperty.SetValue(stat, newValue);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DivisionTalents] ApplyWeaponBoost({statKey}) failed: {ex.Message}");
            }
        }

        /// <summary>
        /// 무기 스탯 부스트 제거 (원본 복원)
        /// </summary>
        public static void RemoveWeaponBoost(Item weapon, string statKey)
        {
            try
            {
                if (weapon == null) return;

                string key = $"Weapon_{weapon.GetInstanceID()}_{statKey}";
                if (!_appliedBoosts.Contains(key)) return;

                var stat = weapon.GetStat(statKey);
                if (stat == null) return;

                var baseValueProperty = stat.GetType().GetProperty("BaseValue");
                if (baseValueProperty == null) return;

                if (_originalStats.TryGetValue(key, out var originalValue))
                {
                    baseValueProperty.SetValue(stat, originalValue);
                    _originalStats.Remove(key);
                }

                _appliedBoosts.Remove(key);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DivisionTalents] RemoveWeaponBoost({statKey}) failed: {ex.Message}");
            }
        }

        /// <summary>
        /// 캐릭터 스탯에 부스트 적용 (덧셈)
        /// </summary>
        public static void ApplyCharacterBoost(Item characterItem, string statKey, float boostValue)
        {
            try
            {
                if (characterItem == null) return;

                var stat = characterItem.GetStat(statKey);
                if (stat == null) return;

                var baseValueProperty = stat.GetType().GetProperty("BaseValue");
                if (baseValueProperty == null) return;

                string key = $"Char_{characterItem.GetInstanceID()}_{statKey}";
                if (_appliedBoosts.Contains(key)) return;

                float originalValue = (float)baseValueProperty.GetValue(stat);
                _originalStats[key] = originalValue;
                _appliedBoosts.Add(key);

                baseValueProperty.SetValue(stat, originalValue + boostValue);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DivisionTalents] ApplyCharacterBoost({statKey}) failed: {ex.Message}");
            }
        }

        /// <summary>
        /// 캐릭터 스탯 부스트 제거
        /// </summary>
        public static void RemoveCharacterBoost(Item characterItem, string statKey)
        {
            try
            {
                if (characterItem == null) return;

                string key = $"Char_{characterItem.GetInstanceID()}_{statKey}";
                if (!_appliedBoosts.Contains(key)) return;

                var stat = characterItem.GetStat(statKey);
                if (stat == null) return;

                var baseValueProperty = stat.GetType().GetProperty("BaseValue");
                if (baseValueProperty == null) return;

                if (_originalStats.TryGetValue(key, out var originalValue))
                {
                    baseValueProperty.SetValue(stat, originalValue);
                    _originalStats.Remove(key);
                }

                _appliedBoosts.Remove(key);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DivisionTalents] RemoveCharacterBoost({statKey}) failed: {ex.Message}");
            }
        }

        /// <summary>
        /// 플레이어가 들고 있는 모든 무기 가져오기 (Duckov-totem 패턴)
        /// </summary>
        public static List<Item> GetAllEquippedWeapons(CharacterMainControl player)
        {
            var weapons = new List<Item>();

            try
            {
                if (player == null) return weapons;

                var components = player.gameObject.GetComponentsInChildren<Component>(true);
                foreach (var comp in components)
                {
                    if (comp == null) continue;
                    var typeName = comp.GetType().Name;

                    if (typeName == "ItemSetting_Gun" || typeName.Contains("Gun") ||
                        typeName == "ItemSetting_MeleeWeapon" || typeName.Contains("Weapon"))
                    {
                        var item = comp.gameObject.GetComponent<Item>();
                        if (item != null && !weapons.Contains(item))
                        {
                            weapons.Add(item);
                        }
                    }
                }
            }
            catch { }

            return weapons;
        }

        /// <summary>
        /// 플레이어의 characterItem (장비 슬롯)
        /// </summary>
        public static Item? GetCharacterItem(CharacterMainControl player)
        {
            try
            {
                return player?.CharacterItem;
            }
            catch { return null; }
        }

        /// <summary>
        /// 플레이어가 사용할 수 있는 모든 무기 가져오기 (씬의 모든 ItemSetting_Gun)
        /// 인벤토리, 장비 슬롯, 활성 무기 모두 포함
        /// </summary>
        public static List<Item> GetAllInventoryGuns(CharacterMainControl player)
        {
            var weapons = new List<Item>();

            try
            {
                // 씬의 모든 ItemSetting_Gun을 통해 Item 가져오기
                var allGuns = UnityEngine.Object.FindObjectsOfType<ItemSetting_Gun>();
                foreach (var gun in allGuns)
                {
                    if (gun == null) continue;
                    var item = gun.GetComponent<Item>();
                    if (item != null && !weapons.Contains(item))
                    {
                        weapons.Add(item);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DivisionTalents] GetAllInventoryGuns error: {ex.Message}");
            }

            return weapons;
        }

        // ─── 매거진 용량 부스트 (Extra 탤런트용) ───
        // CapacityHash로 stat에 접근해서 BaseValue 변경
        // (확인됨: AA12 BaseValue=5, Value=10이 매거진 최대 용량)

        private static int _capacityHash = 0;
        private static bool _capacityHashCached = false;
        private static bool _itemAgentGunFieldsLogged = false;

        private static int GetCapacityHash()
        {
            if (_capacityHashCached) return _capacityHash;
            _capacityHashCached = true;
            try
            {
                var f = typeof(ItemAgent_Gun).GetField("CapacityHash",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null)
                {
                    _capacityHash = (int)f.GetValue(null);
                    Debug.Log($"[DivisionTalents] CapacityHash = {_capacityHash}");
                }
                else
                {
                    Debug.LogWarning("[DivisionTalents] CapacityHash field not found in ItemAgent_Gun");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DivisionTalents] GetCapacityHash error: {ex.Message}");
            }
            return _capacityHash;
        }

        /// <summary>
        /// ItemAgent_Gun의 모든 hash 필드를 한 번 출력 (진단용)
        /// </summary>
        private static void LogAgentGunFields(Item weapon)
        {
            if (_itemAgentGunFieldsLogged) return;
            _itemAgentGunFieldsLogged = true;

            try
            {
                var t = typeof(ItemAgent_Gun);
                var bf = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                // 무기에서 stat 시도
                if (weapon != null)
                {
                    Debug.Log($"===== Probing weapon stats: {weapon.name} =====");
                    foreach (var f in t.GetFields(bf))
                    {
                        if (!f.IsStatic) continue;
                        if (!f.Name.ToLower().Contains("hash")) continue;
                        try
                        {
                            int hash = (int)f.GetValue(null);
                            var stat = weapon.GetStat(hash);
                            if (stat != null)
                            {
                                var valProp = stat.GetType().GetProperty("Value");
                                var baseProp = stat.GetType().GetProperty("BaseValue");
                                var val = valProp?.GetValue(stat);
                                var baseVal = baseProp?.GetValue(stat);
                                Debug.Log($"  Stat[{f.Name}] hash={hash}: Value={val}, BaseValue={baseVal}");
                            }
                        }
                        catch { }
                    }
                    Debug.Log("=============================================");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DivisionTalents] LogAgentGunFields error: {ex.Message}");
            }
        }

        public static void ApplyMagCapacityBoost(Item weapon, float percentageBoost)
        {
            try
            {
                if (weapon == null) return;

                // 첫 호출 시 hash 필드 모두 로깅 (디버그)
                // LogAgentGunFields(weapon); // 필요시 주석 해제

                int hash = GetCapacityHash();
                if (hash == 0) return;

                var stat = weapon.GetStat(hash);
                if (stat == null)
                {
                    Debug.LogWarning($"[DivisionTalents] No CapacityHash stat on {weapon.name}");
                    return;
                }

                var baseValueProp = stat.GetType().GetProperty("BaseValue");
                if (baseValueProp == null) return;

                string key = $"Mag_{weapon.GetInstanceID()}";
                if (_appliedBoosts.Contains(key)) return; // 이미 적용됨

                float originalValue = (float)baseValueProp.GetValue(stat);
                _originalStats[key] = originalValue;
                _appliedBoosts.Add(key);

                float newValue = originalValue * (1f + percentageBoost);
                // 정수 타입이므로 반올림
                newValue = Mathf.Round(newValue);
                baseValueProp.SetValue(stat, newValue);

                Debug.Log($"[DivisionTalents] ★ Magazine capacity: {weapon.name} {originalValue} → {newValue} ★");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DivisionTalents] ApplyMagCapacityBoost failed: {ex.Message}");
            }
        }

        public static void RemoveMagCapacityBoost(Item weapon)
        {
            try
            {
                if (weapon == null) return;

                string key = $"Mag_{weapon.GetInstanceID()}";
                if (!_appliedBoosts.Contains(key)) return;

                int hash = GetCapacityHash();
                if (hash == 0) return;

                var stat = weapon.GetStat(hash);
                if (stat == null) return;

                var baseValueProp = stat.GetType().GetProperty("BaseValue");
                if (baseValueProp == null) return;

                if (_originalStats.TryGetValue(key, out var originalValue))
                {
                    baseValueProp.SetValue(stat, originalValue);
                    _originalStats.Remove(key);
                    Debug.Log($"[DivisionTalents] Magazine capacity restored: {weapon.name} → {originalValue}");
                }
                _appliedBoosts.Remove(key);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DivisionTalents] RemoveMagCapacityBoost failed: {ex.Message}");
            }
        }
    }
}

namespace DivisionTalents
{
    /// <summary>
    /// 전기 탄약 적용기 - Actum Est가 활성화되었을 때 무기를 임시로 전기 무기로 변환
    /// Duckov-totem의 ElementalTotemsPatch와 동일한 방식 사용
    ///
    /// 게임 내 전기 무기 (TypeID 733 - St. Elmo's Engine 기반)에서
    /// bulletPfb / buff / element 를 추출해서 활성 무기에 적용
    /// </summary>
    public static class ElectricAmmoApplier
    {
        // 게임 전기 무기 TypeID
        private const int ELECTRIC_WEAPON_TYPEID = 733;

        // 전기 속성 데이터 (한 번만 로드)
        private static UnityEngine.Object? _electricBullet;
        private static object? _electricBuff;
        private static object? _electricElement;
        private static bool _referencesLoaded = false;
        private static bool _loadAttempted = false;

        // ItemSetting_Gun 필드 캐시
        private static FieldInfo? _bulletField;
        private static FieldInfo? _buffField;
        private static FieldInfo? _elementField;

        // 무기별 원본 설정 저장 (복원용)
        private static System.Runtime.CompilerServices.ConditionalWeakTable<ItemSetting_Gun, OriginalSettings> _originalSettings =
            new System.Runtime.CompilerServices.ConditionalWeakTable<ItemSetting_Gun, OriginalSettings>();

        // 현재 변경된 무기 추적 (해제할 때 사용)
        private static HashSet<int> _modifiedGuns = new HashSet<int>();

        private class OriginalSettings
        {
            public UnityEngine.Object? Bullet;
            public object? Buff;
            public object? Element;
            public float BuffChance;
        }

        /// <summary>
        /// 게임의 전기 무기에서 element/buff/bulletPfb 추출 (지연 로딩)
        /// </summary>
        public static bool LoadReferences()
        {
            if (_referencesLoaded) return true;
            if (_loadAttempted) return false;
            _loadAttempted = true;

            try
            {
                // 필드 캐싱
                var bf = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                _bulletField = typeof(ItemSetting_Gun).GetField("bulletPfb", bf);
                _buffField = typeof(ItemSetting_Gun).GetField("buff", bf);
                _elementField = typeof(ItemSetting_Gun).GetField("element", bf);

                if (_bulletField == null || _buffField == null || _elementField == null)
                {
                    Debug.LogWarning("[DivisionTalents] Electric ammo: gun fields not found");
                    return false;
                }

                // 게임의 전기 무기 인스턴스 생성 (참조용)
                Item electricWeapon = ItemAssetsCollection.InstantiateSync(ELECTRIC_WEAPON_TYPEID);
                if (electricWeapon == null)
                {
                    Debug.LogWarning($"[DivisionTalents] Electric weapon (ID {ELECTRIC_WEAPON_TYPEID}) not found");
                    return false;
                }

                var gunSetting = electricWeapon.GetComponent<ItemSetting_Gun>();
                if (gunSetting == null)
                {
                    Debug.LogWarning("[DivisionTalents] Electric weapon has no ItemSetting_Gun");
                    UnityEngine.Object.Destroy(electricWeapon.gameObject);
                    return false;
                }

                // 데이터 추출
                _electricBullet = _bulletField.GetValue(gunSetting) as UnityEngine.Object;
                _electricBuff = _buffField.GetValue(gunSetting);
                _electricElement = _elementField.GetValue(gunSetting);

                // 참조용 무기 즉시 파괴
                UnityEngine.Object.Destroy(electricWeapon.gameObject);

                if (_electricBullet != null && _electricBuff != null && _electricElement != null)
                {
                    _referencesLoaded = true;
                    Debug.Log($"[DivisionTalents] ★ Electric ammo references loaded - bullet:{_electricBullet.name}, element:{_electricElement} ★");
                    return true;
                }

                Debug.LogWarning("[DivisionTalents] Some electric references missing");
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DivisionTalents] LoadReferences error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 무기에 전기 속성 적용 - 매 발사마다 호출됨 (bulletPfb 등이 게임에서 재설정될 수 있음)
        /// </summary>
        public static void ApplyElectric(ItemSetting_Gun gun)
        {
            try
            {
                if (gun == null) return;
                if (!LoadReferences()) return;
                if (_bulletField == null || _buffField == null || _elementField == null) return;

                int gunId = gun.GetInstanceID();

                // 원본 저장 (한 번만)
                if (!_originalSettings.TryGetValue(gun, out var orig))
                {
                    orig = new OriginalSettings
                    {
                        Bullet = _bulletField.GetValue(gun) as UnityEngine.Object,
                        Buff = _buffField.GetValue(gun),
                        Element = _elementField.GetValue(gun),
                        BuffChance = GetBuffChance(gun)
                    };
                    _originalSettings.AddOrUpdate(gun, orig);
                    Debug.Log($"[DivisionTalents] Saved original gun settings for {gun.gameObject.name}");
                }

                // === 매 발사마다 전기 속성 강제 적용 ===
                _bulletField.SetValue(gun, _electricBullet);
                _buffField.SetValue(gun, _electricBuff);
                _elementField.SetValue(gun, _electricElement);
                SetBuffChance(gun, 1.0f); // 100% 확률로 전기 효과

                // 첫 적용 시에만 로그
                if (!_modifiedGuns.Contains(gunId))
                {
                    _modifiedGuns.Add(gunId);
                    Debug.Log($"[DivisionTalents] ★ Electric ammo applied to {gun.gameObject.name} ★");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DivisionTalents] ApplyElectric error: {ex.Message}");
            }
        }

        /// <summary>
        /// 무기 원본 설정 복원
        /// </summary>
        public static void RemoveElectric(ItemSetting_Gun gun)
        {
            try
            {
                if (gun == null) return;
                if (_bulletField == null || _buffField == null || _elementField == null) return;

                int gunId = gun.GetInstanceID();
                if (!_modifiedGuns.Contains(gunId)) return;

                if (_originalSettings.TryGetValue(gun, out var orig))
                {
                    _bulletField.SetValue(gun, orig.Bullet);
                    _buffField.SetValue(gun, orig.Buff);
                    _elementField.SetValue(gun, orig.Element);
                    SetBuffChance(gun, orig.BuffChance);
                }

                _modifiedGuns.Remove(gunId);
                Debug.Log($"[DivisionTalents] Electric ammo removed from {gun.gameObject.name}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DivisionTalents] RemoveElectric error: {ex.Message}");
            }
        }

        /// <summary>
        /// 모든 변경된 무기에서 전기 속성 제거
        /// </summary>
        public static void RemoveAllElectric()
        {
            try
            {
                var allGuns = UnityEngine.Object.FindObjectsOfType<ItemSetting_Gun>();
                foreach (var gun in allGuns)
                {
                    if (gun != null && _modifiedGuns.Contains(gun.GetInstanceID()))
                    {
                        RemoveElectric(gun);
                    }
                }
            }
            catch { }
        }

        // ─── Buff Chance 헬퍼 (Duckov-totem 패턴) ───

        private static FieldInfo? _buffChanceHashField;
        private static int _buffChanceHash = 0;
        private static bool _buffChanceCached = false;

        private static int GetBuffChanceHash()
        {
            if (_buffChanceCached) return _buffChanceHash;
            _buffChanceCached = true;
            try
            {
                _buffChanceHashField = typeof(ItemAgent_Gun).GetField("BuffChanceHash",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (_buffChanceHashField != null)
                {
                    _buffChanceHash = (int)_buffChanceHashField.GetValue(null);
                }
            }
            catch { }
            return _buffChanceHash;
        }

        private static float GetBuffChance(ItemSetting_Gun gun)
        {
            try
            {
                int hash = GetBuffChanceHash();
                if (hash == 0) return 0f;

                var item = gun.GetComponent<Item>();
                if (item == null) return 0f;

                var stat = item.GetStat(hash);
                if (stat == null) return 0f;

                var valueProp = stat.GetType().GetProperty("Value");
                if (valueProp != null)
                {
                    var v = valueProp.GetValue(stat);
                    if (v != null) return Convert.ToSingle(v);
                }
            }
            catch { }
            return 0f;
        }

        private static void SetBuffChance(ItemSetting_Gun gun, float chance)
        {
            try
            {
                int hash = GetBuffChanceHash();
                if (hash == 0) return;

                var item = gun.GetComponent<Item>();
                if (item == null) return;

                var stat = item.GetStat(hash);
                if (stat == null) return;

                var baseValueProp = stat.GetType().GetProperty("BaseValue");
                if (baseValueProp != null)
                {
                    baseValueProp.SetValue(stat, chance);
                }

                // value 필드도 갱신
                var valueField = stat.GetType().GetField("value",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (valueField != null)
                {
                    valueField.SetValue(stat, chance);
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// ItemSetting_Gun.UseABullet Prefix 패치
    /// Actum Est가 활성화되어 있으면 매 발사마다 전기 속성 보장
    /// </summary>
    [HarmonyPatch]
    public static class ItemSetting_Gun_UseABullet_Patch
    {
        [HarmonyTargetMethods]
        static IEnumerable<MethodBase> TargetMethods()
        {
            var methods = typeof(ItemSetting_Gun).GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);

            foreach (var m in methods)
            {
                if (m.Name == "UseABullet")
                    yield return m;
            }
        }

        [HarmonyPrefix]
        static void Prefix(ItemSetting_Gun __instance)
        {
            try
            {
                if (__instance == null) return;
                var manager = TalentManager.Instance;
                if (manager == null) return;

                if (manager.GetEquippedTalentId() == "actum_est" && manager.IsActumEstActive())
                {
                    ElectricAmmoApplier.ApplyElectric(__instance);
                }
                else
                {
                    ElectricAmmoApplier.RemoveElectric(__instance);
                }
            }
            catch { }
        }
    }
}
