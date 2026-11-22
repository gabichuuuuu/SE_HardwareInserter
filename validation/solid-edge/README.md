# Solid Edge acceptance matrix

This directory intentionally contains no fabricated results. Production release requires one real
result named `SE2022.json` through `SE2026.json`, generated with:

```powershell
.\dev\New-SolidEdgeAcceptanceResult.ps1 -SolidEdgeVersion 2022 -InstallerPath <ci-candidate> -SourceCommitSidecarPath <source-commit.txt> -TestLibraryPath <private-par-corpus> -OutputPath <outside-repository>\SE2022.json
```

CI publishes `solid-edge-acceptance-candidate-<GITHUB_SHA>` with an unsigned installer and
`source-commit.txt`. The unsigned candidate is allowed only for this matrix: release rebuilds the
same locked source, signs the final binary (therefore changing its SHA-256) and runs the signed
installer smoke. All five matrix entries must reference the same candidate installer SHA-256.

Repeat on Solid Edge 2022, 2023, 2024, 2025 and 2026 from clean checkouts with no untracked files.
The private `.par` files are never committed; each JSON contains only relative names and hashes.
`solidEdgeProductVersion` is the dotted numeric value observed from `Edge.exe`. Record it without
inventing an equivalence between product-version numbers and marketing years.

## Evidence commit convention

Run all five validations on one clean source commit. Each JSON records that commit as
`sourceCommitSha` and `commitSha`. Add all five JSON files together in one exclusive evidence
commit whose parent is the validated source commit. That evidence commit must change exactly:

- `validation/solid-edge/SE2022.json`
- `validation/solid-edge/SE2023.json`
- `validation/solid-edge/SE2024.json`
- `validation/solid-edge/SE2025.json`
- `validation/solid-edge/SE2026.json`

Tag the evidence commit, not the source commit. Release verifies that `HEAD^` equals every recorded
source SHA and that `HEAD^..HEAD` contains only those five files. Since no application or build
configuration changes in the evidence commit, the released code is identical to the validated code.

The exclusive evidence commit plus branch protection and human review authenticate the matrix;
there is intentionally no repository HMAC or secret signing key.

Do not hand-edit results, include machine/user names, local paths, secrets or CAD binaries.
