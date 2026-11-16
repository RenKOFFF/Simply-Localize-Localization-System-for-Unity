using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif


namespace SimplyLocalize.HorrorLab
{
    public enum HorrorNotice { None, KeyFound, DoorLocked, Complete }
    public sealed class HorrorPlayground : MonoBehaviour
    {
        public CharacterController player;
        public Camera view;
        public Light flashlight;
        public HorrorInteractable[] interactables;
        public bool HasKey { get; private set; }
        public bool DoorOpen { get; private set; }
        public bool MenuOpen { get; private set; } = true;
        public bool NoteOpen { get; private set; }
        public bool InventoryOpen { get; private set; }
        public HorrorInteractable Focused { get; private set; }
        public HorrorInteractable InspectedSign { get; private set; }
        [SerializeField, Range(.5f, 4f)] private float interactionReach = 2.1f;
        [SerializeField, Range(10f, 90f)] private float interactionHalfAngle = 65f;
        public bool Ready { get; private set; }
        public event Action Changed;
        public event Action<string> DialogueRequested;
        public HorrorNotice Notice { get; private set; }
        private float yaw, pitch = 12, messageUntil, verticalSpeed;
        private bool resumeRequested;
        private bool ownsCursor;
#if ENABLE_INPUT_SYSTEM
        private InputAction move, look, interact, menu, inventory, torch, sprint;
#endif
        private void Awake()
        {
            yaw = player.transform.eulerAngles.y;
#if ENABLE_INPUT_SYSTEM
            move = new InputAction("Move", InputActionType.Value);
            move.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s").With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            move.AddBinding("<Gamepad>/leftStick");
            look = new InputAction("Look", InputActionType.Value, "<Mouse>/delta");
            interact = new InputAction("Interact", InputActionType.Button, "<Keyboard>/e");
            interact.AddBinding("<Gamepad>/buttonSouth");
            menu = new InputAction("Menu", InputActionType.Button, "<Keyboard>/escape");
            menu.AddBinding("<Gamepad>/start");
            inventory = new InputAction("Inventory", InputActionType.Button, "<Keyboard>/tab");
            torch = new InputAction("Flashlight", InputActionType.Button, "<Keyboard>/f");
            sprint = new InputAction("Sprint", InputActionType.Button, "<Keyboard>/leftShift");
            foreach (var action in new[] { move, look, interact, menu, inventory, torch, sprint }) action.Enable();
#endif
            Refresh(); SetCursor();
        }
        private void Update()
        {
#if ENABLE_INPUT_SYSTEM
            if (Application.isFocused && menu.WasPressedThisFrame())
            {
                if (NoteOpen || InventoryOpen) CloseDocument(); else OpenMenu();
            }
            if (Application.isFocused && inventory.WasPressedThisFrame() && !MenuOpen) { InventoryOpen = !InventoryOpen; NoteOpen = false; SetCursor(); Refresh(); }
            if (Ready && Application.isFocused && resumeRequested && !MenuOpen && !NoteOpen && !InventoryOpen)
            {
                var delta = look.ReadValue<Vector2>();
                yaw += delta.x * .12f;
                pitch = Mathf.Clamp(pitch - delta.y * .12f, -35, 65);
                var input = Vector2.ClampMagnitude(move.ReadValue<Vector2>(), 1);
                var direction = Quaternion.Euler(0, yaw, 0) * new Vector3(input.x, 0, input.y);
                if (player.isGrounded && verticalSpeed < 0) verticalSpeed = -2;
                verticalSpeed += Physics.gravity.y * Time.deltaTime;
                player.Move((direction * (sprint.IsPressed() ? 4 : 2.2f) + Vector3.up * verticalSpeed) * Time.deltaTime);
                if (direction.sqrMagnitude > .01f) player.transform.rotation = Quaternion.Slerp(player.transform.rotation, Quaternion.LookRotation(direction), Time.deltaTime * 12);
                if (torch.WasPressedThisFrame()) flashlight.enabled = !flashlight.enabled;
                FindFocus();
                if (interact.WasPressedThisFrame() && Focused != null) Interact(Focused);
            }
            else if (Focused != null || InspectedSign != null) { Focused = null; InspectedSign = null; Refresh(); }
#endif
            if (Notice != HorrorNotice.None && Time.unscaledTime > messageUntil) { Notice = HorrorNotice.None; Refresh(); }
        }
        private void LateUpdate()
        {
            var rotation = Quaternion.Euler(pitch, yaw, 0);
            var pivot = player.transform.position + Vector3.up * 1.6f;
            var offset = rotation * new Vector3(.65f, .25f, -2.6f);
            if (Physics.SphereCast(pivot, .18f, offset.normalized, out var hit, offset.magnitude, ~(1 << 2), QueryTriggerInteraction.Ignore))
                offset = offset.normalized * Mathf.Max(.1f, hit.distance - .08f);
            view.transform.SetPositionAndRotation(pivot + offset, rotation);
            if (Ready && Application.isFocused && resumeRequested && !MenuOpen && !NoteOpen && !InventoryOpen)
            {
                var sign = HorrorInteractionQuery.FindSign(player.transform, view, interactables);
                if (sign != InspectedSign) { InspectedSign = sign; Refresh(); }
            }
        }
        private void FindFocus()
        {
            var next = HorrorInteractionQuery.Find(player.transform, interactables, Focused, interactionReach, interactionHalfAngle);
            if (next != Focused) { Focused = next; Refresh(); }
        }
        public void Interact(HorrorInteractable target)
        {
            if (target == null || !target.IsAvailable || target.kind == HorrorInteraction.Sign) return;
            switch (target.kind)
            {
                case HorrorInteraction.Radio: DialogueRequested?.Invoke(target.contentId); break;
                case HorrorInteraction.Note: NoteOpen = true; InventoryOpen = false; SetCursor(); break;
                case HorrorInteraction.Key:
                    if (!HasKey) { HasKey = true; target.Consume(); target.gameObject.SetActive(false); ShowMessage(HorrorNotice.KeyFound); }
                    break;
                case HorrorInteraction.Door:
                    if (DoorOpen) break;
                    if (!HasKey) ShowMessage(HorrorNotice.DoorLocked);
                    else { DoorOpen = true; target.Consume(); target.transform.Rotate(0, -95, 0); ShowMessage(HorrorNotice.Complete); }
                    break;
            }
            Refresh();
        }
        public void SetReady(bool ready) { Ready = ready; OpenMenu(); }
        // Only the explicit UI action grants gameplay/cursor ownership. Esc never recaptures it.
        public void ResumeGame()
        {
            if (!Ready || !Application.isFocused) return;
            resumeRequested = true; MenuOpen = NoteOpen = InventoryOpen = false;
            SetCursor(); Refresh();
        }
        public void OpenMenu()
        {
            resumeRequested = false; MenuOpen = true; NoteOpen = InventoryOpen = false;
            Focused = InspectedSign = null;
            ReleaseCursor(); Refresh();
        }
        public void CloseDocument() { NoteOpen = InventoryOpen = false; SetCursor(); Refresh(); }
        private void SetCursor()
        {
            bool capture = resumeRequested && Application.isFocused && !Application.isBatchMode
                && !MenuOpen && !NoteOpen && !InventoryOpen;
            if (!capture) { ReleaseCursor(); return; }
            Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; ownsCursor = true;
        }
        private void ReleaseCursor()
        {
            if (!ownsCursor) return;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true; ownsCursor = false;
        }
        private void OnApplicationFocus(bool focused) { if (!focused) OpenMenu(); }
        private void OnDisable()
        {
            resumeRequested = false; MenuOpen = true; Focused = InspectedSign = null;
            ReleaseCursor();
        }
        private void OnEnable() { Refresh(); }
        private void ShowMessage(HorrorNotice notice) { Notice = notice; messageUntil = Time.unscaledTime + 5; }
        private void Refresh() => Changed?.Invoke();
        private void OnDestroy()
        {
#if ENABLE_INPUT_SYSTEM
            foreach (var action in new[] { move, look, interact, menu, inventory, torch, sprint }) action?.Dispose();
#endif
            ReleaseCursor();
        }
    }
}




