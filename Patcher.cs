using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using Mono.Cecil;

namespace DunGenMetadataFixer
{
    // Adds TypeForwarder entries to Assembly-CSharp for every public type in DunGen.dll.
    public static class Patcher
    {
        public static IEnumerable<string> TargetDLLs { get; } = new[] { "Assembly-CSharp.dll" };

        private static readonly ManualLogSource Log =
            Logger.CreateLogSource("DunGenMetadataFixer");

        public static void Patch(AssemblyDefinition asm)
        {
            try
            {
                var dunGenPath = Path.Combine(Paths.ManagedPath, "DunGen.dll");
                if (!File.Exists(dunGenPath))
                {
                    Log.LogWarning($"DunGen.dll not found at {dunGenPath}; skipping forwarder injection.");
                    return;
                }

                using (var dunGenDef = AssemblyDefinition.ReadAssembly(dunGenPath))
                {
                    var dunGenRef = asm.MainModule.AssemblyReferences
                        .FirstOrDefault(r => r.Name == "DunGen");
                    if (dunGenRef == null)
                    {
                        var srcName = dunGenDef.Name;
                        dunGenRef = new AssemblyNameReference(srcName.Name, srcName.Version)
                        {
                            Culture = srcName.Culture,
                            PublicKeyToken = srcName.PublicKeyToken,
                        };
                        asm.MainModule.AssemblyReferences.Add(dunGenRef);
                    }

                    var existing = new HashSet<string>(
                        asm.MainModule.ExportedTypes.Select(t => t.FullName));
                    var existingDefs = new HashSet<string>(
                        asm.MainModule.GetTypes().Select(t => t.FullName));

                    int added = AddForwarders(dunGenDef.MainModule.Types, null, dunGenRef,
                        asm.MainModule, existing, existingDefs);
                    Log.LogInfo($"Added {added} DunGen type forwarder(s) to Assembly-CSharp.");
                }
            }
            catch (Exception ex)
            {
                Log.LogError($"Failed to patch Assembly-CSharp with DunGen forwarders: {ex}");
            }
        }

        private static int AddForwarders(
            IEnumerable<TypeDefinition> types,
            ExportedType parent,
            AssemblyNameReference dunGenRef,
            ModuleDefinition module,
            HashSet<string> existing,
            HashSet<string> existingDefs)
        {
            int count = 0;
            foreach (var t in types)
            {
                if (!t.IsPublic && !t.IsNestedPublic) continue;
                if (existing.Contains(t.FullName)) continue;
                if (existingDefs.Contains(t.FullName)) continue;

                IMetadataScope scope = parent != null ? null : (IMetadataScope)dunGenRef;
                var forwarder = new ExportedType(t.Namespace, t.Name, module, scope)
                {
                    IsForwarder = true,
                    DeclaringType = parent,
                };
                module.ExportedTypes.Add(forwarder);
                existing.Add(t.FullName);
                count++;

                if (t.HasNestedTypes)
                    count += AddForwarders(t.NestedTypes, forwarder, dunGenRef,
                        module, existing, existingDefs);
            }
            return count;
        }
    }
}
