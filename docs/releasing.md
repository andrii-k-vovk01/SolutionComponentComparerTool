# Releasing

Maintainer checklist for publishing a new version.

## 1. Prepare the version (on `develop`)

The version follows the XrmToolBox convention `1.YYYY.M.N`. Update it in all three places:

1. `src/SolutionComponentComparer/Properties/AssemblyInfo.cs` (`AssemblyVersion` and `AssemblyFileVersion`)
2. `SolutionComponentComparer.nuspec` (`<version>` and `<releaseNotes>`)
3. `CHANGELOG.md` (new section at the top)

The package version must match the assembly version, otherwise XrmToolBox always reports an available update.

Build and test in XrmToolBox:

```
.\build.cmd
```

Commit and push `develop`.

## 2. Merge to `main`

Open a pull request from `develop` to `main` on GitHub and merge it.

## 3. Tag and GitHub release

```
git switch main
git pull
git tag v1.YYYY.M.N
git push origin v1.YYYY.M.N
git switch develop
```

On GitHub: **Releases > Draft a new release**, pick the tag, paste the CHANGELOG section as notes, attach `dist\SolutionComponentComparer.dll` and publish.

## 4. NuGet package

```
.\pack.cmd
```

The package is written to `artifacts\`. It must contain only `lib\net48\Plugins\SolutionComponentComparer.dll` and `images\icon.png` (open the `.nupkg` as a zip file to check).

Publish it:

```
dotnet nuget push artifacts\AndriiK.XrmToolBox.SolutionComponentComparer.1.YYYY.M.N.nupkg --api-key <key> --source https://api.nuget.org/v3/index.json
```

Use an API key scoped to `AndriiK.XrmToolBox.*` with push permission only. Never commit the key.

## 5. XrmToolBox Tool Library

First release only: once nuget.org has validated and indexed the package, register the package id `AndriiK.XrmToolBox.SolutionComponentComparer` on the XrmToolBox portal. An administrator reviews the tool before it appears in the Tool Library.

Later releases only need step 4. XrmToolBox offers the update to users automatically.
