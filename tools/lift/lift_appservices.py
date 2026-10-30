#!/usr/bin/env python3
"""Replaces the sources of src/WebFormsForCore.Web.ApplicationServices (System.Web.ApplicationServices), which
WebFormsForCore decompiled from .NET Framework's assembly, with their MIT-licensed originals from the .NET Framework
Reference Source (microsoft/referencesource, System.Web.ApplicationServices), so that no decompiled code remains
(docs/LICENSING.md).

Usage: lift_appservices.py <referencesource checkout> [ForkRoot]

Every Reference Source file of the assembly is copied to the same path in the project, byte for byte except FIXUPS.
Properties/AssemblyInfo.cs stays the fork's (its InternalsVisibleTo names the keys webForCore's System.Web carries).
The per-file source is written to tools/lift/manifests/System.Web.ApplicationServices.tsv.
"""
import os
import subprocess
import sys

COMMIT = "ec9fa9ae770d522a5b5f0607898044b7478574a3"
PROJECT = "src/WebFormsForCore.Web.ApplicationServices"
SOURCE = "System.Web.ApplicationServices"

# Decompiled build constants (unused once the originals are in) and the decompiled resource accessor, which the
# Reference Source's ApplicationServicesStrings.Designer.cs replaces.
REMOVE = ["AssemblyRef.cs", "FXAssembly.cs", "ThisAssembly.cs", "ApplicationServicesStrings.cs"]

KEEP_FORK = {"Properties/AssemblyInfo.cs"}

FIXUPS = {
    # .NET Remoting's IObjectHandle does not exist on .NET; System.Web's ProcessHost takes the ObjectHandle class.
    "Hosting/CustomLoaderHelper.cs": [
        ("internal static IObjectHandle GetCustomLoader(", "internal static ObjectHandle GetCustomLoader( // webForCore: no IObjectHandle on .NET\n            ", 1),
        ('            // Step 2: Create the new AD\n\n            string binFolderPhysicalPath = helperFunctions.MapPath("/bin/");\n\n            AppDomainSetup setup = new AppDomainSetup() {\n                PrivateBinPathProbe = "*",  // disable loading from app base\n                PrivateBinPath = binFolderPhysicalPath,\n                ApplicationBase = helperFunctions.AppPhysicalPath,\n                TargetFrameworkName = _customLoaderTargetFrameworkName\n            };\n\n            if (configFilePath != null) {\n                setup.ConfigurationFile = configFilePath;\n            }\n\n            AppDomain newAppDomainForCustomLoader = AppDomain.CreateDomain("aspnet-custom-loader-" + Guid.NewGuid(), null, setup);\n            try {\n                // Step 3: Instantiate helper in new AD so that we can get a reference to the loader\n                CustomLoaderHelper helper = (CustomLoaderHelper)newAppDomainForCustomLoader.CreateInstanceAndUnwrap(_customLoaderAssemblyName, _customLoaderTypeName,\n                    ignoreCase: false,\n                    bindingAttr: BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.CreateInstance | BindingFlags.Instance,\n                    binder: null,\n                    args: null,\n                    culture: null,\n                    activationAttributes: null);\n                ObjectHandle ohCustomLoader = helper.GetCustomLoaderImpl(customLoaderPhysicalPath);\n\n                // If we got this far, success!\n                newlyCreatedAppDomain = newAppDomainForCustomLoader;\n                return ohCustomLoader;\n            }\n            catch {\n                // If something went wrong, kill the new AD.\n                AppDomain.Unload(newAppDomainForCustomLoader);\n                throw;\n            }\n',
         '            // webForCore: .NET cannot create AppDomains (AppDomain.CreateDomain, AppDomainSetup), so the loader is\n            // instantiated in the current one, which the hosting layer already isolates (PseudoAppDomain).\n            newlyCreatedAppDomain = AppDomain.CurrentDomain;\n            return new CustomLoaderHelper().GetCustomLoaderImpl(customLoaderPhysicalPath);\n', 1),
    ],
    # The fork's System.Web is found by its simple name; the full .NET Framework identity names version 4.0.0.0.
    "Util/SystemWebProxy.cs": [
        ('Type.GetType("System.Web.Security.MembershipAdapter, " + AssemblyRef.SystemWeb, throwOnError:false);',
         'Type.GetType("System.Web.Security.MembershipAdapter, System.Web", throwOnError:false); // webForCore: the fork\'s System.Web', 1),
    ],
}


def main():
    rs = sys.argv[1]
    fork = sys.argv[2] if len(sys.argv) > 2 else os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    project = os.path.join(fork, PROJECT)
    for rel in REMOVE:
        if os.path.exists(os.path.join(project, rel)):
            os.remove(os.path.join(project, rel))
    names = subprocess.run(["git", "-C", rs, "ls-tree", "-r", "--name-only", COMMIT, SOURCE],
                           capture_output=True, check=True, text=True).stdout.split()
    manifest = [f"# microsoft/referencesource {COMMIT[:7]}: source path (relative to repo root)\tdestination (relative to fork root)"]
    for src in names:
        rel = src[len(SOURCE) + 1:]
        if rel in KEEP_FORK:
            continue
        data = subprocess.run(["git", "-C", rs, "show", f"{COMMIT}:{src}"], capture_output=True, check=True).stdout
        fixups = FIXUPS.get(rel, [])
        if fixups:
            bom = data.startswith(b"\xef\xbb\xbf")
            text = data.decode("utf-8-sig")
            crlf = "\r\n" in text
            text = text.replace("\r\n", "\n")
            for old, new, count in fixups:
                found = text.count(old)
                if found != count:
                    sys.exit(f"{rel}: fixup matched {found} times, expected {count}: {old!r}")
                text = text.replace(old, new)
            if crlf:
                text = text.replace("\n", "\r\n")
            data = (b"\xef\xbb\xbf" if bom else b"") + text.encode("utf-8")
        dest = os.path.join(project, rel)
        os.makedirs(os.path.dirname(dest), exist_ok=True)
        with open(dest, "wb") as f:
            f.write(data)
        manifest.append(f"{src}\t{PROJECT}/{rel}")
    with open(os.path.join(fork, "tools", "lift", "manifests", "System.Web.ApplicationServices.tsv"), "w", encoding="utf-8") as f:
        f.write("\n".join(manifest) + "\n")
    print(f"{len(REMOVE)} decompiled files removed, {len(manifest) - 1} Reference Source files copied")


if __name__ == "__main__":
    main()
