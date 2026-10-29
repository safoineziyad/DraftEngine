using DraftEngine.Core.Models;
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
            // PartiesJson, AssembledText: unbounded JSON/free text, deliberately no max length.

            entity.HasIndex(contract => contract.Title);
            entity.HasIndex(contract => contract.ContractType);
            entity.HasIndex(contract => contract.CreatedAt);

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
            // BodyTemplate: unbounded template text, deliberately no max length.

            entity.HasIndex(clause => clause.ContractType);
            entity.HasIndex(clause => clause.SortOrder);
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
    }

    private static void SeedClauses(ModelBuilder modelBuilder)
    {
        // Seed Mutual NDA clauses.
        modelBuilder.Entity<Clause>().HasData(
            new Clause
            {
                Id = 1,
                ContractType = "Mutual NDA",
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
                ContractType = "Mutual NDA",
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
                ContractType = "Mutual NDA",
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
                ContractType = "Mutual NDA",
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
                ContractType = "Mutual NDA",
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
                ContractType = "Mutual NDA",
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
                ContractType = "Mutual NDA",
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
                ContractType = "Mutual NDA",
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
                ContractType = "Mutual NDA",
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
                ContractType = "Mutual NDA",
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
                ContractType = "Mutual NDA",
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
                ContractType = "Mutual NDA",
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
                ContractType = "Non-Disclosure Agreement (One-Way)",
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
                ContractType = "Non-Disclosure Agreement (One-Way)",
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
                ContractType = "Non-Disclosure Agreement (One-Way)",
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
                ContractType = "Non-Disclosure Agreement (One-Way)",
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
                ContractType = "Non-Disclosure Agreement (One-Way)",
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
                ContractType = "Non-Disclosure Agreement (One-Way)",
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
                ContractType = "Non-Disclosure Agreement (One-Way)",
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
                ContractType = "Non-Disclosure Agreement (One-Way)",
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
                ContractType = "Non-Disclosure Agreement (One-Way)",
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
                ContractType = "Non-Disclosure Agreement (One-Way)",
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
                ContractType = "Independent Contractor Agreement",
                Category = "Engagement",
                Title = "Engagement / Scope of Work",
                BodyTemplate = "{{Party1}} engages {{Party2}} as an independent contractor to perform the services described for {{ClientName}} under the scope of {{Purpose}}.",
                IsOptional = false,
                IsDefaultSelected = true,
                SortOrder = 1
            },
            new Clause
            {
                Id = 24,
                ContractType = "Independent Contractor Agreement",
                Category = "Status",
                Title = "Independent Contractor Status",
                BodyTemplate = "{{Party2}} is an independent contractor and not an employee of {{Party1}}. {{Party2}} shall be responsible for taxes, insurance, and benefits.",
                IsOptional = false,
                IsDefaultSelected = true,
                SortOrder = 2
            },
            new Clause
            {
                Id = 25,
                ContractType = "Independent Contractor Agreement",
                Category = "Compensation",
                Title = "Fees and Payment",
                BodyTemplate = "{{Party1}} shall pay {{Party2}} for services rendered as agreed. Payment terms shall be as specified in the engagement details.",
                IsOptional = false,
                IsDefaultSelected = true,
                SortOrder = 3
            },
            new Clause
            {
                Id = 26,
                ContractType = "Independent Contractor Agreement",
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
                ContractType = "Independent Contractor Agreement",
                Category = "Intellectual Property",
                Title = "Intellectual Property Rights",
                BodyTemplate = "All work product created by {{Party2}} under this Agreement shall be assigned to {{Party1}} unless otherwise agreed in writing.",
                IsOptional = false,
                IsDefaultSelected = true,
                SortOrder = 5
            },
            new Clause
            {
                Id = 28,
                ContractType = "Independent Contractor Agreement",
                Category = "Confidentiality",
                Title = "Confidentiality",
                BodyTemplate = "{{Party2}} agrees to keep all {{ConfidentialInformation}} of {{Party1}} confidential and not disclose it to third parties.",
                IsOptional = false,
                IsDefaultSelected = true,
                SortOrder = 6
            },
            new Clause
            {
                Id = 29,
                ContractType = "Independent Contractor Agreement",
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
                ContractType = "Independent Contractor Agreement",
                Category = "Indemnification",
                Title = "Indemnification",
                BodyTemplate = "{{Party2}} agrees to indemnify and hold harmless {{Party1}} from claims arising from {{Party2}}'s negligence or willful misconduct.",
                IsOptional = false,
                IsDefaultSelected = true,
                SortOrder = 8
            },
            new Clause
            {
                Id = 31,
                ContractType = "Independent Contractor Agreement",
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
                ContractType = "Independent Contractor Agreement",
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
                ContractType = "Consulting Agreement",
                Category = "Services",
                Title = "Services",
                BodyTemplate = "{{Party2}} agrees to provide consulting services to {{Party1}} for {{ClientName}} in connection with {{Purpose}} as described herein.",
                IsOptional = false,
                IsDefaultSelected = true,
                SortOrder = 1
            },
            new Clause
            {
                Id = 34,
                ContractType = "Consulting Agreement",
                Category = "Compensation",
                Title = "Fees and Expenses",
                BodyTemplate = "{{Party1}} shall pay {{Party2}} consulting fees as agreed. Reasonable expenses may be reimbursed with prior approval.",
                IsOptional = false,
                IsDefaultSelected = true,
                SortOrder = 2
            },
            new Clause
            {
                Id = 35,
                ContractType = "Consulting Agreement",
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
                ContractType = "Consulting Agreement",
                Category = "Status",
                Title = "Independent Contractor Relationship",
                BodyTemplate = "{{Party2}} is an independent contractor and not an employee of {{Party1}}. {{Party2}} shall have no authority to bind {{Party1}}.",
                IsOptional = false,
                IsDefaultSelected = true,
                SortOrder = 4
            },
            new Clause
            {
                Id = 37,
                ContractType = "Consulting Agreement",
                Category = "Confidentiality",
                Title = "Confidentiality",
                BodyTemplate = "{{Party2}} agrees to keep all {{ConfidentialInformation}} of {{Party1}} confidential and not disclose it to third parties except as required by law.",
                IsOptional = false,
                IsDefaultSelected = true,
                SortOrder = 5
            },
            new Clause
            {
                Id = 38,
                ContractType = "Consulting Agreement",
                Category = "Intellectual Property",
                Title = "Intellectual Property",
                BodyTemplate = "All deliverables and work product created by {{Party2}} under this Agreement shall be owned by {{Party1}} upon full payment.",
                IsOptional = false,
                IsDefaultSelected = true,
                SortOrder = 6
            },
            new Clause
            {
                Id = 39,
                ContractType = "Consulting Agreement",
                Category = "Non-Solicitation",
                Title = "Non-Solicitation",
                BodyTemplate = "During the term and for one year thereafter, {{Party2}} shall not solicit {{Party1}}'s employees or clients.",
                IsOptional = true,
                IsDefaultSelected = false,
                SortOrder = 7
            },
            new Clause
            {
                Id = 40,
                ContractType = "Consulting Agreement",
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
                ContractType = "Consulting Agreement",
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
                ContractType = "Consulting Agreement",
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
