# Human review: Broiler.Graphics

> **Status: APPROVED WITH CONDITIONS for first-preview use.**

This document records the human preview-review decision for Broiler.Graphics. It is a
scoped engineering review for the first preview, not a warranty and not a claim that the
component is free of defects or vulnerabilities.

## Review target

- **Component:** Broiler.Graphics
- **Scope:** Platform-neutral graphics abstractions, managed bitmap/raster/canvas
  implementation, managed image codecs (PNG/APNG, JPEG, BMP), Windows Direct2D backend,
  Windows window/input integration, demos, and tests.
- **Release:** First preview
- **Reviewed revision:** `6cd86dd7d148e0a208d16229a325adbdbd901c1b`
- **Reviewer:** MaiRat / Maik Ratzmer
- **Reviewer contact or profile:** MaiRat
- **Review date:** 2026-07-01
- **Intended preview use:** First-preview evaluation of Broiler.Graphics as a graphics
  abstraction and rendering implementation. This approval does not cover production use
  or security-sensitive processing of untrusted images without additional hardening,
  threat modeling, fuzzing, and security review.

Any source change after the reviewed revision requires renewed review before the approval
can be applied to the changed revision.

## Summary

Broiler.Graphics is fundamentally acceptable as a first preview. The project mainly
contains graphics abstractions and implementations, including a managed raster core,
image codec support, and a Windows Direct2D backend.

The global refactoring is not complete yet. As a result, the code should not be treated
as fully optimized, fully stable, or final from an API, architecture, performance, or
maintenance perspective.

The approval is conditional because security-critical issues may still exist. The project
implements codecs for complex binary image formats (PNG/APNG, JPEG, and BMP), and those
code paths may process untrusted input. The Windows backend also uses native Windows
system APIs and graphics APIs. Vulnerabilities in those APIs, or incorrect and
security-relevant usage of those APIs by this component, cannot be ruled out completely
by this preview review.

## Required warnings for the first preview

The first preview must include clear security and stability warnings:

- The component is preview software and is not fully optimized or final.
- Public APIs and behavior may change while the global refactoring continues.
- Managed image codecs parse complex binary input and must be treated as
  security-sensitive.
- PNG/APNG, JPEG, and BMP decoding should not be used on untrusted input in
  security-sensitive environments without sandboxing, resource limits, fuzzing, and
  additional review.
- The Windows backend uses native Windows APIs and Direct2D/DirectWrite/DXGI/D3D interop.
  Correct and security-relevant usage of all involved Windows APIs is not guaranteed by
  this first-preview review.
- This review is not a production security audit.

## Evidence and commands

- `dotnet test Broiler.Graphics.sln`
  - Completed with exit code 0 on 2026-07-01.
  - The solution command restored projects successfully but did not emit individual
    test-case details because the repository uses console-runner test projects rather
    than test-framework adapter projects.
- `dotnet run --project Broiler.Graphics.Tests\Broiler.Graphics.Tests.csproj`
  - Completed with exit code 0 on 2026-07-01.
  - Result: 76/76 tests passed, 0 failed.
- `dotnet run --project Broiler.Graphics.Windows.Tests\Broiler.Graphics.Windows.Tests.csproj`
  - Completed with exit code 0 on 2026-07-01.
  - Result: 9/9 backend tests passed, 0 failed.

## Review coverage

- [x] Build and automated test commands were run for the first-preview review.
- [x] Graphics abstractions and implementations were considered within the review scope.
- [x] Security-sensitive areas were identified: image codecs, untrusted binary input,
      native Windows API usage, Direct2D/DirectWrite/DXGI/D3D interop, windowing, and
      input handling.
- [x] Public preview limitations were assessed: unfinished global refactoring, unstable
      APIs, incomplete optimization, and incomplete production hardening.
- [x] Dependency and license status was checked at preview level. The repository includes
      an Apache-2.0 license. Any third-party material, if present, must retain its own
      notices.
- [x] Static analysis, dependency/vulnerability scanning, fuzzing, and an independent
      security audit are not recorded for this preview. Their absence is accepted only
      for first-preview use and remains a condition before broader use.
- [x] Residual risks and required warnings are listed in this document.

## Findings and residual risks

- **Conditional approval only:** Broiler.Graphics is acceptable as a first preview, but
  not as a production-ready or security-audited component.
- **Refactoring incomplete:** The global refactoring is still in progress, so the current
  implementation may contain avoidable duplication, non-final architecture, unstable APIs,
  or performance issues.
- **Image codec risk:** PNG/APNG, JPEG, and BMP parsing is security-sensitive. Malformed
  or hostile image data may expose parsing bugs, excessive resource use, denial-of-service
  behavior, or other vulnerabilities.
- **Windows API risk:** The Windows backend uses native Windows APIs and graphics
  interop. Preview approval does not guarantee that every API is used correctly or safely
  in all edge cases.
- **No complete security audit:** No dedicated fuzzing campaign, SAST report,
  dependency/vulnerability scan, or independent security review is recorded here.
- **Preview documentation required:** Release notes, README text, or package metadata
  must preserve the security and preview warnings above.

## Conditions for preview approval

1. Broiler.Graphics may be published or used only as a first preview under the scope
   stated in this document.
2. Security and stability warnings must accompany the preview.
3. The component must not be represented as production-ready, security-audited, or free
   of vulnerabilities.
4. Untrusted image input must be handled with caution. Security-sensitive use requires
   additional sandboxing, resource limits, fuzzing, and review.
5. Broader release use requires follow-up review after the global refactoring and after
   additional security work for the codecs and Windows interop.
6. Source changes after the reviewed revision require renewed review before this approval
   can be applied to the changed code.

## Decision

- [ ] **APPROVED FOR PREVIEW** without additional conditions.
- [x] **APPROVED WITH CONDITIONS** listed above.
- [ ] **NOT APPROVED** for preview use.

**Conditions:** See "Conditions for preview approval" and "Required warnings for the
first preview".

## Human attestation

I confirm that I am a human developer, that I personally reviewed the revision and
evidence identified above, and that the decision is my own. I understand that this
attestation is a scoped engineering review, not a warranty or a claim that the component
is free of defects or vulnerabilities.

- **Name:** Maik Ratzmer
- **Alias/profile:** MaiRat
- **Signature or attributable identity:** MaiRat / Maik Ratzmer
- **Date:** 2026-07-01

AI tooling may assist with formatting this review document, but the review decision and
reviewer identity are attributed to the human reviewer named above.
