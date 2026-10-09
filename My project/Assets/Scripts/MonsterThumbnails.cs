using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 동물 피규어(CreatureLibrary)마다 3D 모습을 한 번 렌더해서 도감·목록·HUD 에 쓰는 그림으로 만든다.
/// 화면 밖 먼 곳에, 비어 있는 레이어 하나만 보는 임시 카메라로 찍는다. 피규어 크기에 맞춰 화면을 채운다.
/// </summary>
public static class MonsterThumbnails
{
    static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();
    const int Size = 256;
    static readonly Vector3 StagePos = new Vector3(0f, -20000f, 0f);

    /// <summary>동물 speciesId 의 그림 (모델이 없으면 null)</summary>
    public static Texture2D Get(string speciesId)
    {
        if (string.IsNullOrEmpty(speciesId))
            return null;
        if (cache.TryGetValue(speciesId, out Texture2D tex) && tex != null)
            return tex;
        if (!CreatureLibrary.TryGetMesh(speciesId, out Mesh mesh))
            return null;
        return cache[speciesId] = Render(speciesId, mesh);
    }

    /// <summary>쓰이지 않는 레이어 (없으면 31)</summary>
    public static int StageLayer
    {
        get
        {
            for (int l = 31; l >= 8; l--)
                if (string.IsNullOrEmpty(LayerMask.LayerToName(l)))
                    return l;
            return 31;
        }
    }

    static Texture2D Render(string speciesId, Mesh mesh)
    {
        int layer = StageLayer;
        var root = new GameObject("ThumbStage");
        root.transform.position = StagePos;

        // 피규어를 가운데에 두고, 카메라 쪽으로 살짝 비스듬히 돌린다.
        var pivot = new GameObject("Pivot").transform;
        pivot.SetParent(root.transform, false);
        pivot.localRotation = Quaternion.Euler(0f, 200f, 0f);
        Bounds b = mesh.bounds;
        var body = new GameObject("Figure", typeof(MeshFilter), typeof(MeshRenderer));
        body.layer = layer;
        body.transform.SetParent(pivot, false);
        body.transform.localPosition = -b.center;
        body.GetComponent<MeshFilter>().sharedMesh = mesh;
        body.GetComponent<MeshRenderer>().sharedMaterial = CreatureLibrary.Material;
        if (CreatureLibrary.TryGetGlass(speciesId, out Mesh glass) && CreatureLibrary.GlassMaterial != null)
        {
            var g = new GameObject("Glass", typeof(MeshFilter), typeof(MeshRenderer));
            g.layer = layer;
            g.transform.SetParent(body.transform, false);
            g.GetComponent<MeshFilter>().sharedMesh = glass;
            g.GetComponent<MeshRenderer>().sharedMaterial = CreatureLibrary.GlassMaterial;
        }

        var lightGo = new GameObject("ThumbLight", typeof(Light));
        lightGo.transform.SetParent(root.transform, false);
        lightGo.transform.rotation = Quaternion.Euler(35f, -30f, 0f);
        var light = lightGo.GetComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.25f;
        light.cullingMask = 1 << layer;

        // 피규어 전체(받침 포함)가 화면의 약 85% 를 채우는 거리
        const float fov = 26f;
        float radius = b.extents.magnitude;
        float dist = radius / Mathf.Sin(fov * 0.5f * Mathf.Deg2Rad) * 0.9f;
        var camGo = new GameObject("ThumbCamera", typeof(Camera));
        camGo.transform.SetParent(root.transform, false);
        camGo.transform.localPosition = new Vector3(0f, radius * 0.25f, -dist);
        camGo.transform.LookAt(root.transform.position);
        var cam = camGo.GetComponent<Camera>();
        cam.enabled = false;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
        cam.cullingMask = 1 << layer;
        cam.fieldOfView = fov;
        cam.nearClipPlane = Mathf.Max(0.01f, dist - radius * 2f);
        cam.farClipPlane = dist + radius * 2f;

        var rt = RenderTexture.GetTemporary(Size, Size, 24, RenderTextureFormat.ARGB32);
        rt.antiAliasing = 1;
        cam.targetTexture = rt;
        cam.Render();

        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = "Thumb_" + speciesId };
        tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
        tex.Apply(false, false);
        RenderTexture.active = prev;

        cam.targetTexture = null;
        RenderTexture.ReleaseTemporary(rt);
        Object.DestroyImmediate(root); // 같은 프레임에 다음 썸네일을 찍으므로 바로 지운다
        return tex;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => cache.Clear();
}
