# Antivirus warnings — FAQ

Some antivirus products occasionally flag RemoteNest releases as
malware. This document explains **why** it happens, **how to verify**
the downloads are authentic, and **how to report** false positives.

## Why does my antivirus warn about RemoteNest?

RemoteNest is distributed **without an Authenticode code-signing
certificate** (yet). Combined with the fact that the portable build is
a self-contained, single-file .NET bundle (~150 MB, with the .NET 8
runtime embedded), some antivirus heuristics incorrectly classify it
as suspicious.

This is a **known, industry-wide problem** affecting most unsigned
.NET single-file apps. It does not mean the binary is malicious — it
means the heuristic engine has not seen enough legitimate builds of
this specific hash yet.

RemoteNest v1.0.7 applies every known non-signing mitigation:

- Full Win32 version metadata (Company, Product, Copyright, Version)
  embedded — visible in Explorer → Properties → Details.
- Explicit `app.manifest` with `asInvoker` execution level and
  Windows 10/11 supportedOS declaration.
- Single-file compression disabled — the bundle has normal entropy
  instead of the high-entropy signature that matches malware packers.
- Embedded debug symbols (`DebugType=embedded`).
- Reproducible builds via GitHub Actions with public workflow logs.

Code signing is on the roadmap for a future release.

## How do I verify the download is authentic?

Every GitHub release publishes a `SHA256SUMS.txt` file alongside the
`RemoteNest-Setup.exe` and `RemoteNest-Portable.exe` assets. Compare
the hash of the file you downloaded:

```powershell
# Windows PowerShell
Get-FileHash .\RemoteNest-Portable.exe -Algorithm SHA256
```

The output should match the line in `SHA256SUMS.txt` from the
corresponding release page.

## How do I report a false positive to my AV vendor?

Submitting the specific file hash helps the vendor whitelist it in
their next signature update. Turnaround is typically 12–72 hours.

| Vendor | Submission URL |
|---|---|
| Microsoft Defender | https://www.microsoft.com/en-us/wdsi/filesubmission |
| Kaspersky | https://opentip.kaspersky.com/ |
| BitDefender | https://www.bitdefender.com/consumer/support/answer/29358/ |
| ESET | https://support.eset.com/en/kb141 |
| Avast / AVG | https://www.avast.com/false-positive-file-form.php |
| Malwarebytes | https://www.malwarebytes.com/false-positives/ |
| Norton | https://submit.norton.com/ |

## Why should I trust the binary at all?

- **Source is open:** https://github.com/xp3z41x/RemoteNest.Desktop
- **Every release is built by GitHub Actions** — you can inspect the
  exact workflow run that produced the artifacts, including every
  command executed and every file touched.
- **Binary metadata is complete and consistent** — right-click →
  Properties → Details shows Company, Product, Version, Copyright.
- **No network behavior beyond what an RDP manager needs:** the app
  only talks to `mstsc.exe` (Remote Desktop client) and `cmdkey.exe`
  (Windows credential manager). Both are native Windows components.

## "Suspicious" behaviors explained

Some AV engines flag based on the fact that RemoteNest:

1. **Launches `mstsc.exe`** — this is the Microsoft Remote Desktop
   client. RemoteNest is literally a Remote Desktop connection
   manager; invoking `mstsc` is its entire purpose.
2. **Uses `cmdkey.exe` to inject credentials** — this is the standard
   Windows mechanism for supplying a username/password to an RDP
   session without hardcoding it in a `.rdp` file. The credentials
   are added just before launching `mstsc` and removed 5 seconds
   later.
3. **Generates a temporary `.rdp` file** — required by `mstsc` to
   start a session. Written to `%TEMP%\RemoteNest\` and deleted
   after the connection is established.
4. **Encrypts stored passwords with DPAPI** — the Windows Data
   Protection API (`System.Security.Cryptography.ProtectedData`).
   Passwords in `%APPDATA%\RemoteNest\remotenest.db` are only
   readable by the Windows user that encrypted them.

None of these behaviors involve injection, privilege escalation, or
persistence beyond the app's own directory.

## Still worried?

Do not install — that's completely reasonable. The source is on
GitHub; you can build from source with `dotnet build` and `dotnet
publish`, inspect every line of the code, and run the locally-built
binary. The result will be bit-identical to the workflow output
(modulo timestamps) thanks to `Deterministic=true` in the build.
