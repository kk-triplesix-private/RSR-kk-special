$json = "$env:APPDATA\XIVLauncher\devPlugins\RotationSolverReborn-Dev\RotationSolver.json"
(Get-Content $json -Raw) -replace '"Name": "Rotation Solver Reborn"', '"Name": "Rotation Solver Reborn [DEV]"' | Set-Content $json -NoNewline
