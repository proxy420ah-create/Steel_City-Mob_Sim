using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace SteelCity.Setup
{
    /// <summary>Headless compile probe — invoked by
    ///   Unity -batchmode -executeMethod SteelCity.Setup.CompileCheck.Run -quit
    /// Forces an assembly compilation pass; "=== compile done ===" prints on
    /// success, compiler errors print via the standard pipeline.</summary>
    public static class CompileCheck
    {
        public static void Run()
        {
            CompilationPipeline.RequestScriptCompilation();
            EditorApplication.update += Wait;
        }

        static void Wait()
        {
            if (EditorApplication.isCompiling) return;
            EditorApplication.update -= Wait;
            Debug.Log("=== compile done ===");
            EditorApplication.Exit(0);
        }
    }
}
