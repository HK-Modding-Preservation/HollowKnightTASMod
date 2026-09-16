using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using GlobalEnums;
using HollowKnightTAS.Core.Cryptography;
using HollowKnightTAS.Core.Rng;
using Mono.Cecil;
using UnityEngine;

namespace HollowKnightTAS.Runtime.Rng
{
    public sealed class RuntimeRngCapabilityResolution
    {
        internal RuntimeRngCapabilityResolution(
            UnityRandomCodecResolution codec,
            RngWhitelist whitelist,
            RngAssemblyIdentity actualCallSiteAssembly,
            RngWhitelistResolution callSites,
            IReadOnlyList<RngCallSiteDescriptor> actualCallSiteDescriptors)
        {
            Codec = codec
                    ?? throw new ArgumentNullException(nameof(codec));
            Whitelist = whitelist
                        ?? throw new ArgumentNullException(nameof(whitelist));
            ActualCallSiteAssembly =
                actualCallSiteAssembly
                ?? throw new ArgumentNullException(
                    nameof(actualCallSiteAssembly));
            CallSites = callSites
                        ?? throw new ArgumentNullException(nameof(callSites));
            ActualCallSiteDescriptors =
                actualCallSiteDescriptors
                ?? throw new ArgumentNullException(
                    nameof(actualCallSiteDescriptors));
        }

        public UnityRandomCodecResolution Codec { get; }
        public RngWhitelist Whitelist { get; }
        public RngAssemblyIdentity ActualCallSiteAssembly { get; }
        public RngWhitelistResolution CallSites { get; }
        public IReadOnlyList<RngCallSiteDescriptor>
            ActualCallSiteDescriptors { get; }
        public bool StateAvailable => Codec.Ready;
        public bool HooksAvailable => Codec.Ready && CallSites.Ready;
        public string CodecId => Codec.Ready
            ? Codec.Codec!.CodecId
            : "unsupported";
        public string CoverageId => HooksAvailable
            ? RngWhitelist.CoverageId
            : StateAvailable
                ? "unity-state-only-build-mismatch"
                : "disabled-codec-mismatch";
    }

    public static class RuntimeRngCapabilityResolver
    {
        public static RuntimeRngCapabilityResolution Resolve()
        {
            var codec = UnityRandomStateCodec_1_5_78_11833.Resolve();
            var whitelist = RngWhitelist.CreateTargetBuild();
            var assembly = typeof(HeroController).Assembly;
            var location = assembly.Location;
            if (string.IsNullOrWhiteSpace(location)
                || !File.Exists(location))
            {
                throw new InvalidOperationException(
                    "Assembly-CSharp path is unavailable.");
            }

            var actualAssembly = new RngAssemblyIdentity(
                Path.GetFileName(location),
                Sha256Utility.ComputeFileHex(location),
                assembly.ManifestModule.ModuleVersionId.ToString("D"));
            var actualSites = ResolveCallSites(
                location,
                assembly,
                whitelist.CallSites);
            var callSites = whitelist.Resolve(
                actualAssembly,
                actualSites);
            return new RuntimeRngCapabilityResolution(
                codec,
                whitelist,
                actualAssembly,
                callSites,
                new ReadOnlyCollection<RngCallSiteDescriptor>(
                    actualSites));
        }

        private static List<RngCallSiteDescriptor> ResolveCallSites(
            string path,
            Assembly reflectionAssembly,
            IReadOnlyList<RngCallSiteDescriptor> expected)
        {
            var result = new List<RngCallSiteDescriptor>();
            using (var definition = AssemblyDefinition.ReadAssembly(path))
            {
                foreach (var item in expected)
                {
                    var method = definition.MainModule.LookupToken(
                        item.MetadataToken) as MethodDefinition;
                    if (method == null || !method.HasBody)
                    {
                        continue;
                    }

                    var randomCalls = method.Body.Instructions
                        .Select(value => value.Operand)
                        .OfType<MethodReference>()
                        .Where(
                            value => string.Equals(
                                value.DeclaringType.FullName,
                                "UnityEngine.Random",
                                StringComparison.Ordinal))
                        .ToList();
                    var randomApis = randomCalls
                        .Select(value => value.FullName)
                        .Distinct(StringComparer.Ordinal)
                        .ToList();
                    if (randomCalls.Count == 0
                        || randomApis.Count != 1)
                    {
                        continue;
                    }

                    var reflected = reflectionAssembly.ManifestModule
                        .ResolveMethod(item.MetadataToken);
                    var body = reflected.GetMethodBody();
                    var bytes = body?.GetILAsByteArray();
                    if (bytes == null)
                    {
                        continue;
                    }

                    result.Add(
                        new RngCallSiteDescriptor(
                            item.CallSiteId,
                            method.DeclaringType.FullName,
                            method.FullName,
                            method.MetadataToken.ToInt32(),
                            Sha256Utility.ComputeHex(bytes),
                            item.HookType,
                            randomApis[0],
                            randomCalls.Count));
                }
            }

            return result;
        }
    }

    public sealed class RngCallSiteHooks : IDisposable
    {
        private readonly RuntimeRngProbe owner;
        private bool attached;

        public RngCallSiteHooks(
            RuntimeRngProbe owner,
            RuntimeRngCapabilityResolution capability)
        {
            this.owner = owner
                         ?? throw new ArgumentNullException(nameof(owner));
            if (capability == null)
            {
                throw new ArgumentNullException(nameof(capability));
            }

            if (!capability.HooksAvailable)
            {
                throw new InvalidOperationException(
                    "RNG call-site hooks require an exact whitelist resolution.");
            }
        }

        public void Attach()
        {
            if (attached)
            {
                return;
            }

            try
            {
                On.Helper.GetRandomVector2InRange +=
                    OnGetRandomVector2InRange;
                On.HeroController.TakeDamage += OnHeroTakeDamage;
                attached = true;
            }
            catch
            {
                On.Helper.GetRandomVector2InRange -=
                    OnGetRandomVector2InRange;
                On.HeroController.TakeDamage -= OnHeroTakeDamage;
                throw;
            }
        }

        public void Dispose()
        {
            if (!attached)
            {
                return;
            }

            On.HeroController.TakeDamage -= OnHeroTakeDamage;
            On.Helper.GetRandomVector2InRange -=
                OnGetRandomVector2InRange;
            attached = false;
        }

        private Vector2 OnGetRandomVector2InRange(
            On.Helper.orig_GetRandomVector2InRange original,
            Vector2 minimum,
            Vector2 maximum)
        {
            var scope = owner.TryBeginCall("helper.random-vector2");
            try
            {
                return original(minimum, maximum);
            }
            finally
            {
                owner.TryCompleteCall(scope);
            }
        }

        private void OnHeroTakeDamage(
            On.HeroController.orig_TakeDamage original,
            HeroController self,
            GameObject source,
            CollisionSide damageSide,
            int damageAmount,
            int hazardType)
        {
            var scope = owner.TryBeginCall("hero.take-damage");
            try
            {
                original(
                    self,
                    source,
                    damageSide,
                    damageAmount,
                    hazardType);
            }
            finally
            {
                owner.TryCompleteCall(scope);
            }
        }
    }
}
