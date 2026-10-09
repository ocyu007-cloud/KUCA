using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;

/// <summary>
/// 수집 동물 3D 모델 (Resources/KUCA/CreatureMeshes.bytes, tools/keyart/build_creatures.py 가 만든다).
/// 형식은 KeyArtGeometry.bytes 와 같은 v3. 같은 위치·색 정점을 합쳐 부드러운 법선으로 만든다 (말랑한 토이 느낌).
/// 등급(수집 대상 종류 id)마다 나올 수 있는 동물 목록과 도감 이름도 여기서 관리한다.
/// </summary>
public static class CreatureLibrary
{
    /// <summary>수집 대상 종류 id → 그 등급에서 나오는 동물 id (노랑은 랜드마크마다 정해진다)</summary>
    public static readonly Dictionary<string, string[]> TierSpecies = new Dictionary<string, string[]>
    {
        { "sprout", new[] { "pigeon", "snail", "ant", "ladybug" } },
        { "crystal", new[] { "stray_cat", "squirrel", "crow", "raccoon_dog" } },
    };

    /// <summary>도감 이름</summary>
    public static readonly Dictionary<string, string> Names = new Dictionary<string, string>
    {
        { "pigeon", "비둘기" }, { "snail", "달팽이" }, { "ant", "개미" }, { "ladybug", "무당벌레" },
        { "stray_cat", "길고양이" }, { "squirrel", "청설모" }, { "crow", "까마귀" }, { "raccoon_dog", "너구리" },
        { "space_rabbit", "공학관 토끼" }, { "library_owl", "중앙도서관 올빼미" }, { "plaza_duck", "사색의 광장 오리" },
        { "gate_magpie", "정문 까치" }, { "lion_cub", "체육대학관 아기 사자" }, { "clock_rooster", "선승관 수탉" },
        { "art_chameleon", "예술·디자인대학 카멜레온" }, { "ceramics_mole", "도예관 두더지" },
        { "observatory_squirrel", "천문대 날다람쥐" }, { "amphitheater_frog", "평화노천극장 개구리" },
        { "stadium_jindo", "대운동장 진돗개" }, { "language_parrot", "외국어대학관 앵무새" },
        { "dorm_hamster", "우정원 햄스터" }, { "electronics_hedgehog", "전자정보대학관 고슴도치" },
        { "business_fox", "국제·경영대학관 여우" }, { "multimedia_meerkat", "멀티미디어·글로벌관 미어캣" },
    };

    /// <summary>등급 이름 (HUD)</summary>
    public static readonly Dictionary<string, string> TierLabels = new Dictionary<string, string>
    {
        { "sprout", "초록 동물" }, { "crystal", "파랑 동물" }, { "star", "황금 동물" },
    };

    /// <summary>노랑(황금) 동물이 사는 랜드마크: 월드 중심 (x, z), 건물 반 크기 (m). tools/keyart 로 계산한 값</summary>
    public class GoldZone
    {
        public string species;
        public Vector2 center;
        public float halfSize;
        public float radius;     // 실제 출현 반경 (서로 절대 겹치지 않게 줄인 값)
    }

    static readonly GoldZone[] goldZones =
    {
        Zone("space_rabbit", 49, 255, 80), Zone("library_owl", -40, -352, 56), Zone("plaza_duck", 119, -352, 26),
        Zone("gate_magpie", -138, 368, 30), Zone("lion_cub", 31, 40, 60), Zone("clock_rooster", 10, -125, 44),
        Zone("art_chameleon", 399, -249, 51), Zone("ceramics_mole", 112, -40, 16), Zone("observatory_squirrel", 170, -584, 16),
        Zone("amphitheater_frog", 276, -652, 48), Zone("stadium_jindo", -198, -20, 90), Zone("language_parrot", -205, 122, 63),
        Zone("dorm_hamster", -262, 222, 50), Zone("electronics_hedgehog", 306, -505, 52), Zone("business_fox", 112, -210, 31),
        Zone("multimedia_meerkat", -288, 47, 42),
    };
    static bool zonesReady;

    static GoldZone Zone(string id, float x, float z, float half) =>
        new GoldZone { species = id, center = new Vector2(x, z), halfSize = half };

    /// <summary>
    /// 출현 반경: 건물 둘레 45 m 까지. 두 랜드마크 범위가 겹치면 (사이 6 m 여유) 크기 비율대로 둘 다 줄인다 → 절대 겹치지 않음.
    /// </summary>
    public static IReadOnlyList<GoldZone> GoldZones
    {
        get
        {
            if (!zonesReady)
            {
                foreach (var z in goldZones)
                    z.radius = z.halfSize + 45f;
                for (int pass = 0; pass < 4; pass++)
                    for (int i = 0; i < goldZones.Length; i++)
                        for (int j = i + 1; j < goldZones.Length; j++)
                        {
                            GoldZone a = goldZones[i], b = goldZones[j];
                            float limit = Vector2.Distance(a.center, b.center) - 6f;
                            float sum = a.radius + b.radius;
                            if (sum > limit)
                            {
                                float k = Mathf.Max(0f, limit) / sum;
                                a.radius *= k;
                                b.radius *= k;
                            }
                        }
                zonesReady = true;
            }
            return goldZones;
        }
    }

