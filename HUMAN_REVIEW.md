# Human review: Broiler.Graphics

> **Status: PENDING — not approved for preview use.**

Broiler.Graphics contains substantial AI-assisted implementation. This record exists so that a
real developer can review a specific revision and make an attributable, evidence-based
decision. Until the decision and attestation below are completed by a human, this file is
not a safety approval.

“Safe” is not an absolute guarantee. Approval means only that the named reviewer found the
specified revision reasonably suitable for the stated preview use, subject to the recorded
limitations and the software license's warranty disclaimer.

## Review target

- **Component:** Broiler.Graphics
- **Scope:** The managed bitmap/codec/raster core, Direct2D backend, input hooks, demos, and tests.
- **Release:** First preview
- **Commit:** `<full reviewed commit SHA — required>`
- **Reviewer:** `<human name — required>`
- **Reviewer contact or profile:** `<required>`
- **Review date:** `<YYYY-MM-DD — required>`
- **Intended preview use:** `<required>`

Any source change after the reviewed commit invalidates an approval until the changed
revision is reviewed again.

## Required evidence

The human reviewer records links, logs, or concise findings for every item:

- [ ] Build and automated tests completed; minimum expected commands: `dotnet test Broiler.Graphics.sln`.
- [ ] Security-sensitive inputs, trust boundaries, file/network access, native interop,
      and code-execution paths were inspected where applicable.
- [ ] Dependency and license notices were checked, including inherited upstream code.
- [ ] AI-generated or AI-modified code received source-level review; no AI summary was
      accepted as a substitute for reading the relevant code.
- [ ] Public APIs, failure behavior, known limitations, and preview compatibility risks
      were assessed.
- [ ] Static analysis, dependency/vulnerability scanning, or an explicit reason for
      omitting each was recorded.
- [ ] Open findings and residual risks are listed below.

### Evidence and commands

`<human reviewer: add exact commands, results, CI links, test reports, and review notes>`

### Findings and residual risks

`<human reviewer: list findings, severity, mitigations, accepted risks, and follow-up issues>`

## Decision

Select exactly one and replace the status at the top to match:

- [ ] **APPROVED FOR PREVIEW** within the intended-use scope above.
- [ ] **APPROVED WITH CONDITIONS** listed below.
- [ ] **NOT APPROVED** for preview use.

**Conditions:** `<required when approval is conditional; otherwise “None”>`

## Human attestation

I confirm that I am a human developer, that I personally reviewed the revision and
evidence identified above, and that the decision is my own. I understand that this
attestation is a scoped engineering review, not a warranty or a claim that the component
is free of defects or vulnerabilities.

- **Name:** `<required>`
- **Signature or attributable commit:** `<required>`
- **Date:** `<required>`

AI tools may help assemble evidence, but must not fill in the reviewer identity, select the
decision, sign the attestation, or change **PENDING** to an approval.

