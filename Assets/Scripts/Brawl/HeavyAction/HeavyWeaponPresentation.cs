using System;
using UnityEngine;

namespace BrawlArena
{
    /// <summary>
    /// Bone-parented, IK-free weapon presenter for souls-style brawlers. The
    /// weapon visual prefab hangs off a hand bone and carries the standard
    /// SpellOrigin muzzle child; aim posing comes entirely from the animation
    /// layers. Visual-only: never gameplay resource, timing, or damage
    /// authority.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HeavyWeaponPresentation :
        MonoBehaviour, IBrawlerWeaponPresentation
    {
        const string MuzzleSocketName = "SpellOrigin";

        [SerializeField, HideInInspector]
        Transform weaponVisualRoot;

        [SerializeField, HideInInspector]
        Transform muzzle;

        [SerializeField, HideInInspector]
        ParticleSystem[] muzzleEffects = Array.Empty<ParticleSystem>();

        bool visible = true;
        bool aimPresented;
        bool muzzleResolved;
        Vector3 presentedAimDirection;
        Renderer[] weaponRenderers;

        public Transform WeaponVisualRoot => weaponVisualRoot;
        public Transform Muzzle => ResolveMuzzle();
        public bool Visible => visible;
        public bool AimPresented => aimPresented;
        public Vector3 PresentedAimDirection => presentedAimDirection;

        /// <summary>
        /// Builder/assembler-facing wiring: the bone-parented weapon visual
        /// root. The SpellOrigin muzzle child and muzzle particle effects are
        /// resolved from it when not already assigned.
        /// </summary>
        public void Configure(Transform configuredWeaponVisualRoot)
        {
            if (configuredWeaponVisualRoot == null)
            {
                throw new ArgumentNullException(nameof(configuredWeaponVisualRoot));
            }

            weaponVisualRoot = configuredWeaponVisualRoot;
            muzzle = FindDescendant(configuredWeaponVisualRoot, MuzzleSocketName);
            muzzleResolved = muzzle != null;
            weaponRenderers = null;
            if ((muzzleEffects == null || muzzleEffects.Length == 0) && muzzle != null)
                muzzleEffects = muzzle.GetComponentsInChildren<ParticleSystem>(true);
        }

        /// <summary>
        /// No IK: aim posing is animation-layer work, so this only records
        /// the direction. Vector3.zero releases aiming.
        /// </summary>
        public void PresentAim(Vector3 worldDirection)
        {
            if (worldDirection.sqrMagnitude <= 0.000001f)
            {
                aimPresented = false;
                presentedAimDirection = Vector3.zero;
                return;
            }

            presentedAimDirection = worldDirection.normalized;
            aimPresented = true;
        }

        public bool TryGetMuzzlePosition(out Vector3 worldPosition)
        {
            worldPosition = Vector3.zero;
            Transform socket = ResolveMuzzle();
            if (socket == null || !IsFinite(socket.position)) return false;
            worldPosition = socket.position;
            return true;
        }

        public void PresentMuzzle(Vector3 worldPosition, Vector3 worldDirection)
        {
            if (!visible || !IsFinite(worldPosition) || !IsFinite(worldDirection))
                return;

            if (worldDirection.sqrMagnitude <= 0.000001f)
            {
                Transform socket = ResolveMuzzle();
                worldDirection = socket != null ? socket.forward : transform.forward;
            }
            if (worldDirection.sqrMagnitude <= 0.000001f) return;

            worldDirection.Normalize();
            Vector3 up = Mathf.Abs(Vector3.Dot(worldDirection, Vector3.up)) > 0.999f
                ? transform.forward
                : Vector3.up;
            Quaternion rotation = Quaternion.LookRotation(worldDirection, up);
            for (int i = 0; i < muzzleEffects.Length; i++)
            {
                ParticleSystem effect = muzzleEffects[i];
                if (effect == null) continue;
                effect.transform.SetPositionAndRotation(worldPosition, rotation);
                effect.Play(true);
            }
        }

        public void SetVisible(bool value)
        {
            visible = value;
            Renderer[] renderers = ResolveWeaponRenderers();
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null) renderers[i].enabled = value;
            if (!value) PresentAim(Vector3.zero);
        }

        public void ResetForRespawn()
        {
            for (int i = 0; i < muzzleEffects.Length; i++)
            {
                ParticleSystem effect = muzzleEffects[i];
                if (effect == null) continue;
                effect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            PresentAim(Vector3.zero);
            SetVisible(true);
        }

        Transform ResolveMuzzle()
        {
            if (muzzle != null) return muzzle;
            if (muzzleResolved) return null;

            muzzle = FindDescendant(weaponVisualRoot, MuzzleSocketName);
            if (muzzle == null) muzzle = FindDescendant(transform, MuzzleSocketName);
            muzzleResolved = true;
            return muzzle;
        }

        Renderer[] ResolveWeaponRenderers()
        {
            if (weaponRenderers != null) return weaponRenderers;
            weaponRenderers = weaponVisualRoot != null
                ? weaponVisualRoot.GetComponentsInChildren<Renderer>(true)
                : Array.Empty<Renderer>();
            return weaponRenderers;
        }

        static Transform FindDescendant(Transform root, string childName)
        {
            if (root == null) return null;
            Transform[] children = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
                if (children[i] != null && children[i].name == childName)
                    return children[i];
            return null;
        }

        static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsNaN(value.y) && !float.IsNaN(value.z) &&
                !float.IsInfinity(value.x) && !float.IsInfinity(value.y) &&
                !float.IsInfinity(value.z);
        }
    }
}
