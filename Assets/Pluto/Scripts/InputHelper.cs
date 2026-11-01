using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Wraps touch / mouse / keyboard / gamepad so the rest of the code does not care
// which Unity input backend is active. Keyboard + gamepad are for laptop testing
// (and a bonus physical controller); the on-screen joysticks are the main input.
//
// Keyboard: W/S throttle, A/D yaw, arrow keys pitch/roll, Space arm, T take off, L land.
public static class InputHelper
{
    public static bool TryGetTapDown(out Vector2 position)
    {
#if ENABLE_INPUT_SYSTEM
        var touch = Touchscreen.current;
        if (touch != null)
        {
            foreach (var t in touch.touches)
            {
                if (!t.press.wasPressedThisFrame) continue;
                position = t.position.ReadValue();
                return true;
            }
        }
        var mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame)
        {
            position = mouse.position.ReadValue();
            return true;
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.touchCount > 0 && Input.GetTouch(0).phase == UnityEngine.TouchPhase.Began)
        {
            position = Input.GetTouch(0).position;
            return true;
        }
        if (Input.GetMouseButtonDown(0))
        {
            position = Input.mousePosition;
            return true;
        }
#endif
        position = default;
        return false;
    }

    // left: x = yaw, y = throttle. right: x = roll, y = pitch.
    public static void ReadExtraSticks(out Vector2 left, out Vector2 right)
    {
        left = Vector2.zero;
        right = Vector2.zero;
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        if (k != null)
        {
            left = new Vector2(Axis(k.aKey.isPressed, k.dKey.isPressed), Axis(k.sKey.isPressed, k.wKey.isPressed));
            right = new Vector2(Axis(k.leftArrowKey.isPressed, k.rightArrowKey.isPressed), Axis(k.downArrowKey.isPressed, k.upArrowKey.isPressed));
        }
        var g = Gamepad.current;
        if (g != null)
        {
            left += g.leftStick.ReadValue();
            right += g.rightStick.ReadValue();
        }
#else
        left = new Vector2(Axis(Input.GetKey(KeyCode.A), Input.GetKey(KeyCode.D)), Axis(Input.GetKey(KeyCode.S), Input.GetKey(KeyCode.W)));
        right = new Vector2(Axis(Input.GetKey(KeyCode.LeftArrow), Input.GetKey(KeyCode.RightArrow)), Axis(Input.GetKey(KeyCode.DownArrow), Input.GetKey(KeyCode.UpArrow)));
#endif
    }

    public static bool ArmKeyDown()
    {
#if ENABLE_INPUT_SYSTEM
        return (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
            || (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame);
#else
        return Input.GetKeyDown(KeyCode.Space);
#endif
    }

    public static bool TakeoffKeyDown()
    {
#if ENABLE_INPUT_SYSTEM
        return (Keyboard.current != null && Keyboard.current.tKey.wasPressedThisFrame)
            || (Gamepad.current != null && Gamepad.current.buttonNorth.wasPressedThisFrame);
#else
        return Input.GetKeyDown(KeyCode.T);
#endif
    }

    public static bool LandKeyDown()
    {
#if ENABLE_INPUT_SYSTEM
        return (Keyboard.current != null && Keyboard.current.lKey.wasPressedThisFrame)
            || (Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame);
#else
        return Input.GetKeyDown(KeyCode.L);
#endif
    }

    static float Axis(bool negative, bool positive) => (positive ? 1f : 0f) - (negative ? 1f : 0f);
}
