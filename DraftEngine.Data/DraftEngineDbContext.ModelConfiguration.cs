using DraftEngine.Core.Models;
using DraftEngine.Core.Models.ContractTypes;
using Microsoft.EntityFrameworkCore;

namespace DraftEngine.Data;

/// <summary>
/// Fluent configuration (relationships, max lengths, indexes and seed data)
/// for <see cref="DraftEngineDbContext"/>, kept in a partial class so the
/// context itself stays readable.
/// </summary>
public partial class DraftEngineDbContext
{
    private static void ConfigureRelationships(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(user => user.Id);

            entity.Property(user => user.Email).HasMaxLength(256).IsRequired();
            entity.Property(user => user.DisplayName).HasMaxLength(200).IsRequired();
            entity.Property(user => user.OpenAiApiKeyEncrypted).HasMaxLength(1024).IsRequired();
            entity.Property(user => user.SubscriptionTier).HasMaxLength(50).IsRequired();

            entity.HasIndex(user => user.Email).IsUnique();

            // A user owns their contracts: removing the user removes everything they created.
            entity.HasMany(user => user.Contracts)
                .WithOne(contract => contract.User)
                .HasForeignKey(contract => contract.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Contract>(entity =>
        {
            entity.ToTable("Contracts");
            entity.HasKey(contract => contract.Id);

            entity.Property(contract => contract.Title).HasMaxLength(300).IsRequired();
            entity.Property(contract => contract.ContractType).HasMaxLength(100).IsRequired();
            entity.Property(contract => contract.ClientName).HasMaxLength(200).IsRequired();
            entity.Property(contract => contract.ClientEmail).HasMaxLength(256).IsRequired();
            entity.Property(contract => contract.GoverningLaw).HasMaxLength(200).IsRequired();
            entity.Property(contract => contract.FinalFilePath).HasMaxLength(1024).IsRequired();
            entity.Property(contract => contract.PartiesJson).IsRequired();
            entity.Property(contract => contract.AssembledText).IsRequired();
            entity.Property(contract => contract.IsDeleted).HasDefaultValue(false);
            entity.Property(contract => contract.IsArchived).HasDefaultValue(false);
            // PartiesJson, AssembledText: unbounded JSON/free text, deliberately no max length.

            entity.HasIndex(contract => contract.Title);
            entity.HasIndex(contract => contract.ContractType);
            entity.HasIndex(contract => contract.CreatedAt);
            entity.HasIndex(contract => contract.IsDeleted);

            // Dependent owned data is removed with the contract.
            entity.HasMany(contract => contract.ContractClauses)
                .WithOne(clause => clause.Contract)
                .HasForeignKey(clause => clause.ContractId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(contract => contract.ContractFields)
                .WithOne(field => field.Contract)
                .HasForeignKey(field => field.ContractId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(contract => contract.ChatMessages)
                .WithOne(message => message.Contract)
                .HasForeignKey(message => message.ContractId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(contract => contract.GeneratedDocuments)
                .WithOne(document => document.Contract)
                .HasForeignKey(document => document.ContractId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Clause>(entity =>
        {
            entity.ToTable("Clauses");
            entity.HasKey(clause => clause.Id);

            entity.Property(clause => clause.ContractType).HasMaxLength(100).IsRequired();
            entity.Property(clause => clause.Category).HasMaxLength(150).IsRequired();
            entity.Property(clause => clause.Title).HasMaxLength(300).IsRequired();
            entity.Property(clause => clause.BodyTemplate).IsRequired();
            entity.Property(clause => clause.Source).HasMaxLength(50).IsRequired();
            entity.Property(clause => clause.IsArchived).HasDefaultValue(false);
            entity.Property(clause => clause.IsUserDefined).HasDefaultValue(false);
            // BodyTemplate: unbounded template text, deliberately no max length.

            entity.HasIndex(clause => clause.ContractType);
            entity.HasIndex(clause => clause.SortOrder);
            entity.HasIndex(clause => clause.OwnerUserId);

            entity.HasOne(clause => clause.OwnerUser)
                .WithMany()
                .HasForeignKey(clause => clause.OwnerUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ContractClause>(entity =>
        {
            entity.ToTable("ContractClauses");
            entity.HasKey(clause => clause.Id);

            entity.Property(clause => clause.CustomBodyOverride).IsRequired();
            // CustomBodyOverride: unbounded override text, deliberately no max length.

            entity.HasIndex(clause => clause.ContractId);

            // Restrict rather than Cascade: clause templates are shared reference data
            // (seeded), while ContractClause rows are the historical record of what was
            // selected for each contract. Deleting a template must not silently rewrite
            // existing contracts, so the referenced clause cannot be deleted while in use.
            entity.HasOne(clause => clause.Clause)
                .WithMany(template => template.ContractClauses)
                .HasForeignKey(clause => clause.ClauseId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ContractField>(entity =>
        {
            entity.ToTable("ContractFields");
            entity.HasKey(field => field.Id);

            entity.Property(field => field.Key).HasMaxLength(150).IsRequired();
            entity.Property(field => field.Value).IsRequired();
            // Value: unbounded answer text, deliberately no max length.
        });

        modelBuilder.Entity<ChatMessage>(entity =>
        {
            entity.ToTable("ChatMessages");
            entity.HasKey(message => message.Id);

            entity.Property(message => message.Content).IsRequired();
            // Content: unbounded message text, deliberately no max length.

            entity.HasIndex(message => message.ContractId);
        });

        modelBuilder.Entity<GeneratedDocument>(entity =>
        {
            entity.ToTable("GeneratedDocuments");
            entity.HasKey(document => document.Id);

            entity.Property(document => document.FilePath).HasMaxLength(1024).IsRequired();
        });

        modelBuilder.Entity<ContractVersion>(entity =>
        {
            entity.ToTable("ContractVersions");
            entity.HasKey(version => version.Id);

            entity.Property(version => version.Reason).HasMaxLength(100).IsRequired();
            entity.Property(version => version.Text).IsRequired();
            // Text: unbounded snapshot text, deliberately no max length.

            entity.HasIndex(version => version.ContractId);
            entity.HasIndex(version => new { version.ContractId, version.VersionNumber }).IsUnique();

            // Snapshots are owned history: removing the contract removes them too.
            entity.HasOne(version => version.Contract)
                .WithMany(contract => contract.Versions)
                .HasForeignKey(version => version.ContractId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WizardDraft>(entity =>
        {
            entity.ToTable("WizardDrafts");
            entity.HasKey(draft => draft.DraftKey);

            entity.Property(draft => draft.DraftKey).HasMaxLength(256).IsRequired();
            entity.Property(draft => draft.EncryptedPayload).IsRequired();
            entity.Property(draft => draft.UpdatedAt).IsRequired();
        });
    }

    private static void SeedClauses(ModelBuilder modelBuilder)
    {
        // Seed Mutual NDA clauses.
        modelBuilder.Entity<Clause>().HasData(
            new Clause
            {
                Id = 1,
                ContractType = ContractTypeRegistry.MutualNdaId,
                Category = "Confidentiality",
                Title = "Confidentiality",
                BodyTemplate = "The parties agree to keep all {{ConfidentialInformation}} confidential and not disclose it to third parties except as permitted by this Agreement.",
                IsOptional = false,
                IsDefaultSelected = true,
                SortOrder = 1
            },
            new Clause
            {
                Id = 2,
                ContractType = ContractTypeRegistry.MutualNdaId,
                Category = "Definitions",
                Title = "Definition of Confidential Information",
                BodyTemplate = "Confidential Information means any non-public information disclosed by {{Party1}} or {{Party2}} that is marked confidential or would reasonably be considered confidential.",
                IsOptional = false,
                IsDefaultSelected = true,
                SortOrder = 2
            },
            new Clause
            {
                Id = 3,
                ContractType = ContractTypeRegistry.MutualNdaId,
                Category = "Exclusions",
                Title = "Exclusions",
                BodyTemplate = "Confidential Information does not include information that is public, already known, independently developed, or rightfully received from third parties.",
                IsOptional = false,
                IsDefaultSelected = true,
                SortOrder = 3
            },
            new Clause
            {
                Id = 4,
                ContractType = ContractTypeRegistry.MutualNdaId,
                Category = "Term",
                Title = "Term",
                BodyTemplate = "This Agreement shall remain in effect for {{TermMonths}} months from the Effective Date ({{EffectiveDate}}).",
                IsOptional = false,
                IsDefaultSelected = true,
                SortOrder = 4
            },
            new Clause
            {
                Id = 5,
                ContractType = ContractTypeRegistry.MutualNdaId,
                Category = "Governing Law",
                Title = "Governing Law",
                BodyTemplate = "This Agreement shall be governed by and construed in accordance with the laws of {{GoverningLaw}}.",
                IsOptional = false,
                IsDefaultSelected = true,
                SortOrder = 5
            },
            new Clause
            {
                Id = 6,
                ContractType = ContractTypeRegistry.MutualNdaId,
                Category = "Miscellaneous",
                Title = "Entire Agreement",
                BodyTemplate = "This Agreement constitutes the entire agreement between the parties with respect to the subject matter hereof.",
                IsOptional = false,
                IsDefaultSelected = true,
                SortOrder = 6
            },
            new Clause
            {
                Id = 7,
                ContractType = ContractTypeRegistry.MutualNdaId,
                Category = "Intellectual Property",
                Title = "No License",
                BodyTemplate = "Nothing in this Agreement grants any license or rights to the other party's intellectual property.",
                IsOptional = false,
                IsDefaultSelected = true,
                SortOrder = 7
            },
            new Clause
            {
                Id = 8,
                ContractType = ContractTypeRegistry.MutualNdaId,
                Category = "Return of Materials",
                Title = "Return of Materials",
                BodyTemplate = "Upon termination, each party shall return or destroy all Confidential Information received from the other party.",
                IsOptional = false,
                IsDefaultSelected = true,
                SortOrder = 8
            },
            new Clause
            {
                Id = 9,
                ContractType = ContractTypeRegistry.MutualNdaId,
                Category = "Remedies",
                Title = "Remedies",
                BodyTemplate = "Each party acknowledges that breach may cause irreparable harm and monetary damages may be inadequate.",
                IsOptional = false,
                IsDefaultSelected = true,
                SortOrder = 9
            },
            new Clause
            {
                Id = 10,
                ContractType = ContractTypeRegistry.MutualNdaId,
                Category = "Assignment",
                Title = "Assignment",
                BodyTemplate = "Neither party may assign this Agreement without the prior written consent of the other party.",
                IsOptional = true,
                IsDefaultSelected = true,
                SortOrder = 10
            },
            new Clause
            {
                Id = 11,
                ContractType = ContractTypeRegistry.MutualNdaId,
                Category = "Miscellaneous",
                Title = "Severability",
                BodyTemplate = "If any provision is found invalid, the remainder shall remain in full force and effect.",
                IsOptional = true,
                IsDefaultSelected = true,
                SortOrder = 11
            },
            new Clause
            {
                Id = 12,
                ContractType = ContractTypeRegistry.MutualNdaId,
                Category = "Miscellaneous",
                Title = "Counterparts",
                BodyTemplate = "This Agreement may be executed in counterparts, each of which shall be deemed an original.",
                IsOptional = true,
                IsDefaultSelected = true,
                SortOrder = 12
            },

            // Seed Non-Disclosure Agreement (One-Way) clauses.
            new Clause
            {
                Id = 13,
                ContractType = ContractTypeRegistry.OneWayNdaId,
                Category = "Confidentiality",
                Title = "Confidentiality",
                BodyTemplate = "The Receiving Party agrees to keep all {{ConfidentialInformation}} confidential and not disclose it to third parties except as permitted by this Agreement.",
                IsOptional = false,
                IsDefaultSelected = true,
                SortOrder = 1
            },
            new Clause
            {
                Id = 14,
                ContractType = ContractTypeRegistry.OneWayNdaId,
                Category = "Definitions",
                Title = "Definition of Confidential Information",
                BodyTemplate = "Confidential Information means any non-public information disclosed by the Disclosing Party that is marked confidential or would reasonably be considered confidential.",
                IsOptional = false,
                IsDefaultSelected = true,
                SortOrder = 2
            },
            new Clause
            {
                Id = 15,
                ContractType = ContractTypeRegistry.OneWayNdaId,
                Category = "Exclusions",
                Title = "Exclusions",
                BodyTemplate = "Confidential Information does not include information that is public, already known, independently developed, or rightfully received from third parties.",
                IsOptional = false,
                IsDefaultSelected = true,
                SortOrder = 3
            },
            new Clause
            {
                Id = 16,
                ContractType = ContractTypeRegistry.OneWayNdaId,
                Category = "Obligations",
                Title = "Obligations of Receiving Party",
                BodyTemplate = "The Receiving Party shall use {{ConfidentialInformation}} solely for the purpose of evaluating the relationship between {{Party1}} and {{Party2}} and shall take reasonable precautions to protect it.",
                IsOptional = false,
                IsDefaultSelected = true,
                SortOrder = 4
            },
            new Clause
            {
                Id = 17,
                ContractType = ContractTypeRegistry.OneWayNdaId,
                Category = "Term",
                Title = "Term / Duration",
                BodyTemplate = "This Agreement shall remain in effect for {{TermMonths}} months from the Effective Date ({{EffectiveDate}}). The obligations of confidentiality shall survive termination.",
                IsOptional = false,
                IsDefaultSelected = true,
                SortOrder = 5
            },
            new Clause
            {
                Id = 18,
                ContractType = ContractTypeRegistry.OneWayNdaId,
                Category = "Return of Materials",
                Title = "Return or Destruction of Information",
                BodyTemplate = "Upon request or termination, the Receiving Party shall return or destroy all Confidential Information received from the Disclosing Party.",
                IsOptional = false,
                IsDefaultSelected = true,
                SortOrder = 6
            },
            new Clause
            {
                Id = 19,
                ContractType = ContractTypeRegistry.OneWayNdaId,
                Category = "Governing Law",
                Title = "Governing Law",
                BodyTemplate = "This Agreement shall be governed by and construed in accordance with the laws of {{GoverningLaw}}.",
                IsOptional = false,
                IsDefaultSelected = true,
                SortOrder = 7
            },
            new Clause
            {
                Id = 20,
                ContractType = ContractTypeRegistry.OneWayNdaId,
                Category = "Remedies",
                Title = "Remedies",
                BodyTemplate = "The Disclosing Party acknowledges that breach may cause irreparable harm and monetary damages may be inadequate.",
                IsOptional = false,
                IsDefaultSelected = true,
                SortOrder = 8
            },
            new Clause
            {
                Id = 21,
                ContractType = ContractTypeRegistry.OneWayNdaId,
                Category = "Miscellaneous",
                Title = "Entire Agreement",
                BodyTemplate = "This Agreement constitutes the entire agreement between the parties with respect to the subject matter hereof.",
                IsOptional = false,
                IsDefaultSelected = true,
                SortOrder = 9
            },
            new Clause
            {
                Id = 22,
                ContractType = ContractTypeRegistry.OneWayNdaId,
                Category = "Miscellaneous",
                Title = "Severability",
                BodyTemplate = "If any provision is found invalid, the remainder shall remain in full force and effect.",
                IsOptional = true,
                IsDefaultSelected = true,
                SortOrder = 10
            },

            // Seed Independent Contractor Agreement clauses.
            new Clause
            {
                Id = 23,
                ContractType = ContractTypeRegistry.ContractorAgreementId,
                Category = "Engagement",
                Title = "Engagement / Scope of Work",
                BodyTemplate = "{{ClientName}} engages {{ContractorName}} as an independent contractor to perform the services described under the scope of {{Purpose}}.",
                IsOptional = false,
                IsDefaultSelected = true,
                SortOrder = 1
            },
            new Clause
            {
                Id = 24,
                ContractType = ContractTypeRegistry.ContractorAgreementId,
                Category = "Status",
                Title = "Independent Contractor Status",
                BodyTemplate = "{{ContractorName}} is an independent contractor and not an employee of {{ClientName}}. {{ContractorName}} shall be responsible for taxes, insurance, and benefits.",
                IsOptional = false,
                IsDefaultSelected = true,
                SortOrder = 2
            },
            new Clause
            {
                Id = 25,
                ContractType = ContractTypeRegistry.ContractorAgreementId,
                Category = "Compensation",
                Title = "Fees and Payment",
                BodyTemplate = "{{ClientName}} shall pay {{ContractorName}} for services rendered as agreed. Payment terms shall be as specified in the engagement details.",
                IsOptional = false,
                IsDefaultSelected = true,
                SortOrder = 3
            },
            new Clause
            {
                Id = 26,
                ContractType = ContractTypeRegistry.ContractorAgreementId,
                Category = "Term",
                Title = "Term and Termination",
                BodyTemplate = "This Agreement shall commence on {{EffectiveDate}} and continue for {{TermMonths}} months unless terminated earlier by either party with written notice.",
                IsOptional = false,
                IsDefaultSelected = true,
                SortOrder = 4
            },
            new Clause
            {
                Id = 27,
                ContractType = ContractTypeRegistry.ContractorAgreementId,
                Category = "Intellectual Property",
                Title = "Intellectual Property Rights",
                BodyTemplate = "All work product created by {{ContractorName}} under this Agreement shall be assigned to {{ClientName}} unless otherwise agreed in writing.",
                IsOptional = false,
                IsDefaultSelected = true,
                SortOrder = 5
            },
            new Clause
            {
                Id = 28,
                ContractType = ContractTypeRegistry.ContractorAgreementId,
                Category = "Confidentiality",
                Title = "Confidentiality",
                BodyTemplate = "{{ContractorName}} agrees to keep all {{ConfidentialInformation}} of {{ClientName}} confidential and not disclose it to third parties.",
                IsOptional = false,
                IsDefaultSelected = true,
                SortOrder = 6
            },
            new Clause
            {
                Id = 29,
                ContractType = ContractTypeRegistry.ContractorAgreementId,
                Category = "Liability",
                Title = "Limitation of Liability",
                BodyTemplate = "Neither party shall be liable for indirect, incidental, or consequential damages arising out of this Agreement.",
                IsOptional = false,
                IsDefaultSelected = true,
                SortOrder = 7
            },
            new Clause
            {
                Id = 30,
                ContractType = ContractTypeRegistry.ContractorAgreementId,
                Category = "Indemnification",
                Title = "Indemnification",
                BodyTemplate = "{{ContractorName}} agrees to indemnify and hold harmless {{ClientName}} from claims arising from {{ContractorName}}'s negligence or willful misconduct.",
                IsOptional = false,
                IsDefaultSelected = true,
                SortOrder = 8
            },
            new Clause
            {
                Id = 31,
                ContractType = ContractTypeRegistry.ContractorAgreementId,
                Category = "Governing Law",
                Title = "Governing Law",
                BodyTemplate = "This Agreement shall be governed by and construed in accordance with the laws of {{GoverningLaw}}.",
                IsOptional = false,
                IsDefaultSelected = true,
                SortOrder = 9
            },
            new Clause
            {
                Id = 32,
                ContractType = ContractTypeRegistry.ContractorAgreementId,
                Category = "Miscellaneous",
                Title = "Entire Agreement / Severability",
                BodyTemplate = "This Agreement constitutes the entire agreement between the parties. If any provision is found invalid, the remainder shall remain in full force and effect.",
                IsOptional = false,
                IsDefaultSelected = true,
                SortOrder = 10
            },

            // Seed Consulting Agreement clauses.
            new Clause
            {
                Id = 33,
                ContractType = ContractTypeRegistry.ConsultingAgreementId,
                Category = "Services",
                Title = "Services",
                BodyTemplate = "{{ConsultantName}} agrees to provide consulting services to {{ClientName}} in connection with {{Purpose}} as described herein.",
                IsOptional = false,
                IsDefaultSelected = true,
                SortOrder = 1
            },
            new Clause
            {
                Id = 34,
                ContractType = ContractTypeRegistry.ConsultingAgreementId,
                Category = "Compensation",
                Title = "Fees and Expenses",
                BodyTemplate = "{{ClientName}} shall pay {{ConsultantName}} consulting fees as agreed. Reasonable expenses may be reimbursed with prior approval.",
                IsOptional = false,
                IsDefaultSelected = true,
                SortOrder = 2
            },
            new Clause
            {
                Id = 35,
                ContractType = ContractTypeRegistry.ConsultingAgreementId,
                Category = "Term",
                Title = "Term and Termination",
                BodyTemplate = "This Agreement shall commence on {{EffectiveDate}} and continue for {{TermMonths}} months unless terminated earlier by either party with written notice.",
                IsOptional = false,
                IsDefaultSelected = true,
                SortOrder = 3
            },
            new Clause
            {
                Id = 36,
                ContractType = ContractTypeRegistry.ConsultingAgreementId,
                Category = "Status",
                Title = "Independent Contractor Relationship",
                BodyTemplate = "{{ConsultantName}} is an independent contractor and not an employee of {{ClientName}}. {{ConsultantName}} shall have no authority to bind {{ClientName}}.",
                IsOptional = false,
                IsDefaultSelected = true,
                SortOrder = 4
            },
            new Clause
            {
                Id = 37,
                ContractType = ContractTypeRegistry.ConsultingAgreementId,
                Category = "Confidentiality",
                Title = "Confidentiality",
                BodyTemplate = "{{ConsultantName}} agrees to keep all {{ConfidentialInformation}} of {{ClientName}} confidential and not disclose it to third parties except as required by law.",
                IsOptional = false,
                IsDefaultSelected = true,
                SortOrder = 5
            },
            new Clause
            {
                Id = 38,
                ContractType = ContractTypeRegistry.ConsultingAgreementId,
                Category = "Intellectual Property",
                Title = "Intellectual Property",
                BodyTemplate = "All deliverables and work product created by {{ConsultantName}} under this Agreement shall be owned by {{ClientName}} upon full payment.",
                IsOptional = false,
                IsDefaultSelected = true,
                SortOrder = 6
            },
            new Clause
            {
                Id = 39,
                ContractType = ContractTypeRegistry.ConsultingAgreementId,
                Category = "Non-Solicitation",
                Title = "Non-Solicitation",
                BodyTemplate = "During the term and for one year thereafter, {{ConsultantName}} shall not solicit {{ClientName}}'s employees or clients.",
                IsOptional = true,
                IsDefaultSelected = false,
                SortOrder = 7
            },
            new Clause
            {
                Id = 40,
                ContractType = ContractTypeRegistry.ConsultingAgreementId,
                Category = "Liability",
                Title = "Limitation of Liability",
                BodyTemplate = "Neither party shall be liable for indirect, incidental, or consequential damages arising out of this Agreement.",
                IsOptional = false,
                IsDefaultSelected = true,
                SortOrder = 8
            },
            new Clause
            {
                Id = 41,
                ContractType = ContractTypeRegistry.ConsultingAgreementId,
                Category = "Governing Law",
                Title = "Governing Law",
                BodyTemplate = "This Agreement shall be governed by and construed in accordance with the laws of {{GoverningLaw}}.",
                IsOptional = false,
                IsDefaultSelected = true,
                SortOrder = 9
            },
            new Clause
            {
                Id = 42,
                ContractType = ContractTypeRegistry.ConsultingAgreementId,
                Category = "Miscellaneous",
                Title = "Miscellaneous",
                BodyTemplate = "This Agreement constitutes the entire agreement between the parties. Any amendments must be in writing and signed by both parties.",
                IsOptional = false,
                IsDefaultSelected = true,
                SortOrder = 10
            }
        );
    }
}
