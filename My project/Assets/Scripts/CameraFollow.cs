using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
#endif

/// <summary>
/// Player를 비스듬히 위에서 내려다보며 따라가는 카메라. 항상 Player(축)를 화면 중심에 두고 그 둘레를 돈다.
/// 폰: 한 손가락 드래그 = Player 를 축으로 회전 (Player 둘레로 원을 그리면 지도가 손가락을 따라 돈다),
///     두 손가락 = 핀치 줌 + 비틀어 회전, 손을 떼면 잠깐 관성으로 돌다 멈춘다, 두 번 탭 = 북쪽 위로 되돌리기
/// 에디터: 오른쪽 드래그 = 회전, 휠 = 줌, R = 북쪽 위로 되돌리기
/// </summary>
public class CameraFollow : MonoBehaviour
{
    public Transform target;

    [Tooltip("타겟으로부터의 거리 (m)")]
    public float distance = 250f;
    public float minDistance = 60f;
    public float maxDistance = 700f;
    [Tooltip("내려다보는 각도 (90 = 바로 위)")]
    [Range(10f, 90f)]
    public float pitch = 55f;
    [Tooltip("카메라가 바라보는 방위 (0 = 북쪽이 화면 위). 화면에 보이는 값 (부드럽게 따라감)")]
    public float yaw = 0f;
    [Tooltip("축(Player)을 따라가는 속도. 0이면 즉시 따라감")]
    public float followSpeed = 6f;

    [Header("Controls")]
    [Tooltip("화면 너비만큼 드래그했을 때 회전 각도 (Player 바로 근처를 문지를 때, 마우스)")]
    public float rotateDegreesPerScreen = 270f;
    [Tooltip("손가락이 Player 에서 이 거리(화면 높이 비율)보다 멀면 Player 둘레를 도는 각도만큼 돌린다")]
    public float dialMinRadius = 0.08f;
    [Tooltip("회전이 손가락을 따라가는 시간 (초). 작을수록 즉각적")]
    public float rotateSmoothTime = 0.06f;
    [Tooltip("줌이 따라가는 시간 (초)")]
    public float zoomSmoothTime = 0.08f;
    [Tooltip("손을 뗀 뒤 관성 회전이 줄어드는 빠르기 (클수록 빨리 멈춤)")]
    public float inertiaDamping = 5f;
    public float mouseZoomStep = 0.1f;
    [Tooltip("두 번 탭으로 인정하는 간격 (초)")]
    public float doubleTapTime = 0.3f;

    float targetYaw;
    float yawVelocity;
    float spin;              // 손을 뗀 뒤 남은 회전 속도 (도/초)
    float dragSpin;          // 드래그 중 회전 속도 (관성의 시작값)
    float targetDistance;
    float distanceVelocity;
    Vector3 pivot;
    bool hasPivot;
    bool dragging;
    float lastTapTime = -1f;
    float resetUntil = -1f;  // 북쪽으로 되돌리는 동안은 천천히 돈다
    Camera cam;

#if ENABLE_INPUT_SYSTEM
    void OnEnable() => EnhancedTouchSupport.Enable();
    void OnDisable() => EnhancedTouchSupport.Disable();
#endif

    void Awake()
    {
        cam = GetComponent<Camera>();
        targetYaw = yaw;
        targetDistance = distance;
    }

    void LateUpdate()
    {
        if (target == null)
            return;

        dragging = false;
        HandleInput();

        float dt = Mathf.Max(Time.unscaledDeltaTime, 1e-4f);
        if (!dragging && Mathf.Abs(spin) > 0.5f)
        {
            // 손을 뗀 뒤 관성으로 조금 더 돌다가 멈춘다.
            targetYaw += spin * dt;
            spin *= Mathf.Exp(-inertiaDamping * dt);
        }
        float smooth = Time.unscaledTime < resetUntil ? 0.22f : rotateSmoothTime;
        yaw = Mathf.SmoothDampAngle(yaw, targetYaw, ref yawVelocity, smooth, Mathf.Infinity, dt);
        distance = Mathf.SmoothDamp(distance, targetDistance, ref distanceVelocity, zoomSmoothTime, Mathf.Infinity, dt);

        // 축(Player 위치)만 부드럽게 따라가고, 카메라 위치·방향은 그 축에서 함께 계산한다.
        // 그래야 돌리는 동안에도 늘 Player 를 중심에 둔 채 그 둘레를 돈다.
        Vector3 goal = target.position;
        if (!hasPivot || followSpeed <= 0f)
        {
            pivot = goal;
            hasPivot = true;
        }
        else
            pivot = Vector3.Lerp(pivot, goal, 1f - Mathf.Exp(-followSpeed * Time.deltaTime));

        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        transform.SetPositionAndRotation(pivot - rot * Vector3.forward * distance, rot);
    }