    /// <summary>(x, z) 가 들어 있는 랜드마크 범위 (없으면 null)</summary>
    public static GoldZone GoldZoneAt(float x, float z)
    {
        var p = new Vector2(x, z);
        foreach (var zone in GoldZones)
            if (Vector2.Distance(zone.center, p) <= zone.radius)
                return zone;
        return null;
    }

    const float PosUnit = 0.0005f;   // build_creatures.py 의 creatures.POS_UNIT (0.5 mm)
    static Dictionary<string, Mesh> meshes;
    static Material material;

    public static Material Material
    {
        get
        {
            if (material == null)
                material = Resources.Load<Material>("KUCA/CreatureToy");   // 비닐 토이 광택 (빌드 포함되게 재질 에셋)
            if (material == null)
            {
                Shader s = Shader.Find("KUCA/VertexColorLit");
                if (s == null) s = Shader.Find("Universal Render Pipeline/Simple Lit");
                material = new Material(s) { name = "KUCA_Creature", enableInstancing = true };
            }
            return material;
        }
    }

    static Material glassMaterial;

    /// <summary>헬멧 유리 같은 투명 부품 재질 (KUCA/CreatureGlass)</summary>
    public static Material GlassMaterial
    {
        get
        {
            if (glassMaterial == null)
                glassMaterial = Resources.Load<Material>("KUCA/CreatureGlass");   // 빌드에 셰이더가 포함되게 재질 에셋으로
            if (glassMaterial == null)
            {
                Shader s = Shader.Find("KUCA/CreatureGlass");
                if (s != null) glassMaterial = new Material(s) { name = "KUCA_CreatureGlass" };
            }
            return glassMaterial;
        }
    }

    /// <summary>투명 부품 메시 (없으면 false). 이름 = Creature_<id>__glass</summary>
    public static bool TryGetGlass(string id, out Mesh mesh) => TryGetMesh(id + "__glass", out mesh);

    public static string NameOf(string id) => id != null && Names.TryGetValue(id, out string n) ? n : id;

    /// <summary>도감 순서의 등급 id (초록 → 파랑 → 황금)</summary>
    public static readonly string[] Tiers = { "sprout", "crystal", "star" };

    /// <summary>등급 색 (UI 강조)</summary>
    public static Color TierColor(string tierId) =>
        tierId == "star" ? new Color(0.96f, 0.76f, 0.25f) :
        tierId == "crystal" ? new Color(0.30f, 0.60f, 0.98f) : new Color(0.33f, 0.80f, 0.42f);

    public static string TierLabel(string tierId) => tierId != null && TierLabels.TryGetValue(tierId, out string l) ? l : tierId;

    /// <summary>그 등급에서 나오는 동물 (황금은 랜드마크마다 한 마리)</summary>
    public static IReadOnlyList<string> SpeciesOfTier(string tierId)
    {
        if (tierId == "star")
        {
            var list = new List<string>();
            foreach (var z in goldZones)
                list.Add(z.species);
            return list;
        }
        return TierSpecies.TryGetValue(tierId, out string[] arr) ? arr : (IReadOnlyList<string>)System.Array.Empty<string>();
    }

    /// <summary>도감 전체 (초록 → 파랑 → 황금 순). 도감 번호 = 이 순서 + 1</summary>
    public static IReadOnlyList<string> AllSpecies
    {
        get
        {
            if (allSpecies == null)
            {
                allSpecies = new List<string>();
                foreach (string t in Tiers)
                    allSpecies.AddRange(SpeciesOfTier(t));
            }
            return allSpecies;
        }
    }
    static List<string> allSpecies;

    /// <summary>동물이 속한 등급 id (모르면 null)</summary>
    public static string TierOf(string species)
    {
        foreach (string t in Tiers)
            foreach (string id in SpeciesOfTier(t))
                if (id == species)
                    return t;
        return null;
    }

    /// <summary>예전 저장(동물 기록 없음)을 위해, key 로 늘 같은 동물을 그 등급에서 고른다.</summary>
    public static string StableSpecies(string tierId, string key)
    {
        IReadOnlyList<string> list = SpeciesOfTier(tierId);
        if (list.Count == 0)
            return null;
        int h = 17;
        foreach (char ch in key ?? "")
            h = h * 31 + ch;
        return list[(h & int.MaxValue) % list.Count];
    }

