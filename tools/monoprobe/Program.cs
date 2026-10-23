// FABLE-091 Mono probe: asks the runtime the phone uses (Mono) to prepare every method of the game, and lists any that fail to load.
using System.Reflection; using System.Runtime.CompilerServices; using System.Runtime.Loader;
var dir = args[0]; var skipFile = args[1]; var outFile = args[2];
var skip = File.Exists(skipFile) ? new HashSet<string>(File.ReadAllLines(skipFile)) : new HashSet<string>();
var done = File.Exists(outFile + ".done") ? new HashSet<string>(File.ReadAllLines(outFile + ".done")) : new HashSet<string>();
AssemblyLoadContext.Default.Resolving += (ctx, name) => { var p = Path.Combine(dir, name.Name + ".dll"); return File.Exists(p) ? ctx.LoadFromAssemblyPath(p) : null; };
using var progress = new StreamWriter(outFile + ".progress", false) { AutoFlush = true };
using var doneW = new StreamWriter(outFile + ".done", true) { AutoFlush = true };
using var res = new StreamWriter(outFile, true) { AutoFlush = true };
if (done.Count == 0) res.WriteLine("RUNTIME " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription + (Type.GetType("Mono.Runtime") != null || typeof(object).Assembly.GetType("Mono.RuntimeStructs") != null ? " (Mono)" : " (not Mono)"));
int methods = 0;
foreach (var an in new[] { "Runewake.Client", "Runewake.Engine" })
{
    var path = Path.Combine(dir, an + ".dll");
    if (!File.Exists(path)) { res.WriteLine("MISSING " + path); continue; }
    var asm = AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
    Type[] types;
    try { types = asm.GetTypes(); }
    catch (ReflectionTypeLoadException e)
    {
        foreach (var le in e.LoaderExceptions) res.WriteLine("FAIL-TYPE " + an + ": " + le?.GetType().Name + ": " + le?.Message + (le is TypeLoadException tl0 ? " [" + tl0.TypeName + "]" : ""));
        types = e.Types.Where(t => t != null).ToArray();
    }
    foreach (var t in types.OrderBy(t => (t.FullName ?? "").Contains("OnlineLobby") ? 0 : 1))
    {
        string tn = t.FullName ?? t.Name;
        if (skip.Contains(tn) || done.Contains(tn)) continue;
        if (t.IsGenericTypeDefinition) { doneW.WriteLine(tn); continue; }
        MethodBase[] ms;
        try
        {
            var f = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            ms = t.GetMethods(f).Cast<MethodBase>().Concat(t.GetConstructors(f & ~BindingFlags.Static)).ToArray();
        }
        catch (Exception e) { res.WriteLine($"FAIL {tn} (its members): {e.GetType().Name}: {e.Message}"); doneW.WriteLine(tn); continue; }
        foreach (var m in ms)
        {
            try { if (m.IsAbstract || m.ContainsGenericParameters || m.GetMethodBody() == null) continue; } catch { }
            progress.WriteLine(tn + "::" + m.Name);
            try { RuntimeHelpers.PrepareMethod(m.MethodHandle); m.MethodHandle.GetFunctionPointer(); methods++; }
            catch (Exception e)
            {
                res.WriteLine($"FAIL {tn}::{m.Name}: {e.GetType().Name}: {e.Message}" + (e is TypeLoadException tl ? " [type " + tl.TypeName + "]" : "")
                    + (e.InnerException != null ? " / inner " + e.InnerException.GetType().Name + ": " + e.InnerException.Message : ""));
                if (tn.Contains("OnlineLobby")) res.WriteLine(e.ToString());
            }
        }
        doneW.WriteLine(tn);
    }
}
res.WriteLine($"PROBE-END ({methods} methods prepared this pass)");
