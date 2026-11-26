# Builds the valheim_physical_sky bundle: copies the shaders, the moon picture and the builder from unity/PhysicalSky into the Unity
# project, runs the builder headless (the project must not be open in the editor), and shows the log's result.
$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$project = "E:\Unity_Projects\ValhiemSkies"
$unity = "D:\Program Files\Unity\Hub\Editor\6000.0.76f1\Editor\Unity.exe"
$folder = Join-Path $project "Assets\ValheimSkies\PhysicalSky"
New-Item -ItemType Directory -Force $folder | Out-Null
Copy-Item -Force (Join-Path $repo "unity\PhysicalSky\PhysicalSky.shader") $folder
Copy-Item -Force (Join-Path $repo "unity\PhysicalSky\Moon.shader") $folder
Copy-Item -Force (Join-Path $repo "unity\PhysicalSky\moon.png") $folder
Copy-Item -Force (Join-Path $repo "unity\PhysicalSky\PhysicalSkyBundleBuilder.cs") (Join-Path $project "Assets\Editor")
$log = Join-Path $env:TEMP "valheim_physical_sky_build.log"
& $unity -batchmode -quit -nographics -projectPath $project -executeMethod PhysicalSkyBundleBuilder.Build -logFile $log | Out-Null
Select-String -Path $log -Pattern "Built valheim_physical_sky|error|Exception" | Select-Object -First 15 | ForEach-Object { $_.Line }
if ($LASTEXITCODE -ne 0) { throw "Unity exited with $LASTEXITCODE (log: $log)" }