    /// <summary>등급에서 동물 하나를 고른다. 모델이 없으면 null</summary>
    public static string PickSpecies(string tierId)
    {
        if (!TierSpecies.TryGetValue(tierId, out string[] list) || list.Length == 0)
            return null;
        string id = list[Random.Range(0, list.Length)];
        return TryGetMesh(id, out _) ? id : null;
    }

    static Dictionary<string, Mesh> farMeshes;

    /// <summary>가까이에서 보일 메시 (LOD0)</summary>
    public static bool TryGetMesh(string id, out Mesh mesh)
    {
        if (meshes == null)
            meshes = Load("KUCA/CreatureMeshes");
        mesh = null;
        return id != null && meshes.TryGetValue(id, out mesh);
    }

    /// <summary>멀리서 보일 가벼운 메시 (LOD1). 파일이 없으면 false (가까운 메시만 쓴다)</summary>
    public static bool TryGetFarMesh(string id, out Mesh mesh)
    {
        if (farMeshes == null)
            farMeshes = Load("KUCA/CreatureMeshesFar", quiet: true);
        mesh = null;
        return id != null && farMeshes.TryGetValue(id, out mesh);
    }

    static Dictionary<string, Mesh> Load(string path, bool quiet = false)
    {
        var result = new Dictionary<string, Mesh>();
        var asset = Resources.Load<TextAsset>(path);
        if (asset == null)
        {
            if (!quiet)
                Debug.LogWarning($"[CreatureLibrary] Resources/{path}.bytes 가 없습니다 (tools/keyart/build_creatures.py).");
            return result;
        }
        byte[] data = asset.bytes;
        if (data.Length < 8 || data[0] != 'K' || data[1] != 'U' || data[2] != 'C' || data[3] != 'A' || System.BitConverter.ToInt32(data, 4) != 3)
        {
            Debug.LogError($"[CreatureLibrary] {path}.bytes 형식이 맞지 않습니다 (v3 필요).");
            return result;
        }
        using (var gz = new GZipStream(new MemoryStream(data, 8, data.Length - 8), CompressionMode.Decompress))
        using (var br = new BinaryReader(gz))
        {
            int count = br.ReadInt32();
            for (int k = 0; k < count; k++)
            {
                string name = System.Text.Encoding.UTF8.GetString(br.ReadBytes(br.ReadInt32()));
                var origin = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                int vc = br.ReadInt32();
                int flags = br.ReadInt32();
                byte[] pos = br.ReadBytes(vc * 6);
                byte[] col = br.ReadBytes(vc * 4);
                if ((flags & 1) != 0)
                    br.ReadBytes(vc * 4);
                byte[] nrm = (flags & 2) != 0 ? br.ReadBytes(vc * 3) : null;   // 피규어: 조형 표면의 정확한 법선
                result[name.Replace("Creature_", "")] = BuildMesh(name, origin, vc, pos, col, nrm);
            }
        }
        return result;
    }

    /// <summary>같은 (위치, 색, 법선) 정점을 하나로 합쳐 인덱스 메시를 만든다. 법선이 없으면 다시 계산 (부드러운 법선)</summary>
    static Mesh BuildMesh(string name, Vector3 origin, int vc, byte[] pos, byte[] col, byte[] nrm)
    {
        var map = new Dictionary<(short, short, short, int, int), int>(vc / 3);
        var verts = new List<Vector3>(vc / 3);
        var cols = new List<Color32>(vc / 3);
        var norms = nrm != null ? new List<Vector3>(vc / 3) : null;
        var tris = new int[vc];
        for (int i = 0; i < vc; i++)
        {
            short x = System.BitConverter.ToInt16(pos, i * 6);
            short y = System.BitConverter.ToInt16(pos, i * 6 + 2);
            short z = System.BitConverter.ToInt16(pos, i * 6 + 4);
            int c = col[i * 4] | (col[i * 4 + 1] << 8) | (col[i * 4 + 2] << 16) | (col[i * 4 + 3] << 24);
            int n = nrm != null ? nrm[i * 3] | (nrm[i * 3 + 1] << 8) | (nrm[i * 3 + 2] << 16) : 0;
            var key = (x, y, z, c, n);
            if (!map.TryGetValue(key, out int idx))
            {
                idx = verts.Count;
                map[key] = idx;
                verts.Add(origin + new Vector3(x, y, z) * PosUnit);
                cols.Add(new Color32(col[i * 4], col[i * 4 + 1], col[i * 4 + 2], col[i * 4 + 3]));
                if (norms != null)
                    norms.Add(new Vector3((sbyte)nrm[i * 3], (sbyte)nrm[i * 3 + 1], (sbyte)nrm[i * 3 + 2]) / 127f);
            }
            tris[i] = idx;
        }
        var mesh = new Mesh { name = name };
        if (verts.Count > 65000)
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(verts);
        mesh.SetColors(cols);
        mesh.SetTriangles(tris, 0);
        if (norms != null) mesh.SetNormals(norms);
        else mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mesh.UploadMeshData(true);
        return mesh;
    }
}
