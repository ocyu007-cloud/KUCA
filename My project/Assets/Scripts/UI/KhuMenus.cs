using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 포켓몬 GO 식 전체 화면들.
/// - 메인 메뉴 (가운데 경희 로고를 누르면): 도감 · 경희몬 · 도구
/// - 컬렉션 (탭 3개): 도감(종류별 발견), 경희몬(잡은 개체, CP), 도구(가방)
/// - 경희몬 상세: 파트너로 지정
/// - 프로필 (프로필 사진을 누르면): 캐릭터와 파트너, 레벨, 메뉴 4개, 활동 기록
/// 걷은 거리와 방문한 경희스팟도 여기서 기록한다. GameHUD 가 같은 캔버스에 만든다.
/// </summary>
public class KhuMenus : MonoBehaviour
{
    public enum Tab { Dex, Monsters, Items }

    static readonly Color Teal = new Color(0.17f, 0.45f, 0.49f);
    static readonly Color TealDeep = new Color(0.10f, 0.33f, 0.37f);
    static readonly Color Mint = new Color(0.38f, 0.86f, 0.70f);
    static readonly Color Gray = new Color(0.42f, 0.47f, 0.49f);
    static readonly Color Orange = new Color(0.96f, 0.58f, 0.10f);
    static readonly Color Green = new Color(0.33f, 0.78f, 0.55f);
    static readonly Color ValueGreen = new Color(0.22f, 0.66f, 0.52f);
    // 메인 메뉴 (파란 그라데이션)
    static readonly Color MenuNavy = new Color(0.10f, 0.22f, 0.52f);
    // 프로필 (버건디 그라데이션)
    static readonly Color PText = new Color(1f, 0.94f, 0.90f);
    static readonly Color PGold = new Color(0.96f, 0.79f, 0.45f);
    static readonly Color PCircle = new Color(0.84f, 0.64f, 0.32f);

    const int MonsterStorage = 300;
    const int ItemStorage = 350;

    GameHUD hud;
    Transform canvasRoot;
    RectTransform safe;
    CollectController collect;
    CollectibleSpawner spawner;
    ModularCharacter character;
    CharacterCustomizer customizer;
    KyungHeeSpots spots;
    PartnerBuddy buddy;

    GameProgress progress;
    IList<CollectibleType> types;

    // 메인 메뉴
    GameObject mainMenu;
    readonly List<Transform> menuButtons = new List<Transform>();

    // 컬렉션
    GameObject collection;
    Tab tab;
    readonly Text[] tabLabels = new Text[3];
    readonly Text[] tabCounts = new Text[3];
    readonly RectTransform[] tabLines = new RectTransform[3];
    InputField search;
    RectTransform grid;
    ScrollRect gridScroll;
    Text emptyText;
    GameObject sortButton;
    int sortMode; // 0 최근, 1 CP, 2 이름
    static readonly string[] SortNames = { "최근 잡은 순", "쿠옹력 높은 순", "이름 순" };

    // 상세 / 목록 팝업
    GameObject detail;
    RawImage detailThumb;
    Text detailCp, detailName, detailInfo, detailPartner;
    Button detailPartnerButton;
    GameProgress.Caught detailTarget;
    GameObject listPopup;
    Text listTitle;
    RectTransform listContent;

    // 스크랩북
    GameObject scrapbook, scrapViewer;
    RectTransform scrapGrid;
    Text scrapEmpty, scrapCaption;
    RawImage scrapPhoto;
    AspectRatioFitter scrapFitter;
    readonly List<Texture2D> scrapTextures = new List<Texture2D>();

    // 프로필
    GameObject profile;
    GameObject profileMe, profileOther;
    Text profileOtherText;
    readonly Text[] profileTabs = new Text[3];
    readonly RectTransform[] profileLines = new RectTransform[3];
    RawImage profileView;
    RectTransform profileHero;
    RectTransform profileRays;
    Text profileName, profilePartner, profileLevel, profileXp;
    RectTransform profileXpFill;
    Text statWalk, statCaught, statSpots;
    Camera profileCam;
    Light profileLight;
    RenderTexture profileTexture;
    float profileYaw;

    // 활동 기록
    Vector3 lastPlayerPos;
    bool hasLastPos;
    float saveTimer, spotTimer;
    bool dirty;

    public void Build(GameHUD owner, Transform canvas, RectTransform safeArea)
    {
        hud = owner;
        canvasRoot = canvas;
        safe = safeArea;
        BuildMainMenu();
        BuildCollection();
        BuildDetail();
        BuildProfile();
        BuildListPopup();
        BuildScrapbook();
    }

    void Start()
    {
        collect = FindAnyObjectByType<CollectController>();
        spawner = collect != null ? collect.spawner : FindAnyObjectByType<CollectibleSpawner>();
        character = hud.character != null ? hud.character : FindAnyObjectByType<ModularCharacter>();
        customizer = hud.customizer != null ? hud.customizer : FindAnyObjectByType<CharacterCustomizer>();
        spots = FindAnyObjectByType<KyungHeeSpots>();
        if (progress == null && collect != null)
            progress = collect.Progress;
        if (types == null && spawner != null)
            types = spawner.types;

        if (character != null)
        {
            buddy = new GameObject("PartnerBuddy").AddComponent<PartnerBuddy>();
            buddy.character = character.transform;
            RefreshBuddy();
        }
        OnProgressChanged(progress, types);
    }

    void OnDestroy()
    {
        foreach (var s in new[] { mainMenu, collection, profile })
            UIInputBlocker.SetModal(s, false);
        if (profileTexture != null)
            profileTexture.Release();
    }

    /// <summary>점수가 바뀔 때마다 GameHUD 가 부른다. 레벨 보상을 주고 열린 화면을 새로 그린다.</summary>
    public void OnProgressChanged(GameProgress p, IList<CollectibleType> t)
    {
        if (p != null) progress = p;
        if (t != null) types = t;
        if (progress == null)
            return;

        GameHUD.LevelOf(progress.score, out int level, out _, out _);
        if (progress.rewardedLevel < level)
        {
            var got = new List<(string, int)>();
            for (int lv = Mathf.Max(1, progress.rewardedLevel + 1); lv <= level; lv++)
                foreach (var r in ItemCatalog.RewardsFor(lv))
                {
                    progress.AddItem(r.id, r.count);
                    got.Add(r);
                }
            bool levelUp = progress.rewardedLevel > 0;
            progress.rewardedLevel = level;
            progress.Save();
            if (levelUp && hud != null)
                hud.Toast($"레벨 {level} 달성!  보상: {ItemCatalog.Describe(got)}");
        }

        if (collection != null && collection.activeSelf)
            FillCollection();
        if (profile != null && profile.activeSelf)
            FillProfile();
    }

    void Update()
    {
        TrackActivity();
        if (profile != null && profile.activeSelf)
        {
            UpdateProfileCamera();
            profileRays.localRotation = Quaternion.Euler(0f, 0f, Time.unscaledTime * 4f);
        }
    }

    // ---------- 활동 기록 (걸은 거리, 방문한 경희스팟) ----------

    void TrackActivity()
    {
        if (progress == null || collect == null || collect.campusMap == null || collect.campusMap.player == null)
            return;
        Vector3 p = collect.campusMap.player.position;
        if (hasLastPos)
        {
            float d = CollectibleSpawner.HorizontalDistance(p, lastPlayerPos);
            // GPS 가 튀거나 순간이동한 경우는 세지 않는다.
            if (d > 0.05f && d < 30f)
            {
                progress.walkedMeters += d;
                dirty = true;
            }
        }
        lastPlayerPos = p;
        hasLastPos = true;

        spotTimer -= Time.unscaledDeltaTime;
        if (spotTimer <= 0f && spots != null)
        {
            spotTimer = 1f;
            foreach (KyungHeeSpot s in spots.Spots)
            {
                if (s == null || !s.InRange || s.Building == null)
                    continue;
                progress.VisitSpot(s.Building.buildingId, out bool isNew);
                if (isNew)
                {
                    dirty = true;
                    hud?.Toast($"새 경희스팟 방문!  {s.Building.DisplayName}");
                }
            }
        }

        saveTimer -= Time.unscaledDeltaTime;
        if (dirty && saveTimer <= 0f)
        {
            saveTimer = 10f;
            dirty = false;
            progress.Save();
        }
    }

