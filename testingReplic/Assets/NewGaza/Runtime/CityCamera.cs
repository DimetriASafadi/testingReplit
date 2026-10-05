using System.Collections.Generic;
using NewGaza.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace NewGaza
{
    [RequireComponent(typeof(Camera))]
    public sealed class CityCamera : MonoBehaviour
    {
        private GameSession session;
        private CityWorld world;
        private Camera view;
        private Vector3 focus;
        private Vector3 targetFocus;
        // North-up by default; rotating the view never changes the geographic layout.
        private float yaw;
        private float zoom = 24;
        private float targetZoom = 24;
        private const float Pitch = 53;
        private const float WheelZoomSensitivity = 0.0045f;
        private const float PinchZoomExponent = 1.8f;
        private const float ZoomResponse = 20f;
        private Vector2 start;
        private Vector2 previous;
        private float pressTime;
        private bool held;
        private bool moved;
        private bool beganOnUI;
        private bool inspected;
        private bool multiTouch;
        private float lastSpan;
        private float lastAngle;
        private CitySelectable previousSelection;
        private float lastTapTime;
        private float finaleUntil;
        private float saveViewAt = -1;
        private readonly List<RaycastResult> uiHits = new List<RaycastResult>();
        public static bool ModalOpen { get; set; }
        // Audio follows the rendered, eased view, never the pinch target ahead of the image.
        public Vector3 AudioFocus { get { return focus; } }
        public float AudioZoom { get { return zoom; } }

        public void Initialize(GameSession game, CityWorld city)
        {
            session = game;
            world = city;
            view = GetComponent<Camera>();
            view.orthographic = true;
            focus = targetFocus = world.DistrictPosition(session.State.selectedDistrict);
            zoom = targetZoom = world.DistrictViewingSize(session.State.selectedDistrict);
            var saved = session.State.camera;
            if (saved != null)
            {
                focus = targetFocus = new Vector3(
                    Mathf.Clamp(saved.x, world.MapMinX, world.MapMaxX), 0,
                    Mathf.Clamp(saved.z, world.MapMinZ, world.MapMaxZ));
                yaw = saved.yaw;
                zoom = targetZoom = Mathf.Clamp(saved.zoom, .6f, Mathf.Max(125, OverviewZoom()));
            }
            UpdatePose(true);
        }

        internal void CaptureSave()
        {
            // Save the user's intended view, not an unfinished easing interpolation.
            session.State.camera = new CameraSaveState {
                x = targetFocus.x, z = targetFocus.z, zoom = targetZoom,
                yaw = Mathf.Repeat(yaw, 360)
            };
        }

        public void Focus(Vector3 point)
        {
            targetFocus = point;
            targetZoom = world.DistrictViewingSize(session.State.selectedDistrict);
            finaleUntil = 0;
        }
        public void FrameCity()
        {
            targetFocus = CityCentre(); targetZoom = OverviewZoom(); finaleUntil = 0;
            saveViewAt = Time.unscaledTime + .25f;
        }
        public void FocusWorkSite(Vector3 point, float width, float depth)
        {
            targetFocus = point;
            targetZoom = ActiveWorkPresentation.ViewingSize(width, depth, view.aspect);
            finaleUntil = 0;
            saveViewAt = Time.unscaledTime + .25f;
        }
        public void FocusDistrictView(Vector3 point, int district)
        {
            targetFocus = point; targetZoom = world.DistrictViewingSize(district);
            finaleUntil = 0; saveViewAt = Time.unscaledTime + .25f;
        }
        public void PlayFinale()
        {
            if (!session.Economy.CityComplete)
            {
                session.Notify("أكمل الأحياء وشارع الرشيد لعرض المشهد الختامي.");
                return;
            }
            targetFocus = CityCentre();
            targetZoom = OverviewZoom();
            finaleUntil = Time.unscaledTime + 18;
            world.PlayFinale();
        }

        private Vector3 CityCentre()
        {
            return new Vector3((world.MapMinX + world.MapMaxX) * 0.5f, 0,
                (world.MapMinZ + world.MapMaxZ) * 0.5f);
        }

        private float OverviewZoom()
        {
            Vector3 centre = CityCentre();
            var rotation = Quaternion.Euler(Pitch, yaw, 0);
            Vector3 right = rotation * Vector3.right;
            Vector3 up = rotation * Vector3.up;
            float width = 0, height = 0;
            for (int i = 0; i < 4; i++)
            {
                var corner = new Vector3(i % 2 == 0 ? world.MapMinX : world.MapMaxX,
                    0, i < 2 ? world.MapMinZ : world.MapMaxZ);
                var offset = corner - centre;
                width = Mathf.Max(width, Mathf.Abs(Vector3.Dot(offset, right)) + 20);
                height = Mathf.Max(height, Mathf.Abs(Vector3.Dot(offset, up)) + 20);
            }
            return Mathf.Max(height / 0.72f, width / Mathf.Max(0.25f, view.aspect * 0.9f));
        }

        private void Update()
        {
            if (session == null || !session.Ready) return;
            var beforeFocus = targetFocus;
            float beforeZoom = targetZoom, beforeYaw = yaw;
            if (finaleUntil > Time.unscaledTime)
            {
                yaw += Time.unscaledDeltaTime * 5;
                targetZoom = OverviewZoom();
            }
            else if (!ModalOpen) ReadInput();
            else { held = false; multiTouch = false; }
            UpdatePose(false);
            if (finaleUntil <= Time.unscaledTime &&
                (beforeFocus != targetFocus || beforeZoom != targetZoom || beforeYaw != yaw))
                saveViewAt = Time.unscaledTime + .25f;
            if (saveViewAt >= 0 && Time.unscaledTime >= saveViewAt)
            {
                saveViewAt = -1;
                session.Save();
            }
        }

        private void ReadInput()
        {
            if (!Application.isFocused) { CancelGesture(); return; }
            var touchscreen = Touchscreen.current;
            if (touchscreen != null)
            {
                int count = 0;
                Vector2 first = default, second = default;
                foreach (var touch in touchscreen.touches)
                {
                    if (!touch.press.isPressed) continue;
                    var touchPosition = touch.position.ReadValue();
                    if (!CanProjectPointer(touchPosition)) { CancelGesture(); return; }
                    if (count == 0) first = touchPosition;
                    else if (count == 1) second = touchPosition;
                    count++;
                }
                if (count >= 2)
                {
                    float span = Vector2.Distance(first, second);
                    float angle = Mathf.Atan2(second.y - first.y, second.x - first.x) * Mathf.Rad2Deg;
                    if (!multiTouch)
                    {
                        beganOnUI = IsUI(first) || IsUI(second) || (held && beganOnUI);
                        multiTouch = true;
                    }
                    else if (!beganOnUI && !IsUI(first) && !IsUI(second))
                    {
                        if (span > 20 && lastSpan > 20)
                            targetZoom = Mathf.Clamp(targetZoom * Mathf.Pow(lastSpan / span, PinchZoomExponent), 0.6f, Mathf.Max(125, OverviewZoom()));
                        yaw = Mathf.Repeat(yaw - Mathf.DeltaAngle(lastAngle, angle), 360);
                    }
                    lastSpan = span;
                    lastAngle = angle;
                    held = false;
                    moved = true;
                    return;
                }
                if (multiTouch)
                {
                    // Require all fingers lifted after pinch: never turn its tail into a tap.
                    if (count == 0) multiTouch = false;
                    return;
                }
                if (count == 1) { Pointer(first, true); return; }
                if (held) { Pointer(previous, false); return; }
            }
            var mouse = Mouse.current;
            if (mouse == null) { CancelGesture(); return; }
            var position = mouse.position.ReadValue();
            if (!CanProjectPointer(position)) { CancelGesture(); return; }
            float scroll = mouse.scroll.ReadValue().y;
            if (ScreenInputValidation.Finite(scroll) && !IsUI(position) && Mathf.Abs(scroll) > 0)
                targetZoom = Mathf.Clamp(targetZoom * Mathf.Exp(-scroll * WheelZoomSensitivity), 0.6f, Mathf.Max(125, OverviewZoom()));
            if (mouse.rightButton.isPressed && !IsUI(position))
            {
                float delta = mouse.delta.ReadValue().x;
                if (ScreenInputValidation.Finite(delta))
                    yaw = Mathf.Repeat(yaw + delta * 0.25f, 360);
            }
            Pointer(position, mouse.leftButton.isPressed);
        }

        private float DragThreshold => Mathf.Max(12, Screen.height * 0.012f);

        private void Pointer(Vector2 position, bool down)
        {
            if (!CanProjectPointer(position)) { CancelGesture(); return; }
            if (down && !held)
            {
                held = true; start = previous = position;
                pressTime = Time.unscaledTime;
                moved = inspected = false;
                beganOnUI = IsUI(position);
                return;
            }
            if (down && held)
            {
                if (beganOnUI) { previous = position; return; }
                if (Vector2.Distance(position, start) > DragThreshold) moved = true;
                if (moved)
                {
                    var before = GroundPoint(previous);
                    var after = GroundPoint(position);
                    targetFocus += before - after;
                    targetFocus.x = Mathf.Clamp(targetFocus.x, world.MapMinX, world.MapMaxX);
                    targetFocus.z = Mathf.Clamp(targetFocus.z, world.MapMinZ, world.MapMaxZ);
                }
                else if (!inspected && Time.unscaledTime - pressTime > 0.55f)
                {
                    inspected = true;
                    Tap(position, true);
                }
                previous = position;
                return;
            }
            if (!down && held)
            {
                held = false;
                if (!moved && !inspected && !beganOnUI && !IsUI(position)) Tap(position, false);
            }
        }

        private void CancelGesture()
        {
            held = multiTouch = moved = inspected = beganOnUI = false;
        }

        internal bool CanProjectPointer(Vector2 position)
        {
            if (!Application.isFocused || view == null || !view.isActiveAndEnabled ||
                !ScreenInputValidation.Finite(view.orthographicSize) || view.orthographicSize <= 0) return false;
            var rect = view.pixelRect;
            return ScreenInputValidation.InViewport(position.x, position.y, rect.x, rect.y, rect.width, rect.height);
        }

        internal bool TryGroundPoint(Vector2 screen, out Vector3 point)
        {
            point = targetFocus;
            if (!CanProjectPointer(screen)) return false;
            var ray = view.ScreenPointToRay(screen);
            var plane = new Plane(Vector3.up, Vector3.zero);
            if (!plane.Raycast(ray, out float d) || !ScreenInputValidation.Finite(d)) return false;
            var ground = ray.GetPoint(d);
            if (!ScreenInputValidation.Finite(ground.x) || !ScreenInputValidation.Finite(ground.y) ||
                !ScreenInputValidation.Finite(ground.z)) return false;
            point = ground;
            return true;
        }

        private Vector3 GroundPoint(Vector2 screen) => TryGroundPoint(screen, out var point) ? point : targetFocus;

        private void Tap(Vector2 position, bool inspect)
        {
            if (!CanProjectPointer(position)) return;
            if (session.Development != null && session.Development.Placing)
            {
                session.Development.PreviewAt(GroundPoint(position));
                return;
            }
            var ray = view.ScreenPointToRay(position);
            if (session.Development != null && session.Development.TryPick(ray)) return;
            CitySelectable selected = null;
            if (Physics.Raycast(ray, out var hit, 1600))
                selected = hit.collider.GetComponentInParent<CitySelectable>();
            if (selected == null)
            {
                // Roads are selected analytically on their real polylines, without
                // thousands of per-edge mobile physics colliders.
                var roadPlane = new Plane(Vector3.up, new Vector3(0f, -.084f, 0f));
                if (roadPlane.Raycast(ray, out float distance) &&
                    world.Roads != null &&
                    world.Roads.TryPick(ray.GetPoint(distance), .10f, out var road))
                {
                    previousSelection = null;
                    session.SelectRoad(road.Definition.id);
                }
                return;
            }
            if (!session.State.districts[selected.districtIndex].unlocked)
            {
                session.Notify("أكمل الحي السابق بنسبة 100% واستلم مكافأته أولاً.");
                return;
            }
            bool secondTap = previousSelection == selected && Time.unscaledTime - lastTapTime < 1.2f;
            previousSelection = selected;
            lastTapTime = Time.unscaledTime;
            if (session.State.selectedDistrict != selected.districtIndex)
                session.ChooseDistrict(selected.districtIndex);
            if (selected.plotIndex >= 0 && session.Development != null &&
                session.Development.SelectParcel(selected.districtIndex, selected.plotIndex)) return;
            session.SelectPlot(selected.plotIndex);
            if (inspect || !secondTap) return;
            if (selected.plotIndex == -2)
                session.Perform(e => e.StartSalvage(selected.districtIndex, session.Now));
            else if (selected.plotIndex >= 0)
            {
                var def = GameCatalog.Districts[selected.districtIndex].projects[selected.plotIndex];
                var project = session.Economy.FindProject(selected.districtIndex, def.id);
                if (project.completed && def.income > 0)
                    session.Perform(e => e.CollectIncome(selected.districtIndex, def.id, session.Now));
                else
                    // Spending is confirmed in the context card, never silently on a second tap.
                    session.Notify("راجع تفاصيل المشروع ثم أكّد البناء من البطاقة.");
            }
        }

        private bool IsUI(Vector2 screen)
        {
            if (!CanProjectPointer(screen)) return true;
            if (EventSystem.current == null) return false;
            uiHits.Clear();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = screen }, uiHits);
            return uiHits.Count > 0;
        }

        private void UpdatePose(bool immediate)
        {
            float blend = immediate ? 1 : 1 - Mathf.Exp(-8 * Time.unscaledDeltaTime);
            focus = Vector3.Lerp(focus, targetFocus, blend);
            float zoomBlend = immediate ? 1 : 1 - Mathf.Exp(-ZoomResponse * Time.unscaledDeltaTime);
            zoom = Mathf.Lerp(zoom, targetZoom, zoomBlend);
            view.orthographicSize = zoom;
            transform.rotation = Quaternion.Euler(Pitch, yaw, 0);
            // Orthographic framing does not depend on camera distance. Stay near the
            // inspected neighborhood so its shadows are inside the mobile shadow range;
            // pull back for the whole-city view to preserve the existing clipping bounds.
            float distance = Mathf.Clamp(zoom * 2f + 10f, 35f, 700f);
            transform.position = focus - transform.forward * distance;
        }
        private void OnDisable() { held = false; multiTouch = false; ModalOpen = false; }
    }
}