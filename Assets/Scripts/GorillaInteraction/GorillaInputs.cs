using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace GorillaInteraction
{
    public sealed class GorillaInputs
    {
        public enum InputState
        {
            Off,
            Hovering,
            Pressed
        }

        public static readonly GorillaInputs Left =
            new GorillaInputs(XRNode.LeftHand);

        public static readonly GorillaInputs Right =
            new GorillaInputs(XRNode.RightHand);

        public XRNode Hand { get; }

        public float PressThreshold = 0.75f;
        public float HoverThreshold = 0.1f;

        private InputDevice Device
        {
            get
            {
                InputDevice device =
                    InputDevices.GetDeviceAtXRNode(Hand);

                if (device.isValid)
                    return device;

                return default;
            }
        }

        private GorillaInputs(XRNode hand)
        {
            Hand = hand;
        }

        public Vector2 Thumbstick =>
            GetValue(CommonUsages.primary2DAxis, Vector2.zero);

        public InputState PrimaryButton =>
            GetButtonState(
                CommonUsages.primaryButton,
                CommonUsages.primaryTouch
            );

        public InputState SecondaryButton =>
            GetButtonState(
                CommonUsages.secondaryButton,
                CommonUsages.secondaryTouch
            );

        public float TriggerPressure =>
                GetValue(CommonUsages.trigger, 0f);

        public float GripPressure =>
                GetValue(CommonUsages.grip, 0f);

        public InputState Trigger =>
            GetAnalogState(
                TriggerPressure,
                GetTouch(CommonUsages.triggerButton)
            );

        public InputState Grip =>
            GetAnalogState(GripPressure, false);

        private InputState GetButtonState(
            InputFeatureUsage<bool> buttonUsage,
            InputFeatureUsage<bool> touchUsage)
        {
            InputDevice device = Device;

            if (!device.isValid)
                return InputState.Off;

            bool pressed = false;
            bool touched = false;

            bool hasButton =
                device.TryGetFeatureValue(
                    buttonUsage, out pressed);

            bool hasTouch =
                device.TryGetFeatureValue(
                    touchUsage, out touched);

            if (hasButton && pressed)
                return InputState.Pressed;

            if (hasTouch && touched)
                return InputState.Hovering;

            return InputState.Off;
        }

        private InputState GetAnalogState(
            float value,
            bool touched)
        {
            if (value >= PressThreshold)
                return InputState.Pressed;

            if (value >= HoverThreshold || touched)
                return InputState.Hovering;

            return InputState.Off;
        }

        private bool GetTouch(
            InputFeatureUsage<bool> usage)
        {
            InputDevice device = Device;

            return device.isValid &&
                   device.TryGetFeatureValue(
                       usage, out bool value) &&
                   value;
        }

        private float GetValue(
            InputFeatureUsage<float> usage,
            float fallback)
        {
            InputDevice device = Device;

            if (device.isValid &&
                device.TryGetFeatureValue(usage, out float value))
            {
                return value;
            }

            return fallback;
        }

        private Vector2 GetValue(
            InputFeatureUsage<Vector2> usage,
            Vector2 fallback)
        {
            InputDevice device = Device;

            if (device.isValid &&
                device.TryGetFeatureValue(usage, out Vector2 value))
            {
                return value;
            }

            return fallback;
        }
    }
}