    void OnApplicationPause(bool paused)
    {
        if (paused && progress != null)
            progress.Save();
    }

    // ---------- 메인 메뉴 ----------

    public void OpenMainMenu()
    {
        Show(mainMenu, true);
        // 버튼이 살짝 작게 시작해서 PressScale 이 원래 크기로 되돌리며 튀어나온다.
        foreach (Transform b in menuButtons)
            b.localScale = Vector3.one * 0.75f;
    }

    void BuildMainMenu()
    {
        mainMenu = FullScreen("MainMenu", new Color(0.62f, 0.82f, 1f), new Color(0.08f, 0.24f, 0.62f), out RectTransform inner);
        MenuButton(inner, "도감", HudIcons.Dex, new Vector2(-250f, 330f), () => OpenCollection(Tab.Dex));
        MenuButton(inner, "경희몬", HudIcons.Monster, new Vector2(0f, 600f), () => OpenCollection(Tab.Monsters));
        MenuButton(inner, "도구", HudIcons.Bag, new Vector2(250f, 330f), () => OpenCollection(Tab.Items));
        CloseFab(inner, () => Show(mainMenu, false), filled: false, MenuNavy);
    }

    void MenuButton(RectTransform parent, string label, Sprite icon, Vector2 pos, UnityEngine.Events.UnityAction onClick)
    {
        const float size = 210f;
        Button b = InvisibleButton(parent, label, onClick, new Vector2(size + 20f, size + 90f));
        RectTransform rt = (RectTransform)b.transform;
        Place(rt, new Vector2(0.5f, 0f), pos);
        rt.pivot = new Vector2(0.5f, 0f);
        menuButtons.Add(rt);

        Image halo = Circle(rt, "Halo", new Color(1f, 1f, 1f, 0.55f), size + 22f);
        Place(halo.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, -11f));
        halo.rectTransform.pivot = new Vector2(0.5f, 0f);
        Image ring = Circle(rt, "Ring", MenuNavy, size);
        Place(ring.rectTransform, new Vector2(0.5f, 0f), Vector2.zero);
        ring.rectTransform.pivot = new Vector2(0.5f, 0f);
        Image face = Circle(ring.transform, "Face", new Color(0.97f, 0.99f, 0.97f), size - 14f);
        Place(face.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero);
        Image ic = UIKit.Image(face.transform, "Icon", MenuNavy);
        ic.sprite = icon;
        Place(ic.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero);
        ic.rectTransform.sizeDelta = new Vector2(size * 0.56f, size * 0.56f);

