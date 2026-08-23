using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using BepInEx;
using HarmonyLib;
using Microsoft.Win32;
using Shapes;
using SmartTutorial;
using TestMod;
using UnityEngine;
using UnityEngine.Profiling;
using static UnityEngine.InputSystem.Controls.AxisControl;

namespace RCM_PanFix{

    [BepInDependency(RCMManager.IDENTIFIER, BepInDependency.DependencyFlags.HardDependency)]
    [BepInPlugin(IDENTIFIER, "Panning Fix Plugin", "1.0.0.0")]
    internal class PanFix : BaseUnityPlugin{
        const string IDENTIFIER = "RCM.plugins.panfix";
        private void Awake(){
            new Harmony(IDENTIFIER).PatchAll();
            RCMManager.ConnectMod("Better Panning").ContinueWith(t => {
                RCMModUI mod = t.Result;

                // begin mod UI construction here...
                mod.CreateLabelField("panning patched enabled");


            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        // allows you to zoom out further
        [HarmonyPatch(typeof(CameraZoomer), "Update")]
        public static class CameraZoomer_Update_RTSMiddleMousePatch{
            [HarmonyPrefix]
            public static bool Prefix(CameraZoomer __instance){
                __instance.zoomLimits.x = -500;
                __instance.zoomLimits.y = 120;
                return true;
            }
        }


        // this patch prevents time slowing from affecting camera speed
        //[HarmonyPatch(typeof(Scrolling), "Translate")]
        //public static class Scrolling_Translate_RTSMiddleMousePatch{
        //    [HarmonyPrefix]
        //    public static bool Prefix(Scrolling __instance, Vector3 translation){
                
        //        if (!(translation == Vector3.zero)){
        //            Vector3 vector = Quaternion.Euler(0f, __instance.transform.rotation.eulerAngles.y, 0f) * translation;
        //            Vector3 vector2 = __instance.UnrotatedPosition();
        //            Vector3 vector3 = vector2 + translation;
        //            if ((!(vector3.x > vector2.x) || !(vector2.x > __instance._sqrt2 * 0.5f * __instance._grid.WorldWidth)) && (!(vector3.x < vector2.x) 
        //            || !(vector2.x < (0f - __instance._sqrt2) * 0.5f * __instance._grid.WorldWidth)) && (!(vector3.z > vector2.z) 
        //            || !(vector2.z > __instance._sqrt2 * 0.5f * __instance._grid.WorldHeight)) && (!(vector3.z < vector2.z) || !(vector2.z < (0f - __instance._sqrt2) * 0.5f * __instance._grid.WorldHeight)))
        //            {
        //                __instance.transform.Translate(vector * Time.unscaledDeltaTime, Space.World);
        //            }
        //        }
        //        return false;
        //    }
        //}
       static void ApplyCamMovement(Scrolling __instance, Vector3 translation){
            if (!(translation == Vector3.zero))
            {
                Vector3 vector = Quaternion.Euler(0f, __instance.transform.rotation.eulerAngles.y, 0f) * translation;
                Vector3 vector2 = __instance.UnrotatedPosition();
                Vector3 vector3 = vector2 + translation;
                if ((!(vector3.x > vector2.x) || !(vector2.x > __instance._sqrt2 * 0.5f * __instance._grid.WorldWidth)) && (!(vector3.x < vector2.x)
                || !(vector2.x < (0f - __instance._sqrt2) * 0.5f * __instance._grid.WorldWidth)) && (!(vector3.z > vector2.z)
                || !(vector2.z > __instance._sqrt2 * 0.5f * __instance._grid.WorldHeight)) && (!(vector3.z < vector2.z) || !(vector2.z < (0f - __instance._sqrt2) * 0.5f * __instance._grid.WorldHeight)))
                {
                    __instance.transform.Translate(vector, Space.World);
                }
            }
        }
        Vector3 GetWorldPointUnderCursor(Camera cam)
        {
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            Plane ground = new Plane(Vector3.up, Vector3.zero); // y=0 plane
            ground.Raycast(ray, out float dist);
            return ray.GetPoint(dist);
        }


        [HarmonyPatch(typeof(Scrolling), "Update")]
        public static class Scrolling_Update_RTSMiddleMousePatch{
            private static Vector3 _lastMousePosition;
            [DllImport("user32.dll")]
            private static extern IntPtr GetForegroundWindow();
            [DllImport("user32.dll", SetLastError = true)]
            private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
            [DllImport("user32.dll", SetLastError = true)]
            private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);
            [DllImport("user32.dll")]
            private static extern bool SetCursorPos(int X, int Y);
            [StructLayout(LayoutKind.Sequential)]
            private struct RECT{
                public int left;
                public int top;
                public int right;
                public int bottom;
            }
            [StructLayout(LayoutKind.Sequential)]
            private struct POINT{
                public int x;
                public int y;
                public POINT(int x, int y) { this.x = x; this.y = y; }
            }
            // Inertia / fling state and tuning
            private static Vector3 _inertiaVelocity = Vector3.zero;   // world units per second
            private const float INERTIA_MIN_SPEED = 0.1f;            // below this we zero velocity
            private const float INERTIA_MAX_SPEED = 40000f;            // clamp max fling speed
            // Call this from your Update middle-mouse section.
            // Returns true if a wrap occurred (we moved the OS cursor), in which case you should
            // update _lastMousePosition and skip applying movement this frame to avoid a jump.
            // Returns true if we wrapped the cursor. Updates lastMousePosition to the new client coords.
            private static bool WrapCursorIfNeeded(ref Vector3 lastMousePosition)
            {
                IntPtr hWnd = GetForegroundWindow();
                if (hWnd == IntPtr.Zero) return false;

                // Get client rect (in client coordinates)
                if (!GetClientRect(hWnd, out RECT clientRect)) return false;
                int clientW = clientRect.right - clientRect.left;
                int clientH = clientRect.bottom - clientRect.top;
                if (clientW <= 0 || clientH <= 0) return false;

                // Convert client (0,0) to screen coords
                POINT topLeft = new POINT(0, 0);
                if (!ClientToScreen(hWnd, ref topLeft)) return false;
                int clientScreenX = topLeft.x;
                int clientScreenY = topLeft.y;

                // Unity Input.mousePosition is in client coords with origin bottom-left.
                Vector3 m = Input.mousePosition;
                int mx = Mathf.RoundToInt(m.x);
                int my = Mathf.RoundToInt(m.y);

                bool wrapped = false;
                int newMx = mx;
                int newMy = my;

                // Horizontal wrap: preserve relative Y inside client so no vertical jump
                if (mx <= 0)
                {
                    newMx = clientW - 2;
                    wrapped = true;
                }
                else if (mx >= clientW - 1)
                {
                    newMx = 1;
                    wrapped = true;
                }

                // Vertical wrap
                if (my <= 0)
                {
                    newMy = clientH - 2;
                    wrapped = true;
                }
                else if (my >= clientH - 1)
                {
                    newMy = 1;
                    wrapped = true;
                }

                if (!wrapped) return false;

                // Convert Unity client coords (origin bottom-left) to screen coords for SetCursorPos.
                // Unity Y origin bottom-left -> client Y from top = clientH - newMy
                int screenX = clientScreenX + newMx;
                int screenY = clientScreenY + (clientH - newMy);

                // Set OS cursor position
                SetCursorPos(screenX, screenY);

                // Update lastMousePosition to the new Unity client coords
                lastMousePosition = new Vector3(newMx, newMy, 0f);

                // Skip applying movement this frame to avoid a large jump; next frame Input.mousePosition will reflect the wrapped position.
                return true;
            }
            [HarmonyPrefix]
            public static bool Prefix(Scrolling __instance){
                // --- Original early-return conditions preserved ---
                if (SingleCardViewPanel.IsShowing_Static() || TutorialPanel.IsCurrentlyShowingACard_Static() || MouseRectangleInput.IsDrawingRectangle_Static() ||
                    DeckOverview.IsShowing_Static() || PlaceMapObjectsBeforePlayerCanStart.PlayerHasNotPressedStartYet_Static() || InGameMenu.IsShowing_Static())
                    return false;
                
                if (__instance._isAutoScrollingActivated){
                    Vector3 vector = Vector3.Lerp(__instance.transform.position, __instance._autoScrollPosition, 0.02f);
                    __instance.transform.position = vector;

                    if (__instance._activateScrollingAgainAfterAutoScroll && (__instance.transform.position - __instance._autoScrollPosition).sqrMagnitude < 1f)
                        __instance._isAutoScrollingActivated = false;
                }

                if (__instance._isAutoZoomActivated) __instance._mainCam.fieldOfView = Mathf.Lerp(__instance._mainCam.fieldOfView, __instance._autoZoomFieldOfView, 0.02f);
                

                if (__instance._isAutoScrollingActivated || FinishLevel.IsFinishing_Static())
                    return false;

                // --- RTS Middle Mouse Dragging ---
                if (Input.GetMouseButtonDown(2)){
                    __instance._isMiddleMouseScrollingActivated = true;
                    _lastMousePosition = Input.mousePosition;
                }
                else if (Input.GetMouseButtonUp(2)) __instance._isMiddleMouseScrollingActivated = false;
                

                if (__instance._isMiddleMouseScrollingActivated){

                    // If edge panning is disabled, allow wrap-around when cursor hits render area edges.
                    // If a wrap occurs we skip movement this frame to avoid a jump.
                    if (!Game.Options.EnableScreenEdgePan)
                    {
                        Camera cam = __instance._mainCam;
                        if (WrapCursorIfNeeded(ref _lastMousePosition))
                        {
                            // Wrapped this frame; don't apply movement now. Next frame Input.mousePosition will be the wrapped position.
                            return false;
                        }
                    }

                    // Per-frame pixel delta
                    Vector3 pixelDelta = Input.mousePosition - _lastMousePosition;
                    _lastMousePosition = Input.mousePosition;


                    // Camera height above world (use camera Y position)
                    float camHeight = __instance._mainCam != null ? __instance._mainCam.transform.position.y : 1f;
                    if (camHeight <= 0f) camHeight = 1f; // safety

                    // Scale converts pixel delta (per frame) into world units for this frame.
                    float scale = (camHeight / (float)Screen.height);

                    // Convert delta to world movement for this frame
                    Vector3 movement = new Vector3(pixelDelta.x * scale, 0f, pixelDelta.y * scale);

                    //// Convert pixel delta to world movement
                    //Vector3 movement = new Vector3(pixelDelta.x * scale, 0f, pixelDelta.y * scale);

                    // Apply user sensitivity and invert option
                    movement *= Game.Options.MiddleClickPanSpeed;
                    if (Game.Options.InvertMiddleClickPan) movement = -movement;

                    // Apply movement immediately (camera follows drag)
                    ApplyCamMovement(__instance, -movement);



                    // Exponential smoothing time constant (seconds). Smaller = less smoothing, fast flings preserved.
                    const float SMOOTH_TIME = 0.08f; // tune 0.02 - 0.08

                    // Compute smoothing alpha for this frame: alpha = 1 - exp(-dt / tau)
                    float dt = Mathf.Max(Time.unscaledDeltaTime, 1f / 1000f);
                    float alpha = 1f - Mathf.Exp(-dt / SMOOTH_TIME);

                    // Smooth the velocity: new = lerp(old, instant, alpha)
                    _inertiaVelocity = Vector3.Lerp(_inertiaVelocity, movement, alpha);

                    // Clamp max speed to avoid runaway flings
                    if (_inertiaVelocity.sqrMagnitude > INERTIA_MAX_SPEED * INERTIA_MAX_SPEED)
                        _inertiaVelocity = _inertiaVelocity.normalized * INERTIA_MAX_SPEED;



                    // Tutorial hook (preserve original behaviour)
                    if (!__instance._middleMouseScrollingSentToTutorialController)
                    {
                        TutorialController.AddUsedInput_Static(HasUsedCertainInputCondition.Input.ScrollingViaMiddleMouseButton);
                        __instance._middleMouseScrollingSentToTutorialController = true;
                    }

                    // While dragging we do not run the rest of Update
                    return false;
                }
                if (!__instance._isMiddleMouseScrollingActivated)
                {
                    if (_inertiaVelocity.sqrMagnitude > INERTIA_MIN_SPEED * INERTIA_MIN_SPEED)
                    {
                        // Use unscaled delta so fling is independent of Time.timeScale
                        float dt = Mathf.Max(Time.unscaledDeltaTime, 1f / 1000f);


                        // Move camera opposite to velocity so fling direction matches drag direction
                        ApplyCamMovement(__instance, -_inertiaVelocity);

                        // Exponential damping time constant (seconds). Smaller = stops faster.
                        const float INERTIA_DAMP_TIME = 0.2f; // tune 0.2 - 0.6

                        // Compute decay factor for this frame: decay = exp(-dt / tau)
                        float decay = Mathf.Exp(-dt / INERTIA_DAMP_TIME);

                        // Apply decay
                        _inertiaVelocity *= decay;

                        // Stop when below threshold
                        if (_inertiaVelocity.magnitude < INERTIA_MIN_SPEED)
                            _inertiaVelocity = Vector3.zero;
                    }
                }



                // --- If middle mouse is NOT active, run original scrolling logic ---
                // We simply let Harmony skip the original Update() and re-run the rest here.

                float x = Input.mousePosition.x;
                float y = Input.mousePosition.y;

                bool flag = x > -120f && x < Screen.width + 120f &&
                            y > -120f && y < Screen.height + 120f;

                bool flag2 = UserInput.Instance.ScrollLeftIsPressed ||
                             UserInput.Instance.ScrollRightIsPressed ||
                             UserInput.Instance.ScrollUpIsPressed ||
                             UserInput.Instance.ScrollDownIsPressed;

                if (!__instance._arrowKeyScrollingSentToTutorialController && flag2){
                    TutorialController.AddUsedInput_Static(
                        HasUsedCertainInputCondition.Input.ScrollingViaArrowKeys);
                    __instance._arrowKeyScrollingSentToTutorialController = true;
                }

                float num2 = Game.Options.CameraMoveSpeedFactor;
                if (Time.timeScale > 0f) num2 /= Time.timeScale;

                Vector3 zero = Vector3.zero;
                if (UserInput.Instance.ScrollLeftIsPressed || (Game.Options.EnableScreenEdgePan && flag && x < __instance.margin && x > -120f))
                    zero.x = -__instance._speedToUse * num2;
                else if (UserInput.Instance.ScrollRightIsPressed || (Game.Options.EnableScreenEdgePan && flag && x > Screen.width - __instance.margin && x < Screen.width + 120f))
                    zero.x = __instance._speedToUse * num2;

                if (UserInput.Instance.ScrollDownIsPressed || (Game.Options.EnableScreenEdgePan && flag && y < __instance.margin && y > -120f))
                    zero.z = -__instance._speedToUse * num2;
                else if (UserInput.Instance.ScrollUpIsPressed || (Game.Options.EnableScreenEdgePan && flag && y > Screen.height - __instance.margin && y < Screen.height + 120f))
                    zero.z = __instance._speedToUse * num2;

                if (zero != Vector3.zero){
                    if (CardTooltip.IsShowing_Static()) CardTooltip.HideTooltipForOneFrame_Static();

                    ApplyCamMovement(__instance, zero);

                    if (!flag2 && !__instance._borderScrollingSentToTutorialController){
                        TutorialController.AddUsedInput_Static(HasUsedCertainInputCondition.Input.ScrollingViaBorder);
                        __instance._borderScrollingSentToTutorialController = true;
                    }
                }

                return false;
            }
        }


}




}