    /// <summary>카메라를 북쪽이 위로 오게 부드럽게 되돌린다.</summary>
    public void ResetNorthUp()
    {
        spin = 0f;
        // 가장 가까운 쪽으로 돌아 0도(북쪽 위)에 멈춘다.
        targetYaw = yaw + Mathf.DeltaAngle(yaw, 0f);
        yawVelocity = 0f;
        resetUntil = Time.unscaledTime + 0.8f;
    }

    void AddYaw(float degrees)
    {
        resetUntil = -1f;
        targetYaw += degrees;
        // 드래그 중 회전 속도를 부드럽게 기록해 두었다가 손을 떼면 관성으로 쓴다.
        float dt = Mathf.Max(Time.unscaledDeltaTime, 1e-4f);
        dragSpin = Mathf.Lerp(dragSpin, degrees / dt, 0.5f);
    }

    /// <summary>Player 화면 위치를 축으로, 손가락이 그 둘레를 돈 각도(도). 축에 너무 가까우면 좌우 이동량으로 대신한다.</summary>
    float DialDelta(Vector2 now, Vector2 delta)
    {
        Vector2 center = cam != null ? (Vector2)cam.WorldToScreenPoint(pivot) : new Vector2(Screen.width, Screen.height) * 0.5f;
        Vector2 before = now - delta;
        float minR = dialMinRadius * Screen.height;
        Vector2 a = before - center, b = now - center;
        if (a.magnitude < minR || b.magnitude < minR)
            return delta.x / Mathf.Max(1, Screen.width) * rotateDegreesPerScreen;
        // 화면에서 반시계로 돌리면 지도도 반시계로 돈다 (카메라는 시계 방향 = yaw 증가).
        return Vector2.SignedAngle(a, b);
    }

    void Zoom(float factor)
    {
        targetDistance = Mathf.Clamp(targetDistance * factor, minDistance, maxDistance);
    }

    void HandleInput()
    {
        // 모달 화면(꾸미기, 메모판)이 열려 있으면 지도 카메라를 움직이지 않는다 (목록 스크롤이 줌이 되지 않게).
        if (UIInputBlocker.IsModalOpen)
        {
            spin = 0f;
            return;
        }
#if ENABLE_INPUT_SYSTEM
        var touches = Touch.activeTouches;
        if (touches.Count == 1)
        {
            Touch t = touches[0];
            // UI(패널, 버튼) 위에서 시작한 터치는 카메라 조작에 쓰지 않는다.
            if (!UIInputBlocker.AllowsGameInput(t.startScreenPosition))
                return;
            var phase = t.phase;
            if (phase == UnityEngine.InputSystem.TouchPhase.Began)
            {
                spin = dragSpin = 0f; // 돌던 화면을 손가락으로 잡으면 멈춘다
                if (Time.unscaledTime - lastTapTime < doubleTapTime)
                {
                    ResetNorthUp();
                    lastTapTime = -1f;
                }
                else
                    lastTapTime = Time.unscaledTime;
            }
            else if (phase == UnityEngine.InputSystem.TouchPhase.Moved)
            {
                dragging = true;
                AddYaw(DialDelta(t.screenPosition, t.delta));
            }
            else if (phase == UnityEngine.InputSystem.TouchPhase.Stationary)
            {
                dragging = true;
                dragSpin = Mathf.Lerp(dragSpin, 0f, 0.3f);
            }
            else if (phase == UnityEngine.InputSystem.TouchPhase.Ended)
                spin = dragSpin;
        }
        else if (touches.Count >= 2)
        {
            Touch a = touches[0], b = touches[1];
            Vector2 aPrev = a.screenPosition - a.delta, bPrev = b.screenPosition - b.delta;
            float now = Vector2.Distance(a.screenPosition, b.screenPosition);
            float before = Vector2.Distance(aPrev, bPrev);
            if (now > 1f && before > 1f)
                Zoom(before / now);
            // 두 손가락을 비틀면 그만큼 돌린다.
            float twist = Vector2.SignedAngle(bPrev - aPrev, b.screenPosition - a.screenPosition);
            if (Mathf.Abs(twist) < 45f)
                AddYaw(twist);
            dragging = true;
            spin = 0f;
            lastTapTime = -1f;
        }

        Mouse mouse = Mouse.current;
        if (mouse != null && touches.Count == 0)
        {
            if (mouse.rightButton.isPressed)
            {
                dragging = true;
                if (mouse.rightButton.wasPressedThisFrame)
                    spin = dragSpin = 0f;
                AddYaw(mouse.delta.ReadValue().x / Mathf.Max(1, Screen.width) * rotateDegreesPerScreen);
            }
            else if (mouse.rightButton.wasReleasedThisFrame)
                spin = dragSpin;
            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
                Zoom(scroll > 0f ? 1f - mouseZoomStep : 1f + mouseZoomStep);
        }

        Keyboard kb = Keyboard.current;
        if (kb != null && kb.rKey.wasPressedThisFrame)
            ResetNorthUp();
#endif
    }
}
