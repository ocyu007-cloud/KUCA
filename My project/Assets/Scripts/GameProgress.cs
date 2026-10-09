using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// 점수, 잡은 경희몬(한 마리씩), 파트너, 도구, 활동 기록을 기기에 JSON으로 저장한다.
/// 경로: Application.persistentDataPath/progress.json
/// </summary>
[Serializable]
public class GameProgress
{
    [Serializable]
    public class Entry
    {
        public string id;
        public int count;
    }

    /// <summary>잡은 경희몬 한 마리</summary>
    [Serializable]
    public class Caught
    {
        public string uid;
        public string typeId;
        public int cp;
        public long caughtAt; // DateTime.Ticks (현지 시각)
        /// <summary>잡을 때 찍은 사진 파일 이름 (스크랩북 폴더 안, 없으면 빈 문자열)</summary>
        public string photo = "";
        /// <summary>동물 캐릭터 id (CreatureLibrary), 일반 경희몬이면 빈 문자열</summary>
        public string speciesId = "";

        public DateTime CaughtAt => new DateTime(caughtAt);
    }

    [Serializable]
    public class PartnerRecord
    {
        public string uid;
        public long since;
    }

    public int score;
    public int totalCollected;
    /// <summary>종류별 획득 수 (도감)</summary>
    public List<Entry> collected = new List<Entry>();
    /// <summary>동물별 획득 수 (도감)</summary>
    public List<Entry> species = new List<Entry>();
    /// <summary>지금 가지고 있는 경희몬</summary>
    public List<Caught> caught = new List<Caught>();
    public string partnerUid = "";
    public List<PartnerRecord> partnerHistory = new List<PartnerRecord>();
    /// <summary>가지고 있는 도구 (id, 개수)</summary>
    public List<Entry> items = new List<Entry>();
    /// <summary>레벨 보상을 이 레벨까지 받았다</summary>
    public int rewardedLevel;
    public float walkedMeters;
    public List<string> visitedSpots = new List<string>();

    static string FilePath => Path.Combine(Application.persistentDataPath, "progress.json");

    public int CountOf(string id)
    {
        Entry e = collected.Find(x => x.id == id);
        return e != null ? e.count : 0;
    }

    /// <summary>잡을 때 찍은 사진을 모아 두는 폴더</summary>
    public static string ScrapbookDir => Path.Combine(Application.persistentDataPath, "scrapbook");

    public Caught Add(CollectibleType type, string photo = "")
    {
        score += type.points;
        totalCollected++;
        Entry e = collected.Find(x => x.id == type.id);
        if (e == null)
            collected.Add(e = new Entry { id = type.id });
        e.count++;

        var c = new Caught
        {
            uid = Guid.NewGuid().ToString("N"),
            typeId = type.id,
            cp = RollCp(type, UnityEngine.Random.value, UnityEngine.Random.value),
            caughtAt = DateTime.Now.Ticks,
            photo = photo ?? "",
        };
        caught.Add(c);
        return c;
    }

    public void AddSpecies(string id)
    {
        if (species == null)
            species = new List<Entry>();
        Entry e = species.Find(x => x.id == id);
        if (e == null)
            species.Add(e = new Entry { id = id });
        e.count++;
    }

    public int SpeciesCountOf(string id)
    {
        Entry e = species?.Find(x => x.id == id);
        return e != null ? e.count : 0;
    }

    /// <summary>점수가 높은 종류일수록 CP 가 높다 (10점 → 약 90~210).</summary>
    public static int RollCp(CollectibleType type, float a, float b) =>
        Mathf.Max(10, Mathf.RoundToInt(type.points * (9f + a * 8f) + b * 40f));

    public Caught Find(string uid) => string.IsNullOrEmpty(uid) ? null : caught.Find(c => c.uid == uid);

    public Caught Partner => Find(partnerUid);

    public void SetPartner(string uid)
    {
        if (partnerUid == uid)
            return;
        partnerUid = uid;
        partnerHistory.Add(new PartnerRecord { uid = uid, since = DateTime.Now.Ticks });
    }

    public int ItemCount(string id)
    {
        Entry e = items.Find(x => x.id == id);
        return e != null ? e.count : 0;
    }

    public int TotalItems
    {
        get
        {
            int n = 0;
            foreach (var e in items)
                n += e.count;
            return n;
        }
    }

    public void AddItem(string id, int count)
    {
        Entry e = items.Find(x => x.id == id);
        if (e == null)
            items.Add(e = new Entry { id = id });
        e.count += count;
    }

    public void VisitSpot(string id, out bool isNew)
    {
        isNew = !string.IsNullOrEmpty(id) && !visitedSpots.Contains(id);
        if (isNew)
            visitedSpots.Add(id);
    }

    /// <summary>
    /// 예전 저장 파일 맞추기: 종류별 개수만 있으면 모자란 만큼 경희몬을 채워 넣고 (CP 는 순서로 정해지는 값이라 매번 같다),
    /// 동물 기록이 없는 경희몬에는 동물을 정해 준다.
    /// </summary>
    void Migrate()
    {
        caught ??= new List<Caught>();
        species ??= new List<Entry>();
        partnerHistory ??= new List<PartnerRecord>();
        items ??= new List<Entry>();
        visitedSpots ??= new List<string>();
        foreach (Entry e in collected)
        {
            int have = caught.FindAll(c => c.typeId == e.id).Count;
            for (int i = have; i < e.count; i++)
            {
                var rng = new System.Random(StableHash(e.id) * 31 + i);
                var type = new CollectibleType { id = e.id, points = PointsGuess(e.id) };
                caught.Add(new Caught
                {
                    uid = $"old_{e.id}_{i}",
                    typeId = e.id,
                    cp = RollCp(type, (float)rng.NextDouble(), (float)rng.NextDouble()),
                    caughtAt = DateTime.Now.AddMinutes(-(e.count - i) * 7).Ticks,
                });
            }
        }

        // 동물 피규어가 들어오기 전에 잡은 경희몬은 어떤 동물인지 기록이 없다.
        // 등급에 맞는 동물을 uid 로 하나 정해(늘 같은 결과) 도감에도 올린다.
        foreach (Caught c in caught)
        {
            if (!string.IsNullOrEmpty(c.speciesId))
                continue;
            c.speciesId = CreatureLibrary.StableSpecies(c.typeId, c.uid) ?? "";
            if (c.speciesId.Length > 0)
                AddSpecies(c.speciesId);
        }
    }

    static int StableHash(string s)
    {
        int h = 17;
        foreach (char ch in s)
            h = h * 31 + ch;
        return h;
    }

    static int PointsGuess(string id) => id == "star" ? 100 : id == "crystal" ? 30 : 10;

    public static GameProgress Load()
    {
        GameProgress p = null;
        try
        {
            if (File.Exists(FilePath))
                p = JsonUtility.FromJson<GameProgress>(File.ReadAllText(FilePath));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[GameProgress] 저장 데이터를 읽지 못했습니다: {e.Message}");
        }
        p ??= new GameProgress();
        p.Migrate();
        return p;
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(FilePath, JsonUtility.ToJson(this));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[GameProgress] 저장하지 못했습니다: {e.Message}");
        }
    }
}
