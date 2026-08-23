# Baseline Governance

## Baseline Roles
- Product / Requirement Baseline: approved feature requirements, scenarios, acceptance, non-goals, and delivery constraints.
- Architecture / Runtime Boundary Baseline: canonical owners, contracts, source-of-truth boundaries, compatibility, and retirement state.

## Alignment Protocol
Before non-trivial changes, compare the approved requirement against the current owner and contract boundaries. Report requirement or architecture drift explicitly.

## Compatibility Boundary
Existing monitor keys, persisted settings, sensor collection behavior, and user data files must remain compatible unless the approved feature requires an additive change.
