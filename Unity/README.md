# R.A.T. UI asset-bundle source

`Assets/Editor/UseItemsAnywhereQuickUseWheelBuilder.cs` is the source for both
`quickusewheel` and `itemusedelaytimer`. It generates the prefabs and supporting
UI sprites, then builds self-contained Windows asset bundles.

The project currently targets Unity `2022.3.43f1`. Copy the builder into the
same relative path in a Unity project that has UGUI and TextMesh Pro installed,
then run:

```powershell
& 'C:\Path\To\Unity.exe' `
  -batchmode `
  -quit `
  -projectPath 'C:\Path\To\UnityProject' `
  -executeMethod UseItemsAnywhereQuickUseWheelBuilder.Build `
  -quickUseWheelOutput 'C:\Path\To\UseItemsAnywhere\UseItemsAnywhere\Resources' `
  -logFile 'C:\Path\To\UseItemsAnywhere\unity-wheel-build.log'
```

The output directory also receives Unity manifest files and a root manifest
bundle. Only the two extensionless bundles named above are distributed by the
mod.

## License

R.A.T. is licensed under the Creative Commons Attribution-NonCommercial-
NoDerivatives 4.0 International Public License. See the repository's `LICENSE`
file for the license notice and canonical legal-code link.
