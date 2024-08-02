using System;
using System.IO;
using System.Collections.Generic;
using Serilog;
using Microsoft.CodeAnalysis;
using System.Text;
using System.Diagnostics;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;

namespace ModShardPackerReference;
public static class FilePacker
{
    /// <summary>
    /// Pack the content folder into a sml file named <see cref="namePacked"/>.
    /// </summary>
    /// <param name="namePacked"></param>
    /// <param name="folderToPack"></param>
    /// <param name="outputfolder"></param>
    /// <param name="dllfolder"></param>
    /// <param name="frontVersion"></param>
    /// <param name="neededType"></param>
    /// <returns></returns>
    /// <exception cref="DirectoryNotFoundException"></exception>
    public static bool Pack(string? namePacked, string folderToPack, string outputfolder, string dllfolder, string frontVersion, Type[] neededType)
    {
        Log.Information("Starting packing {0}", folderToPack);

        DirectoryInfo dir = new(folderToPack);
        namePacked ??= dir.Name;
        FileInfo[] textures = dir.GetFiles("*.png", SearchOption.AllDirectories);
        FileInfo[] scripts = dir.GetFiles("*.lua", SearchOption.AllDirectories);
        FileInfo[] codes = dir.GetFiles("*.gml", SearchOption.AllDirectories);
        FileInfo[] assemblies = dir.GetFiles("*.asm", SearchOption.AllDirectories);
        int offset = 0;

        if (!Directory.Exists(outputfolder))
        {
            throw new DirectoryNotFoundException($"Could not find the given output folder '{outputfolder}'");
        }

        FileStream fs = new(Path.Join(outputfolder, namePacked + ".sml"), FileMode.Create);

        Write(fs, "MSLM");
        Log.Information("Writting header...");
        
        Write(fs, frontVersion);
        Log.Information("Writting version...");

        Write(fs, textures.Length);
        foreach (FileInfo tex in textures)
        {
            string name = dir.Name + tex.FullName.Replace(folderToPack, "");
            Write(fs, name.Length);
            Write(fs, name);
            Write(fs, offset);
            int len = CalculateBytesLength(tex);
            Write(fs, len);
            offset += len;
        }
        Log.Information("Preparing textures...");

        Write(fs, scripts.Length);
        foreach (FileInfo scr in scripts)
        {
            string name = dir.Name + scr.FullName.Replace(folderToPack, "");
            Write(fs, name.Length);
            Write(fs, name);
            Write(fs, offset);
            int len = CalculateBytesLength(scr);
            Write(fs, len);
            offset += len;
            
        }
        Log.Information("Preparing scripts...");

        Write(fs, codes.Length);
        foreach (FileInfo cds in codes)
        {
            string name = dir.Name + cds.FullName.Replace(folderToPack, "");
            Write(fs, name.Length);
            Write(fs, name);
            Write(fs, offset);
            int len = CalculateBytesLength(cds);
            Write(fs, len);
            offset += len;
        }
        Log.Information("Preparing codes...");

        Write(fs, assemblies.Length);
        foreach (FileInfo asm in assemblies)
        {
            string name = dir.Name + asm.FullName.Replace(folderToPack, "");
            Write(fs, name.Length);
            Write(fs, name);
            Write(fs, offset);
            int len = CalculateBytesLength(asm);
            Write(fs, len);
            offset += len;
        }
        Log.Information("Preparing assemblies...");

        foreach (FileInfo tex in textures)
            Write(fs, tex);
        Log.Information("Writting textures...");

        foreach (FileInfo scr in scripts)
            Write(fs, scr);
        Log.Information("Writting scripts...");

        foreach (FileInfo cds in codes)
            Write(fs, cds);
        Log.Information("Writting codes...");

        foreach (FileInfo asm in assemblies)
            Write(fs, asm);
        Log.Information("Writting assemblies...");

        Log.Information("Starting compilation...");

        bool successful;
        byte[] code;
        try
        {
            successful = CompileMod(namePacked, folderToPack, dllfolder, neededType, out code, out _);
        }
        catch
        {
            throw;
        }

        if (!successful)
        {
            fs.Close();
            File.Delete(fs.Name);
            Log.Information("Failed packing {0}", namePacked);
            return false;
        }

        Write(fs, code.Length);
        Write(fs, code);
        Log.Information("Successfully packed {0}", namePacked);
        fs.Close();

        return true;
    }
    public static void Write(FileStream fs, object obj)
    {
        Type type = obj.GetType();
        if (type == typeof(int))
        {
            byte[]? bytes = BitConverter.GetBytes((int)obj);
            fs.Write(bytes, 0, bytes.Length);
        }
        else if(type == typeof(string))
        {
            byte[]? bytes = Encoding.UTF8.GetBytes((string)obj);
            fs.Write(bytes, 0, bytes.Length);
        }
        else if(type == typeof(FileInfo))
        {
            FileStream stream = new(((FileInfo)obj).FullName, FileMode.Open);
            byte[]? bytes = new byte[stream.Length];
            stream.Read(bytes, 0, bytes.Length);
            fs.Write(bytes, 0, bytes.Length);
            stream.Close();
        }
        else if(type == typeof(byte[]))
        {
            fs.Write((byte[])obj);
        }
    }
    public static int CalculateBytesLength(FileInfo f)
    {
        FileStream stream = new(f.FullName, FileMode.Open);
        int len = (int)stream.Length;
        stream.Close();
        return len;
    }
    public static List<MetadataReference> GetSystemMetadataReferences()
    {
        string trustedAssemblies = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string)!;
        string[] trustedList = trustedAssemblies.Split(Path.PathSeparator);
        List<string> required = new()
        {
            "System.Runtime.dll",
            "System.Linq.dll",
            "System.ObjectModel.dll",
            "System.Collections.dll",
            "System.Private.CoreLib.dll",
            "System.Text.RegularExpressions.dll"
        };
        IEnumerable<string> filteredPath = trustedList.Where(p => required.Exists(r => p.Contains(r)));
        return filteredPath.Select(x => MetadataReference.CreateFromFile(x) as MetadataReference).ToList();
    }
    public static Diagnostic[] RoslynCompile(string name, IEnumerable<string> files, IEnumerable<string> preprocessorSymbols, string dllfolder, Type[] neededType, out byte[] code, out byte[] pdb)
    {
        NameSyntax[] qualifiedNames = {
            SyntaxFactory.ParseName("System"),
            SyntaxFactory.ParseName("System.Linq"),
            SyntaxFactory.ParseName("System.Collections"),
            SyntaxFactory.ParseName("System.Collections.Generic"),
            SyntaxFactory.ParseName("System.Runtime")
        };
        // creating compilation options
        CSharpCompilationOptions options = new(
            OutputKind.DynamicallyLinkedLibrary, 
            checkOverflow: true, 
            optimizationLevel: OptimizationLevel.Release, 
            allowUnsafe: false
        );

        // creating parse options
        CSharpParseOptions parseOptions = new(LanguageVersion.Preview, preprocessorSymbols: preprocessorSymbols);

        // creating emit options
        EmitOptions emitOptions = new(debugInformationFormat: DebugInformationFormat.PortablePdb);

        // convert string of dll into MetadataReference
        IEnumerable<MetadataReference> defaultReferences = Directory.GetFiles(dllfolder, "*.dll")
            .Select(x => MetadataReference.CreateFromFile(x));
        
        try
        {
            defaultReferences = defaultReferences
                .Concat(neededType.Select(x => MetadataReference.CreateFromFile(x.Assembly.Location)))
                .Concat(GetSystemMetadataReferences());
        }
        catch(Exception ex)
        {
            if (ex is ArgumentNullException || ex is ArgumentException)
            {
                Log.Error(ex, "Something went wrong");
            }
            else if (ex is IOException)
            {
                Log.Error(ex, "Error with the dlls, check if they are correctly referenced.");
            }
            else
            {
                Log.Error(ex, "Unexpected error");
            }
            throw;
        }

        IEnumerable<SyntaxTree> src = files.Select(f => SyntaxFactory.ParseSyntaxTree(File.ReadAllText(f), parseOptions, f, Encoding.UTF8));
        // update tree before compilation to add needed using
        src = src.Select(tree => (tree.GetRoot() as CompilationUnitSyntax)!
            .AddUsings(qualifiedNames.Select(qualifiedNameSpace => SyntaxFactory.UsingDirective(qualifiedNameSpace)).ToArray()).NormalizeWhitespace().SyntaxTree);

        Log.Information("Compilation: Writting ast...");
        CSharpCompilation comp = CSharpCompilation.Create(name, src, defaultReferences, options);

        Log.Information("Compilation: used Assemblies...");
        foreach(string? assemblyReferencesDisplay in comp.GetUsedAssemblyReferences().Select(usedAssemblyReferences => usedAssemblyReferences.Display))
        {
            if (assemblyReferencesDisplay != null)
            {
                FileVersionInfo fileVersionInfo = FileVersionInfo.GetVersionInfo(assemblyReferencesDisplay);
                Log.Information("{{{0}}} {{{1}}} {{{2}}}", assemblyReferencesDisplay, fileVersionInfo.FileVersion, fileVersionInfo.ProductName);
            }
            else
            {
                Log.Error("Cannot find the assembly");
            }
        }

        foreach(SyntaxTree tree in comp.SyntaxTrees)
        {
            // get the semantic model for this tree
            SemanticModel model = comp.GetSemanticModel(tree);
            
            // find everywhere in the AST that refers to a type
            SyntaxNode root = tree.GetRoot();
            IEnumerable<TypeSyntax> allTypeNames = root.DescendantNodesAndSelf().OfType<TypeSyntax>();
            
            foreach(TypeSyntax typeName in allTypeNames)
            {
                // what does roslyn think the type _name_ actually refers to?
                Microsoft.CodeAnalysis.TypeInfo effectiveType = model.GetTypeInfo(typeName);
                if(effectiveType.Type != null && effectiveType.Type.TypeKind == TypeKind.Error)
                {
                    // if it's an error type (ie. couldn't be resolved), cast and proceed
                    Log.Error("Cannot understand type {0} of variable {1}", (IErrorTypeSymbol)effectiveType.Type, typeName);
                }
            }
        }

        using MemoryStream peStream = new();
        using MemoryStream pdbStream = new();

        EmitResult results = comp.Emit(peStream, pdbStream, options: emitOptions);

        code = peStream.ToArray();
        pdb = pdbStream.ToArray();

        return results.Diagnostics.ToArray();
    }
    public static bool CompileMod(string name, string path, string dllfolder, Type[] neededType, out byte[] code, out byte[] pdb)
    {
        IEnumerable<string> files = Directory
            .GetFiles(path, "*.cs", SearchOption.AllDirectories)
            .Where(file => !IgnoreCompletely(path, file));
        
        Log.Information("Compilation: Gathering files...");

        Diagnostic[] result = Array.Empty<Diagnostic>();
        try
        {
            result = RoslynCompile(name, files, new[] { "FNA" }, dllfolder, neededType, out code, out pdb);
        }
        catch
        {
            throw;
        }

        Log.Information("Compilation: Gathering results...");
        
        foreach(Diagnostic err in result.Where(e => e.Severity == DiagnosticSeverity.Error))
        {
            Log.Error(err.ToString());
        }
        foreach(Diagnostic warning in result.Where(e => e.Severity == DiagnosticSeverity.Warning))
        {
            Log.Warning(warning.ToString());
        }
        foreach(Diagnostic info in result.Where(e => e.Severity == DiagnosticSeverity.Info))
        {
            Log.Information(info.ToString());
        }

        if(Array.Exists(result, e => e.Severity == DiagnosticSeverity.Error))
        {
            return false;
        }
        return true;
    }
    public static bool IgnoreCompletely(string root, string file)
    {
        string path_from_root = file.Replace(root + Path.DirectorySeparatorChar.ToString(), "");
        string[] dirs = path_from_root.Split(Path.DirectorySeparatorChar.ToString()); 
        
        string dir = "";
        if (dirs.Length > 1) // not only a file, but also folders
        {
            dir = dirs[0]; // topmost directory
        }
        return dir.StartsWith('.') || dir == "bin" || dir == "obj";
    }
}