        Text t = T(rt, label, 42, TextAnchor.LowerCenter, Color.white, bold: true);
        Shadow(t);
        Place(t.rectTransform, new Vector2(0.5f, 1f), Vector2.zero);
        t.rectTransform.sizeDelta = new Vector2(size + 40f, 70f);
    }

    // ---------- 컬렉션 (도감 / 경희몬 / 도구) ----------

    public void OpenCollection(Tab which)
    {
        Show(mainMenu, false);
        Show(collection, true);
        search.text = "";
        SelectTab(which);
    }

    void BuildCollection()
    {
        collection = FullScreen("Collection", new Color(0.96f, 0.99f, 0.97f), new Color(0.85f, 0.95f, 0.90f), out RectTransform inner);
        Frame(collection.transform, new Color(0.56f, 0.86f, 0.56f));

        // 탭
        RectTransform tabs = UIKit.Rect(inner, "Tabs");
        tabs.anchorMin = new Vector2(0f, 1f);
        tabs.anchorMax = new Vector2(1f, 1f);
        tabs.pivot = new Vector2(0.5f, 1f);
        tabs.offsetMin = tabs.offsetMax = Vector2.zero;
        tabs.sizeDelta = new Vector2(0f, 210f);
        string[] names = { "도감", "경희몬", "도구" };
        for (int i = 0; i < 3; i++)
        {
            Tab which = (Tab)i;
            Button b = InvisibleButton(tabs, names[i], () => SelectTab(which), Vector2.zero);
            RectTransform br = (RectTransform)b.transform;
            br.anchorMin = new Vector2(i / 3f, 0f);
            br.anchorMax = new Vector2((i + 1) / 3f, 1f);
            br.offsetMin = br.offsetMax = Vector2.zero;
            tabLabels[i] = T(br, names[i], 42, TextAnchor.MiddleCenter, Gray, bold: true);
            SetBox(tabLabels[i].rectTransform, 0f, 0.5f, 0.85f, 0f);
            tabCounts[i] = T(br, "", 34, TextAnchor.MiddleCenter, Gray);
            SetBox(tabCounts[i].rectTransform, 0f, 0.2f, 0.5f, 0f);
            Image line = UIKit.Image(br, "Line", TealDeep);
            line.sprite = HudIcons.Pill;
            line.type = Image.Type.Sliced;
            line.pixelsPerUnitMultiplier = 20f;
            Place(line.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 14f));
            line.rectTransform.sizeDelta = new Vector2(160f, 7f);
            tabLines[i] = line.rectTransform;
        }

        // 검색
        Image bar = UIKit.Image(inner, "Search", new Color(0.86f, 0.93f, 0.86f), raycast: true);
        bar.sprite = HudIcons.Pill;
        bar.type = Image.Type.Sliced;
        bar.pixelsPerUnitMultiplier = 120f / 96f;
        RectTransform sr = bar.rectTransform;
        sr.anchorMin = new Vector2(0f, 1f);
        sr.anchorMax = new Vector2(1f, 1f);
        sr.pivot = new Vector2(0.5f, 1f);
        sr.offsetMin = new Vector2(44f, -326f);
        sr.offsetMax = new Vector2(-44f, -230f);
        Image glass = UIKit.Image(bar.transform, "Icon", TealDeep);
        glass.sprite = HudIcons.Search;
        Place(glass.rectTransform, new Vector2(0f, 0.5f), new Vector2(36f, 0f));
        glass.rectTransform.sizeDelta = new Vector2(52f, 52f);
        search = bar.gameObject.AddComponent<InputField>();
        Text input = T(bar.transform, "", 38, TextAnchor.MiddleLeft, TealDeep);
        input.supportRichText = false;
        UIKit.Stretch(input.rectTransform, 0f, 0f);
        input.rectTransform.offsetMin = new Vector2(104f, 0f);
        Text hint = T(bar.transform, "검색", 38, TextAnchor.MiddleLeft, new Color(Gray.r, Gray.g, Gray.b, 0.7f), bold: true);
        UIKit.Stretch(hint.rectTransform, 0f, 0f);
        hint.rectTransform.offsetMin = new Vector2(104f, 0f);
        search.textComponent = input;
        search.placeholder = hint;
        search.characterLimit = 20;
        search.onValueChanged.AddListener(_ => FillCollection());

        // 그리드
        RectTransform area = UIKit.Rect(inner, "Grid");
        UIKit.StretchBetween(area, 0f, 346f);
        // 내용: 등급별 구역(제목 + 3열 그리드)을 세로로 쌓는다. 경희몬·도구 탭은 제목 없는 구역 하나.
        grid = ScrollContent(area, out gridScroll);
        UIKit.Vertical(grid, 8f, new RectOffset(0, 0, 4, 280));

        emptyText = T(area, "", 38, TextAnchor.MiddleCenter, Gray);
        UIKit.Stretch(emptyText.rectTransform, 80f, 200f);

        // 정렬 버튼 (경희몬 탭)
        Button sort = InvisibleButton(inner, "Sort", CycleSort, new Vector2(124f, 124f));
        Place((RectTransform)sort.transform, new Vector2(1f, 0f), new Vector2(-50f, 60f));
        Image sRing = Circle(sort.transform, "Ring", Teal, 124f);
        Place(sRing.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero);
        Image sFace = Circle(sRing.transform, "Face", Color.white, 112f);
        Place(sFace.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero);
        Image sIcon = UIKit.Image(sFace.transform, "Icon", Teal);
        sIcon.sprite = HudIcons.Sort;
        Place(sIcon.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero);
        sIcon.rectTransform.sizeDelta = new Vector2(60f, 60f);
        sortButton = sort.gameObject;

        CloseFab(inner, () => Show(collection, false), filled: true);
    }

    void SelectTab(Tab which)
    {
        tab = which;
        for (int i = 0; i < 3; i++)
        {
            bool on = i == (int)which;
            tabLabels[i].color = tabCounts[i].color = on ? TealDeep : new Color(Gray.r, Gray.g, Gray.b, 0.55f);
            tabLines[i].gameObject.SetActive(on);
        }
        sortButton.SetActive(which == Tab.Monsters);
        gridScroll.verticalNormalizedPosition = 1f;
        FillCollection();
    }

    void CycleSort()
    {
        sortMode = (sortMode + 1) % SortNames.Length;
        hud?.Toast($"정렬: {SortNames[sortMode]}");
        FillCollection();
    }

    void FillCollection()
    {
        Clear(grid);
        if (progress == null)
            return;
        string q = search != null ? search.text.Trim() : "";

        IReadOnlyList<string> all = CreatureLibrary.AllSpecies;
        int found = 0;
        foreach (string id in all)
            if (progress.SpeciesCountOf(id) > 0)
                found++;
        tabCounts[0].text = $"{found} / {all.Count}";
        tabCounts[1].text = $"{progress.caught.Count} / {MonsterStorage}";
        tabCounts[2].text = $"{progress.TotalItems} / {ItemStorage}";

        int shown = 0;
        switch (tab)
        {
            case Tab.Dex:
                // 초록 → 파랑 → 황금 구역. 번호는 도감 전체 순서, 못 만난 동물은 그림자로.
                foreach (string tier in CreatureLibrary.Tiers)
                {
                    IReadOnlyList<string> species = CreatureLibrary.SpeciesOfTier(tier);
                    var visible = new List<string>();
                    int tierFound = 0;
                    foreach (string id in species)
                    {
                        bool known = progress.SpeciesCountOf(id) > 0;
                        if (known) tierFound++;
                        if (q.Length == 0 || (known && CreatureLibrary.NameOf(id).Contains(q)))
                            visible.Add(id);
                    }
                    if (visible.Count == 0)
                        continue;
                    RectTransform section = Section(CreatureLibrary.TierLabel(tier), CreatureLibrary.TierColor(tier),
                        $"{tierFound} / {species.Count} 발견");
                    foreach (string id in visible)
                    {
                        DexCell(section, IndexOf(all, id) + 1, id, progress.SpeciesCountOf(id) > 0);
                        shown++;
                    }
                }
                emptyText.text = shown == 0 ? "검색 결과가 없어요" : "";
                break;

            case Tab.Monsters:
                var list = new List<GameProgress.Caught>(progress.caught);
                if (sortMode == 0) list.Sort((x, y) => y.caughtAt.CompareTo(x.caughtAt));
                else if (sortMode == 1) list.Sort((x, y) => y.cp.CompareTo(x.cp));
                else list.Sort((x, y) => string.CompareOrdinal(NameOf(x), NameOf(y)));
                // 파트너는 항상 맨 앞
                int pi = list.FindIndex(c => c.uid == progress.partnerUid);
                if (pi > 0) { var pc = list[pi]; list.RemoveAt(pi); list.Insert(0, pc); }
                RectTransform mons = Section(null, Color.clear, null);
                foreach (var c in list)
                {
                    if (q.Length > 0 && !NameOf(c).Contains(q))
                        continue;
                    MonsterCell(mons, c);
                    shown++;
                }
                emptyText.text = shown > 0 ? "" : q.Length > 0 ? "검색 결과가 없어요"
                    : "아직 잡은 경희몬이 없어요\n지도에서 경희몬을 찾아 걸어가 보세요";
                break;

            case Tab.Items:
                RectTransform items = Section(null, Color.clear, null);
                foreach (var item in ItemCatalog.All)
                {
                    int n = progress.ItemCount(item.id);
                    if (n <= 0 || (q.Length > 0 && !item.name.Contains(q)))
                        continue;
                    ItemCell(items, item, n);
                    shown++;
                }
                emptyText.text = shown > 0 ? "" : q.Length > 0 ? "검색 결과가 없어요" : "가방이 비어 있어요\n레벨을 올리면 도구를 받아요";
                break;
        }
    }

    static int IndexOf(IReadOnlyList<string> list, string id)
    {
        for (int i = 0; i < list.Count; i++)
            if (list[i] == id)
                return i;
        return -1;
    }

    /// <summary>구역 하나 (title 이 있으면 색 점 + 제목 + 오른쪽 부제). 반환값이 칸을 넣을 3열 그리드.</summary>
    RectTransform Section(string title, Color color, string sub)
    {
        if (title != null)
        {
            RectTransform head = UIKit.Rect(grid, "Header_" + title);
            UIKit.Size(head, 92f);
            Image dot = Circle(head, "Dot", color, 30f);
            Place(dot.rectTransform, new Vector2(0f, 0.5f), new Vector2(52f, -6f));
            Text t = T(head, title, 40, TextAnchor.MiddleLeft, TealDeep, bold: true);
            SetBox(t.rectTransform, 98f, 0f, 1f, 300f);
            t.rectTransform.offsetMin += new Vector2(0f, -12f);
            Text s2 = T(head, sub ?? "", 32, TextAnchor.MiddleRight, Gray);
            SetBox(s2.rectTransform, 0f, 0f, 1f, 52f);
            s2.rectTransform.offsetMin = new Vector2(400f, -12f);
        }
        RectTransform section = UIKit.Rect(grid, "Grid_" + (title ?? "All"));
        var g = section.gameObject.AddComponent<GridLayoutGroup>();
        g.cellSize = new Vector2(310f, 350f);
        g.spacing = new Vector2(16f, 18f);
        g.padding = new RectOffset(20, 20, 6, 6);
        g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        g.constraintCount = 3;
        g.childAlignment = TextAnchor.UpperCenter;
        return section;
    }

    void MonsterCell(RectTransform parent, GameProgress.Caught c)
    {
        Button b = InvisibleButton(parent, c.uid, () => OpenDetail(c), Vector2.zero);
        Transform cell = b.transform;

        Text cp = T(cell, $"<size=30>쿠옹력</size> {c.cp}", 58, TextAnchor.MiddleCenter, Gray, bold: true);
        TopBox(cp.rectTransform, 0f, 74f);
        Thumb(cell, c.speciesId, true, 190f, new Vector2(0f, -172f));
        Text name = T(cell, NameOf(c), 38, TextAnchor.MiddleCenter, Gray);
        BottomBox(name.rectTransform, 40f, 60f);
        FitOneLine(name, 38);
        // 막대 색 = 등급 (초록·파랑·황금)
        Image bar = UIKit.Image(cell, "Bar", CreatureLibrary.TierColor(c.typeId));
        bar.sprite = HudIcons.Pill;
        bar.type = Image.Type.Sliced;
        bar.pixelsPerUnitMultiplier = 12f;
        Place(bar.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 22f));
        bar.rectTransform.sizeDelta = new Vector2(150f, 10f);

        if (c.uid == progress.partnerUid)
        {
            Image tag = UIKit.Image(cell, "Partner", Orange);
            tag.sprite = HudIcons.Pill;
            tag.type = Image.Type.Sliced;
            tag.pixelsPerUnitMultiplier = 120f / 44f;
            Place(tag.rectTransform, new Vector2(1f, 1f), new Vector2(-12f, -84f));
            tag.rectTransform.sizeDelta = new Vector2(110f, 44f);
            Text tt = T(tag.transform, "파트너", 26, TextAnchor.MiddleCenter, Color.white, bold: true);
            UIKit.Stretch(tt.rectTransform);
        }
    }

    void DexCell(RectTransform parent, int number, string species, bool known)
    {
        string tier = CreatureLibrary.TierOf(species);
        Button b = InvisibleButton(parent, species, () =>
        {
            if (known)
                ShowList(CreatureLibrary.NameOf(species), DexRows(species));
            else
                hud?.Toast(tier == "star" ? "아직 만나지 못한 황금 동물이에요. 캠퍼스 랜드마크 근처를 찾아보세요"
                                          : "아직 만나지 못한 동물이에요");
        }, Vector2.zero);
        Transform cell = b.transform;
        Text no = T(cell, $"No.{number:000}", 32, TextAnchor.MiddleCenter, Gray, bold: true);
        TopBox(no.rectTransform, 8f, 56f);
        Thumb(cell, species, known, 200f, new Vector2(0f, -170f));
        Text name = T(cell, known ? CreatureLibrary.NameOf(species) : "???", 34, TextAnchor.MiddleCenter, Gray);
        BottomBox(name.rectTransform, 40f, 56f);
        FitOneLine(name, 34);
        Text cnt = T(cell, known ? $"잡은 수 {progress.SpeciesCountOf(species)}" : "", 28, TextAnchor.MiddleCenter, ValueGreen);
        BottomBox(cnt.rectTransform, 6f, 36f);
    }

    List<(string, string)> DexRows(string species)
    {
        var mine = progress.caught.FindAll(c => c.speciesId == species);
        int best = 0;
        long first = long.MaxValue;
        foreach (var c in mine)
        {
            best = Mathf.Max(best, c.cp);
            first = Math.Min(first, c.caughtAt);
        }
        string tier = CreatureLibrary.TierOf(species);
        CollectibleType t = TypeOf(tier);
        var rows = new List<(string, string)>
        {
            ("등급", CreatureLibrary.TierLabel(tier)),
            ("잡은 수", $"{progress.SpeciesCountOf(species)}마리"),
            ("가지고 있는 수", $"{mine.Count}마리"),
            ("최고 쿠옹력", best > 0 ? best.ToString() : "-"),
            ("처음 만난 날", first != long.MaxValue ? new DateTime(first).ToString("yyyy.MM.dd") : "-"),
        };
        if (t != null)
            rows.Add(("1마리당 경험치", $"+{t.points} XP"));
        if (tier == "star")
            rows.Add(("사는 곳", "캠퍼스 랜드마크 근처에서만 나와요"));
        return rows;
    }

    void ItemCell(RectTransform parent, ItemCatalog.Item item, int count)
    {
        Button b = InvisibleButton(parent, item.id, () => ShowList(item.name, new List<(string, string)>
        {
            ("가지고 있는 수", $"{count}개"),
            (item.description, ""),
        }), Vector2.zero);
        Transform cell = b.transform;
        Text n = T(cell, $"×{count}", 50, TextAnchor.MiddleCenter, Gray, bold: true);
        TopBox(n.rectTransform, 0f, 74f);
        Image icon = UIKit.Image(cell, "Icon", Color.white);
        icon.sprite = HudIcons.ItemIcon(item.id);
        icon.preserveAspect = true;
        Place(icon.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -170f));
        icon.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        icon.rectTransform.sizeDelta = new Vector2(150f, 150f);
        Text name = T(cell, item.name, 40, TextAnchor.MiddleCenter, Gray);
        BottomBox(name.rectTransform, 30f, 60f);
    }

    // ---------- 경희몬 상세 ----------

    void BuildDetail()
    {
        detail = Overlay(collection.transform, "Detail", new Vector2(880f, 1160f), out RectTransform card);
        detailThumb = new GameObject("Thumb", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        detailThumb.transform.SetParent(card, false);
        detailThumb.raycastTarget = false;
        Place(detailThumb.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -40f));
        detailThumb.rectTransform.sizeDelta = new Vector2(420f, 420f);

        detailCp = T(card, "", 76, TextAnchor.MiddleCenter, Gray, bold: true);
        TopBox(detailCp.rectTransform, 470f, 100f);
        detailName = T(card, "", 60, TextAnchor.MiddleCenter, TealDeep, bold: true);
        TopBox(detailName.rectTransform, 580f, 80f);
        detailInfo = T(card, "", 32, TextAnchor.UpperCenter, Gray);
        TopBox(detailInfo.rectTransform, 680f, 120f);
        detailPartner = T(card, "", 36, TextAnchor.MiddleCenter, Orange, bold: true);
        TopBox(detailPartner.rectTransform, 810f, 60f);

        detailPartnerButton = PillButton(card, "파트너로 지정", Teal, Color.white, SetDetailAsPartner);
        Place((RectTransform)detailPartnerButton.transform, new Vector2(0.5f, 0f), new Vector2(-180f, 60f));
        ((RectTransform)detailPartnerButton.transform).sizeDelta = new Vector2(380f, 116f);
        Button close = PillButton(card, "닫기", new Color(0.88f, 0.92f, 0.9f), Gray, () => SetActive(detail, false));
        Place((RectTransform)close.transform, new Vector2(0.5f, 0f), new Vector2(210f, 60f));
        ((RectTransform)close.transform).sizeDelta = new Vector2(300f, 116f);
    }

    void OpenDetail(GameProgress.Caught c)
    {
        detailTarget = c;
        CollectibleType t = TypeOf(c.typeId);
        detailThumb.texture = ThumbOf(c.speciesId);
        detailCp.text = $"<size=40>쿠옹력</size> {c.cp}";
        detailName.text = NameOf(c);
        string xp = t != null ? $"  ·  1마리당 +{t.points} XP" : "";
        detailInfo.text = $"{CreatureLibrary.TierLabel(c.typeId)}{xp}\n잡은 날짜  {c.CaughtAt:yyyy.MM.dd HH:mm}";
        bool isPartner = c.uid == progress.partnerUid;
        detailPartner.text = isPartner ? "♥ 지금 함께 다니는 파트너예요" : "";
        detailPartnerButton.interactable = !isPartner;
        UIKit.SetLabel(detailPartnerButton, isPartner ? "현재 파트너" : "파트너로 지정");
        detailPartnerButton.GetComponent<Image>().color = isPartner ? new Color(0.7f, 0.76f, 0.77f) : Teal;
        SetActive(detail, true);
    }

    void SetDetailAsPartner()
    {
        if (detailTarget == null)
            return;
        progress.SetPartner(detailTarget.uid);
        progress.Save();
        RefreshBuddy();
        hud?.Toast($"{NameOf(detailTarget)}(쿠옹력 {detailTarget.cp}){CatchCameraScreen.Josa(NameOf(detailTarget), "과", "와")} 함께 다녀요!");
        OpenDetail(detailTarget);
        FillCollection();
    }

    void RefreshBuddy()
    {
        if (buddy == null || progress == null)
            return;
        GameProgress.Caught p = progress.Partner;
        if (p == null)
            buddy.Hide();
        else
            buddy.Show(p.speciesId);
    }

    // ---------- 목록 팝업 (역대 파트너, 모험노트, 도감·도구 정보) ----------

    void BuildListPopup()
    {
        listPopup = Overlay(canvasRoot, "ListPopup", new Vector2(900f, 1300f), out RectTransform card);
        listTitle = T(card, "", 52, TextAnchor.MiddleCenter, TealDeep, bold: true);
        TopBox(listTitle.rectTransform, 40f, 90f);
        RectTransform area = UIKit.Rect(card, "List");
        area.anchorMin = Vector2.zero;
        area.anchorMax = Vector2.one;
        area.offsetMin = new Vector2(30f, 190f);
        area.offsetMax = new Vector2(-30f, -150f);
        listContent = ScrollContent(area, out _);
        UIKit.Vertical(listContent, 6f, new RectOffset(20, 20, 10, 20));
        Button close = PillButton(card, "닫기", Teal, Color.white, () => SetActive(listPopup, false));
        Place((RectTransform)close.transform, new Vector2(0.5f, 0f), new Vector2(0f, 50f));
        ((RectTransform)close.transform).sizeDelta = new Vector2(360f, 110f);
    }

    void ShowList(string title, List<(string left, string right)> rows)
    {
        listTitle.text = title;
        Clear(listContent);
        if (rows.Count == 0)
            UIKit.Size(T(listContent, "아직 기록이 없어요", 36, TextAnchor.MiddleCenter, Gray), 160f);
        foreach (var (left, right) in rows)
        {
            RectTransform row = UIKit.Rect(listContent, "Row");
            if (string.IsNullOrEmpty(right))
            {
                T(row, left, 34, TextAnchor.UpperLeft, Gray);
                UIKit.Vertical(row, 0f, new RectOffset(0, 0, 16, 16));
                continue;
            }
            UIKit.Size(row, 96f);
            Text l = T(row, left, 36, TextAnchor.MiddleLeft, Gray);
            UIKit.Stretch(l.rectTransform);
            Text r = T(row, right, 38, TextAnchor.MiddleRight, ValueGreen, bold: true);
            UIKit.Stretch(r.rectTransform);
            Image line = UIKit.Image(row, "Line", new Color(Gray.r, Gray.g, Gray.b, 0.15f));
            line.rectTransform.anchorMin = new Vector2(0f, 0f);
            line.rectTransform.anchorMax = new Vector2(1f, 0f);
            line.rectTransform.offsetMin = Vector2.zero;
            line.rectTransform.offsetMax = new Vector2(0f, 2f);
        }
        SetActive(listPopup, true);
        listPopup.transform.SetAsLastSibling();
    }


    // ---------- 스크랩북 (잡을 때 찍은 사진) ----------

    void BuildScrapbook()
    {
        scrapbook = Overlay(canvasRoot, "Scrapbook", new Vector2(960f, 1560f), out RectTransform card);
        Text t = T(card, "스크랩북", 52, TextAnchor.MiddleCenter, TealDeep, bold: true);
        TopBox(t.rectTransform, 40f, 90f);
        RectTransform area = UIKit.Rect(card, "Grid");
        area.anchorMin = Vector2.zero;
        area.anchorMax = Vector2.one;
        area.offsetMin = new Vector2(24f, 190f);
        area.offsetMax = new Vector2(-24f, -150f);
        scrapGrid = ScrollContent(area, out _);
        var g = scrapGrid.gameObject.AddComponent<GridLayoutGroup>();
        g.cellSize = new Vector2(280f, 360f);
        g.spacing = new Vector2(16f, 20f);
        g.padding = new RectOffset(12, 12, 10, 20);
        g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        g.constraintCount = 3;
        g.childAlignment = TextAnchor.UpperCenter;
        scrapEmpty = T(area, "", 36, TextAnchor.MiddleCenter, Gray);
        UIKit.Stretch(scrapEmpty.rectTransform, 60f, 100f);
        Button close = PillButton(card, "닫기", Teal, Color.white, CloseScrapbook);
        Place((RectTransform)close.transform, new Vector2(0.5f, 0f), new Vector2(0f, 50f));
        ((RectTransform)close.transform).sizeDelta = new Vector2(360f, 110f);

        // 사진 크게 보기
        Image dim = UIKit.Image(canvasRoot, "ScrapViewer", new Color(0f, 0f, 0f, 0.92f), raycast: true);
        UIKit.Stretch(dim.rectTransform);
        scrapViewer = dim.gameObject;
        RectTransform frame = UIKit.Rect(dim.transform, "Frame");
        frame.anchorMin = Vector2.zero;
        frame.anchorMax = Vector2.one;
        frame.offsetMin = new Vector2(30f, 330f);
        frame.offsetMax = new Vector2(-30f, -120f);
        var photo = new GameObject("Photo", typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter));
        photo.transform.SetParent(frame, false);
        scrapPhoto = photo.GetComponent<RawImage>();
        scrapPhoto.raycastTarget = false;
        scrapFitter = photo.GetComponent<AspectRatioFitter>();
        scrapFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        scrapCaption = T(dim.transform, "", 36, TextAnchor.MiddleCenter, Color.white, bold: true);
        BottomBox(scrapCaption.rectTransform, 210f, 100f);
        Button back = PillButton(dim.transform, "닫기", new Color(1f, 1f, 1f, 0.15f), Color.white, () => scrapViewer.SetActive(false));
        Place((RectTransform)back.transform, new Vector2(0.5f, 0f), new Vector2(0f, 80f));
        ((RectTransform)back.transform).sizeDelta = new Vector2(320f, 110f);
        scrapViewer.SetActive(false);
    }

    void ShowScrapbook()
    {
        Clear(scrapGrid);
        FreeScrapTextures();
        var list = progress.caught.FindAll(c => !string.IsNullOrEmpty(c.photo));
        list.Sort((a, b) => b.caughtAt.CompareTo(a.caughtAt));
        int shown = 0;
        foreach (var c in list)
        {
            string path = System.IO.Path.Combine(GameProgress.ScrapbookDir, c.photo);
            if (!System.IO.File.Exists(path))
                continue;
            var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
            if (!tex.LoadImage(System.IO.File.ReadAllBytes(path)))
            {
                Destroy(tex);
                continue;
            }
            scrapTextures.Add(tex);
            GameProgress.Caught caught = c;
            Button b = InvisibleButton(scrapGrid, c.uid, () => ViewScrap(tex, caught), Vector2.zero);
            RectTransform box = UIKit.Rect(b.transform, "Box");
            TopBox(box, 0f, 270f);
            box.gameObject.AddComponent<RectMask2D>();
            Image bg = UIKit.Image(box, "Bg", new Color(0.88f, 0.92f, 0.9f));
            UIKit.Stretch(bg.rectTransform);
            var go = new GameObject("Photo", typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter));
            go.transform.SetParent(box, false);
            go.GetComponent<RawImage>().texture = tex;
            go.GetComponent<RawImage>().raycastTarget = false;
            var fit = go.GetComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fit.aspectRatio = (float)tex.width / tex.height;
            Text name = T(b.transform, NameOf(c), 32, TextAnchor.MiddleCenter, Gray, bold: true);
            BottomBox(name.rectTransform, 34f, 50f);
            Text date = T(b.transform, c.CaughtAt.ToString("yyyy.MM.dd"), 26, TextAnchor.MiddleCenter, new Color(Gray.r, Gray.g, Gray.b, 0.75f));
            BottomBox(date.rectTransform, 2f, 34f);
            shown++;
        }
        scrapEmpty.text = shown == 0 ? "아직 사진이 없어요\n경희몬을 잡을 때 찍은 사진이 여기에 모여요" : "";
        SetActive(scrapbook, true);
    }

    void ViewScrap(Texture2D tex, GameProgress.Caught c)
    {
        scrapPhoto.texture = tex;
        scrapFitter.aspectRatio = (float)tex.width / tex.height;
        scrapCaption.text = $"{NameOf(c)}  ·  쿠옹력 {c.cp}  ·  {c.CaughtAt:yyyy.MM.dd HH:mm}";
        SetActive(scrapViewer, true);
    }

    void CloseScrapbook()
    {
        scrapbook.SetActive(false);
        scrapViewer.SetActive(false);
        Clear(scrapGrid);
        FreeScrapTextures();
    }

    void FreeScrapTextures()
    {
        foreach (var t in scrapTextures)
            if (t != null)
                Destroy(t);
        scrapTextures.Clear();
    }

    // ---------- 프로필 ----------

    public void OpenProfile()
    {
        Show(profile, true);
        SelectProfileTab(0);
        profileYaw = 0f;
        FillProfile();
        EnsureProfileCamera();
        if (buddy != null)
            buddy.profilePose = true;
    }

    void CloseProfile()
    {
        Show(profile, false);
        if (profileCam != null)
            profileCam.enabled = profileLight.enabled = false;
        if (buddy != null)
            buddy.profilePose = false;
    }

    void BuildProfile()
    {
        profile = FullScreen("Profile", new Color(0.62f, 0.13f, 0.22f), new Color(0.24f, 0.02f, 0.08f), out RectTransform inner);
        Frame(profile.transform, new Color(0.40f, 0.05f, 0.12f));

        // 탭: 나 / 프렌드 / 소셜
        RectTransform tabs = UIKit.Rect(inner, "Tabs");
        tabs.anchorMin = new Vector2(0f, 1f);
        tabs.anchorMax = new Vector2(1f, 1f);
        tabs.pivot = new Vector2(0.5f, 1f);
        tabs.offsetMin = tabs.offsetMax = Vector2.zero;
        tabs.sizeDelta = new Vector2(0f, 190f);
        string[] names = { "나", "프렌드", "소셜" };
        for (int i = 0; i < 3; i++)
        {
            int idx = i;
            Button b = InvisibleButton(tabs, names[i], () => SelectProfileTab(idx), Vector2.zero);
            RectTransform br = (RectTransform)b.transform;
            br.anchorMin = new Vector2(i / 3f, 0f);
            br.anchorMax = new Vector2((i + 1) / 3f, 1f);
            br.offsetMin = br.offsetMax = Vector2.zero;
            profileTabs[i] = T(br, names[i], 42, TextAnchor.MiddleCenter, PText, bold: true);
            SetBox(profileTabs[i].rectTransform, 0f, 0.3f, 0.8f, 0f);
            Image line = UIKit.Image(br, "Line", PGold);
            Place(line.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 16f));
            line.rectTransform.sizeDelta = new Vector2(230f, 6f);
            profileLines[i] = line.rectTransform;
        }

        // "나" 탭: 세로로 스크롤되는 내용
        RectTransform area = UIKit.Rect(inner, "Me");
        UIKit.StretchBetween(area, 0f, 190f);
        profileMe = area.gameObject;
        RectTransform content = ScrollContent(area, out ScrollRect scroll);
        UIKit.Vertical(content, 0f, new RectOffset(0, 0, 0, 240));

        BuildProfileHero(Section(content, "Hero", 960f), scroll);
        BuildProfileLevel(Section(content, "Level", 200f));
        BuildProfileButtons(Section(content, "Buttons", 300f));
        BuildProfileStats(content);

        // 프렌드 / 소셜 (준비 중)
        RectTransform other = UIKit.Rect(inner, "Other");
        UIKit.StretchBetween(other, 0f, 190f);
        profileOther = other.gameObject;
        profileOtherText = T(other, "", 40, TextAnchor.MiddleCenter, PText);
        UIKit.Stretch(profileOtherText.rectTransform, 80f, 200f);

        CloseFab(inner, CloseProfile, filled: true, PCircle);
    }

    void BuildProfileHero(RectTransform s, ScrollRect scroll)
    {
        Image rays = UIKit.Image(s, "Rays", new Color(1f, 0.85f, 0.6f, 0.22f));
        rays.sprite = HudIcons.Rays;
        Place(rays.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(40f, 0f));
        rays.rectTransform.sizeDelta = new Vector2(900f, 900f); // 영역(높이 960) 안에서 사라지도록
        profileRays = rays.rectTransform;
        s.gameObject.AddComponent<RectMask2D>();

        profileView = new GameObject("View", typeof(RectTransform), typeof(RawImage), typeof(ProfileDragArea)).GetComponent<RawImage>();
        profileView.transform.SetParent(s, false);
        UIKit.Stretch(profileView.rectTransform);
        var drag = profileView.GetComponent<ProfileDragArea>();
        drag.scroll = scroll;
        drag.onDragX = dx => profileYaw += dx * 0.4f;
        profileHero = s;

        profileName = T(s, "", 66, TextAnchor.UpperLeft, PGold, bold: true);
        profileName.horizontalOverflow = HorizontalWrapMode.Overflow;
        TopBox(profileName.rectTransform, 30f, 90f);
        profileName.rectTransform.offsetMin = new Vector2(56f, profileName.rectTransform.offsetMin.y);
        profilePartner = T(s, "", 44, TextAnchor.UpperLeft, PGold);
        profilePartner.horizontalOverflow = HorizontalWrapMode.Overflow;
        TopBox(profilePartner.rectTransform, 112f, 60f);
        profilePartner.rectTransform.offsetMin = new Vector2(58f, profilePartner.rectTransform.offsetMin.y);

        Button change = PillButton(s, "파트너 정하기", new Color(1f, 1f, 1f, 0.14f), PGold, () => OpenCollection(Tab.Monsters));
        Place((RectTransform)change.transform, new Vector2(0f, 1f), new Vector2(56f, -190f));
        ((RectTransform)change.transform).sizeDelta = new Vector2(250f, 70f);
        change.GetComponentInChildren<Text>().fontSize = 30;
    }

    void BuildProfileLevel(RectTransform s)
    {
        profileLevel = T(s, "1", 130, TextAnchor.LowerLeft, PGold, bold: true);
        profileLevel.horizontalOverflow = HorizontalWrapMode.Overflow;
        Place(profileLevel.rectTransform, new Vector2(0f, 1f), new Vector2(52f, 0f));
        profileLevel.rectTransform.sizeDelta = new Vector2(220f, 140f);
        Text lv = T(s, "레벨", 34, TextAnchor.UpperLeft, PGold, bold: true);
        Place(lv.rectTransform, new Vector2(0f, 1f), new Vector2(60f, -140f));
        lv.rectTransform.sizeDelta = new Vector2(200f, 50f);

        Image bar = UIKit.Image(s, "Bar", new Color(1f, 1f, 1f, 0.15f));
        bar.sprite = HudIcons.Pill;
        bar.type = Image.Type.Sliced;
        bar.pixelsPerUnitMultiplier = 6f;
        RectTransform br = bar.rectTransform;
        br.anchorMin = new Vector2(0f, 1f);
        br.anchorMax = new Vector2(1f, 1f);
        br.offsetMin = new Vector2(250f, -92f);
        br.offsetMax = new Vector2(-120f, -72f);
        Image fill = UIKit.Image(bar.transform, "Fill", PGold);
        fill.sprite = HudIcons.Pill;
        fill.type = Image.Type.Sliced;
        fill.pixelsPerUnitMultiplier = 6f;
        profileXpFill = fill.rectTransform;
        profileXpFill.anchorMin = Vector2.zero;
        profileXpFill.offsetMin = profileXpFill.offsetMax = Vector2.zero;

        Image chev = UIKit.Image(s, "Chevron", PGold);
        chev.sprite = HudIcons.Chevron;
        Place(chev.rectTransform, new Vector2(1f, 1f), new Vector2(-50f, -50f));
        chev.rectTransform.sizeDelta = new Vector2(64f, 64f);

        profileXp = T(s, "", 44, TextAnchor.MiddleRight, PGold, bold: true);
        RectTransform xr = profileXp.rectTransform;
        xr.anchorMin = new Vector2(0f, 1f);
        xr.anchorMax = new Vector2(1f, 1f);
        xr.offsetMin = new Vector2(250f, -170f);
        xr.offsetMax = new Vector2(-120f, -104f);
    }

    void BuildProfileButtons(RectTransform s)
    {
        var items = new (string label, Sprite icon, UnityEngine.Events.UnityAction action)[]
        {
            ("역대\n파트너", HudIcons.Partner, ShowPartnerHistory),
            ("스크랩북", HudIcons.Scrapbook, ShowScrapbook),
            ("모험노트", HudIcons.Notebook, ShowAdventureNote),
            ("갈아입는다", HudIcons.Hanger, () => { if (customizer != null) customizer.SetOpen(true); }),
        };
        for (int i = 0; i < items.Length; i++)
        {
            Button b = InvisibleButton(s, items[i].label, items[i].action, new Vector2(220f, 290f));
            RectTransform rt = (RectTransform)b.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2((i + 0.5f) / 4f, 1f);
            rt.anchoredPosition = new Vector2(0f, -10f);
            Image c = Circle(rt, "Circle", PCircle, 148f);
            Place(c.rectTransform, new Vector2(0.5f, 1f), Vector2.zero);
            Image hi = Circle(c.transform, "Shine", new Color(1f, 1f, 1f, 0.16f), 120f);
            Place(hi.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(-8f, 8f));
            Image ic = UIKit.Image(c.transform, "Icon", Color.white);
            ic.sprite = items[i].icon;
            Place(ic.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero);
            ic.rectTransform.sizeDelta = new Vector2(80f, 80f);
            Text t = T(rt, items[i].label, 30, TextAnchor.UpperCenter, PText);
            Place(t.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -160f));
            t.rectTransform.sizeDelta = new Vector2(220f, 100f);
        }
    }

    void BuildProfileStats(RectTransform content)
    {
        RectTransform div = Section(content, "Divider", 70f);
        Image l1 = UIKit.Image(div, "L", new Color(PText.r, PText.g, PText.b, 0.25f));
        l1.rectTransform.anchorMin = new Vector2(0f, 0.5f);
        l1.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        l1.rectTransform.offsetMin = new Vector2(30f, -1f);
        l1.rectTransform.offsetMax = new Vector2(-70f, 1f);
        Image l2 = UIKit.Image(div, "R", new Color(PText.r, PText.g, PText.b, 0.25f));
        l2.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        l2.rectTransform.anchorMax = new Vector2(1f, 0.5f);
        l2.rectTransform.offsetMin = new Vector2(70f, -1f);
        l2.rectTransform.offsetMax = new Vector2(-30f, 1f);
        Text dt = T(div, "활 동", 30, TextAnchor.MiddleCenter, new Color(PText.r, PText.g, PText.b, 0.7f));
        UIKit.Stretch(dt.rectTransform);

        statWalk = StatRow(content, HudIcons.Footprints, "걸은 거리");
        statCaught = StatRow(content, HudIcons.Monster, "잡은 경희몬");
        statSpots = StatRow(content, HudIcons.Pin, "방문한 경희스팟");
    }

    Text StatRow(RectTransform content, Sprite icon, string label)
    {
        RectTransform row = Section(content, label, 110f);
        Image ic = UIKit.Image(row, "Icon", PGold);
        ic.sprite = icon;
        Place(ic.rectTransform, new Vector2(0f, 0.5f), new Vector2(90f, 0f));
        ic.rectTransform.sizeDelta = new Vector2(66f, 66f);
        Text l = T(row, label, 40, TextAnchor.MiddleLeft, PText);
        SetBox(l.rectTransform, 190f, 0f, 1f, 400f);
        Text v = T(row, "", 42, TextAnchor.MiddleLeft, PGold, bold: true);
        SetBox(v.rectTransform, 0f, 0f, 1f, 60f);
        v.rectTransform.anchorMin = new Vector2(0.6f, 0f);
        return v;
    }

    void SelectProfileTab(int i)
    {
        for (int k = 0; k < 3; k++)
        {
            profileTabs[k].color = k == i ? PGold : new Color(PText.r, PText.g, PText.b, 0.55f);
            profileLines[k].gameObject.SetActive(k == i);
        }
        profileMe.SetActive(i == 0);
        profileOther.SetActive(i != 0);
        profileOtherText.text = i == 1 ? "프렌드 기능은 준비 중이에요\n같이 캠퍼스를 걷는 친구를 곧 추가할 수 있어요"
                                       : "소셜 기능은 준비 중이에요";
        if (profileView != null)
            profileView.enabled = i == 0;
    }

    void FillProfile()
    {
        if (progress == null)
            return;
        profileName.text = hud != null ? hud.Nickname : "";
        GameProgress.Caught partner = progress.Partner;
        profilePartner.text = partner != null ? $"& {NameOf(partner)}" : "& 파트너를 정해 보세요";

        GameHUD.LevelOf(progress.score, out int level, out int xpIn, out int xpNeed);
        profileLevel.text = level.ToString();
        profileXp.text = $"{xpIn:N0} / {xpNeed:N0}";
        profileXpFill.anchorMax = new Vector2(Mathf.Max(0.03f, xpNeed > 0 ? (float)xpIn / xpNeed : 0f), 1f);

        statWalk.text = progress.walkedMeters >= 1000f ? $"{progress.walkedMeters / 1000f:F1} km" : $"{progress.walkedMeters:F0} m";
        statCaught.text = $"{progress.totalCollected:N0}";
        statSpots.text = $"{progress.visitedSpots.Count:N0}";
    }

    void ShowPartnerHistory()
    {
        var rows = new List<(string, string)>();
        for (int i = progress.partnerHistory.Count - 1; i >= 0; i--)
        {
            var r = progress.partnerHistory[i];
            GameProgress.Caught c = progress.Find(r.uid);
            string name = c != null ? $"{NameOf(c)} (쿠옹력 {c.cp})" : "떠나간 경희몬";
            rows.Add((name, new DateTime(r.since).ToString("yyyy.MM.dd") + (r.uid == progress.partnerUid ? " ~ 지금" : "")));
        }
        ShowList("역대 파트너", rows);
    }

    void ShowAdventureNote()
    {
        var list = new List<GameProgress.Caught>(progress.caught);
        list.Sort((a, b) => b.caughtAt.CompareTo(a.caughtAt));
        var rows = new List<(string, string)>();
        for (int i = 0; i < list.Count && i < 50; i++)
            rows.Add(($"{list[i].CaughtAt:MM.dd HH:mm}  {NameOf(list[i])}", $"쿠옹력 {list[i].cp}"));
        ShowList("모험노트", rows);
    }

    void EnsureProfileCamera()
    {
        if (character == null)
            return;
        if (profileCam == null)
        {
            var camGo = new GameObject("ProfileCamera", typeof(Camera));
            camGo.transform.SetParent(transform, false);
            profileCam = camGo.GetComponent<Camera>();
            profileCam.clearFlags = CameraClearFlags.SolidColor;
            profileCam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            profileCam.cullingMask = 1 << character.gameObject.layer;
            profileCam.fieldOfView = 26f;
            profileCam.nearClipPlane = 1f;
            profileCam.farClipPlane = 500f;

            var lightGo = new GameObject("ProfileLight", typeof(Light));
            lightGo.transform.SetParent(transform, false);
            profileLight = lightGo.GetComponent<Light>();
            profileLight.type = LightType.Directional;
            profileLight.intensity = 1.1f;
            profileLight.shadows = LightShadows.None;
            profileLight.cullingMask = 1 << character.gameObject.layer;
        }

        // 화면 영역 비율에 맞는 렌더 텍스처
        Canvas.ForceUpdateCanvases();
        Rect r = profileHero.rect;
        float aspect = r.height > 1f ? r.width / r.height : 1f;
        int h = 900, w = Mathf.Clamp(Mathf.RoundToInt(h * aspect), 256, 2048);
        if (profileTexture == null || profileTexture.width != w || profileTexture.height != h)
        {
            if (profileTexture != null)
                profileTexture.Release();
            profileTexture = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { name = "ProfileView", antiAliasing = 2 };
        }
        profileCam.targetTexture = profileTexture;
        profileView.texture = profileTexture;
        profileCam.enabled = profileLight.enabled = true;
    }

    void UpdateProfileCamera()
    {
        if (profileCam == null || character == null)
            return;
        // 꾸미기 화면이 위에 열려 있으면 그쪽 미리보기 카메라에 맡긴다.
        bool show = profileMe.activeSelf && !CharacterCustomizer.IsOpen;
        profileCam.enabled = profileLight.enabled = show;
        // 캐릭터와 왼쪽의 파트너가 함께 들어오도록, 둘 사이를 바라본다.
        Transform c = character.transform;
        Vector3 pivot = c.TransformPoint(-0.4f, 0.85f, 0f);
        Vector3 offset = Quaternion.Euler(0f, profileYaw, 0f) * new Vector3(0f, 0.25f, 5.2f);
        Vector3 pos = c.TransformPoint(new Vector3(-0.4f, 0.85f, 0f) + offset);
        profileCam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(pivot - pos, Vector3.up));
        profileLight.transform.rotation = profileCam.transform.rotation * Quaternion.Euler(25f, -30f, 0f);
    }

    // ---------- 공통 도구 ----------

    CollectibleType TypeOf(string id) => spawner != null ? spawner.TypeOf(id) : null;

    static string NameOf(GameProgress.Caught c) => string.IsNullOrEmpty(c.speciesId) ? "경희몬" : CreatureLibrary.NameOf(c.speciesId);

    static Texture ThumbOf(string species) => MonsterThumbnails.Get(species);

    void Thumb(Transform cell, string species, bool known, float size, Vector2 pos)
    {
        var raw = new GameObject("Thumb", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        raw.transform.SetParent(cell, false);
        raw.raycastTarget = false;
        raw.texture = ThumbOf(species);
        // 못 만난 동물은 실루엣(그림자)만 보인다
        raw.color = known ? Color.white : new Color(0.12f, 0.17f, 0.19f, 0.8f);
        RectTransform rt = raw.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(size, size);
    }

    void Show(GameObject screen, bool open)
    {
        screen.SetActive(open);
        UIInputBlocker.SetModal(screen, open);
        if (open)
            screen.transform.SetAsLastSibling();
    }

    static void SetActive(GameObject go, bool on)
    {
        go.SetActive(on);
        if (on)
            go.transform.SetAsLastSibling();
    }

    /// <summary>그라데이션 배경의 전체 화면. inner 는 안전 영역.</summary>
    GameObject FullScreen(string name, Color topLeft, Color bottomRight, out RectTransform inner)
    {
        var raw = new GameObject(name, typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        raw.transform.SetParent(canvasRoot, false);
        raw.texture = HudIcons.Gradient(topLeft, bottomRight);
        raw.raycastTarget = true;
        UIKit.Stretch(raw.rectTransform);
        inner = UIKit.Rect(raw.transform, "Inner");
        inner.anchorMin = safe.anchorMin;
        inner.anchorMax = safe.anchorMax;
        inner.offsetMin = inner.offsetMax = Vector2.zero;
        raw.gameObject.SetActive(false);
        return raw.gameObject;
    }

    /// <summary>화면 양옆의 색 띠</summary>
    static void Frame(Transform parent, Color color)
    {
        for (int i = 0; i < 2; i++)
        {
            Image bar = UIKit.Image(parent, i == 0 ? "FrameL" : "FrameR", color);
            RectTransform r = bar.rectTransform;
            r.anchorMin = new Vector2(i, 0f);
            r.anchorMax = new Vector2(i, 1f);
            r.pivot = new Vector2(i, 0.5f);
            r.offsetMin = r.offsetMax = Vector2.zero;
            r.sizeDelta = new Vector2(16f, 0f);
        }
    }

    /// <summary>화면을 어둡게 덮고 가운데에 흰 카드를 띄운다.</summary>
    static GameObject Overlay(Transform parent, string name, Vector2 size, out RectTransform card)
    {
        Image dim = UIKit.Image(parent, name, new Color(0.05f, 0.1f, 0.12f, 0.45f), raycast: true);
        UIKit.Stretch(dim.rectTransform);
        Image c = UIKit.Image(dim.transform, "Card", new Color(0.98f, 1f, 0.99f));
        c.sprite = HudIcons.Pill;
        c.type = Image.Type.Sliced;
        c.pixelsPerUnitMultiplier = 1.4f;
        c.raycastTarget = true;
        Place(c.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero);
        c.rectTransform.sizeDelta = size;
        card = c.rectTransform;
        dim.gameObject.SetActive(false);
        return dim.gameObject;
    }

    /// <summary>아래 가운데의 둥근 닫기 버튼</summary>
    static void CloseFab(RectTransform parent, UnityEngine.Events.UnityAction onClick, bool filled, Color? accent = null)
    {
        Color ringColor = accent ?? Teal;
        Color fillColor = accent.HasValue ? Color.Lerp(accent.Value, Color.black, 0.12f) : new Color(0.2f, 0.56f, 0.58f);
        const float size = 124f;
        Button b = InvisibleButton(parent, "Close", onClick, new Vector2(size, size));
        Place((RectTransform)b.transform, new Vector2(0.5f, 0f), new Vector2(0f, 60f));
        Image halo = Circle(b.transform, "Halo", new Color(1f, 1f, 1f, filled ? 0.9f : 0.55f), size + 16f);
        Place(halo.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero);
        Image ring = Circle(b.transform, "Ring", ringColor, size);
        Place(ring.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero);
        Image face = Circle(ring.transform, "Face", filled ? fillColor : new Color(0.97f, 0.99f, 0.97f), size - 10f);
        Place(face.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero);
        Image x = UIKit.Image(face.transform, "X", filled ? Color.white : ringColor);
        x.sprite = HudIcons.Close;
        Place(x.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero);
        x.rectTransform.sizeDelta = new Vector2(64f, 64f);
    }

    static Button PillButton(Transform parent, string label, Color bg, Color fg, UnityEngine.Events.UnityAction onClick)
    {
        Button b = UIKit.Button(parent, label, onClick, bg, 40);
        Image img = b.GetComponent<Image>();
        img.sprite = HudIcons.Pill;
        img.type = Image.Type.Sliced;
        img.pixelsPerUnitMultiplier = 1.1f;
        Text t = b.GetComponentInChildren<Text>();
        t.color = fg;
        t.fontStyle = FontStyle.Bold;
        b.targetGraphic = img;
        return b;
    }

    static Button InvisibleButton(Transform parent, string name, UnityEngine.Events.UnityAction onClick, Vector2 size)
    {
        Image hit = UIKit.Image(parent, name, new Color(0f, 0f, 0f, 0f), raycast: true);
        if (size != Vector2.zero)
            hit.rectTransform.sizeDelta = size;
        var button = hit.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(onClick);
        hit.gameObject.AddComponent<PressScale>();
        return button;
    }

    static Image Circle(Transform parent, string name, Color color, float size)
    {
        Image img = UIKit.Image(parent, name, color);
        img.sprite = HudIcons.Circle;
        img.rectTransform.sizeDelta = new Vector2(size, size);
        return img;
    }

    static void Shadow(Text t)
    {
        var o = t.gameObject.AddComponent<Outline>();
        o.effectColor = new Color(0f, 0f, 0.15f, 0.35f);
        o.effectDistance = new Vector2(2f, -2f);
    }

    /// <summary>"체육대학관 아기 사자"처럼 긴 이름은 한 줄에 들어가도록 글자를 줄인다.</summary>
    static void FitOneLine(Text t, int maxSize)
    {
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Truncate;
        t.resizeTextForBestFit = true;
        t.resizeTextMinSize = 20;
        t.resizeTextMaxSize = maxSize;
        t.rectTransform.offsetMin = new Vector2(10f, t.rectTransform.offsetMin.y);
        t.rectTransform.offsetMax = new Vector2(-10f, t.rectTransform.offsetMax.y);
    }

    static Text T(Transform parent, string text, int size, TextAnchor anchor, Color color, bool bold = false)
    {
        Text t = UIKit.Text(parent, text, size, anchor, color);
        if (bold)
            t.fontStyle = FontStyle.Bold;
        return t;
    }

    static RectTransform Section(RectTransform content, string name, float height)
    {
        RectTransform s = UIKit.Rect(content, name);
        UIKit.Size(s, height);
        return s;
    }

    /// <summary>세로로 스크롤되는 영역을 만들고 내용을 넣을 content 를 돌려준다 (레이아웃은 호출한 쪽에서).</summary>
    static RectTransform ScrollContent(RectTransform area, out ScrollRect scroll)
    {
        scroll = area.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Elastic;
        scroll.scrollSensitivity = 30f;
        RectTransform viewport = UIKit.Rect(area, "Viewport");
        UIKit.Stretch(viewport);
        viewport.gameObject.AddComponent<RectMask2D>();
        var hit = viewport.gameObject.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);
        RectTransform content = UIKit.Rect(viewport, "Content");
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.offsetMin = content.offsetMax = Vector2.zero;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.viewport = viewport;
        scroll.content = content;
        return content;
    }

    static void Place(RectTransform rt, Vector2 anchor, Vector2 pos)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
        rt.anchoredPosition = pos;
    }

    /// <summary>위에서 top 만큼 내려온 곳에 높이 height 인 가로로 꽉 찬 칸</summary>
    static void TopBox(RectTransform rt, float top, float height)
    {
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(0f, -top - height);
        rt.offsetMax = new Vector2(0f, -top);
    }

    /// <summary>아래에서 bottom 만큼 올라온 곳에 높이 height 인 가로로 꽉 찬 칸</summary>
    static void BottomBox(RectTransform rt, float bottom, float height)
    {
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.offsetMin = new Vector2(0f, bottom);
        rt.offsetMax = new Vector2(0f, bottom + height);
    }

    static void SetBox(RectTransform rt, float left, float yMin, float yMax, float right)
    {
        rt.anchorMin = new Vector2(0f, yMin);
        rt.anchorMax = new Vector2(1f, yMax);
        rt.offsetMin = new Vector2(left, 0f);
        rt.offsetMax = new Vector2(-right, 0f);
    }

    static void Clear(RectTransform content)
    {
        for (int i = content.childCount - 1; i >= 0; i--)
            Destroy(content.GetChild(i).gameObject);
    }
}

/// <summary>
/// 프로필의 캐릭터 영역: 좌우로 끌면 캐릭터를 돌리고, 위아래로 끌면 바깥 스크롤로 넘긴다.
/// </summary>
public class ProfileDragArea : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public ScrollRect scroll;
    public Action<float> onDragX;
    bool vertical;

    public void OnBeginDrag(PointerEventData e)
    {
        vertical = Mathf.Abs(e.delta.y) > Mathf.Abs(e.delta.x);
        if (vertical && scroll != null)
            scroll.OnBeginDrag(e);
    }

    public void OnDrag(PointerEventData e)
    {
        if (vertical && scroll != null)
            scroll.OnDrag(e);
        else
            onDragX?.Invoke(e.delta.x);
    }

    public void OnEndDrag(PointerEventData e)
    {
        if (vertical && scroll != null)
            scroll.OnEndDrag(e);
    }
}
