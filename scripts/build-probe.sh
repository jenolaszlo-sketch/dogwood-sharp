#!/bin/bash
set -euo pipefail

probeProj="docs/reviews/d3-impl-01-r5-r3/evidence/differential/probe/R5R1Probe.csproj"
dotnet restore "$probeProj" --locked-mode
dotnet build "$probeProj" -c Release -f "${TFM}" --no-restore

probeDll="docs/reviews/d3-impl-01-r5-r3/evidence/differential/probe/bin/Release/${TFM}/PrepProbe.dll"
if [ ! -f "$probeDll" ]; then echo "Probe not built at $probeDll"; exit 1; fi
echo "Probe built: $probeDll"