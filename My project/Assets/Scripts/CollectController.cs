using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
#endif

/// <summary>
/// 수집 대상을 탭하면, Player가 수집 반경 안에 있을 때 카메라 화면을 열고, 함께 사진을 찍으면 획득한다.
/// Player 둘레에 수집 반경 링을 그리고, 반경 안의 대상을 강조한다.
/// </summary>
public class CollectController : MonoBehaviour
{
    public CampusMap campusMap;
    public CollectibleSpawner spawner;
    public GameHUD hud;
    public Camera cam;
    [Tooltip("잡을 때 여는 카메라 화면. 비우면 같은 오브젝트에 만든다.")]
    public CatchCameraScreen catchScreen;

    [Tooltip("수집할 수 있는 거리 (m)")]
    public float collectRadius = 40f;

    [Header("Tap")]
    [Tooltip("이보다 많이 움직이면 탭이 아니라 드래그로 본다 (픽셀)")]
    public float tapMaxMove = 20f;
    public float tapMaxDuration = 0.4f;

    public GameProgress Progress { get; private set; }

    /// <summary>경희스팟을 탭했을 때 (메모판이 듣는다)</summary>
    public event System.Action<KyungHeeSpot> SpotTapped;

    /// <summary>수집 대상도 스팟도 아닌 건물을 탭했을 때</summary>
    public event System.Action<CampusBuildingInfo> BuildingTapped;

    LineRenderer ring;
    const int RingSegments = 64;

    void Awake()
    {
        Progress = GameProgress.Load();
        if (cam == null)
            cam = Camera.main;
        CreateRing();
    }

#if ENABLE_INPUT_SYSTEM
    void OnEnable() => EnhancedTouchSupport.Enable();
    void OnDisable() => EnhancedTouchSupport.Disable();
#endif

    void Start()
    {
        if (catchScreen == null && !TryGetComponent(out catchScreen))
            catchScreen = gameObject.AddComponent<CatchCameraScreen>();
        if (hud != null)
            hud.ShowProgress(Progress, spawner != null ? spawner.types : null);
    }

    void Update()
    {
        if (campusMap == null || campusMap.player == null)
            return;

        Vector3 p = campusMap.player.position;
        UpdateRing(p);

        if (spawner != null)
            foreach (var c in spawner.Active)
                if (c != null)
                    c.SetInRange(CollectibleSpawner.HorizontalDistance(c.transform.position, p) <= collectRadius);

        if (TryGetTap(out Vector2 screenPos) && UIInputBlocker.AllowsGameInput(screenPos))
            HandleTap(screenPos);
    }

    bool TryGetTap(out Vector2 pos)
    {
        pos = default;
#if ENABLE_INPUT_SYSTEM
        foreach (Touch t in Touch.activeTouches)
        {
            if (t.phase != UnityEngine.InputSystem.TouchPhase.Ended)
                continue;
            if (Touch.activeTouches.Count > 1)
                return false;
            float moved = (t.screenPosition - t.startScreenPosition).magnitude;
            float duration = (float)(t.time - t.startTime);
            if (moved <= tapMaxMove && duration <= tapMaxDuration)
            {
                pos = t.screenPosition;
                return true;
            }
        }

        Mouse mouse = Mouse.current;
        if (Touch.activeTouches.Count == 0 && mouse != null && mouse.leftButton.wasReleasedThisFrame)
        {
            pos = mouse.position.ReadValue();
            return true;
        }
#endif
        return false;
    }

    void HandleTap(Vector2 screenPos)
    {
        if (cam == null || spawner == null)
            return;

        Ray ray = cam.ScreenPointToRay(screenPos);
        Collectible target = null;
        KyungHeeSpot spot = null;
        float best = float.MaxValue;
        // 화면에서 가장 앞에 있는 수집 대상 또는 경희스팟
        foreach (RaycastHit hit in Physics.RaycastAll(ray, 5000f, ~0, QueryTriggerInteraction.Collide))
        {
            if (hit.distance >= best)
                continue;
            var c = hit.collider.GetComponent<Collectible>();
            var s = hit.collider.GetComponent<KyungHeeSpot>();
            if (c == null && s == null)
                continue;
            best = hit.distance;
            target = c;
            spot = s;
        }
        if (spot != null)
        {
            SpotTapped?.Invoke(spot);
            return;
        }
        if (target == null)
        {
            TapBuilding(ray);
            return;
        }

        float dist = CollectibleSpawner.HorizontalDistance(target.transform.position, campusMap.player.position);
        if (dist > collectRadius)
        {
            hud?.Toast($"{target.DisplayName}까지 {dist:F0}m — {collectRadius:F0}m 안으로 걸어가세요");
            return;
        }

        // 바로 얻지 않고, 카메라 화면에서 경희몬과 함께 사진을 찍어야 잡힌다.
        if (catchScreen != null)
            catchScreen.Open(target, this);
        else
            CompleteCatch(target.Type, target, "", target.SpeciesId);
    }

    /// <summary>사진을 찍어 잡았을 때 (CatchCameraScreen 이 부른다). target 은 그사이 사라졌으면 null.
    /// speciesId: 동물 캐릭터면 그 id (도감 기록용), 아니면 null.</summary>
    public GameProgress.Caught CompleteCatch(CollectibleType type, Collectible target, string photoFile, string speciesId = null)
    {
        speciesId ??= target != null ? target.SpeciesId : null;
        GameProgress.Caught caught = Progress.Add(type, photoFile);
        if (speciesId != null)
        {
            caught.speciesId = speciesId;
            Progress.AddSpecies(speciesId);
        }
        Progress.Save();
        string name = CreatureLibrary.NameOf(speciesId);
        hud?.Toast($"{name} 획득!  쿠옹력 {caught.cp}  ·  +{type.points} XP");
        hud?.ShowProgress(Progress, spawner.types);
        if (target != null)
            spawner.Remove(target);
        return caught;
    }

    void TapBuilding(Ray ray)
    {
        if (BuildingTapped == null)
            return;
        if (Physics.Raycast(ray, out RaycastHit hit, 5000f, ~0, QueryTriggerInteraction.Ignore))
        {
            var info = hit.collider.GetComponentInParent<CampusBuildingInfo>();
            if (info != null)
                BuildingTapped(info);
        }
    }

    void CreateRing()
    {
        var go = new GameObject("CollectRadiusRing");
        go.transform.SetParent(transform, false);
        ring = go.AddComponent<LineRenderer>();
        ring.loop = true;
        ring.useWorldSpace = true;
        ring.positionCount = RingSegments;
        ring.widthMultiplier = 1.5f;
        ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ring.receiveShadows = false;
        Shader s = Shader.Find("Sprites/Default");
        ring.material = new Material(s != null ? s : Shader.Find("Universal Render Pipeline/Unlit"));
        Color c = new Color(0.15f, 0.45f, 1f, 0.8f);
        ring.startColor = c;
        ring.endColor = c;
    }

    void UpdateRing(Vector3 center)
    {
        if (ring == null)
            return;
        for (int i = 0; i < RingSegments; i++)
        {
            float a = i * Mathf.PI * 2f / RingSegments;
            ring.SetPosition(i, new Vector3(center.x + Mathf.Cos(a) * collectRadius, 0.5f, center.z + Mathf.Sin(a) * collectRadius));
        }
    }
}
