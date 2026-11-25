# Launches Valheim, loads the first world with the first character, takes sky screenshots and quits.
# The mod's Weather and Time of Day settings are set first, so each run shows a chosen sky.
#   tools\skyshots.ps1 -Weather Clear -Time Noon -Out <folder> [-Name noon_clear]
# Writes <Name>_0..4.png (four directions about 30 degrees up, then straight up) and <Name>.jpg (all five tiled).
param(
    [string]$Weather = "Natural",
    [string]$Time = "Natural",
    [Parameter(Mandatory = $true)][string]$Out,
    [string]$Name = "",
    [int]$Settle = 25
)
$ErrorActionPreference = "Stop"
$game = "D:\SteamLibrary\steamapps\common\Valheim"
$cfg = Join-Path $game "BepInEx\config\MagicMike.ValheimSkies.cfg"
$input = Join-Path $PSScriptRoot "gameinput.ps1"
if ($Name -eq "") { $Name = ($Time + "_" + $Weather).ToLowerInvariant() -replace '[^a-z0-9_]', '_' }
New-Item -ItemType Directory -Force $Out | Out-Null

if (Get-Process valheim -ErrorAction SilentlyContinue) { throw "Valheim is already running" }

# Set Weather and Time of Day in the [.. - Weather] section (added if missing)
function Set-Setting([string]$key, [string]$value) {
    $lines = [System.Collections.Generic.List[string]](Get-Content $cfg)
    $index = $lines.FindIndex({ param($l) $l -match "^$([regex]::Escape($key)) = " })
    if ($index -ge 0) { $lines[$index] = "$key = $value" }
    else {
        $section = $lines.FindIndex({ param($l) $l -match '^\[\d+ - Weather\]' })
        if ($section -lt 0) { $lines.Add(""); $lines.Add("[4 - Weather]"); $section = $lines.Count - 1 }
        $lines.Insert($section + 1, "$key = $value")
    }
    Set-Content $cfg $lines
}
Set-Setting "Weather" $Weather
Set-Setting "Time of Day" $Time

Start-Process "steam://rungameid/892970"
$t = 0
while ($t -lt 180) { $p = Get-Process valheim -ErrorAction SilentlyContinue; if ($p -and $p.MainWindowHandle -ne 0) { break }; Start-Sleep 3; $t += 3 }
Start-Sleep 35
& $input click 960 780 | Out-Null; Start-Sleep 3     # Start Game
& $input click 960 1037 | Out-Null; Start-Sleep 3    # character: Start
& $input click 620 819 | Out-Null; Start-Sleep 55    # world: Start
& $input look 0 -12 -Out 25 | Out-Null               # about 30 degrees up
Start-Sleep $Settle

for ($i = 0; $i -lt 4; $i++) {
    Start-Sleep 2
    & $input shot -Out (Join-Path $Out "$($Name)_$i.png") | Out-Null
    & $input look 40 0 -Out 20 | Out-Null
}
& $input look 0 -25 -Out 25 | Out-Null; Start-Sleep 2
& $input shot -Out (Join-Path $Out "$($Name)_4.png") | Out-Null

$p = Get-Process valheim -ErrorAction SilentlyContinue
if ($p) { $p.CloseMainWindow() | Out-Null; $t = 0; while (-not $p.HasExited -and $t -lt 60) { Start-Sleep 2; $t += 2; $p.Refresh() } }

Add-Type -AssemblyName System.Drawing
$tile = New-Object System.Drawing.Bitmap 1920, 1620
$g = [System.Drawing.Graphics]::FromImage($tile)
for ($i = 0; $i -lt 5; $i++) {
    $im = [System.Drawing.Image]::FromFile((Join-Path $Out "$($Name)_$i.png"))
    $g.DrawImage($im, ($i % 2) * 960, [Math]::Floor($i / 2) * 540, 960, 540); $im.Dispose()
}
$tile.Save((Join-Path $Out "$Name.jpg"), [System.Drawing.Imaging.ImageFormat]::Jpeg)
Join-Path $Out "$Name.jpg"
