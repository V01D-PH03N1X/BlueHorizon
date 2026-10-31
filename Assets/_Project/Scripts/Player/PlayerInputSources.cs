using BlueHorizon.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BlueHorizon.Player
{
    /// <summary>Produces commands for the locally owned player: keyboard/mouse, or the automated test bot.</summary>
    public interface IPlayerInputSource
    {
        float Yaw { get; }
        float Pitch { get; }
        /// <summary>Per rendered frame: accumulate look and latch button presses.</summary>
        void UpdateFrame(float deltaTime);
        /// <summary>Per network tick: write intent into the command and clear latches.</summary>
        void Fill(ref PlayerInputCommand command);
        void SetLook(float yaw, float pitch);
        void Dispose();
    }

    /// <summary>User preferences that affect input. Persisted in PlayerPrefs.</summary>
    public static class InputPreferences
    {
        const string SensitivityKey = "bh.sensitivity";
        const string InvertKey = "bh.invertY";

        public static float Sensitivity
        {
            get => PlayerPrefs.GetFloat(SensitivityKey, 1.0f);
            set => PlayerPrefs.SetFloat(SensitivityKey, Mathf.Clamp(value, 0.05f, 5f));
        }

        public static bool InvertY
        {
            get => PlayerPrefs.GetInt(InvertKey, 0) == 1;
            set => PlayerPrefs.SetInt(InvertKey, value ? 1 : 0);
        }

        /// <summary>Set by the camera every frame: current FOV / base FOV (1 when not zoomed).</summary>
        public static float ZoomSensitivityScale { get; set; } = 1f;

        /// <summary>True while a menu owns the cursor; gameplay input is suppressed.</summary>
        public static bool GameplayBlocked { get; set; }
    }

    /// <summary>Keyboard and mouse via the Input System. Bindings are documented in Documentation/README.md.</summary>
    public sealed class KeyboardMouseInputSource : IPlayerInputSource
    {
        const float MouseDegreesPerCount = 0.022f * 2.5f;

        readonly InputAction _move = new("Move");
        readonly InputAction _look = new("Look", binding: "<Mouse>/delta");
        readonly (InputAction action, InputButtons button)[] _buttons;
        InputButtons _latched;
        float _yaw, _pitch;
        readonly float _minPitch, _maxPitch;

        public float Yaw => _yaw;
        public float Pitch => _pitch;

        public KeyboardMouseInputSource(float minPitch, float maxPitch)
        {
            _minPitch = minPitch;
            _maxPitch = maxPitch;
            _move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            _buttons = new[]
            {
                (Make("Jump", "<Keyboard>/space"), InputButtons.Jump),
                (Make("Crouch", "<Keyboard>/leftCtrl", "<Keyboard>/c"), InputButtons.Crouch),
                (Make("Sprint", "<Keyboard>/leftShift"), InputButtons.Sprint),
                (Make("Fire", "<Mouse>/leftButton"), InputButtons.Fire),
                (Make("Reload", "<Keyboard>/r"), InputButtons.Reload),
                (Make("Interact", "<Keyboard>/f"), InputButtons.Interact),
                (Make("Smoke", "<Keyboard>/q"), InputButtons.Ability1),
                (Make("Wayblocker", "<Keyboard>/e"), InputButtons.Ability2),
                (Make("Heal", "<Keyboard>/x"), InputButtons.Signature),
                (Make("Primary", "<Keyboard>/1"), InputButtons.Weapon1),
                (Make("Secondary", "<Keyboard>/2"), InputButtons.Weapon2),
                (Make("Suppressor", "<Keyboard>/t"), InputButtons.ToggleAttachment),
                (Make("Aim", "<Mouse>/rightButton"), InputButtons.Aim),
            };
            _move.Enable();
            _look.Enable();
        }

        static InputAction Make(string name, params string[] bindings)
        {
            var action = new InputAction(name, InputActionType.Button);
            foreach (string b in bindings) action.AddBinding(b);
            action.Enable();
            return action;
        }

        public void UpdateFrame(float deltaTime)
        {
            if (InputPreferences.GameplayBlocked)
                return;

            // While zoomed, look speed scales with the field of view so aiming feels consistent through optics.
            Vector2 delta = _look.ReadValue<Vector2>() * (MouseDegreesPerCount * InputPreferences.Sensitivity * InputPreferences.ZoomSensitivityScale);
            _yaw = Mathf.Repeat(_yaw + delta.x, 360f);
            float dy = InputPreferences.InvertY ? -delta.y : delta.y;
            _pitch = Mathf.Clamp(_pitch + dy, _minPitch, _maxPitch);

            foreach ((InputAction action, InputButtons button) in _buttons)
            {
                if (action.WasPressedThisFrame()) _latched |= button;
            }
        }

        public void Fill(ref PlayerInputCommand command)
        {
            command.yaw = _yaw;
            command.pitch = _pitch;
            if (InputPreferences.GameplayBlocked)
            {
                command.SetMove(Vector2.zero);
                command.buttons = InputButtons.None;
                _latched = InputButtons.None;
                return;
            }

            command.SetMove(_move.ReadValue<Vector2>());
            InputButtons held = InputButtons.None;
            foreach ((InputAction action, InputButtons button) in _buttons)
            {
                if (action.IsPressed()) held |= button;
            }
            command.buttons = held | _latched;
            _latched = InputButtons.None;
        }

        public void SetLook(float yaw, float pitch)
        {
            _yaw = yaw;
            _pitch = Mathf.Clamp(pitch, _minPitch, _maxPitch);
        }

        public void Dispose()
        {
            _move.Dispose();
            _look.Dispose();
            foreach ((InputAction action, _) in _buttons) action.Dispose();
        }
    }
}
