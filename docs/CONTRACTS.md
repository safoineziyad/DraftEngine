# Shared Contracts

## ContractTypeDefinition
See Core/Models/ContractTypes/ContractTypeDefinition.cs and ContractTypeRegistry.cs. Canonical ids: MutualNda, OneWayNda, IndependentContractorAgreement, ConsultingAgreement. Map "Non-Disclosure Agreement (One-Way)" and "One-Way NDA" to OneWayNda.

## Schema changes (to add)
- Clause: IsUserDefined (bool), IsArchived (bool) - add via migration
- Contract: IsArchived (bool), ArchivedAt (DateTime?) - add via migration
- WizardDraft table: Id, UserId (int), PayloadEncrypted (string/blob), CreatedAt, UpdatedAt (encrypted payload)
- Seed clauses 13-42 for all types

## Interfaces
- IContractTypeRegistry (already exists as static registry; wrap if needed)
- IClauseManagementService
- IAiConsentService
- IAiRedactionService
- IAiEndpointSettings
- ILicenseService
- IUpdateCheckService
- IWizardDraftService
- IContractEditService
