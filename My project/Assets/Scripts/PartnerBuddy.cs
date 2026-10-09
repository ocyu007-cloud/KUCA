using UnityEngine;

/// <summary>
/// 파트너 동물 피규어(CreatureLibrary). 지도에서는 캐릭터 옆에서 통통 뛰며 따라다니고,
/// 프로필 화면에서는 캐릭터 옆 바닥에 선다 (캐릭터 레이어라 프로필 카메라에 함께 찍힌다).
/// </summary>
public class PartnerBuddy : MonoBehaviour
{
    public Transform character;
    [Tooltip("피규어 키 (받침 포함, 캐릭터 로컬 단위. 캐릭터 키는 약 1.7)")]
    public float height = 0.75f;
    public float followSpeed = 4f;

    /// <summary>true 면 프로필 화면 자리에 선다</summary>
    public bool profilePose;

    string speciesId;
    Transform visual;    // 발밑이 피벗인 피규어
    float phase;

    public void Show(string species)
    {
        if (string.IsNullOrEmpty(species) || !CreatureLibrary.TryGetMesh(species, out Mesh mesh))
        {
            Hide();
            return;
        }
        if (visual != null && speciesId == species)
            return;
        Hide();
        speciesId = species;

        int layer = character != null ? character.gameObject.layer : 0;
        var pivot = new GameObject("Partner_" + species).transform;
        pivot.SetParent(transform, false);
        Bounds b = mesh.bounds;
        var body = new GameObject("Figure", typeof(MeshFilter), typeof(MeshRenderer));
        body.layer = layer;
        body.transform.SetParent(pivot, false);
        // 피벗을 발밑(받침 바닥 가운데)으로 옮긴다.
        body.transform.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z);
        body.GetComponent<MeshFilter>().sharedMesh = mesh;
        body.GetComponent<MeshRenderer>().sharedMaterial = CreatureLibrary.Material;
        if (CreatureLibrary.TryGetGlass(species, out Mesh glass) && CreatureLibrary.GlassMaterial != null)
        {
            var g = new GameObject("Glass", typeof(MeshFilter), typeof(MeshRenderer));
            g.layer = layer;
            g.transform.SetParent(body.transform, false);
            g.GetComponent<MeshFilter>().sharedMesh = glass;
            g.GetComponent<MeshRenderer>().sharedMaterial = CreatureLibrary.GlassMaterial;
            g.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        visual = pivot;
        visual.localScale = Vector3.one * ScaleFor(mesh);
        SnapToTarget();
    }

    public void Hide()
    {
        if (visual != null)
            Destroy(visual.gameObject);
        visual = null;
        speciesId = null;
    }

    float ScaleFor(Mesh mesh)
    {
        float charScale = character != null ? character.lossyScale.y : 1f;
        return height * charScale / Mathf.Max(mesh.bounds.size.y, 1e-4f);
    }

    void LateUpdate()
    {
        if (visual == null || character == null)
            return;
        // 꾸미기 미리보기에는 나오지 않게 한다.
        bool show = !CharacterCustomizer.IsOpen;
        if (visual.gameObject.activeSelf != show)
            visual.gameObject.SetActive(show);

        phase += Time.deltaTime;
        Vector3 toChar = character.position - visual.position;
        toChar.y = 0f;
        if (profilePose)
        {
            // 프로필: 캐릭터 옆 바닥에 서서 카메라(캐릭터 정면) 쪽을 본다.
            visual.position = TargetPosition();
            visual.rotation = character.rotation * Quaternion.Euler(0f, -18f + Mathf.Sin(phase * 1.2f) * 6f, 0f);
            return;
        }
        // 지도: 캐릭터 뒤쪽 옆을 따라다니며 통통 뛰고, 캐릭터가 가는 쪽을 본다.
        visual.position = Vector3.Lerp(visual.position, TargetPosition(), 1f - Mathf.Exp(-followSpeed * Time.deltaTime));
        if (toChar.sqrMagnitude > 1f)
            visual.rotation = Quaternion.Slerp(visual.rotation, Quaternion.LookRotation(character.forward), 1f - Mathf.Exp(-6f * Time.deltaTime));
    }

    Vector3 TargetPosition()
    {
        if (profilePose)
            return character.TransformPoint(-0.8f, 0f, -0.2f);
        float hop = Mathf.Abs(Mathf.Sin(phase * 3.2f)) * 0.12f;
        return character.TransformPoint(-0.85f, hop, -0.45f);
    }

    void SnapToTarget()
    {
        if (visual != null && character != null)
            visual.position = TargetPosition();
    }
}
