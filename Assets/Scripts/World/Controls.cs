// Central input manager. Combines keyboard and joypad into semantic, edge-pressed
// actions everything else reads (Controls.Left, Controls.A, ...). Self-creates at
// runtime, so nothing needs to be added to the scene.
//
// Keyboard:  arrows / WASD = directions;  >  = A;  <  = B;  Enter = Start;  Space = Select.
// Joypad:    left stick + d-pad (hat) = directions;  South/East = A;  West/North = B;
//            Start button = Start;  Select button = Select.
//
// Joypad note: the Input System maps pads by NAMED buttons, not the numbers a
// controller prints. If A/B land on the wrong physical buttons for your pad, say
// which and we swap the named buttons below — keyboard matches the spec exactly.

using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace NightRider.World
{
    [DefaultExecutionOrder(-1000)]   // resolve input before anything reads it
    public class Controls : MonoBehaviour
    {
        public static bool Left, Right, Up, Down, A, B, Start, Select;

        const float Dead = 0.5f;
        Vector2 _prevStick;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (FindAnyObjectByType<Controls>() != null) return;
            var go = new GameObject("Controls");
            DontDestroyOnLoad(go);
            go.AddComponent<Controls>();
        }

        void Update()
        {
            var kb = Keyboard.current;
            var gp = Gamepad.current;

            // Directions: keyboard edge + d-pad edge + stick edge.
            Vector2 s = gp != null ? gp.leftStick.ReadValue() : Vector2.zero;

            Left  = Hit(kb?.leftArrowKey)  || Hit(kb?.aKey) || Hit(gp?.dpad.left)  || (s.x < -Dead && _prevStick.x >= -Dead);
            Right = Hit(kb?.rightArrowKey) || Hit(kb?.dKey) || Hit(gp?.dpad.right) || (s.x >  Dead && _prevStick.x <=  Dead);
            Up    = Hit(kb?.upArrowKey)    || Hit(kb?.wKey) || Hit(gp?.dpad.up)    || (s.y >  Dead && _prevStick.y <=  Dead);
            Down  = Hit(kb?.downArrowKey)  || Hit(kb?.sKey) || Hit(gp?.dpad.down)  || (s.y < -Dead && _prevStick.y >= -Dead);
            _prevStick = s;

            A      = Hit(kb?.periodKey) || Hit(gp?.buttonEast)  || Hit(gp?.buttonWest);
            B      = Hit(kb?.commaKey)  || Hit(gp?.buttonNorth) || Hit(gp?.buttonSouth);
            Start  = Hit(kb?.enterKey)  || Hit(kb?.numpadEnterKey) || Hit(gp?.startButton);
            Select = Hit(kb?.spaceKey)  || Hit(gp?.selectButton);
        }

        // A KeyControl is a ButtonControl, so this covers keys, d-pad, and buttons.
        static bool Hit(ButtonControl b) => b != null && b.wasPressedThisFrame;
    }
}
