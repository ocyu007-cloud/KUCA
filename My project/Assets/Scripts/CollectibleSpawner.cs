using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Player 주변의 캠퍼스 안, 건물이 없는 곳에 수집 대상을 뿌린다.
/// 멀어진 대상은 지우고, 개수가 모자라면 일정 간격으로 다시 채운다.
/// 초록·파랑은 그 등급 동물 중 무작위, 노랑은 랜드마크 범위(CreatureLibrary.GoldZones) 안에서 그 장소의 동물만 나온다.
/// </summary>
public class CollectibleSpawner : MonoBehaviour
{
    public CampusMap campusMap;
    [Tooltip("건물 루트. 이 아래 충돌체 위에는 스폰하지 않는다")]
    public Transform buildingsRoot;

    [Header("Spawn")]
    public int targetCount = 25;
    [Tooltip("Player로부터 이 거리(m) 안에 스폰")]
    public float spawnRadius = 300f;
    [Tooltip("Player와 너무 붙어서 나오지 않도록 하는 최소 거리 (m)")]
    public float minSpawnDistance = 40f;
    [Tooltip("이 거리(m)보다 멀어지면 지운다")]
    public float despawnRadius = 500f;
    [Tooltip("대상끼리 최소 간격 (m)")]
    public float minSpacing = 25f;
    [Tooltip("모자란 대상을 하나씩 채우는 간격 (초)")]
    public float refillInterval = 4f;

    public List<CollectibleType> types = new List<CollectibleType>
    {
        new CollectibleType { id = "sprout", points = 10, weight = 60f },   // 초록 동물
        new CollectibleType { id = "crystal", points = 30, weight = 30f },  // 파랑 동물
        new CollectibleType { id = "star", points = 100, weight = 10f },    // 황금 동물 (랜드마크 근처에서만)
    };

    public IReadOnlyList<Collectible> Active => active;

    public CollectibleType TypeOf(string id) => types.Find(t => t.id == id);

    readonly List<Collectible> active = new List<Collectible>();
    float refillTimer;
    bool initialFillDone;

    void Update()
    {
        if (campusMap == null || campusMap.player == null || !campusMap.HasLocation)
            return;

        Vector3 center = campusMap.player.position;

        for (int i = active.Count - 1; i >= 0; i--)
        {
            if (active[i] == null)
            {
                active.RemoveAt(i);
                continue;
            }
            if (HorizontalDistance(active[i].transform.position, center) > despawnRadius)
            {
                Destroy(active[i].gameObject);
                active.RemoveAt(i);
            }
        }

        // 처음 위치를 받으면 한 번에 채우고, 이후에는 천천히 하나씩 채운다.
        if (!initialFillDone)
        {
            for (int i = 0; i < targetCount * 3 && active.Count < targetCount; i++)
                TrySpawn(center);
            initialFillDone = true;
            return;
        }

        refillTimer -= Time.deltaTime;
        if (refillTimer <= 0f && active.Count < targetCount)
        {
            refillTimer = refillInterval;
            for (int i = 0; i < 10 && !TrySpawn(center); i++) { }
        }
    }

    public void Remove(Collectible c)
    {
        active.Remove(c);
        if (c != null)
            Destroy(c.gameObject);
    }

    bool TrySpawn(Vector3 center)
    {
        Vector2 offset = Random.insideUnitCircle.normalized * Random.Range(minSpawnDistance, spawnRadius);
        float px = center.x + offset.x, pz = center.z + offset.y;
        Vector3 pos = new Vector3(px, KeyArtTerrain.HeightAt(px, pz), pz);   // 키아트 지형 언덕 위

        if (!IsInsideMap(pos) || IsOnBuilding(pos))
            return false;
        foreach (var other in active)
            if (other != null && HorizontalDistance(other.transform.position, pos) < minSpacing)
                return false;

        CollectibleType type = PickType();
        string species = null;
        if (type.id == "star")
        {
            // 노랑(황금) 동물은 자기 랜드마크 범위 안에서만 나온다. 범위 밖이면 파랑으로 바꾼다
            CreatureLibrary.GoldZone zone = CreatureLibrary.GoldZoneAt(px, pz);
            if (zone != null)
                species = zone.species;
            else
                type = types.Find(t => t.id == "crystal") ?? type;
        }
        Collectible c = Collectible.Create(type, pos, transform, species);
        if (c == null)
            return false; // 동물 모델이 없으면 내보내지 않는다
        active.Add(c);
        return true;
    }

    bool IsInsideMap(Vector3 p)
    {
        float hw = campusMap.mapWidth * 0.5f - 5f;
        float hh = campusMap.mapHeight * 0.5f - 5f;
        return p.x > -hw && p.x < hw && p.z > -hh && p.z < hh;
    }

    bool IsOnBuilding(Vector3 p)
    {
        if (buildingsRoot == null)
            return false;
        // 위에서 아래로 쏴서 건물 충돌체에 먼저 맞으면 건물 위
        var ray = new Ray(new Vector3(p.x, 500f, p.z), Vector3.down);
        foreach (RaycastHit hit in Physics.RaycastAll(ray, 1000f, ~0, QueryTriggerInteraction.Ignore))
            if (hit.collider.transform.IsChildOf(buildingsRoot))
                return true;
        return false;
    }

    CollectibleType PickType()
    {
        float total = 0f;
        foreach (var t in types) total += Mathf.Max(0f, t.weight);
        float r = Random.value * total;
        foreach (var t in types)
        {
            r -= Mathf.Max(0f, t.weight);
            if (r <= 0f) return t;
        }
        return types[types.Count - 1];
    }

    public static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x, dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }
}
