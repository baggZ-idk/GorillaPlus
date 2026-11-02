
using System;
using System.Collections.Generic;
using UnityEngine;
using GorillaInteraction;

namespace GorillaLocomotion
{
    public class GorillaFingers : MonoBehaviour
    {
        [Serializable]
        public class Finger
        {
            public List<Transform> Bones = new List<Transform>();

            [Min(0f)]
            public float MaxAngle = 160f;

            public Vector3 RotationAxis = Vector3.right;

            [Range(0f, 1f)]
            public float Curl;
        }

        [Header("Fingers")]
        public Finger Pinky = new Finger();
        public Finger Index = new Finger();
        public Finger Thumb = new Finger();

        public bool UseLeftHand = true;

        [Header("Thumb Buttons")]
        [Range(0f, 1f)]
        public float ButtonCurl = 1f;

        [Header("Motion Smoothing")]
        [Tooltip("Higher values make fingers respond faster.")]
        [Min(0f)]
        public float SmoothingSpeed = 15f;

        [Tooltip("Enable for locally controlled hands. Disable for remote networked hands.")]
        public bool ReadLocalInput = true;

        private Quaternion[][] initialRotations;
        private float[] smoothedCurls = new float[3];

        private void Awake()
        {
            CacheInitialRotations();

            smoothedCurls[0] = Pinky.Curl;
            smoothedCurls[1] = Index.Curl;
            smoothedCurls[2] = Thumb.Curl;
        }

        private void CacheInitialRotations()
        {
            Finger[] fingers = { Pinky, Index, Thumb };

            initialRotations = new Quaternion[fingers.Length][];

            for (int i = 0; i < fingers.Length; i++)
            {
                Finger finger = fingers[i];

                initialRotations[i] =
                    new Quaternion[finger.Bones.Count];

                for (int j = 0; j < finger.Bones.Count; j++)
                {
                    Transform bone = finger.Bones[j];

                    if (bone != null)
                        initialRotations[i][j] = bone.localRotation;
                }
            }
        }

        private void LateUpdate()
        {
            if (ReadLocalInput)
            {
                GorillaInputs inputs = UseLeftHand
                    ? GorillaInputs.Left
                    : GorillaInputs.Right;

                Pinky.Curl = Mathf.Clamp01(inputs.GripPressure);
                Index.Curl = Mathf.Clamp01(inputs.TriggerPressure);

                bool primaryFullyPressed =
                    inputs.PrimaryButton == GorillaInputs.InputState.Pressed;

                bool secondaryFullyPressed =
                    inputs.SecondaryButton == GorillaInputs.InputState.Pressed;

                Thumb.Curl =
                    primaryFullyPressed || secondaryFullyPressed
                        ? ButtonCurl
                        : 0f;
            }

            float smoothing = SmoothingSpeed <= 0f
                ? 1f
                : 1f - Mathf.Exp(-SmoothingSpeed * Time.deltaTime);

            Finger[] fingers = { Pinky, Index, Thumb };

            for (int i = 0; i < fingers.Length; i++)
            {
                smoothedCurls[i] = Mathf.Lerp(
                    smoothedCurls[i],
                    Mathf.Clamp01(fingers[i].Curl),
                    smoothing
                );

                ApplyFinger(fingers[i], initialRotations[i], smoothedCurls[i]);
            }
        }

        private void ApplyFinger(
            Finger finger,
            Quaternion[] startingRotations,
            float curl)
        {
            if (finger.Bones == null ||
                startingRotations == null)
                return;

            int boneCount = finger.Bones.Count;

            if (boneCount == 0)
                return;

            Vector3 axis = finger.RotationAxis.normalized;

            if (axis == Vector3.zero)
                return;

            float anglePerBone =
                finger.MaxAngle * Mathf.Clamp01(curl) / boneCount;

            for (int i = 0; i < boneCount; i++)
            {
                Transform bone = finger.Bones[i];

                if (bone == null)
                    continue;

                bone.localRotation =
                    startingRotations[i] *
                    Quaternion.AngleAxis(anglePerBone, axis);
            }
        }

        public void SetCurls(float pinky, float index, float thumb)
        {
            Pinky.Curl = Mathf.Clamp01(pinky);
            Index.Curl = Mathf.Clamp01(index);
            Thumb.Curl = Mathf.Clamp01(thumb);
        }
    }
}
