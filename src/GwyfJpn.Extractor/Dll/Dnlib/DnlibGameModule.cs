using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using GwyfJpn.Core;

namespace GwyfJpn.Extractor;

/// <summary>
/// Loads the shipped game assembly through dnlib without executing it.
/// Unity/Mono builds use <see cref="CLRRuntimeReaderKind.Mono"/> so metadata matches the CLR loader.
/// </summary>
internal sealed class DnlibGameModule : IDisposable
{
    private DnlibGameModule(ModuleDefMD module, DisplaySinkMapping mapping)
    {
        Module = module;
        Mapping = mapping;
    }

    public ModuleDefMD Module { get; }
    public DisplaySinkMapping Mapping { get; }

    public static DnlibGameModule Load(string gameDir, string? mappingPath = null)
    {
        var mapping = DisplaySinkMapping.Load(mappingPath ?? DisplaySinkMapping.FindDefaultMappingPath());
        var assemblyPath = GameInstallPaths.ResolveMainAssemblyPath(gameDir, mapping.Game);
        if (!File.Exists(assemblyPath))
        {
            throw new FileNotFoundException("Main game assembly was not found.", assemblyPath);
        }

        var managedDir = GameInstallPaths.ResolveManagedDir(gameDir, mapping.Game);
        var context = ModuleDef.CreateModuleContext();
        var resolver = (AssemblyResolver)context.AssemblyResolver;
        resolver.EnableTypeDefCache = true;
        resolver.PreSearchPaths.Add(managedDir);

        var options = new ModuleCreationOptions(context)
        {
            Runtime = CLRRuntimeReaderKind.Mono
        };
        var module = ModuleDefMD.Load(assemblyPath, options);
        return new DnlibGameModule(module, mapping);
    }

    public IEnumerable<TypeDef> EnumerateTypes()
    {
        return Module.GetTypes().Where(t => t != null).OrderBy(t => t.FullName, StringComparer.Ordinal);
    }

    public void Dispose()
    {
        Module.Dispose();
    }
}

/// <summary>
/// Indexes every method body once and exposes call-graph helpers for pruning.
/// </summary>
internal sealed class MethodBodyIndex
{
    private MethodBodyIndex(IReadOnlyDictionary<MethodDef, IList<Instruction>> methodInstructions)
    {
        MethodInstructions = methodInstructions;
    }

    public IReadOnlyDictionary<MethodDef, IList<Instruction>> MethodInstructions { get; }

    public static MethodBodyIndex Build(ModuleDefMD module)
    {
        var methods = new Dictionary<MethodDef, IList<Instruction>>();
        foreach (var type in module.GetTypes())
        {
            foreach (var method in type.Methods)
            {
                if (!method.HasBody || method.Body.Instructions.Count == 0)
                {
                    continue;
                }

                methods[method] = method.Body.Instructions;
            }
        }

        return new MethodBodyIndex(methods);
    }
}
